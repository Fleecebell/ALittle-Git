/**
 * 统计接口测试：真 SQLite（node:sqlite）跑真 schema + 真 SQL，只在 D1 的
 * prepare/bind/batch 这一小层打桩，抓 SQL 写错、字段名拼错、聚合口径错。
 * 覆盖 overview / compare / scene / recent、P1/P2 拆分、since、READ_KEY、清库。
 */

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { DatabaseSync } from "node:sqlite";

import { handleStats, handleAdminClear } from "../src/stats.js";
import worker from "../src/index.js";
import { CORS_HEADERS } from "../src/http.js";

const here = dirname(fileURLToPath(import.meta.url));
const schema = readFileSync(join(here, "..", "schema.sql"), "utf8");

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
  run() {
    const r = this.db.prepare(this.sql).run(...this.args);
    return { success: true, meta: { changes: Number(r.changes) } };
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

/**
 * 关卡 9101：进入 2、通关 1、中断 1、死 2（P1 一次 / P2 一次）
 *   (10,0) P1 停 5s 发呆 2s 跳 3；P2 停 2s 发呆 1s 跳 2；(11,0) P1 停 1s，P2 被夹死
 *   通关 30s + 中断 50s → 平均 40s
 * 关卡 S1：进入 1、死 1、没有跑完记录
 * P2 的 session 在一小时后，用来验证 since 与 last_ts
 */
function seed(db) {
  const P1 = "player_aaaaaa";
  const P2 = "player_bbbbbb";
  const S1 = "s_20261109_1400_aaaaaa";
  const S2 = "s_20261109_1500_bbbbbb";
  const ss = { scene: "Start", app_version: "1.0", unity_version: "2022.3.55f1c1" };

  insert(db, { player: P1, session: S1, type: "session_start", ts: BASE_TS, meta: { ...ss, platform: "WindowsEditor" } });
  insert(db, { player: P2, session: S2, type: "session_start", ts: BASE_TS + 3600_000, meta: { ...ss, platform: "WindowsPlayer" } });

  insert(db, { player: P1, session: S1, type: "level_start", ts: BASE_TS + 1000, meta: { scene: "9101", level_index: 1, attempt: 1, session_time_s: 1 } });
  insert(db, { player: P2, session: S2, type: "level_start", ts: BASE_TS + 3601_000, meta: { scene: "9101", level_index: 1, attempt: 1, session_time_s: 1 } });

  insert(db, { player: P1, session: S1, type: "cell", ts: BASE_TS + 2000, meta: { scene: "9101", attempt: 1, actor: "player1", cell_x: 20, cell_y: 0, x: 10.5, y: 0.5, dwell_s: 5, idle_s: 2, jumps: 3, cell_size: 0.5 } });
  insert(db, { player: P1, session: S1, type: "cell", ts: BASE_TS + 2100, meta: { scene: "9101", attempt: 1, actor: "player1", cell_x: 22, cell_y: 0, x: 11.5, y: 0.5, dwell_s: 1, idle_s: 0, jumps: 0, cell_size: 0.5 } });
  insert(db, { player: P2, session: S2, type: "cell", ts: BASE_TS + 3600_500, meta: { scene: "9101", attempt: 1, actor: "player2", cell_x: 20, cell_y: 0, x: 10.5, y: 0.5, dwell_s: 2, idle_s: 1, jumps: 2, cell_size: 0.5 } });

  // 路线：同一局 P1 通关（clear）、P2 中途退出（quit）
  insert(db, { player: P1, session: S1, type: "route", ts: BASE_TS + 32_000, meta: { scene: "9101", level_index: 1, attempt: 1, actor: "player1", outcome: "clear", cells: "20,0;21,0;21,1;22,0", cell_count: 4, duration_s: 30, cell_size: 0.5 } });
  insert(db, { player: P2, session: S2, type: "route", ts: BASE_TS + 32_100, meta: { scene: "9101", level_index: 1, attempt: 1, actor: "player2", outcome: "quit", cells: "20,0;20,1;21,0", cell_count: 3, duration_s: 31, cell_size: 0.5 } });

  insert(db, { player: P1, session: S1, type: "death", ts: BASE_TS + 3000, meta: { scene: "9101", level_index: 1, attempt: 1, cause: "hazard:Spike", actor: "player1", x: 10.2, y: 0.1, cell_x: 20, cell_y: 0, cell_size: 0.5, survived_s: 12.5, deaths_in_level: 1, jumps_before_death: 2 } });
  insert(db, { player: P2, session: S2, type: "death", ts: BASE_TS + 3600_700, meta: { scene: "9101", level_index: 1, attempt: 1, cause: "crush", actor: "player2", x: 11.1, y: 0.2, cell_x: 22, cell_y: 0, cell_size: 0.5, survived_s: 8, deaths_in_level: 1, jumps_before_death: 0 } });

  insert(db, { player: P1, session: S1, type: "level_clear", ts: BASE_TS + 33_000, meta: { scene: "9101", level_index: 1, attempt: 1, duration_s: 30, deaths: 1, jumps: 3, distance: 42.5, session_time_s: 33 } });
  insert(db, { player: P2, session: S2, type: "level_quit", ts: BASE_TS + 3600_900, meta: { scene: "9101", level_index: 1, attempt: 1, duration_s: 50, deaths: 1, jumps: 0, distance: 10, session_time_s: 50 } });

  insert(db, { player: P1, session: S1, type: "level_start", ts: BASE_TS + 40_000, meta: { scene: "S1", level_index: 8, attempt: 1, session_time_s: 40 } });
  insert(db, { player: P1, session: S1, type: "death", ts: BASE_TS + 41_000, meta: { scene: "S1", level_index: 8, attempt: 1, cause: "hazard:Lava", actor: "player1", x: 0, y: -3, cell_x: 0, cell_y: -4, cell_size: 0.5, survived_s: 1, deaths_in_level: 1, jumps_before_death: 0 } });
}

const TOTAL_EVENTS = 15;
const env = (db, extra = {}) => ({ DB: new FakeD1(db), ...extra });

const get = (path, e, headers) =>
  handleStats(new URL("https://example.com" + path), e, new Request("https://example.com" + path, { headers }));

const near = (actual, expected, msg) =>
  assert.ok(Math.abs(actual - expected) < 0.001, `${msg}: ${actual} != ${expected}`);

test("overview：总量、按关卡聚合、P1/P2 拆分、死因", async () => {
  const db = makeDb();
  seed(db);
  const res = await get("/v1/stats/overview?since=0", env(db));
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.ok, true);

  assert.equal(body.totals.players, 2);
  assert.equal(body.totals.sessions, 2);
  assert.equal(body.totals.events, TOTAL_EVENTS);
  assert.equal(body.totals.levels, 2);
  assert.equal(body.totals.deaths, 3);
  assert.equal(body.totals.p1_deaths, 2);
  assert.equal(body.totals.p2_deaths, 1);
  assert.equal(body.totals.jumps, 5);
  near(body.totals.playtime_s, 80, "累计时长");
  assert.equal(body.totals.first_ts, BASE_TS);
  assert.equal(body.totals.last_ts, BASE_TS + 3_601_000);

  assert.deepEqual(body.scenes.map((s) => s.scene), ["9101", "S1"]);
  const lvl = body.scenes[0];
  assert.equal(lvl.level_index, 1);
  assert.equal(lvl.starts, 2);
  assert.equal(lvl.clears, 1);
  assert.equal(lvl.quits, 1);
  assert.equal(lvl.deaths, 2);
  assert.equal(lvl.p1.deaths, 1);
  assert.equal(lvl.p2.deaths, 1);
  assert.equal(lvl.p1_deaths, 1);
  assert.equal(lvl.p2_deaths, 1);
  assert.equal(lvl.jumps, 5);
  assert.equal(lvl.p1_jumps, 3);
  assert.equal(lvl.p2_jumps, 2);
  near(lvl.dwell_s, 8, "停留总时长");
  near(lvl.p1.dwell_s, 6, "P1 停留");
  near(lvl.p2.dwell_s, 2, "P2 停留");
  assert.equal(lvl.cells, 2);
  near(lvl.clear_rate, 0.5, "通关率");
  near(lvl.deaths_per_start, 1, "每次进入死亡");
  near(lvl.avg_duration_s, 40, "平均单次时长");

  const s1 = body.scenes[1];
  assert.equal(s1.starts, 1);
  assert.equal(s1.deaths, 1);
  assert.equal(s1.clear_rate, 0);
  assert.equal(s1.avg_duration_s, null);
  assert.equal(s1.cells, 0);

  const causes = body.causes.map((c) => [c.cause, c.n]).sort((a, b) => String(a[0]).localeCompare(String(b[0])));
  assert.deepEqual(causes, [["crush", 1], ["hazard:Lava", 1], ["hazard:Spike", 1]]);
  const actors = body.actors.map((a) => [a.actor, a.n]).sort((a, b) => String(a[0]).localeCompare(String(b[0])));
  assert.deepEqual(actors, [["player1", 2], ["player2", 1]]);
  assert.equal(body.apps.length, 2);
});

