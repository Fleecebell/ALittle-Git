/**
 * 只读查询端：把 D1 里的事件表按"人能看懂的维度"聚合出来。
 * 所有 SQL 都在这里，dashboard 只负责画图。
 *
 * 路由（都是 GET，都可带 ?since=<epoch_ms> 只看某时间之后的数据）：
 *   /v1/stats/overview  全局概览：每个关卡的死/跳/时长/通关率 + 总量 + 死因分布
 *   /v1/stats/scene     单个关卡的全量明细：格子热力、死亡点、每次游玩时长、进关记录
 *   /v1/stats/recent    最近发生的事件流水（排查"到底有没有在收数据"）
 *
 * 鉴权：wrangler.toml 里配了 READ_KEY 才校验，取 ?key= 或 X-Read-Key 头。
 * 不配 = 谁都能读（测试期方便，正式上线建议配上）。
 */

import { json } from "./http.js";

/** json_extract 简写，少写点重复字符串 */
const F = (key) => `json_extract(metadata,'$.${key}')`;

const DEATH_EV = "event_type='death'";
const CELL_EV = "event_type='cell'";
const CLOSE_EV = "event_type IN ('level_clear','level_quit')";

// ---------------------------------------------------------------- 全局概览

const Q_SCENE_PROGRESS = `
SELECT
  ${F("scene")} AS scene,
  MAX(${F("level_index")}) AS level_index,
  SUM(CASE WHEN event_type='level_start' THEN 1 ELSE 0 END) AS starts,
  SUM(CASE WHEN event_type='level_clear' THEN 1 ELSE 0 END) AS clears,
  SUM(CASE WHEN event_type='level_quit' THEN 1 ELSE 0 END) AS quits,
  SUM(CASE WHEN ${CLOSE_EV} THEN ${F("duration_s")} ELSE 0 END) AS playtime_s,
  AVG(CASE WHEN ${CLOSE_EV} THEN ${F("duration_s")} END) AS avg_duration_s
FROM game_events
WHERE event_type IN ('level_start','level_clear','level_quit') AND client_ts >= ?
GROUP BY scene`;

const Q_SCENE_DEATHS = `
SELECT ${F("scene")} AS scene, COUNT(*) AS deaths
FROM game_events
WHERE ${DEATH_EV} AND client_ts >= ?
GROUP BY scene`;

const Q_SCENE_CELLS = `
SELECT
  ${F("scene")} AS scene,
  COUNT(DISTINCT ${F("cell_x")} || ',' || ${F("cell_y")}) AS cells,
  SUM(${F("jumps")}) AS jumps,
  SUM(${F("jump_fails")}) AS jump_fails,
  SUM(${F("dwell_s")}) AS dwell_s,
  SUM(${F("idle_s")}) AS idle_s
FROM game_events
WHERE ${CELL_EV} AND client_ts >= ?
GROUP BY scene`;

const Q_TOTALS = `
SELECT
  COUNT(*) AS events,
  COUNT(DISTINCT player_id) AS players,
  COUNT(DISTINCT session_id) AS sessions,
  MIN(client_ts) AS first_ts,
  MAX(client_ts) AS last_ts
FROM game_events
WHERE client_ts >= ?`;

const Q_CAUSES = `
SELECT ${F("cause")} AS cause, COUNT(*) AS n
FROM game_events
WHERE ${DEATH_EV} AND client_ts >= ?
GROUP BY cause
ORDER BY n DESC`;

const Q_ACTORS = `
SELECT ${F("actor")} AS actor, COUNT(*) AS n
FROM game_events
WHERE ${DEATH_EV} AND client_ts >= ?
GROUP BY actor`;

const Q_APPS = `
SELECT ${F("platform")} AS platform, ${F("app_version")} AS version,
       ${F("unity_version")} AS unity, COUNT(*) AS n
FROM game_events
WHERE event_type='session_start' AND client_ts >= ?
GROUP BY platform, version, unity
ORDER BY n DESC`;

// ---------------------------------------------------------------- 单关明细

const Q_SCENE_CELLS_DETAIL = `
SELECT
  ${F("cell_x")} AS cx,
  ${F("cell_y")} AS cy,
  SUM(${F("dwell_s")}) AS dwell,
  SUM(${F("idle_s")}) AS idle,
  SUM(${F("jumps")}) AS jumps,
  SUM(${F("jump_fails")}) AS fails
FROM game_events
WHERE ${CELL_EV} AND ${F("scene")} = ? AND client_ts >= ?
GROUP BY cx, cy`;

