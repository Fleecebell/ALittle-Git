/**
 * 统计接口测试：用真 SQLite（Node 24 内置 node:sqlite）跑真 schema + 真 SQL，
 * 只把 D1 的 prepare/bind/batch 这一小层用适配器模拟出来。
 * 这样能抓到 SQL 写错、字段名拼错、聚合口径错，而不是只测 HTTP 外壳。
 */

import { test, before } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { DatabaseSync } from "node:sqlite";

import { handleStats } from "../src/stats.js";
import worker from "../src/index.js";
import dashboardHtml from "../src/dashboard-page.js";

const here = dirname(fileURLToPath(import.meta.url));
const schema = readFileSync(join(here, "..", "schema.sql"), "utf8");

/** 模拟 D1 的 statement 对象 */
class FakeStatement {
  constructor(db, sql, args = []) {
    this.db = db;
    this.sql = sql;
    this.args = args;
  }
  bind(...args) {
    return new FakeStatement(this.db, this.sql, args);
  }
  all() {
    return { results: this.db.prepare(this.sql).all(...this.args) };
  }
  first() {
    return this.db.prepare(this.sql).get(...this.args);
  }
  run() {
    this.db.prepare(this.sql).run(...this.args);
    return { success: true, meta: { changes: 1 } };
  }
}

class FakeD1 {
  constructor(db) {
    this.db = db;
  }
  prepare(sql) {
    return new FakeStatement(this.db, sql);
  }
  async batch(statements) {
    return statements.map((s) => ({ success: true, results: s.all().results }));
  }
}

const BASE_TS = Date.UTC(2026, 10, 9, 6, 0, 0); // 2026-11-09 14:00 (+08)

function makeDb() {
  const db = new DatabaseSync(":memory:");
  db.exec(schema);
  return db;
}

function insert(db, { player = "player_aaaaaa", session = "s_20261109_1400_aaaaaa", type, ts, meta }) {
  db.prepare(
    "INSERT INTO game_events (player_id, session_id, event_type, metadata, client_ts, server_ts) VALUES (?, ?, ?, ?, ?, ?)"
  ).run(player, session, type, JSON.stringify(meta), ts, ts);
}

function seed(db) {
  const P1 = "player_aaaaaa";
  const P2 = "player_bbbbbb";
  const S1 = "s_20261109_1400_aaaaaa";
  const S2 = "s_20261109_1500_bbbbbb";

  insert(db, { player: P1, session: S1, type: "session_start", ts: BASE_TS, meta: { scene: "Start", platform: "WindowsEditor", app_version: "1.0", unity_version: "2022.3.55f1c1" } });
  insert(db, { player: P2, session: S2, type: "session_start", ts: BASE_TS + 3600_000, meta: { scene: "Start", platform: "WindowsPlayer", app_version: "1.0", unity_version: "2022.3.55f1c1" } });

  // 关卡 9101：两个人各进一次，其中一个通关
  insert(db, { player: P1, session: S1, type: "level_start", ts: BASE_TS + 1000, meta: { scene: "9101", level_index: 1, attempt: 1, session_time_s: 1 } });
  insert(db, { player: P2, session: S2, type: "level_start", ts: BASE_TS + 3600_000 + 1000, meta: { scene: "9101", level_index: 1, attempt: 1, session_time_s: 1 } });

  // 格子：10,0 待了 5 秒（2 秒发呆）跳 3 次失败 1 次；11,0 只是路过
  insert(db, { player: P1, session: S1, type: "cell", ts: BASE_TS + 2000, meta: { scene: "9101", attempt: 1, cell_x: 10, cell_y: 0, x: 10.5, y: 0.5, dwell_s: 5, idle_s: 2, jumps: 3, jump_fails: 1 } });
  insert(db, { player: P2, session: S2, type: "cell", ts: BASE_TS + 3600_500, meta: { scene: "9101", attempt: 1, cell_x: 11, cell_y: 0, x: 11.5, y: 0.5, dwell_s: 1, idle_s: 0, jumps: 0, jump_fails: 0 } });

  // 两次死亡：(10,0) 尖刺、(11,0) 被夹
  insert(db, { player: P1, session: S1, type: "death", ts: BASE_TS + 3000, meta: { scene: "9101", level_index: 1, attempt: 1, cause: "hazard:Spike", actor: "player1", x: 10.2, y: 0.1, cell_x: 10, cell_y: 0, survived_s: 12.5, deaths_in_level: 1, jumps_before_death: 2 } });
  insert(db, { player: P2, session: S2, type: "death", ts: BASE_TS + 3600_700, meta: { scene: "9101", level_index: 1, attempt: 1, cause: "crush", actor: "player2", x: 11.1, y: 0.2, cell_x: 11, cell_y: 0, survived_s: 8, deaths_in_level: 1, jumps_before_death: 0 } });

  // 通关记录：一次通关 30 秒
  insert(db, { player: P1, session: S1, type: "level_clear", ts: BASE_TS + 33_000, meta: { scene: "9101", level_index: 1, attempt: 1, duration_s: 30, deaths: 1, jumps: 3, jump_fails: 1, distance: 42.5, session_time_s: 33 } });
  insert(db, { player: P2, session: S2, type: "level_quit", ts: BASE_TS + 3600_900, meta: { scene: "9101", level_index: 1, attempt: 1, duration_s: 50, deaths: 1, jumps: 0, jump_fails: 0, distance: 10, session_time_s: 50 } });

  // 另一关 S1 的一条死亡，验证按场景分组
  insert(db, { player: P1, session: S1, type: "level_start", ts: BASE_TS + 40_000, meta: { scene: "S1", level_index: 8, attempt: 1, session_time_s: 40 } });
  insert(db, { player: P1, session: S1, type: "death", ts: BASE_TS + 41_000, meta: { scene: "S1", level_index: 8, attempt: 1, cause: "hazard:Lava", actor: "player1", x: 0, y: -3, cell_x: 0, cell_y: -4, survived_s: 1, deaths_in_level: 1, jumps_before_death: 0 } });
}