test("overview：since 过滤掉旧数据", async () => {
  const db = makeDb();
  seed(db);
  const body = await (await get(`/v1/stats/overview?since=${BASE_TS + 3600_000}`, env(db))).json();
  assert.deepEqual(body.scenes.map((s) => s.scene), ["9101"]);
  assert.equal(body.scenes[0].starts, 1);
  assert.equal(body.scenes[0].deaths, 1);
  assert.equal(body.scenes[0].p2.deaths, 1);
  assert.equal(body.scenes[0].p1.deaths, 0);
});

test("compare：每关一行，带每列最大值方便画条形", async () => {
  const db = makeDb();
  seed(db);
  const body = await (await get("/v1/stats/compare?since=0", env(db))).json();
  assert.equal(body.ok, true);
  assert.equal(body.scenes.length, 2);
  assert.equal(body.max.starts, 2);
  assert.equal(body.max.deaths, 2);
  assert.equal(body.max.deaths_per_start, 1);
  assert.equal(body.max.jumps, 5);
  near(body.max.avg_duration_s, 40, "max 平均时长");

  const lvl = body.scenes.find((s) => s.scene === "9101");
  assert.equal(lvl.p1_deaths, 1);
  assert.equal(lvl.p2_deaths, 1);
  assert.equal(lvl.p2_jumps, 2);
  assert.equal(lvl.idle_ratio, undefined);
});