const Q_SCENE_DEATH_CELLS = `
SELECT
  ${F("cell_x")} AS cx,
  ${F("cell_y")} AS cy,
  COUNT(*) AS deaths,
  GROUP_CONCAT(DISTINCT ${F("cause")}) AS causes
FROM game_events
WHERE ${DEATH_EV} AND ${F("scene")} = ? AND client_ts >= ?
GROUP BY cx, cy`;

const Q_SCENE_DEATH_LIST = `
SELECT
  client_ts AS ts, ${F("cause")} AS cause, ${F("actor")} AS actor,
  ${F("x")} AS x, ${F("y")} AS y, ${F("survived_s")} AS survived_s,
  ${F("jumps_before_death")} AS jumps_before_death,
  ${F("deaths_in_level")} AS deaths_in_level,
  ${F("attempt")} AS attempt
FROM game_events
WHERE ${DEATH_EV} AND ${F("scene")} = ? AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT 500`;

const Q_SCENE_RUNS = `
SELECT
  client_ts AS ts, event_type, ${F("duration_s")} AS duration_s,
  ${F("deaths")} AS deaths, ${F("jumps")} AS jumps,
  ${F("jump_fails")} AS jump_fails, ${F("distance")} AS distance,
  ${F("attempt")} AS attempt, session_id
FROM game_events
WHERE ${CLOSE_EV} AND ${F("scene")} = ? AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT 2000`;

const Q_SCENE_STARTS = `
SELECT client_ts AS ts, session_id, ${F("attempt")} AS attempt,
       ${F("session_time_s")} AS session_time_s
FROM game_events
WHERE event_type='level_start' AND ${F("scene")} = ? AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT 500`;

// ---------------------------------------------------------------- 最近流水

const Q_RECENT = `
SELECT client_ts AS ts, event_type, player_id, session_id, metadata
FROM game_events
WHERE event_type IN ('death','level_start','level_clear','level_quit','session_start')
  AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT ?`;

// ---------------------------------------------------------------- 工具

function parseSince(raw) {
  if (raw === null || raw === "") return 0;
  const n = Number(raw);
  if (!Number.isFinite(n) || n < 0) return 0;
  return Math.floor(n);
}

/** 一条 batch 往返取回多条 SELECT 的结果；D1 的 batch 会按顺序返回 */
async function runBatch(env, statements) {
  const prepared = statements.map(({ sql, args }) => {
    const stmt = env.DB.prepare(sql);
    return args && args.length ? stmt.bind(...args) : stmt;
  });
  const results = await env.DB.batch(prepared);
  return results.map((r) => (r && Array.isArray(r.results) ? r.results : []));
}

const num = (v) => (typeof v === "number" && Number.isFinite(v) ? v : 0);
const ratio = (a, b) => (b > 0 ? Math.round((a / b) * 10000) / 10000 : null);

function gridKey(cx, cy) {
  return `${cx},${cy}`;
}

// ---------------------------------------------------------------- 路由

export async function handleStats(url, env, request) {
  if (!env || !env.DB) {
    return json({ ok: false, error: "db_not_bound" }, 503);
  }

  // 可选读鉴权
  if (env.READ_KEY) {
    const provided =
      (request && request.headers.get("x-read-key")) ||
      url.searchParams.get("key") ||
      "";
    if (provided !== env.READ_KEY) {
      return json({ ok: false, error: "unauthorized" }, 401);
    }
  }

  const since = parseSince(url.searchParams.get("since"));
  const path = url.pathname.replace(/\/+$/, "");

  try {
    if (path === "/v1/stats/overview") return json(await overview(env, since));
    if (path === "/v1/stats/scene") return json(await scene(env, url, since));
    if (path === "/v1/stats/recent") return json(await recent(env, url, since));
    return json({ ok: false, error: "not_found" }, 404);
  } catch (err) {
    console.error("stats_query_failed", err && err.message);
    return json({ ok: false, error: "query_failed", message: String(err && err.message) }, 500);
  }
}

// ---------------------------------------------------------------- 概览实现

