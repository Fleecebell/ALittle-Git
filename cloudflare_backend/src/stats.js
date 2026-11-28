/**
 * 只读统计接口（看板用）+ 数据清除接口。
 *
 * 数据模型里 actor 只有两个值：player1 / player2（P2 是延迟模仿 P1 的那个）。
 * 老数据没有 actor 字段，一律当 player1（COALESCE），这样历史数据不会凭空消失。
 *
 * 路由（GET，只读）：
 *   /v1/stats/overview?since=<ms>      所有关卡 + 总量 + 死因/角色/客户端分布
 *   /v1/stats/scene?scene=9101&since=  单关格子（按 P1/P2 分开）、死亡流水、每次游玩
 *   /v1/stats/compare?since=           横向对比：每关一行，指标齐全
 *   /v1/stats/recent?limit=50          最新事件流水
 *
 * 路由（POST，需要 ADMIN_KEY）：
 *   /v1/admin/clear  {"scene":"9101"}  或 {"all":true}
 */

import { json } from "./http.js";

/** 取 metadata 里的字段 */
const F = (key) => `json_extract(metadata,'$.${key}')`;

/** actor 归一化：没有 actor 的老数据算玩家 1 */
const ACTOR = `COALESCE(json_extract(metadata,'$.actor'),'player1')`;

/**
 * 格子索引统一换算到 0.5 世界单位一格（新客户端的密度）。
 * 老数据的 cell_size 缺省按 1 算，索引自动 ×2，和新数据画在同一张图上不会错位。
 */
const CELL_IDX = (axis) =>
  `CAST(ROUND(CAST(${F("cell_" + axis)} AS REAL) * COALESCE(CAST(${F("cell_size")} AS REAL), 1) * 2) AS INTEGER)`;

/** 看板统一使用 0.5 的格子边长 */
const GRID_SIZE = 0.5;

const num = (v) => (v === null || v === undefined ? 0 : Number(v));
const ratio = (a, b) => (b > 0 ? Math.round((a / b) * 10000) / 10000 : null);

function parseSince(url) {
  const raw = url.searchParams.get("since");
  if (!raw) return 0;
  const n = Number(raw);
  return Number.isFinite(n) && n > 0 ? Math.floor(n) : 0;
}

/** 一次 batch 跑多条 SQL，按顺序取 results */
async function runBatch(env, statements) {
  const prepared = statements.map((s) => env.DB.prepare(s.sql).bind(...(s.args || [])));
  const out = await env.DB.batch(prepared);
  return out.map((r) => (r && r.results) || []);
}

/* ------------------------------------------------------------------ SQL */

const Q_SCENE_PROGRESS = `
SELECT
  ${F("scene")} AS scene,
  MAX(CAST(${F("level_index")} AS INTEGER)) AS level_index,
  SUM(CASE WHEN event_type='level_start' THEN 1 ELSE 0 END) AS starts,
  SUM(CASE WHEN event_type='level_clear' THEN 1 ELSE 0 END) AS clears,
  SUM(CASE WHEN event_type='level_quit'  THEN 1 ELSE 0 END) AS quits,
  SUM(CASE WHEN event_type IN ('level_clear','level_quit')
           THEN CAST(${F("duration_s")} AS REAL) ELSE 0 END) AS playtime_s,
  SUM(CASE WHEN event_type IN ('level_clear','level_quit')
           THEN CAST(${F("duration_s")} AS REAL) ELSE 0 END)
    / NULLIF(SUM(CASE WHEN event_type IN ('level_clear','level_quit') THEN 1 ELSE 0 END), 0) AS avg_duration_s
FROM game_events
WHERE event_type IN ('level_start','level_clear','level_quit')
  AND client_ts >= ?
GROUP BY scene
HAVING scene IS NOT NULL
ORDER BY starts DESC`;

const Q_SCENE_DEATHS = `
SELECT
  ${F("scene")} AS scene,
  COUNT(*) AS deaths,
  SUM(CASE WHEN ${ACTOR}<>'player2' THEN 1 ELSE 0 END) AS p1_deaths,
  SUM(CASE WHEN ${ACTOR}='player2' THEN 1 ELSE 0 END) AS p2_deaths
FROM game_events
WHERE event_type='death' AND client_ts >= ?
GROUP BY scene`;