test("scene：格子按 P1/P2 合并、边界、死亡流水、时长", async () => {
  const db = makeDb();
  seed(db);
  const body = await (await get("/v1/stats/scene?scene=9101&since=0", env(db))).json();

  assert.equal(body.ok, true);
  assert.equal(body.scene, "9101");
  assert.deepEqual(body.bounds, { minX: 20, maxX: 22, minY: 0, maxY: 0, width: 3, height: 1 });
  assert.equal(body.cells.length, 2);
  assert.equal(body.cell_size, 0.5);

  const c10 = body.cells.find((c) => c.x === 20 && c.y === 0);
  assert.equal(c10.p1.dwell, 5);
  assert.equal(c10.p1.jumps, 3);
  assert.equal(c10.p1.deaths, 1);
  assert.equal(c10.p2.dwell, 2);
  assert.equal(c10.p2.jumps, 2);
  assert.equal(c10.p2.deaths, 0);
  assert.equal(c10.deaths, 1);
  assert.deepEqual(c10.causes, ["hazard:Spike"]);

  const c11 = body.cells.find((c) => c.x === 22 && c.y === 0);
  assert.equal(c11.p1.jumps, 0);
  assert.equal(c11.p2.deaths, 1);
  assert.deepEqual(c11.causes, ["crush"]);

  // 路线：倒序（最新在前），cells 已解析成坐标数组，outcome 原样带出
  assert.equal(body.routes.length, 2);
  assert.equal(body.routes[0].actor, "player2");
  assert.equal(body.routes[0].outcome, "quit");
  assert.deepEqual(body.routes[0].cells, [[20, 0], [20, 1], [21, 0]]);
  assert.equal(body.routes[1].actor, "player1");
  assert.equal(body.routes[1].outcome, "clear");
  assert.deepEqual(body.routes[1].cells, [[20, 0], [21, 0], [21, 1], [22, 0]]);

  const s = body.stats;
  assert.equal(s.starts, 2);
  assert.equal(s.clears, 1);
  assert.equal(s.quits, 1);
  assert.equal(s.deaths, 2);
  assert.equal(s.p1_deaths, 1);
  assert.equal(s.p2_deaths, 1);
  assert.equal(s.jumps, 5);
  assert.equal(s.p1_jumps, 3);
  assert.equal(s.p2_jumps, 2);
  near(s.dwell_s, 8, "单关停留");
  near(s.clear_rate, 0.5, "单关通关率");
  near(s.avg_duration_s, 40, "单关平均时长");

  assert.equal(body.deaths.length, 2);
  const p2death = body.deaths.find((d) => d.actor === "player2");
  assert.equal(p2death.cause, "crush");
  assert.equal(p2death.x, 11.1);
  assert.equal(body.runs.length, 2);
});

test("scene：不存在的关卡返回空数据而不是报错", async () => {
  const db = makeDb();
  seed(db);
  const body = await (await get("/v1/stats/scene?scene=nope&since=0", env(db))).json();
  assert.equal(body.ok, true);
  assert.deepEqual(body.cells, []);
  assert.equal(body.stats.starts, 0);
  assert.equal(body.stats.clear_rate, null);
  assert.equal(body.stats.avg_duration_s, null);
  assert.deepEqual(body.bounds, { minX: 0, maxX: 0, minY: 0, maxY: 0, width: 0, height: 0 });
});

test("scene：缺 scene 参数返回 400", async () => {
  const res = await get("/v1/stats/scene", env(makeDb()));
  assert.equal(res.status, 400);
  assert.equal((await res.json()).error, "scene_required");
});