async function overview(env, since) {
  const args = [since];
  const [progress, deaths, cells, totals, causes, actors, apps] = await runBatch(env, [
    { sql: Q_SCENE_PROGRESS, args },
    { sql: Q_SCENE_DEATHS, args },
    { sql: Q_SCENE_CELLS, args },
    { sql: Q_TOTALS, args },
    { sql: Q_CAUSES, args },
    { sql: Q_ACTORS, args },
    { sql: Q_APPS, args },
  ]);

  const byScene = new Map();
  const ensure = (name) => {
    const key = name === null || name === undefined ? "(未知场景)" : String(name);
    let row = byScene.get(key);
    if (!row) {
      row = {
        scene: key,
        level_index: null,
        starts: 0,
        clears: 0,
        quits: 0,
        deaths: 0,
        jumps: 0,
        jump_fails: 0,
        dwell_s: 0,
        idle_s: 0,
        cells: 0,
        playtime_s: 0,
        avg_duration_s: null,
      };
      byScene.set(key, row);
    }
    return row;
  };

  for (const r of progress) {
    const row = ensure(r.scene);
    row.level_index = r.level_index === null ? null : num(r.level_index);
    row.starts = num(r.starts);
    row.clears = num(r.clears);
    row.quits = num(r.quits);
    row.playtime_s = num(r.playtime_s);
    row.avg_duration_s = r.avg_duration_s === null ? null : num(r.avg_duration_s);
  }
  for (const r of deaths) ensure(r.scene).deaths = num(r.deaths);
  for (const r of cells) {
    const row = ensure(r.scene);
    row.cells = num(r.cells);
    row.jumps = num(r.jumps);
    row.jump_fails = num(r.jump_fails);
    row.dwell_s = num(r.dwell_s);
    row.idle_s = num(r.idle_s);
  }

  const scenes = [...byScene.values()].map((r) => ({
    ...r,
    clear_rate: ratio(r.clears, r.starts),
    deaths_per_start: ratio(r.deaths, r.starts),
    jump_fail_rate: ratio(r.jump_fails, r.jumps + r.jump_fails),
    idle_ratio: ratio(r.idle_s, r.dwell_s),
  }));

  // 有数据但没通关记录的场景（比如测试数据）也按关卡序号排在一起
  scenes.sort((a, b) => {
    const ai = a.level_index === null ? 9999 : a.level_index;
    const bi = b.level_index === null ? 9999 : b.level_index;
    if (ai !== bi) return ai - bi;
    return a.scene.localeCompare(b.scene);
  });

  const t = totals[0] || {};
  return {
    ok: true,
    since,
    totals: {
      events: num(t.events),
      players: num(t.players),
      sessions: num(t.sessions),
      first_ts: t.first_ts === undefined ? null : t.first_ts,
      last_ts: t.last_ts === undefined ? null : t.last_ts,
      levels: scenes.length,
      deaths: scenes.reduce((s, r) => s + r.deaths, 0),
      jumps: scenes.reduce((s, r) => s + r.jumps, 0),
      playtime_s: Math.round(scenes.reduce((s, r) => s + r.playtime_s, 0) * 10) / 10,
    },
    scenes,
    causes: causes.map((r) => ({ cause: r.cause || "(未记录)", n: num(r.n) })),
    actors: actors.map((r) => ({ actor: r.actor || "(未知)", n: num(r.n) })),
    apps: apps.map((r) => ({
      platform: r.platform || "",
      version: r.version || "",
      unity: r.unity || "",
      n: num(r.n),
    })),
  };
}

// ---------------------------------------------------------------- 单关实现