const Q_SCENE_CELLS = `
SELECT
  ${F("scene")} AS scene,
  COUNT(DISTINCT ${F("cell_x")} || ',' || ${F("cell_y")}) AS cells,
  SUM(CAST(${F("dwell_s")} AS REAL)) AS dwell_s,
  SUM(CAST(${F("jumps")}   AS INTEGER)) AS jumps,
  SUM(CASE WHEN ${ACTOR}='player2' THEN CAST(${F("jumps")} AS INTEGER) ELSE 0 END) AS p2_jumps,
  SUM(CASE WHEN ${ACTOR}='player2' THEN CAST(${F("dwell_s")} AS REAL) ELSE 0 END) AS p2_dwell_s
FROM game_events
WHERE event_type='cell' AND client_ts >= ?
GROUP BY scene`;

const Q_TOTALS = `
SELECT
  COUNT(DISTINCT player_id)  AS players,
  COUNT(DISTINCT session_id) AS sessions,
  COUNT(*)                   AS events,
  MIN(client_ts)             AS first_ts,
  MAX(client_ts)             AS last_ts
FROM game_events
WHERE client_ts >= ?`;

const Q_CAUSES = `
SELECT ${F("cause")} AS cause, COUNT(*) AS n
FROM game_events
WHERE event_type='death' AND client_ts >= ?
GROUP BY cause
ORDER BY n DESC`;

const Q_ACTORS = `
SELECT ${ACTOR} AS actor, COUNT(*) AS n
FROM game_events
WHERE event_type='death' AND client_ts >= ?
GROUP BY actor
ORDER BY n DESC`;

const Q_APPS = `
SELECT DISTINCT
  ${F("platform")}       AS platform,
  ${F("app_version")}    AS version,
  ${F("unity_version")}  AS unity
FROM game_events
WHERE event_type='session_start' AND client_ts >= ?
LIMIT 20`;

const Q_CELLS = `
SELECT
  ${CELL_IDX("x")} AS x,
  ${CELL_IDX("y")} AS y,
  ${ACTOR} AS actor,
  SUM(CAST(${F("dwell_s")} AS REAL))    AS dwell,
  SUM(CAST(${F("jumps")}   AS INTEGER)) AS jumps,
  MIN(CAST(${F("cell_size")} AS REAL))  AS cell_size
FROM game_events
WHERE event_type='cell' AND ${F("scene")} = ? AND client_ts >= ?
GROUP BY x, y, actor`;

// 每趟尝试的路线：客户端把走过的格子拼成 "x,y;x,y;..." 存进 metadata.cells。
// outcome：clear（通关）/ death（死在这一趟）/ quit（中途退出），老数据没有这个字段按 clear 处理
// 每趟尝试一条：通关 clear、死亡 death、中途退出 quit。
// 死亡次数可能远多于通关，所以按 outcome 各自取最近 20 条（窗口函数），
// 否则一关死 47 次就会把唯一那条通关路线挤出 LIMIT。
const Q_ROUTES = `
SELECT ts, actor, attempt, cells, cell_count, duration_s, cell_size, outcome FROM (
  SELECT
    client_ts AS ts,
    ${F("actor")}       AS actor,
    ${F("attempt")}     AS attempt,
    ${F("cells")}       AS cells,
    CAST(${F("cell_count")} AS INTEGER) AS cell_count,
    CAST(${F("duration_s")} AS REAL)    AS duration_s,
    CAST(${F("cell_size")}  AS REAL)    AS cell_size,
    COALESCE(${F("outcome")}, 'clear')  AS outcome,
    ROW_NUMBER() OVER (
      PARTITION BY COALESCE(${F("outcome")}, 'clear')
      ORDER BY client_ts DESC, ${F("actor")}
    ) AS rn
  FROM game_events
  WHERE event_type='route' AND ${F("scene")} = ? AND client_ts >= ?
)
WHERE rn <= 20
ORDER BY ts DESC, actor`;

const Q_DEATH_CELLS = `
SELECT
  ${CELL_IDX("x")} AS x,
  ${CELL_IDX("y")} AS y,
  ${ACTOR} AS actor,
  COUNT(*) AS n,
  GROUP_CONCAT(DISTINCT ${F("cause")}) AS causes
FROM game_events
WHERE event_type='death' AND ${F("scene")} = ? AND client_ts >= ?
GROUP BY x, y, actor`;