function env(db, extra = {}) {
  return { DB: new FakeD1(db), ...extra };
}

function get(path, e) {
  return handleStats(new URL("https://example.com" + path), e, new Request("https://example.com" + path));
}

before(() => {});

/* ---------------------------------------------------------------- 概览 */

test("overview：总量、按关卡聚合、死因", async () => {
  const db = makeDb();
  seed(db);

  const res = await get("/v1/stats/overview?since=0", env(db));
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.ok, true);

  assert.equal(body.totals.players, 2);
  assert.equal(body.totals.sessions, 2);
  assert.equal(body.totals.deaths, 3);
  assert.equal(body.totals.levels, 2);
  assert.equal(body.totals.jumps, 3);

  const lv = body.scenes.find((s) => s.scene === "9101");
  assert.ok(lv, "应该有 9101 这一行");
  assert.equal(lv.starts, 2);
  assert.equal(lv.clears, 1);
  assert.equal(lv.quits, 1);
  assert.equal(lv.deaths, 2);
  assert.equal(lv.clear_rate, 0.5);
  assert.equal(lv.deaths_per_start, 1);
  assert.equal(lv.jumps, 3);
  assert.equal(lv.jump_fails, 1);
  assert.equal(lv.dwell_s, 6);
  assert.equal(lv.idle_s, 2);
  assert.equal(lv.cells, 2);
  // 平均时长 = (30 + 50) / 2
  assert.equal(lv.avg_duration_s, 40);

  const s1 = body.scenes.find((s) => s.scene === "S1");
  assert.equal(s1.starts, 1);
  assert.equal(s1.deaths, 1);

  assert.deepEqual(
    body.causes.map((c) => c.cause).sort(),
    ["crush", "hazard:Lava", "hazard:Spike"]
  );
  assert.equal(body.actors.find((a) => a.actor === "player1").n, 2);

  // 客户端分布来自 session_start
  assert.equal(body.apps.length, 2);
  assert.ok(body.apps.every((a) => a.version === "1.0"));
});

test("overview：since 过滤会把旧数据排除", async () => {
  const db = makeDb();
  seed(db);
  // 只看第一小时之后的数据（P2 的 session 在 +1h）
  const res = await get(`/v1/stats/overview?since=${BASE_TS + 3600_000}`, env(db));
  const body = await res.json();
  assert.equal(body.totals.players, 1, "只有 P2 的数据在时间窗内");
  assert.equal(body.totals.deaths, 1);
});

/* ---------------------------------------------------------------- 单关 */

