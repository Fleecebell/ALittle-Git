/**
 * 用 Node 内置的 SQLite (node:sqlite) 验证 schema.sql 与 queries.sql 真的能跑：
 *   1. schema.sql 可重复执行（幂等）
 *   2. Worker 的 INSERT 语句字段数/顺序与表结构一致
 *   3. queries.sql 里每条查询都能执行，且关键聚合结果符合预期
 * 这层测试不需要装 wrangler、不需要联网。
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const schemaSql = readFileSync(join(root, "schema.sql"), "utf8");
const queriesSql = readFileSync(join(root, "queries.sql"), "utf8");

// 与 src/index.js 保持一致（改一处记得改另一处）
const INSERT_SQL =
  "INSERT INTO game_events (player_id, session_id, event_type, metadata, client_ts, server_ts) " +
  "VALUES (?, ?, ?, ?, ?, ?)";

const P = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const S = "s_20261109_120000_1a2b";
const BASE_TS = 1762650000000; // 2025-11-09 前后，任意固定值

/** 把 queries.sql 按分号切成一条条语句，丢掉纯注释块 */
function splitStatements(sql) {
  return sql
    .split(";")
    .map((chunk) => chunk.trim())
    .filter((chunk) => chunk.length > 0 && /\b(SELECT|WITH)\b/i.test(chunk));
}

function findByMarker(marker) {
  const hit = splitStatements(queriesSql).filter((sql) => sql.includes(marker));
  assert.equal(hit.length, 1, `queries.sql 中应恰好有一条包含 "${marker}" 的查询`);
  return hit[0];
}

function seed() {
  const db = new DatabaseSync(":memory:");
  db.exec(schemaSql);
  db.exec(schemaSql); // 幂等：重复执行不应报错

  const insert = db.prepare(INSERT_SQL);
  const push = (type, ts, meta) =>
    insert.run(P, S, type, JSON.stringify(meta), ts, ts + 5);

  // 两次进入 9101，只通关一次
  push("level_start", BASE_TS, { scene: "9101", attempt: 1 });
  push("level_start", BASE_TS + 60000, { scene: "9101", attempt: 2 });
  push("level_clear", BASE_TS + 60000 + 30000, {
    scene: "9101",
    attempt: 2,
    duration_s: 30,
    deaths: 0,
    jumps: 7,
    jump_fails: 3,
  });

  // 3 次死亡：2 次岩浆、1 次夹死
  push("death", BASE_TS + 10000, {
    scene: "9101", attempt: 1, cause: "hazard:lava", actor: "player1",
    x: 10.2, y: 0.5, cell_x: 10, cell_y: 0, survived_s: 10,
  });
  push("death", BASE_TS + 20000, {
    scene: "9101", attempt: 1, cause: "hazard:lava", actor: "player1",
    x: 10.8, y: 0.4, cell_x: 10, cell_y: 0, survived_s: 20,
  });
  push("death", BASE_TS + 40000, {
    scene: "9101", attempt: 1, cause: "crush", actor: "player2",
    x: 20.0, y: 3.0, cell_x: 20, cell_y: 3, survived_s: 40,
  });

  // 两个格子的驻留/跳跃增量，故意分两行模拟"每 60s 增量上报"
  push("cell", BASE_TS + 30000, {
    scene: "9101", attempt: 1, cell_x: 10, cell_y: 0, x: 10.5, y: 0.5,
    dwell_s: 6, idle_s: 3, jumps: 3, jump_fails: 2, part: 1,
  });
  push("cell", BASE_TS + 60000, {
    scene: "9101", attempt: 1, cell_x: 10, cell_y: 0, x: 10.5, y: 0.5,
    dwell_s: 6, idle_s: 3, jumps: 2, jump_fails: 1, part: 2,
  });
  push("cell", BASE_TS + 60000, {
    scene: "9101", attempt: 1, cell_x: 11, cell_y: 0, x: 11.5, y: 0.5,
    dwell_s: 3, idle_s: 0, jumps: 2, jump_fails: 0, part: 2,
  });

  return db;
}