const Q_DEATHS = `
SELECT
  client_ts AS ts,
  ${ACTOR} AS actor,
  ${F("cause")} AS cause,
  CAST(${F("x")} AS REAL) AS x,
  CAST(${F("y")} AS REAL) AS y,
  CAST(${F("survived_s")} AS REAL) AS survived_s,
  CAST(${F("deaths_in_level")} AS INTEGER) AS deaths_in_level,
  CAST(${F("jumps_before_death")} AS INTEGER) AS jumps_before_death
FROM game_events
WHERE event_type='death' AND ${F("scene")} = ? AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT 500`;

const Q_RUNS = `
SELECT
  client_ts AS ts,
  event_type AS type,
  CAST(${F("duration_s")} AS REAL)    AS duration_s,
  CAST(${F("deaths")} AS INTEGER)     AS deaths,
  CAST(${F("jumps")} AS INTEGER)      AS jumps,
  CAST(${F("distance")} AS REAL)      AS distance
FROM game_events
WHERE event_type IN ('level_clear','level_quit') AND ${F("scene")} = ? AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT 2000`;

const Q_STARTS = `
SELECT
  client_ts AS ts,
  ${ACTOR} AS actor,
  CAST(${F("attempt")} AS INTEGER) AS attempt,
  CAST(${F("session_time_s")} AS REAL) AS session_time_s
FROM game_events
WHERE event_type='level_start' AND ${F("scene")} = ? AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT 500`;

const Q_RECENT = `
SELECT client_ts AS ts, event_type AS type, session_id AS session, metadata AS meta
FROM game_events
WHERE event_type IN ('death','level_start','level_clear','level_quit','session_start')
  AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT ?`;

/* ------------------------------------------------------------------ 合并 */

function sceneRow(base) {
  const row = {
    scene: base.scene,
    level_index: base.level_index === null || base.level_index === undefined ? null : num(base.level_index),
    starts: num(base.starts),
    clears: num(base.clears),
    quits: num(base.quits),
    playtime_s: Math.round(num(base.playtime_s) * 10) / 10,
    avg_duration_s:
      base.avg_duration_s === null || base.avg_duration_s === undefined
        ? null
        : Math.round(num(base.avg_duration_s) * 10) / 10,
    deaths: 0,
    p1: { deaths: 0, jumps: 0, dwell_s: 0 },
    p2: { deaths: 0, jumps: 0, dwell_s: 0 },
    jumps: 0,
    dwell_s: 0,
    cells: 0,
  };
  row.clear_rate = ratio(row.clears, row.starts);
  row.deaths_per_start = ratio(row.deaths, row.starts);
  return row;
}

function mergeScenes(progress, deathRows, cellRows) {
  const byScene = new Map();
  for (const p of progress) byScene.set(p.scene, sceneRow(p));

  for (const d of deathRows) {
    const row = byScene.get(d.scene);
    if (!row) continue;
    row.deaths += num(d.deaths);
    row.p1.deaths += num(d.p1_deaths);
    row.p2.deaths += num(d.p2_deaths);
  }

  for (const c of cellRows) {
    const row = byScene.get(c.scene);
    if (!row) continue;
    row.cells += num(c.cells);
    row.jumps += num(c.jumps);
    row.dwell_s += num(c.dwell_s);
    row.p2.jumps += num(c.p2_jumps);
    row.p2.dwell_s += num(c.p2_dwell_s);
  }

  for (const row of byScene.values()) {
    row.p1.jumps = row.jumps - row.p2.jumps;
    row.p1.dwell_s = Math.round((row.dwell_s - row.p2.dwell_s) * 10) / 10;
    row.p2.dwell_s = Math.round(row.p2.dwell_s * 10) / 10;
    row.dwell_s = Math.round(row.dwell_s * 10) / 10;
    row.deaths_per_start = ratio(row.deaths, row.starts);
    // 扁平别名：横向对比表和前端表格直接按 key 取值，省得每处都写 .p1.deaths
    row.p1_deaths = row.p1.deaths;
    row.p2_deaths = row.p2.deaths;
    row.p1_jumps = row.p1.jumps;
    row.p2_jumps = row.p2.jumps;
  }

  return [...byScene.values()].sort((a, b) => b.deaths - a.deaths || b.starts - a.starts);
}