async function scene(env, url, since) {
  const name = url.searchParams.get("scene");
  if (!name) return { ok: false, error: "scene_required" };

  const args = [name, since];
  const [cellRows, deathCellRows, deathRows, runs, starts] = await runBatch(env, [
    { sql: Q_SCENE_CELLS_DETAIL, args },
    { sql: Q_SCENE_DEATH_CELLS, args },
    { sql: Q_SCENE_DEATH_LIST, args },
    { sql: Q_SCENE_RUNS, args },
    { sql: Q_SCENE_STARTS, args },
  ]);

  // 把一个格子上的"走过/跳过/死过"合成一条，前端画热力图直接取字段
  const gridMap = new Map();
  const cellOf = (cx, cy) => {
    const key = gridKey(cx, cy);
    let c = gridMap.get(key);
    if (!c) {
      c = { x: cx, y: cy, deaths: 0, causes: [], jumps: 0, fails: 0, dwell: 0, idle: 0 };
      gridMap.set(key, c);
    }
    return c;
  };

  for (const r of cellRows) {
    const cx = num(r.cx);
    const cy = num(r.cy);
    const c = cellOf(cx, cy);
    c.dwell = Math.round(num(r.dwell) * 100) / 100;
    c.idle = Math.round(num(r.idle) * 100) / 100;
    c.jumps = num(r.jumps);
    c.fails = num(r.fails);
  }
  for (const r of deathCellRows) {
    const cx = num(r.cx);
    const cy = num(r.cy);
    const c = cellOf(cx, cy);
    c.deaths = num(r.deaths);
    c.causes = r.causes ? String(r.causes).split(",") : [];
  }

  const cells = [...gridMap.values()].map((c) => ({
    ...c,
    fail_rate: c.jumps + c.fails > 0 ? ratio(c.fails, c.jumps + c.fails) : null,
    idle_ratio: c.dwell > 0 ? ratio(c.idle, c.dwell) : null,
  }));

  let minX = null, maxX = null, minY = null, maxY = null;
  for (const c of cells) {
    if (minX === null || c.x < minX) minX = c.x;
    if (maxX === null || c.x > maxX) maxX = c.x;
    if (minY === null || c.y < minY) minY = c.y;
    if (maxY === null || c.y > maxY) maxY = c.y;
  }

  const parsedRuns = runs.map((r) => ({
    ts: r.ts,
    type: r.event_type,
    duration_s: num(r.duration_s),
    deaths: num(r.deaths),
    jumps: num(r.jumps),
    jump_fails: num(r.jump_fails),
    distance: Math.round(num(r.distance) * 10) / 10,
    attempt: num(r.attempt),
    session_id: r.session_id,
  }));

  const clearCount = parsedRuns.filter((r) => r.type === "level_clear").length;
  const quitCount = parsedRuns.filter((r) => r.type === "level_quit").length;
  const totalDuration = parsedRuns.reduce((s, r) => s + r.duration_s, 0);

  const startRows = starts.map((r) => ({
    ts: r.ts,
    attempt: num(r.attempt),
    session_time_s: num(r.session_time_s),
    session_id: r.session_id,
  }));

  return {
    ok: true,
    since,
    scene: name,
    level_index: null,
    stats: {
      starts: startRows.length,
      clears: clearCount,
      quits: quitCount,
      clear_rate: ratio(clearCount, Math.max(clearCount + quitCount, startRows.length)),
      playtime_s: Math.round(totalDuration * 10) / 10,
      avg_duration_s: parsedRuns.length ? Math.round((totalDuration / parsedRuns.length) * 10) / 10 : null,
      deaths: cells.reduce((s, c) => s + c.deaths, 0),
      jumps: cells.reduce((s, c) => s + c.jumps, 0),
      jump_fails: cells.reduce((s, c) => s + c.fails, 0),
      cells: cells.length,
    },
    bounds:
      minX === null
        ? { minX: 0, maxX: 0, minY: 0, maxY: 0 }
        : { minX, maxX, minY, maxY, width: maxX - minX + 1, height: maxY - minY + 1 },
    cells,
    deaths: deathRows.map((r) => ({
      ts: r.ts,
      cause: r.cause || "(未记录)",
      actor: r.actor || "",
      x: num(r.x),
      y: num(r.y),
      survived_s: Math.round(num(r.survived_s) * 10) / 10,
      jumps_before_death: num(r.jumps_before_death),
      deaths_in_level: num(r.deaths_in_level),
      attempt: num(r.attempt),
    })),
    runs: parsedRuns,
    starts: startRows,
  };
}

// ---------------------------------------------------------------- 流水实现

async function recent(env, url, since) {
  let limit = Number(url.searchParams.get("limit") || 60);
  if (!Number.isFinite(limit)) limit = 60;
  limit = Math.max(1, Math.min(200, Math.floor(limit)));

  const [rows] = await runBatch(env, [{ sql: Q_RECENT, args: [since, limit] }]);
  return {
    ok: true,
    since,
    events: rows.map((r) => {
      let meta = {};
      try {
        meta = typeof r.metadata === "string" ? JSON.parse(r.metadata) : r.metadata || {};
      } catch {
        meta = {};
      }
      return {
        ts: r.ts,
        type: r.event_type,
        player_id: r.player_id,
        session_id: r.session_id,
        meta,
      };
    }),
  };
}