test("schema.sql 可重复执行，字段与首页索引齐全", () => {
  const db = seed();
  const cols = db.prepare("PRAGMA table_info(game_events)").all().map((c) => c.name);
  assert.deepEqual(cols, [
    "id", "player_id", "session_id", "event_type", "metadata", "client_ts", "server_ts",
  ]);

  const indexes = db
    .prepare("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='game_events'")
    .all()
    .map((r) => r.name);
  assert.ok(indexes.includes("idx_events_type_ts"));
  assert.ok(indexes.includes("idx_events_session_ts"));
  assert.ok(indexes.includes("idx_events_player_ts"));
});

test("Worker 的 INSERT 语句与表结构匹配（7 列 6 个占位符）", () => {
  const db = seed();
  const stmt = db.prepare(INSERT_SQL);
  assert.doesNotThrow(() =>
    stmt.run(P, S, "session_start", "{}", BASE_TS, BASE_TS)
  );
});

test("queries.sql 每条语句都能执行，且返回列", () => {
  const db = seed();
  for (const sql of splitStatements(queriesSql)) {
    const stmt = db.prepare(sql);
    const rows = stmt.all();
    const firstLine = sql.split("\n").find((l) => l.trim().startsWith("SELECT")).trim();
    assert.ok(Array.isArray(rows), `查询应返回数组: ${firstLine}`);
  }
});

test("死亡统计：每关死亡数 / 热点格子 / 死因分布", () => {
  const db = seed();

  const perScene = db.prepare(findByMarker("AS deaths_per_session")).all();
  assert.equal(perScene.length, 1);
  assert.equal(perScene[0].scene, "9101");
  assert.equal(perScene[0].deaths, 3);
  assert.equal(perScene[0].deaths_per_session, 3);

  const hotspots = db.prepare(findByMarker("GROUP_CONCAT(DISTINCT")).all();
  assert.equal(hotspots[0].cell_x, 10, "死亡最多的格子应排第一");
  assert.equal(hotspots[0].deaths, 2);
  assert.match(hotspots[0].causes, /hazard:lava/);

  const causes = db.prepare(findByMarker("GROUP BY cause")).all();
  const map = Object.fromEntries(causes.map((r) => [r.cause, r.deaths]));
  assert.deepEqual(map, { "hazard:lava": 2, crush: 1 });
});

test("空格统计：同一格子两行增量会被 SUM，失败率计算正确", () => {
  const db = seed();
  const rows = db.prepare(findByMarker("AS jumps")).all();
  const cell10 = rows.find((r) => r.cell_x === 10);
  assert.equal(cell10.jumps, 5, "3 + 2 应合并");
  assert.equal(cell10.jump_fails, 3, "2 + 1 应合并");
  assert.equal(cell10.fail_rate, 0.375, "3 / (5+3)");
});

test("驻留统计：dwell 合并、idle_ratio 正确，且过滤短驻留格子", () => {
  const db = seed();
  const rows = db.prepare(findByMarker("AS idle_ratio")).all();
  assert.equal(rows.length, 1, "只有 dwell > 5 的格子入选");
  assert.equal(rows[0].cell_x, 10);
  assert.equal(rows[0].dwell_s, 12);
  assert.equal(rows[0].idle_s, 6);
  assert.equal(rows[0].idle_ratio, 0.5);
});

test("关卡时长与通关率", () => {
  const db = seed();

  const times = db.prepare(findByMarker("AS avg_seconds")).all();
  assert.equal(times[0].clears, 1);
  assert.equal(times[0].avg_seconds, 30);
  assert.equal(times[0].avg_deaths, 0);

  const rates = db.prepare(findByMarker("AS clear_rate")).all();
  assert.equal(rates[0].enters, 2);
  assert.equal(rates[0].clears, 1);
  assert.equal(rates[0].clear_rate, 0.5);
});

test("会话时间线与玩家概览", () => {
  const db = seed();

  const timeline = db
    .prepare(findByMarker("WHERE session_id = 'REPLACE_WITH_SESSION_ID'").replace("REPLACE_WITH_SESSION_ID", S))
    .all();
  assert.ok(timeline.length >= 8);
  assert.ok(timeline.every((r) => r.event_type && r.at_utc));

  const players = db.prepare(findByMarker("AS first_seen")).all();
  assert.equal(players[0].player_id, P);
  assert.equal(players[0].players_sessions ?? players[0].sessions, 1);
});