/** 把 (x,y,actor) 的格子行 + 死亡行合成每格 {p1:{...}, p2:{...}} */
function mergeCells(cellRows, deathRows) {
  const byCell = new Map();
  const blank = () => ({ deaths: 0, jumps: 0, dwell: 0 });
  const key = (x, y) => `${x},${y}`;

  for (const r of cellRows) {
    const x = num(r.x);
    const y = num(r.y);
    const k = key(x, y);
    let cell = byCell.get(k);
    if (!cell) {
      cell = { x, y, p1: blank(), p2: blank(), causes: [] };
      byCell.set(k, cell);
    }
    const side = r.actor === "player2" ? cell.p2 : cell.p1;
    side.dwell = Math.round((side.dwell + num(r.dwell)) * 10) / 10;
    side.jumps += num(r.jumps);
  }

  for (const r of deathRows) {
    const x = num(r.x);
    const y = num(r.y);
    const k = key(x, y);
    let cell = byCell.get(k);
    if (!cell) {
      cell = { x, y, p1: blank(), p2: blank(), causes: [] };
      byCell.set(k, cell);
    }
    const side = r.actor === "player2" ? cell.p2 : cell.p1;
    side.deaths += num(r.n);
    if (r.causes) {
      for (const c of String(r.causes).split(",")) {
        if (c && !cell.causes.includes(c)) cell.causes.push(c);
      }
    }
  }

  return [...byCell.values()];
}

// "12,3;13,3;13,4" -> [[12,3],[13,3],[13,4]]，顺带把老格子（cell_size=1）换算到 0.5 的索引
function parseRouteCells(raw, size) {
  if (!raw) return [];
  const scale = (Number(size) > 0 ? Number(size) : 1) / GRID_SIZE;
  const out = [];
  for (const pair of String(raw).split(";")) {
    const [x, y] = pair.split(",");
    const nx = Number(x);
    const ny = Number(y);
    if (Number.isFinite(nx) && Number.isFinite(ny)) {
      out.push([Math.round(nx * scale), Math.round(ny * scale)]);
    }
  }
  return out;
}

function boundsOf(cells) {
  if (!cells.length) return { minX: 0, maxX: 0, minY: 0, maxY: 0, width: 0, height: 0 };
  let minX = Infinity;
  let maxX = -Infinity;
  let minY = Infinity;
  let maxY = -Infinity;
  for (const c of cells) {
    if (c.x < minX) minX = c.x;
    if (c.x > maxX) maxX = c.x;
    if (c.y < minY) minY = c.y;
    if (c.y > maxY) maxY = c.y;
  }
  return { minX, maxX, minY, maxY, width: maxX - minX + 1, height: maxY - minY + 1 };
}

/* ------------------------------------------------------------------ 路由 */