test("scene：格子合并、边界、死亡流水、时长", async () => {
  const db = makeDb();
  seed(db);

  const res = await get("/v1/stats/scene?scene=9101&since=0", env(db));
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.ok, true);
  assert.equal(body.scene, "9101");

  assert.equal(body.cells.length, 2);

  const c10 = body.cells.find((c) => c.x === 10 && c.y === 0);
  assert.equal(c10.jumps, 3);
  assert.equal(c10.fails, 1);
  assert.equal(c10.dwell, 5);
  assert.equal(c10.idle, 2);
  assert.equal(c10.fail_rate, 0.25);
  assert.equal(c10.idle_ratio, 0.4);
  assert.equal(c10.deaths, 1);
  assert.deepEqual(c10.causes, ["hazard:Spike"]);

  const c11 = body.cells.find((c) => c.x === 11 && c.y === 0);
  assert.equal(c11.deaths, 1);
  assert.deepEqual(c11.causes, ["crush"]);
  assert.equal(c11.fail_rate, null, "没跳过就不该有失败率");

  assert.deepEqual(body.bounds, { minX: 10, maxX: 11, minY: 0, maxY: 0, width: 2, height: 1 });

  assert.equal(body.stats.deaths, 2);
  assert.equal(body.stats.jumps, 3);
  assert.equal(body.stats.jump_fails, 1);
  assert.equal(body.stats.clears, 1);
  assert.equal(body.stats.quits, 1);
  assert.equal(body.stats.playtime_s, 80);

  assert.equal(body.deaths.length, 2);
  assert.equal(body.deaths[0].cause, "crush", "按时间倒序，最新的在前");
  assert.equal(body.deaths[1].cause, "hazard:Spike");
  assert.equal(body.deaths[1].survived_s, 12.5);

  assert.equal(body.runs.length, 2);
  assert.equal(body.runs[0].type, "level_quit");
  assert.equal(body.starts.length, 2);
});

test("scene：不存在的关卡返回空数据而不是报错", async () => {
  const db = makeDb();
  seed(db);
  const body = await (await get("/v1/stats/scene?scene=NOPE&since=0", env(db))).json();
  assert.equal(body.ok, true);
  assert.equal(body.cells.length, 0);
  assert.equal(body.deaths.length, 0);
  assert.deepEqual(body.bounds, { minX: 0, maxX: 0, minY: 0, maxY: 0 });
});

test("scene：缺少 scene 参数要明确报错", async () => {
  const db = makeDb();
  const body = await (await get("/v1/stats/scene", env(db))).json();
  assert.equal(body.ok, false);
  assert.equal(body.error, "scene_required");
});

/* ---------------------------------------------------------------- 流水 */

test("recent：按时间倒序、带上 metadata", async () => {
  const db = makeDb();
  seed(db);
  const body = await (await get("/v1/stats/recent?since=0&limit=5", env(db))).json();
  assert.equal(body.ok, true);
  assert.equal(body.events.length, 5);
  for (let i = 1; i < body.events.length; i++) {
    assert.ok(body.events[i - 1].ts >= body.events[i].ts, "时间必须递减");
  }
  const death = body.events.find((e) => e.type === "death");
  assert.ok(death && death.meta.scene);
});

/* ---------------------------------------------------------------- 鉴权与错误 */

test("配了 READ_KEY 就必须带 key", async () => {
  const db = makeDb();
  seed(db);
  const e = env(db, { READ_KEY: "sekrit" });

  const noKey = await get("/v1/stats/overview", e);
  assert.equal(noKey.status, 401);

  const wrong = await get("/v1/stats/overview?key=nope", e);
  assert.equal(wrong.status, 401);

  const ok = await get("/v1/stats/overview?key=sekrit", e);
  assert.equal(ok.status, 200);
});

test("没绑 DB 返回 503", async () => {
  const res = await handleStats(new URL("https://example.com/v1/stats/overview"), {}, null);
  assert.equal(res.status, 503);
});

test("未知统计路径返回 404 JSON", async () => {
  const db = makeDb();
  const res = await get("/v1/stats/nope", env(db));
  assert.equal(res.status, 404);
});

/* ---------------------------------------------------------------- Worker 路由 */

test("GET / 返回看板 HTML", async () => {
  const res = await worker.fetch(new Request("https://example.com/"), {});
  assert.equal(res.status, 200);
  assert.match(res.headers.get("Content-Type"), /text\/html/);
  const text = await res.text();
  assert.match(text, /玩家行为数据看板/);
  assert.match(text, /heatmap/);
});

test("GET /v1 返回接口清单 JSON", async () => {
  const res = await worker.fetch(new Request("https://example.com/v1"), {});
  const body = await res.json();
  assert.equal(body.ok, true);
  assert.equal(body.endpoints.ingest, "POST /v1/events");
  assert.ok(body.endpoints.overview);
});

test("GET /v1/stats/overview 走 Worker 也能出数", async () => {
  const db = makeDb();
  seed(db);
  const res = await worker.fetch(
    new Request("https://example.com/v1/stats/overview?since=0"),
    env(db)
  );
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.totals.deaths, 3);
});

test("POST /v1/stats/overview 返回 405", async () => {
  const db = makeDb();
  const res = await worker.fetch(
    new Request("https://example.com/v1/stats/overview", { method: "POST", body: "{}" }),
    env(db)
  );
  assert.equal(res.status, 405);
});

/* ---------------------------------------------------------------- 看板资源 */

test("dashboard-page.js 与 dashboard.html 内容一致（没忘记重新生成）", () => {
  const html = readFileSync(join(here, "..", "src", "dashboard.html"), "utf8");
  assert.equal(dashboardHtml, html);
});
