import { test } from "node:test";
import assert from "node:assert/strict";
import worker from "../src/index.js";

const URL = "https://example.com/v1/events";

function makeD1({ fail = false } = {}) {
  const calls = { batches: [], preparedSql: [] };
  const DB = {
    prepare(sql) {
      calls.preparedSql.push(sql);
      const stmt = {
        bind(...args) {
          return { sql, args };
        },
      };
      return stmt;
    },
    async batch(statements) {
      if (fail) throw new Error("D1_ERROR: no such table");
      calls.batches.push(statements);
      return statements.map(() => ({ success: true, meta: { changes: 1 } }));
    },
  };
  return { env: { DB }, calls };
}

function makeEvent(overrides = {}) {
  return {
    player_id: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    session_id: "s_20261109_120000_1a2b",
    event_type: "death",
    client_ts: Date.now(),
    metadata: { scene: "9101", x: 1.5 },
    ...overrides,
  };
}

function post(body, { headers = {}, env, url = URL } = {}) {
  return worker.fetch(
    new Request(url, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...headers },
      body: typeof body === "string" ? body : JSON.stringify(body),
    }),
    env
  );
}

test("OPTIONS 预检返回 204 且带 CORS 头", async () => {
  const { env } = makeD1();
  const res = await worker.fetch(
    new Request(URL, { method: "OPTIONS" }),
    env
  );
  assert.equal(res.status, 204);
  assert.equal(res.headers.get("Access-Control-Allow-Origin"), "*");
  assert.match(res.headers.get("Access-Control-Allow-Methods"), /POST/);
});

test("GET /health 返回 ok", async () => {
  const { env } = makeD1();
  const res = await worker.fetch(new Request("https://example.com/health"), env);
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.ok, true);
  assert.equal(body.service, "alittle-telemetry");
});

test("单条事件写库，7 个字段按顺序绑定", async () => {
  const { env, calls } = makeD1();
  const ts = Date.now();
  const res = await post(makeEvent({ client_ts: ts }), { env });
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.ok, true);
  assert.equal(body.inserted, 1);

  assert.equal(calls.batches.length, 1);
  const stmt = calls.batches[0][0];
  assert.match(stmt.sql, /INSERT INTO game_events/);
  assert.equal(stmt.args[0], "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
  assert.equal(stmt.args[1], "s_20261109_120000_1a2b");
  assert.equal(stmt.args[2], "death");
  assert.equal(stmt.args[3], JSON.stringify({ scene: "9101", x: 1.5 }), "metadata 应序列化为 JSON 文本");
  assert.equal(stmt.args[4], ts, "client_ts 原样保存");
  assert.equal(typeof stmt.args[5], "number", "server_ts 由服务端补");
});

test("批量格式 {events:[...]} 与裸数组都支持", async () => {
  for (const body of [
    { events: [makeEvent(), makeEvent({ event_type: "level_start" })] },
    [makeEvent(), makeEvent({ event_type: "level_start" })],
  ]) {
    const { env, calls } = makeD1();
    const res = await post(body, { env });
    assert.equal(res.status, 200);
    assert.equal((await res.json()).inserted, 2);
    assert.equal(calls.batches[0].length, 2);
  }
});

test("超过 50 条自动分批（200 条 = 4 批）", async () => {
  const { env, calls } = makeD1();
  const events = Array.from({ length: 200 }, () => makeEvent());
  const res = await post({ events }, { env });
  assert.equal(res.status, 200);
  assert.equal(calls.batches.length, 4);
  assert.deepEqual(
    calls.batches.map((b) => b.length),
    [50, 50, 50, 50]
  );
});

test("字段非法返回 400 并给出可定位的错误", async () => {
  const cases = [
    [{ player_id: "短" }, /player_id_invalid/],
    [{ session_id: "bad id with space" }, /session_id_invalid/],
    [{ event_type: "Space Jump!" }, /event_type_invalid/],
    [{ client_ts: "yesterday" }, /client_ts_invalid/],
    [{ client_ts: 946684800000 }, /client_ts_invalid/], // 2000 年，早于下限
    [{ metadata: { blob: "x".repeat(5000) } }, /metadata_too_large/],
  ];
  for (const [overrides, expected] of cases) {
    const { env, calls } = makeD1();
    const res = await post({ events: [makeEvent(overrides)] }, { env });
    assert.equal(res.status, 400, JSON.stringify(overrides));
    const body = await res.json();
    assert.equal(body.ok, false);
    assert.match(JSON.stringify(body.details), expected);
    assert.equal(calls.batches.length, 0, "非法事件不应写库");
  }
});

test("坏 JSON / 空对象 / 非对象数组元素都被拒绝", async () => {
  const { env } = makeD1();
  assert.equal((await post("{not json", { env })).status, 400);
  assert.equal((await post({ foo: 1 }, { env })).status, 400);

  const res = await post({ events: [null] }, { env });
  assert.equal(res.status, 400);
  assert.match(JSON.stringify((await res.json()).details), /not_an_object/);
});

test("秒级时间戳自动归一化为毫秒", async () => {
  const { env, calls } = makeD1();
  const seconds = 1762650000;
  const res = await post({ events: [makeEvent({ client_ts: seconds })] }, { env });
  assert.equal(res.status, 200);
  assert.equal(calls.batches[0][0].args[4], seconds * 1000);
});

test("超过 200 条返回 413", async () => {
  const { env } = makeD1();
  const events = Array.from({ length: 201 }, () => makeEvent());
  const res = await post({ events }, { env });
  assert.equal(res.status, 413);
  assert.equal((await res.json()).error, "too_many_events");
});

test("部分合法时写入合法的部分并报告 rejected", async () => {
  const { env, calls } = makeD1();
  const res = await post(
    { events: [makeEvent(), { player_id: "x" }] },
    { env }
  );
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.inserted, 1);
  assert.equal(body.rejected, 1);
  assert.equal(calls.batches[0].length, 1);
});

test("D1 写入失败返回 503（客户端应重试而不是丢弃）", async () => {
  const { env } = makeD1({ fail: true });
  const res = await post(makeEvent(), { env });
  assert.equal(res.status, 503);
  assert.equal((await res.json()).error, "db_write_failed");
});

test("配置了 INGEST_KEY 时必须带正确请求头", async () => {
  const { env } = makeD1();
  env.INGEST_KEY = "secret-key";

  const denied = await post(makeEvent(), { env });
  assert.equal(denied.status, 401);

  const wrong = await post(makeEvent(), { env, headers: { "x-telemetry-key": "nope" } });
  assert.equal(wrong.status, 401);

  const ok = await post(makeEvent(), { env, headers: { "x-telemetry-key": "secret-key" } });
  assert.equal(ok.status, 200);
});

test("不支持的方法返回 405，未知路径 404", async () => {
  const { env } = makeD1();
  const put = await worker.fetch(new Request(URL, { method: "PUT" }), env);
  assert.equal(put.status, 405);

  const notFound = await post(makeEvent(), { env, url: "https://example.com/nope" });
  assert.equal(notFound.status, 404);
});