export async function handleStats(url, env, request) {
  if (!env || !env.DB) return json({ ok: false, error: "db_not_bound" }, 503);

  // 只读 key（可选）。没配 READ_KEY 就是公开可读。
  const needKey = env.READ_KEY;
  if (needKey) {
    const given =
      (request && request.headers.get("x-read-key")) || url.searchParams.get("key") || "";
    if (given !== needKey) return json({ ok: false, error: "unauthorized" }, 401);
  }

  const since = parseSince(url);
  const path = url.pathname;

  try {
    if (path === "/v1/stats/overview") {
      const [progress, deathRows, cellRows, totals, causes, actors, apps] = await runBatch(env, [
        { sql: Q_SCENE_PROGRESS, args: [since] },
        { sql: Q_SCENE_DEATHS, args: [since] },
        { sql: Q_SCENE_CELLS, args: [since] },
        { sql: Q_TOTALS, args: [since] },
        { sql: Q_CAUSES, args: [since] },
        { sql: Q_ACTORS, args: [since] },
        { sql: Q_APPS, args: [since] },
      ]);

      const scenes = mergeScenes(progress, deathRows, cellRows);
      const t = totals[0] || {};
      const totalDeaths = scenes.reduce((s, x) => s + x.deaths, 0);
      const totalJumps = scenes.reduce((s, x) => s + x.jumps, 0);

      return json({
        ok: true,
        totals: {
          players: num(t.players),
          sessions: num(t.sessions),
          events: num(t.events),
          levels: scenes.length,
          deaths: totalDeaths,
          jumps: totalJumps,
          p1_deaths: scenes.reduce((s, x) => s + x.p1.deaths, 0),
          p2_deaths: scenes.reduce((s, x) => s + x.p2.deaths, 0),
          playtime_s: Math.round(scenes.reduce((s, x) => s + num(x.playtime_s), 0) * 10) / 10,
          first_ts: t.first_ts === null || t.first_ts === undefined ? null : num(t.first_ts),
          last_ts: t.last_ts === null || t.last_ts === undefined ? null : num(t.last_ts),
        },
        scenes,
        causes: causes.map((c) => ({ cause: c.cause, n: num(c.n) })),
        actors: actors.map((a) => ({ actor: a.actor, n: num(a.n) })),
        apps: apps.map((a) => ({ platform: a.platform, version: a.version, unity: a.unity })),
      });
    }

    if (path === "/v1/stats/compare") {
      const [progress, deathRows, cellRows] = await runBatch(env, [
        { sql: Q_SCENE_PROGRESS, args: [since] },
        { sql: Q_SCENE_DEATHS, args: [since] },
        { sql: Q_SCENE_CELLS, args: [since] },
      ]);
      const scenes = mergeScenes(progress, deathRows, cellRows);
      // 每列的最大值，前端画条形图时直接用，省得再算一遍
      const max = {
        starts: Math.max(1, ...scenes.map((s) => s.starts)),
        deaths: Math.max(1, ...scenes.map((s) => s.deaths)),
        deaths_per_start: Math.max(0.0001, ...scenes.map((s) => s.deaths_per_start || 0)),
        clear_rate: Math.max(0.0001, ...scenes.map((s) => s.clear_rate || 0)),
        avg_duration_s: Math.max(0.0001, ...scenes.map((s) => s.avg_duration_s || 0)),
        jumps: Math.max(1, ...scenes.map((s) => s.jumps)),
        dwell_s: Math.max(0.0001, ...scenes.map((s) => s.dwell_s)),
        cells: Math.max(1, ...scenes.map((s) => s.cells)),
      };
      return json({ ok: true, scenes, max });
    }

    if (path === "/v1/stats/scene") {
      const scene = url.searchParams.get("scene");
      if (!scene) return json({ ok: false, error: "scene_required" }, 400);

      const [cellRows, deathCells, deaths, runs, starts, routeRows] = await runBatch(env, [
        { sql: Q_CELLS, args: [scene, since] },
        { sql: Q_DEATH_CELLS, args: [scene, since] },
        { sql: Q_DEATHS, args: [scene, since] },
        { sql: Q_RUNS, args: [scene, since] },
        { sql: Q_STARTS, args: [scene, since] },
        { sql: Q_ROUTES, args: [scene, since] },
      ]);

      const cells = mergeCells(cellRows, deathCells);
      const stats = {
        starts: starts.length,
        clears: runs.filter((r) => r.type === "level_clear").length,
        quits: runs.filter((r) => r.type === "level_quit").length,
        deaths: deaths.length,
        p1_deaths: deaths.filter((d) => d.actor !== "player2").length,
        p2_deaths: deaths.filter((d) => d.actor === "player2").length,
        jumps: cells.reduce((s, c) => s + c.p1.jumps + c.p2.jumps, 0),
        p1_jumps: cells.reduce((s, c) => s + c.p1.jumps, 0),
        p2_jumps: cells.reduce((s, c) => s + c.p2.jumps, 0),
        dwell_s: Math.round(cells.reduce((s, c) => s + c.p1.dwell + c.p2.dwell, 0) * 10) / 10,
        cells: cells.length,
      };
      stats.clear_rate = ratio(stats.clears, stats.starts);
      stats.avg_duration_s = runs.length
        ? Math.round((runs.reduce((s, r) => s + num(r.duration_s), 0) / runs.length) * 10) / 10
        : null;

      return json({
        ok: true,
        scene,
        cells: cells.map((c) => ({
          x: c.x,
          y: c.y,
          p1: c.p1,
          p2: c.p2,
          deaths: c.p1.deaths + c.p2.deaths,
          causes: c.causes,
        })),
        bounds: boundsOf(cells),
        // 格子边长固定报 0.5：索引已经统一换算到这个密度了
        cell_size: GRID_SIZE,
        stats,
        routes: routeRows.map((r) => {
          const cells = parseRouteCells(r.cells, r.cell_size);
          return {
            ts: num(r.ts),
            actor: r.actor === "player2" ? "player2" : "player1",
            attempt: num(r.attempt),
            outcome: ["clear", "death", "quit"].includes(r.outcome) ? r.outcome : "clear",
            // cell_count 以实际解析出来的格子数为准（老数据可能没写这个字段）
            cell_count: cells.length || num(r.cell_count),
            duration_s: num(r.duration_s),
            cells,
          };
        }),
        deaths: deaths.map((d) => ({
          ts: num(d.ts),
          actor: d.actor,
          cause: d.cause,
          x: num(d.x),
          y: num(d.y),
          survived_s: num(d.survived_s),
          deaths_in_level: num(d.deaths_in_level),
          jumps_before_death: num(d.jumps_before_death),
        })),
        runs: runs.map((r) => ({
          ts: num(r.ts),
          type: r.type,
          duration_s: num(r.duration_s),
          deaths: num(r.deaths),
          jumps: num(r.jumps),
          distance: num(r.distance),
        })),
        starts: starts.map((s) => ({
          ts: num(s.ts),
          attempt: num(s.attempt),
          session_time_s: num(s.session_time_s),
        })),
      });
    }

    if (path === "/v1/stats/recent") {
      const limitRaw = Number(url.searchParams.get("limit"));
      const limit = Math.min(200, Math.max(1, Number.isFinite(limitRaw) ? Math.floor(limitRaw) : 50));
      const [rows] = await runBatch(env, [{ sql: Q_RECENT, args: [since, limit] }]);
      return json({
        ok: true,
        events: rows.map((r) => {
          let meta = {};
          try {
            meta = JSON.parse(r.meta || "{}");
          } catch {
            meta = { raw: r.meta };
          }
          return { ts: num(r.ts), type: r.type, session: r.session, meta };
        }),
      });
    }

    return json({ ok: false, error: "not_found" }, 404);
  } catch (err) {
    console.error("stats_failed", err && err.message);
    return json({ ok: false, error: "query_failed", message: String((err && err.message) || err) }, 500);
  }
}