test("recent：按时间倒序、limit 生效", async () => {
  const db = makeDb();
  seed(db);
  const body = await (await get("/v1/stats/recent?since=0&limit=3", env(db))).json();
  assert.equal(body.ok, true);
  assert.equal(body.events.length, 3);
  const tsList = body.events.map((e) => e.ts);
  assert.deepEqual(tsList, [...tsList].sort((a, b) => b - a));
  const death = body.events.find((e) => e.type === "death");
  assert.ok(death);
  assert.equal(death.meta.actor, "player2");
});

test("READ_KEY：没带 key 401，带对了 200", async () => {
  const db = makeDb();
  seed(db);
  const e = env(db, { READ_KEY: "secret" });

  const denied = await get("/v1/stats/overview", e);
  assert.equal(denied.status, 401);
  assert.equal((await denied.json()).error, "unauthorized");

  assert.equal((await get("/v1/stats/overview", e, { "x-read-key": "secret" })).status, 200);
  assert.equal((await get("/v1/stats/overview?key=secret", e)).status, 200);
});

test("未知路径 404、DB 未绑定 503", async () => {
  assert.equal((await get("/v1/stats/nope", env(makeDb()))).status, 404);
  const noDb = await handleStats(new URL("https://example.com/v1/stats/overview"), {}, new Request("https://example.com/x"));
  assert.equal(noDb.status, 503);
  assert.equal((await noDb.json()).error, "db_not_bound");
});

test("CORS 允许 X-Admin-Key（否则浏览器清库会被预检挡住）", () => {
  assert.match(CORS_HEADERS["Access-Control-Allow-Headers"], /X-Admin-Key/);
});

const postClear = (body, e, key) => {
  const headers = { "Content-Type": "application/json" };
  if (key !== undefined) headers["x-admin-key"] = key;
  return handleAdminClear(
    new URL("https://example.com/v1/admin/clear"),
    e,
    new Request("https://example.com/v1/admin/clear", { method: "POST", headers, body: JSON.stringify(body) })
  );
};

test("clear：没配 ADMIN_KEY 时一律拒绝（安全默认）", async () => {
  const db = makeDb();
  seed(db);
  const res = await postClear({ all: true }, env(db), "anything");
  assert.equal(res.status, 403);
  assert.equal((await res.json()).error, "admin_key_not_configured");
  const ov = await (await get("/v1/stats/overview?since=0", env(db))).json();
  assert.equal(ov.totals.events, TOTAL_EVENTS);
});

test("clear：密钥错 401", async () => {
  const db = makeDb();
  seed(db);
  const res = await postClear({ all: true }, env(db, { ADMIN_KEY: "s3cret" }), "wrong");
  assert.equal(res.status, 401);
});

test("clear：单关只删这一关", async () => {
  const db = makeDb();
  seed(db);
  const e = env(db, { ADMIN_KEY: "s3cret" });
  const res = await postClear({ scene: "S1" }, e, "s3cret");
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.scope, "scene");
  assert.equal(body.deleted, 2);
  const ov = await (await get("/v1/stats/overview?since=0", e)).json();
  assert.deepEqual(ov.scenes.map((s) => s.scene), ["9101"]);
  assert.equal(ov.totals.events, TOTAL_EVENTS - 2);
});

test("clear：全清", async () => {
  const db = makeDb();
  seed(db);
  const e = env(db, { ADMIN_KEY: "s3cret" });
  const body = await (await postClear({ all: true }, e, "s3cret")).json();
  assert.equal(body.scope, "all");
  assert.equal(body.deleted, TOTAL_EVENTS);
  const ov = await (await get("/v1/stats/overview?since=0", e)).json();
  assert.deepEqual(ov.scenes, []);
  assert.equal(ov.totals.events, 0);
  assert.equal(ov.totals.players, 0);
});

test("clear：既没 scene 也没 all 时 400", async () => {
  const db = makeDb();
  seed(db);
  const res = await postClear({}, env(db, { ADMIN_KEY: "s3cret" }), "s3cret");
  assert.equal(res.status, 400);
  assert.equal((await res.json()).error, "scene_or_all_required");
});

test("Worker：清库路由只接受 POST，GET 返回 405", async () => {
  const res = await worker.fetch(new Request("https://example.com/v1/admin/clear"), env(makeDb()));
  assert.equal(res.status, 405);
  assert.equal(res.headers.get("Allow"), "POST, OPTIONS");
});

test("Worker：POST /v1/admin/clear 走通并返回 JSON", async () => {
  const db = makeDb();
  seed(db);
  const res = await worker.fetch(
    new Request("https://example.com/v1/admin/clear", {
      method: "POST",
      headers: { "Content-Type": "application/json", "x-admin-key": "s3cret" },
      body: JSON.stringify({ scene: "9101" }),
    }),
    env(db, { ADMIN_KEY: "s3cret" })
  );
  assert.equal(res.status, 200);
  assert.equal((await res.json()).deleted, 11); // 15 = 9101 的 11（含 2 条 route）+ S1 的 2 + 两条 session_start
});