/* ------------------------------------------------------------------ 清库 */

/**
 * 清数据。安全默认：**没配 ADMIN_KEY 就一律拒绝**（避免"忘了配就等于公开可删"）。
 *   POST /v1/admin/clear  {"scene":"9101"}    只清这一关
 *   POST /v1/admin/clear  {"all":true}        全清
 */
export async function handleAdminClear(url, env, request) {
  if (!env || !env.DB) return json({ ok: false, error: "db_not_bound" }, 503);

  if (!env.ADMIN_KEY) {
    return json(
      {
        ok: false,
        error: "admin_key_not_configured",
        hint: "先运行 npx wrangler secret put ADMIN_KEY，再在看板 ⚙ 里填同一个值",
      },
      403
    );
  }

  const given = request.headers.get("x-admin-key") || "";
  if (given !== env.ADMIN_KEY) return json({ ok: false, error: "unauthorized" }, 401);

  let body = {};
  try {
    body = await request.json();
  } catch {
    body = {};
  }

  try {
    if (body && body.all === true) {
      const res = await env.DB.prepare("DELETE FROM game_events").run();
      const deleted = num(res && res.meta && res.meta.changes);
      return json({ ok: true, scope: "all", deleted });
    }

    if (body && typeof body.scene === "string" && body.scene) {
      const res = await env.DB.prepare(
        `DELETE FROM game_events WHERE ${F("scene")} = ?`
      )
        .bind(body.scene)
        .run();
      const deleted = num(res && res.meta && res.meta.changes);
      return json({ ok: true, scope: "scene", scene: body.scene, deleted });
    }

    return json({ ok: false, error: "scene_or_all_required" }, 400);
  } catch (err) {
    console.error("clear_failed", err && err.message);
    return json({ ok: false, error: "clear_failed", message: String((err && err.message) || err) }, 500);
  }
}
