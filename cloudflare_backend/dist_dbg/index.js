var __defProp = Object.defineProperty;
var __name = (target, value) => __defProp(target, "name", { value, configurable: true });

// src/http.js
var CORS_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, PUT, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, X-Telemetry-Key, X-Read-Key, X-Admin-Key",
  "Access-Control-Max-Age": "86400"
};
function json(body, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      ...CORS_HEADERS,
      ...extraHeaders
    }
  });
}
__name(json, "json");
function html(body, status = 200) {
  return new Response(body, {
    status,
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      "Cache-Control": "no-store",
      ...CORS_HEADERS
    }
  });
}
__name(html, "html");

// src/stats.js
var F = /* @__PURE__ */ __name((key) => `json_extract(metadata,'$.${key}')`, "F");
var ACTOR = `COALESCE(json_extract(metadata,'$.actor'),'player1')`;
var CELL_IDX = /* @__PURE__ */ __name((axis) => `CAST(ROUND(CAST(${F("cell_" + axis)} AS REAL) * COALESCE(CAST(${F("cell_size")} AS REAL), 1) * 2) AS INTEGER)`, "CELL_IDX");
var GRID_SIZE = 0.5;
var num = /* @__PURE__ */ __name((v) => v === null || v === void 0 ? 0 : Number(v), "num");
var ratio = /* @__PURE__ */ __name((a, b) => b > 0 ? Math.round(a / b * 1e4) / 1e4 : null, "ratio");
function parseSince(url) {
  const raw = url.searchParams.get("since");
  if (!raw) return 0;
  const n = Number(raw);
  return Number.isFinite(n) && n > 0 ? Math.floor(n) : 0;
}
__name(parseSince, "parseSince");
async function runBatch(env, statements) {
  const prepared = statements.map((s) => env.DB.prepare(s.sql).bind(...s.args || []));
  const out = await env.DB.batch(prepared);
  return out.map((r) => r && r.results || []);
}
__name(runBatch, "runBatch");
var Q_SCENE_PROGRESS = `
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
var Q_SCENE_DEATHS = `
SELECT
  ${F("scene")} AS scene,
  COUNT(*) AS deaths,
  SUM(CASE WHEN ${ACTOR}<>'player2' THEN 1 ELSE 0 END) AS p1_deaths,
  SUM(CASE WHEN ${ACTOR}='player2' THEN 1 ELSE 0 END) AS p2_deaths
FROM game_events
WHERE event_type='death' AND client_ts >= ?
GROUP BY scene`;
var Q_SCENE_CELLS = `
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
var Q_TOTALS = `
SELECT
  COUNT(DISTINCT player_id)  AS players,
  COUNT(DISTINCT session_id) AS sessions,
  COUNT(*)                   AS events,
  MIN(client_ts)             AS first_ts,
  MAX(client_ts)             AS last_ts
FROM game_events
WHERE client_ts >= ?`;
var Q_CAUSES = `
SELECT ${F("cause")} AS cause, COUNT(*) AS n
FROM game_events
WHERE event_type='death' AND client_ts >= ?
GROUP BY cause
ORDER BY n DESC`;
var Q_ACTORS = `
SELECT ${ACTOR} AS actor, COUNT(*) AS n
FROM game_events
WHERE event_type='death' AND client_ts >= ?
GROUP BY actor
ORDER BY n DESC`;
var Q_APPS = `
SELECT DISTINCT
  ${F("platform")}       AS platform,
  ${F("app_version")}    AS version,
  ${F("unity_version")}  AS unity
FROM game_events
WHERE event_type='session_start' AND client_ts >= ?
LIMIT 20`;
var Q_CELLS = `
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
var Q_ROUTES = `
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
var Q_DEATH_CELLS = `
SELECT
  ${CELL_IDX("x")} AS x,
  ${CELL_IDX("y")} AS y,
  ${ACTOR} AS actor,
  COUNT(*) AS n,
  GROUP_CONCAT(DISTINCT ${F("cause")}) AS causes
FROM game_events
WHERE event_type='death' AND ${F("scene")} = ? AND client_ts >= ?
GROUP BY x, y, actor`;
var Q_DEATHS = `
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
var Q_RUNS = `
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
var Q_STARTS = `
SELECT
  client_ts AS ts,
  ${ACTOR} AS actor,
  CAST(${F("attempt")} AS INTEGER) AS attempt,
  CAST(${F("session_time_s")} AS REAL) AS session_time_s
FROM game_events
WHERE event_type='level_start' AND ${F("scene")} = ? AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT 500`;
var Q_RECENT = `
SELECT client_ts AS ts, event_type AS type, session_id AS session, metadata AS meta
FROM game_events
WHERE event_type IN ('death','level_start','level_clear','level_quit','session_start')
  AND client_ts >= ?
ORDER BY client_ts DESC
LIMIT ?`;
function sceneRow(base) {
  const row = {
    scene: base.scene,
    level_index: base.level_index === null || base.level_index === void 0 ? null : num(base.level_index),
    starts: num(base.starts),
    clears: num(base.clears),
    quits: num(base.quits),
    playtime_s: Math.round(num(base.playtime_s) * 10) / 10,
    avg_duration_s: base.avg_duration_s === null || base.avg_duration_s === void 0 ? null : Math.round(num(base.avg_duration_s) * 10) / 10,
    deaths: 0,
    p1: { deaths: 0, jumps: 0, dwell_s: 0 },
    p2: { deaths: 0, jumps: 0, dwell_s: 0 },
    jumps: 0,
    dwell_s: 0,
    cells: 0
  };
  row.clear_rate = ratio(row.clears, row.starts);
  row.deaths_per_start = ratio(row.deaths, row.starts);
  return row;
}
__name(sceneRow, "sceneRow");
function mergeScenes(progress, deathRows, cellRows) {
  const byScene = /* @__PURE__ */ new Map();
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
    row.p1_deaths = row.p1.deaths;
    row.p2_deaths = row.p2.deaths;
    row.p1_jumps = row.p1.jumps;
    row.p2_jumps = row.p2.jumps;
  }
  return [...byScene.values()].sort((a, b) => b.deaths - a.deaths || b.starts - a.starts);
}
__name(mergeScenes, "mergeScenes");
function mergeCells(cellRows, deathRows) {
  const byCell = /* @__PURE__ */ new Map();
  const blank = /* @__PURE__ */ __name(() => ({ deaths: 0, jumps: 0, dwell: 0 }), "blank");
  const key = /* @__PURE__ */ __name((x, y) => `${x},${y}`, "key");
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
__name(mergeCells, "mergeCells");
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
__name(parseRouteCells, "parseRouteCells");
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
__name(boundsOf, "boundsOf");
async function handleStats(url, env, request) {
  if (!env || !env.DB) return json({ ok: false, error: "db_not_bound" }, 503);
  const needKey = env.READ_KEY;
  if (needKey) {
    const given = request && request.headers.get("x-read-key") || url.searchParams.get("key") || "";
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
        { sql: Q_APPS, args: [since] }
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
          first_ts: t.first_ts === null || t.first_ts === void 0 ? null : num(t.first_ts),
          last_ts: t.last_ts === null || t.last_ts === void 0 ? null : num(t.last_ts)
        },
        scenes,
        causes: causes.map((c) => ({ cause: c.cause, n: num(c.n) })),
        actors: actors.map((a) => ({ actor: a.actor, n: num(a.n) })),
        apps: apps.map((a) => ({ platform: a.platform, version: a.version, unity: a.unity }))
      });
    }
    if (path === "/v1/stats/compare") {
      const [progress, deathRows, cellRows] = await runBatch(env, [
        { sql: Q_SCENE_PROGRESS, args: [since] },
        { sql: Q_SCENE_DEATHS, args: [since] },
        { sql: Q_SCENE_CELLS, args: [since] }
      ]);
      const scenes = mergeScenes(progress, deathRows, cellRows);
      const max = {
        starts: Math.max(1, ...scenes.map((s) => s.starts)),
        deaths: Math.max(1, ...scenes.map((s) => s.deaths)),
        deaths_per_start: Math.max(1e-4, ...scenes.map((s) => s.deaths_per_start || 0)),
        clear_rate: Math.max(1e-4, ...scenes.map((s) => s.clear_rate || 0)),
        avg_duration_s: Math.max(1e-4, ...scenes.map((s) => s.avg_duration_s || 0)),
        jumps: Math.max(1, ...scenes.map((s) => s.jumps)),
        dwell_s: Math.max(1e-4, ...scenes.map((s) => s.dwell_s)),
        cells: Math.max(1, ...scenes.map((s) => s.cells))
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
        { sql: Q_ROUTES, args: [scene, since] }
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
        cells: cells.length
      };
      stats.clear_rate = ratio(stats.clears, stats.starts);
      stats.avg_duration_s = runs.length ? Math.round(runs.reduce((s, r) => s + num(r.duration_s), 0) / runs.length * 10) / 10 : null;
      return json({
        ok: true,
        scene,
        cells: cells.map((c) => ({
          x: c.x,
          y: c.y,
          p1: c.p1,
          p2: c.p2,
          deaths: c.p1.deaths + c.p2.deaths,
          causes: c.causes
        })),
        bounds: boundsOf(cells),
        // 格子边长固定报 0.5：索引已经统一换算到这个密度了
        cell_size: GRID_SIZE,
        stats,
        routes: routeRows.map((r) => {
          const cells2 = parseRouteCells(r.cells, r.cell_size);
          return {
            ts: num(r.ts),
            actor: r.actor === "player2" ? "player2" : "player1",
            attempt: num(r.attempt),
            outcome: ["clear", "death", "quit"].includes(r.outcome) ? r.outcome : "clear",
            // cell_count 以实际解析出来的格子数为准（老数据可能没写这个字段）
            cell_count: cells2.length || num(r.cell_count),
            duration_s: num(r.duration_s),
            cells: cells2
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
          jumps_before_death: num(d.jumps_before_death)
        })),
        runs: runs.map((r) => ({
          ts: num(r.ts),
          type: r.type,
          duration_s: num(r.duration_s),
          deaths: num(r.deaths),
          jumps: num(r.jumps),
          distance: num(r.distance)
        })),
        starts: starts.map((s) => ({
          ts: num(s.ts),
          attempt: num(s.attempt),
          session_time_s: num(s.session_time_s)
        }))
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
        })
      });
    }
    return json({ ok: false, error: "not_found" }, 404);
  } catch (err) {
    console.error("stats_failed", err && err.message);
    return json({ ok: false, error: "query_failed", message: String(err && err.message || err) }, 500);
  }
}
__name(handleStats, "handleStats");
async function handleAdminClear(url, env, request) {
  if (!env || !env.DB) return json({ ok: false, error: "db_not_bound" }, 503);
  if (!env.ADMIN_KEY) {
    return json(
      {
        ok: false,
        error: "admin_key_not_configured",
        hint: "\u5148\u8FD0\u884C npx wrangler secret put ADMIN_KEY\uFF0C\u518D\u5728\u770B\u677F \u2699 \u91CC\u586B\u540C\u4E00\u4E2A\u503C"
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
      ).bind(body.scene).run();
      const deleted = num(res && res.meta && res.meta.changes);
      return json({ ok: true, scope: "scene", scene: body.scene, deleted });
    }
    return json({ ok: false, error: "scene_or_all_required" }, 400);
  } catch (err) {
    console.error("clear_failed", err && err.message);
    return json({ ok: false, error: "clear_failed", message: String(err && err.message || err) }, 500);
  }
}
__name(handleAdminClear, "handleAdminClear");

// src/backgrounds.js
var PREFIX = "bg:";
var MAX_BYTES = 3 * 1024 * 1024;
var ALLOWED = /* @__PURE__ */ new Set(["image/webp", "image/png", "image/jpeg", "image/gif"]);
var SCENE_RE = /^[A-Za-z0-9 _.\-]{1,64}$/;
function keyOf(scene) {
  return PREFIX + scene;
}
__name(keyOf, "keyOf");
function authOk(env, url, request) {
  const need = env.ADMIN_KEY;
  if (!need) return { ok: false, status: 403, error: "admin_key_not_configured" };
  const given = request.headers.get("x-admin-key") || url.searchParams.get("key") || "";
  if (given !== need) return { ok: false, status: 401, error: "unauthorized" };
  return { ok: true };
}
__name(authOk, "authOk");
function parseDataUrl(raw) {
  const m = /^data:([a-z]+\/[a-z0-9.+-]+);base64,(.+)$/i.exec(String(raw || ""));
  if (!m) return null;
  const mime = m[1].toLowerCase();
  if (!ALLOWED.has(mime)) return null;
  const bin = atob(m[2]);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return { mime, bytes };
}
__name(parseDataUrl, "parseDataUrl");
function metaOf(scene, meta) {
  const m = meta || {};
  return {
    scene,
    mime: m.mime || "image/webp",
    w: Number(m.w) || 0,
    h: Number(m.h) || 0,
    bytes: Number(m.bytes) || 0,
    updated_at: Number(m.updated_at) || 0,
    // 背景在世界坐标里的摆放位置，跟着图一起共享，别人打开就是对齐好的样子
    px: Number(m.px) || 0,
    py: Number(m.py) || 0,
    pw: Number(m.pw) || 0,
    ph: Number(m.ph) || 0,
    alpha: m.alpha === void 0 ? 0.55 : Number(m.alpha),
    url: `/v1/backgrounds/${encodeURIComponent(scene)}`
  };
}
__name(metaOf, "metaOf");
function clampNum(v, min, max, fallback) {
  const n = Number(v);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, n));
}
__name(clampNum, "clampNum");
async function listBackgrounds(env) {
  if (!env.BG) return json({ ok: false, error: "bg_not_configured" }, 503);
  const out = [];
  let cursor;
  do {
    const page = await env.BG.list({ prefix: PREFIX, cursor });
    for (const k of page.keys) {
      out.push(metaOf(k.name.slice(PREFIX.length), k.metadata));
    }
    cursor = page.list_complete ? void 0 : page.cursor;
  } while (cursor);
  out.sort((a, b) => String(a.scene).localeCompare(String(b.scene)));
  return json({ ok: true, backgrounds: out });
}
__name(listBackgrounds, "listBackgrounds");
async function getBackground(env, scene) {
  if (!env.BG) return json({ ok: false, error: "bg_not_configured" }, 503);
  const got = await env.BG.getWithMetadata(keyOf(scene), "arrayBuffer");
  if (!got || !got.value) return json({ ok: false, error: "not_found" }, 404);
  const meta = metaOf(scene, got.metadata);
  return new Response(got.value, {
    status: 200,
    headers: {
      "Content-Type": meta.mime,
      "Content-Length": String(got.value.byteLength || meta.bytes || 0),
      // 背景图不常变，允许浏览器缓存一分钟；改了会带上新的 updated_at 查询参数
      "Cache-Control": "public, max-age=60",
      "X-BG-Updated-At": String(meta.updated_at),
      ...CORS_HEADERS
    }
  });
}
__name(getBackground, "getBackground");
async function putBackground(env, request, scene, url) {
  if (!env.BG) return json({ ok: false, error: "bg_not_configured" }, 503);
  const auth = authOk(env, url, request);
  if (!auth.ok) return json({ ok: false, error: auth.error }, auth.status);
  let body;
  try {
    body = await request.json();
  } catch {
    return json({ ok: false, error: "invalid_json" }, 400);
  }
  const parsed = parseDataUrl(body && body.data);
  if (!parsed) return json({ ok: false, error: "invalid_image_data_url" }, 400);
  if (parsed.bytes.byteLength > MAX_BYTES) {
    return json({ ok: false, error: "image_too_large", max_bytes: MAX_BYTES }, 413);
  }
  const meta = {
    mime: parsed.mime,
    w: clampNum(body.w, 0, 2e4, 0),
    h: clampNum(body.h, 0, 2e4, 0),
    bytes: parsed.bytes.byteLength,
    updated_at: Date.now(),
    px: Math.round(clampNum(body.px, -1e6, 1e6, 0) * 100) / 100,
    py: Math.round(clampNum(body.py, -1e6, 1e6, 0) * 100) / 100,
    pw: Math.round(clampNum(body.pw, 0, 1e6, 0) * 100) / 100,
    ph: Math.round(clampNum(body.ph, 0, 1e6, 0) * 100) / 100,
    alpha: Math.round(clampNum(body.alpha, 0.05, 1, 0.55) * 100) / 100
  };
  await env.BG.put(keyOf(scene), parsed.bytes, { metadata: meta });
  return json({ ok: true, background: metaOf(scene, meta) });
}
__name(putBackground, "putBackground");
async function deleteBackground(env, request, scene, url) {
  if (!env.BG) return json({ ok: false, error: "bg_not_configured" }, 503);
  const auth = authOk(env, url, request);
  if (!auth.ok) return json({ ok: false, error: auth.error }, auth.status);
  await env.BG.delete(keyOf(scene));
  return json({ ok: true, deleted: scene });
}
__name(deleteBackground, "deleteBackground");
async function handleBackgrounds(url, env, request) {
  const path = url.pathname.replace(/^\/v1\/backgrounds\/?/, "");
  const scene = path ? decodeURIComponent(path) : "";
  if (scene && !SCENE_RE.test(scene)) {
    return json({ ok: false, error: "invalid_scene" }, 400);
  }
  if (!scene) {
    if (request.method === "GET" || request.method === "HEAD") return listBackgrounds(env);
    return json({ ok: false, error: "scene_required" }, 400);
  }
  if (request.method === "GET" || request.method === "HEAD") return getBackground(env, scene);
  if (request.method === "PUT" || request.method === "POST") {
    return putBackground(env, request, scene, url);
  }
  if (request.method === "DELETE") return deleteBackground(env, request, scene, url);
  return json({ ok: false, error: "method_not_allowed" }, 405);
}
__name(handleBackgrounds, "handleBackgrounds");

// src/dashboard-page.js
var dashboard_page_default = [
  "<!DOCTYPE html>",
  '<html lang="zh-CN">',
  "<head>",
  '<meta charset="utf-8">',
  '<meta name="viewport" content="width=device-width, initial-scale=1">',
  "<title>ALittle \u73A9\u5BB6\u884C\u4E3A\u6570\u636E\u770B\u677F</title>",
  "<style>",
  "  :root{",
  "    --bg:#0d1117; --panel:#161b22; --panel2:#1c2330; --line:#252d3a;",
  "    --text:#e6edf3; --dim:#8b949e; --red:#ff4757; --blue:#4da3ff;",
  "    --green:#40d68f; --orange:#ffa940;",
  "  }",
  "  *{box-sizing:border-box}",
  "  html,body{margin:0; background:var(--bg); color:var(--text);",
  '    font:13px/1.5 -apple-system,"Segoe UI","Microsoft YaHei",system-ui,sans-serif}',
  "  a{color:var(--blue); text-decoration:none}",
  "  code{background:#0b0f14; border:1px solid var(--line); border-radius:4px; padding:1px 4px; font-size:12px}",
  "  header{display:flex; align-items:center; gap:10px; padding:10px 14px;",
  "    border-bottom:1px solid var(--line); position:sticky; top:0; background:rgba(13,17,23,.96); z-index:20; backdrop-filter:blur(6px)}",
  "  .brand{font-weight:600; letter-spacing:.3px; margin-right:4px}",
  "  .brand em{color:var(--dim); font-style:normal; font-weight:400; font-size:12px; margin-left:6px}",
  "  select,button,input[type=text],input[type=number],input[type=password]{",
  "    background:var(--panel2); color:var(--text); border:1px solid var(--line);",
  "    border-radius:7px; padding:5px 8px; font:inherit; font-size:12px; outline:none}",
  "  button{cursor:pointer}",
  "  button:hover{border-color:#3a4757}",
  "  button.primary{background:#1f6feb; border-color:#1f6feb}",
  "  button.danger{background:#3a1418; border-color:#7d2a32; color:#ff8b95}",
  "  button.danger:hover{background:#4a1a20}",
  "  .spacer{flex:1}",
  "  .status{font-size:12px; color:var(--dim)}",
  "  .status.err{color:#ff8b95}",
  "  main{display:grid; grid-template-columns:212px 1fr; gap:12px; padding:12px 14px 40px}",
  "  aside{min-width:0}",
  "  .side-title{font-size:11px; color:var(--dim); margin:2px 0 8px; letter-spacing:.4px}",
  "  .lvl{display:block; width:100%; text-align:left; margin-bottom:6px; padding:8px 9px;",
  "    background:var(--panel); border:1px solid var(--line); border-radius:9px; color:var(--text)}",
  "  .lvl.on{border-color:#2f81f7; background:#152238}",
  "  .lvl .nm{display:flex; align-items:baseline; justify-content:space-between; gap:6px}",
  "  .lvl .nm b{font-size:13px}",
  "  .lvl .nm em{font-style:normal; color:var(--dim); font-size:10px}",
  "  .lvl .sub{display:flex; gap:7px; flex-wrap:wrap; margin-top:4px; font-size:10.5px; color:var(--dim)}",
  "  .dot{display:inline-block; width:7px; height:7px; border-radius:50%; margin-right:3px; vertical-align:middle}",
  "  .kpis{display:grid; grid-template-columns:repeat(auto-fit,minmax(112px,1fr)); gap:9px; margin-bottom:12px}",
  "  .kpi{background:var(--panel); border:1px solid var(--line); border-radius:9px; padding:9px 11px}",
  "  .kpi .v{font-size:19px; font-weight:600; letter-spacing:.3px}",
  "  .kpi .l{color:var(--dim); font-size:11px; margin-top:1px}",
  "  .kpi .l i{font-style:normal}",
  "  .tabs{display:flex; gap:6px; margin-bottom:10px; flex-wrap:wrap}",
  "  .tab{background:transparent; border:1px solid transparent; color:var(--dim); padding:5px 10px; border-radius:8px; font-size:12.5px}",
  "  .tab.on{background:var(--panel2); color:var(--text); border-color:var(--line)}",
  "  .tab:hover{color:var(--text); background:var(--panel2)}",
  "  .card{background:var(--panel); border:1px solid var(--line); border-radius:11px; padding:12px; margin-bottom:12px}",
  "  .card h3{margin:0 0 10px; font-size:13px; font-weight:600}",
  "  .card h3 small{color:var(--dim); font-weight:400; font-size:11px; margin-left:6px}",
  "  .muted{color:var(--dim)}",
  "  .empty{padding:26px 10px; text-align:center; color:var(--dim); line-height:1.9}",
  "  table{width:100%; border-collapse:collapse; font-size:12px}",
  "  th,td{padding:5px 7px; text-align:left; border-bottom:1px solid #1d2430; white-space:nowrap}",
  "  th{color:var(--dim); font-weight:500; font-size:11px; position:sticky; top:0; background:var(--panel)}",
  "  td.num,th.num{text-align:right; font-variant-numeric:tabular-nums}",
  "  .scroll{max-height:440px; overflow:auto}",
  "  .toolbar{display:flex; align-items:center; gap:8px; flex-wrap:wrap; margin-bottom:10px}",
  "  .seg{display:flex; gap:4px; background:#0f141c; border:1px solid var(--line); border-radius:8px; padding:3px}",
  "  .seg button{background:transparent; border:1px solid transparent; color:var(--dim); padding:4px 9px; border-radius:6px; font-size:12px}",
  "  .seg button.on{background:var(--panel2); color:var(--text); border-color:var(--line)}",
  "  .sep{width:1px; height:20px; background:var(--line)}",
  "  .heatwrap{position:relative; width:100%; display:flex; justify-content:center}",
  "  canvas{border:1px solid var(--line); border-radius:8px; display:block; touch-action:none}",
  "  canvas.editing{cursor:grab; border-color:#2f81f7}",
  "  canvas.editing:active{cursor:grabbing}",
  "  .tip{position:absolute; pointer-events:none; display:none; background:rgba(8,12,18,.96);",
  "    border:1px solid #33405a; border-radius:7px; padding:6px 9px; font-size:11.5px; line-height:1.6; z-index:5; white-space:nowrap}",
  "  .legend{display:flex; gap:16px; flex-wrap:wrap; margin-top:9px; font-size:11px; color:var(--dim)}",
  "  .legend .grad{display:inline-block; width:74px; height:8px; border-radius:4px; vertical-align:middle; margin:0 5px}",
  "  .hint{color:var(--dim); font-size:11px; margin-top:8px; line-height:1.7}",
  "  .grid2{display:grid; grid-template-columns:1fr 1fr; gap:12px}",
  "  @media (max-width:1080px){ .grid2{grid-template-columns:1fr} main{grid-template-columns:1fr} }",
  "  .badge{display:inline-block; padding:1px 6px; border-radius:5px; font-size:10.5px; border:1px solid transparent}",
  "  .badge.clear{background:#0f2e20; color:#5fd39b; border-color:#175236}",
  "  .badge.quit{background:#2e2416; color:#e0ac5a; border-color:#54401f}",
  "  .badge.death{background:#2e1519; color:#ff8b95; border-color:#5d222a}",
  "  .badge.p1{background:#331318; color:#ff8b95; border-color:#5d222a}",
  "  .badge.p2{background:#10233a; color:#82bfff; border-color:#1e3c5e}",
  "  .badge.start{background:#17212e; color:#9fb3c8; border-color:#26313f}",
  "  .bar{position:relative; background:#0f141c; border-radius:4px; height:14px; min-width:60px; overflow:hidden}",
  "  .bar i{position:absolute; left:0; top:0; bottom:0; border-radius:4px; background:linear-gradient(90deg,#44506a,#8b98ab)}",
  "  .bar.red i{background:linear-gradient(90deg,#a3272f,#ff4757)}",
  "  .bar.blue i{background:linear-gradient(90deg,#24558c,#4da3ff)}",
  "  .barcell{display:flex; align-items:center; gap:7px}",
  "  .barval{min-width:52px; text-align:right; font-variant-numeric:tabular-nums}",
  "  #cfg{display:none; position:fixed; right:14px; top:52px; width:340px; z-index:30;",
  "    background:var(--panel); border:1px solid #33405a; border-radius:11px; padding:14px;",
  "    box-shadow:0 14px 40px rgba(0,0,0,.55)}",
  "  #cfg label{display:block; font-size:11px; color:var(--dim); margin:9px 0 4px}",
  "  #cfg input{width:100%}",
  "  #cfg .row{display:flex; gap:7px; margin-top:12px}",
  "  #cfg .row button{flex:1}",
  "  .cmp{overflow:auto}",
  "  .cmp th:first-child, .cmp td:first-child{position:sticky; left:0; background:var(--panel); z-index:2; min-width:112px}",
  "  .cmp td.best{color:#5fd39b}",
  "  .cmp td.worst{color:#ff8b95}",
  "  .bgedit .row{display:flex; gap:6px; align-items:center; flex-wrap:wrap; margin-top:8px}",
  "  .bgedit input[type=number]{width:74px}",
  "  .bgedit input[type=range]{width:110px; accent-color:#4da3ff}",
  "  .filebtn{position:relative; overflow:hidden; display:inline-block}",
  "  .filebtn input{position:absolute; inset:0; opacity:0; cursor:pointer; width:100%}",
  "  .tag{display:inline-block; padding:0 5px; border-radius:4px; font-size:10px; margin-left:5px}",
  "  .tag.p1{background:#331318; color:#ff8b95}",
  "  .tag.p2{background:#10233a; color:#82bfff}",
  "</style>",
  "</head>",
  "<body>",
  "",
  "<header>",
  '  <div class="brand">ALittle<em>\u73A9\u5BB6\u884C\u4E3A\u6570\u636E\u770B\u677F</em></div>',
  '  <select id="range" title="\u65F6\u95F4\u8303\u56F4">',
  '    <option value="all">\u5168\u90E8\u65F6\u95F4</option>',
  '    <option value="24h">\u6700\u8FD1 24 \u5C0F\u65F6</option>',
  '    <option value="7d">\u6700\u8FD1 7 \u5929</option>',
  '    <option value="30d">\u6700\u8FD1 30 \u5929</option>',
  "  </select>",
  '  <button id="refresh">\u5237\u65B0</button>',
  '  <label class="muted" style="font-size:11.5px;display:flex;align-items:center;gap:5px">',
  '    <input type="checkbox" id="auto" style="accent-color:#4da3ff"> \u81EA\u52A8\u5237\u65B0',
  "  </label>",
  '  <div class="spacer"></div>',
  '  <div class="status" id="status">\u52A0\u8F7D\u4E2D\u2026</div>',
  '  <button id="cfgBtn" title="\u8BBE\u7F6E">\u2699</button>',
  "</header>",
  "",
  '<div id="cfg">',
  '  <div style="font-size:12px;font-weight:600;margin-bottom:2px">\u8BBE\u7F6E</div>',
  "  <label>API \u5730\u5740</label>",
  '  <input id="cfgApi" type="text" placeholder="https://\u2026workers.dev">',
  "  <label>\u53EA\u8BFB\u5BC6\u94A5\uFF08\u914D\u4E86 READ_KEY \u624D\u9700\u8981\uFF09</label>",
  '  <input id="cfgKey" type="text" placeholder="\u53EF\u7559\u7A7A">',
  "  <label>\u7BA1\u7406\u5BC6\u94A5\uFF08\u6E05\u6570\u636E\u7528\uFF0C\u5373 ADMIN_KEY\uFF09</label>",
  '  <input id="cfgAdmin" type="password" placeholder="\u53EF\u7559\u7A7A">',
  '  <div class="row">',
  '    <button class="primary" id="cfgSave">\u4FDD\u5B58\u5E76\u5237\u65B0</button>',
  '    <button id="cfgClose">\u5173\u95ED</button>',
  "  </div>",
  '  <label style="margin-top:16px">\u6E05\u6570\u636E\uFF08\u4E0D\u53EF\u64A4\u9500\uFF09</label>',
  '  <div class="row">',
  '    <button class="danger" id="clearScene">\u6E05\u7A7A\u672C\u5173</button>',
  '    <button class="danger" id="clearAll">\u6E05\u7A7A\u5168\u90E8</button>',
  "  </div>",
  '  <div class="hint">\u6E05\u6570\u636E\u9700\u8981\u670D\u52A1\u7AEF\u914D\u4E86 <code>ADMIN_KEY</code>\uFF08<code>wrangler secret put ADMIN_KEY</code>\uFF09\uFF0C\u5E76\u5728\u4E0A\u9762\u586B\u540C\u4E00\u4E2A\u503C\u3002\u6CA1\u914D\u5C31\u4E00\u5F8B\u62D2\u7EDD\u3002</div>',
  '  <div class="hint">\u80CC\u666F\u56FE\u9ED8\u8BA4\u53EA\u5B58\u5728\u4F60\u81EA\u5DF1\u7684\u6D4F\u89C8\u5668\uFF1B\u5728\u80CC\u666F\u56FE\u90A3\u4E00\u884C\u70B9\u300C\u4E0A\u4F20\u5230\u670D\u52A1\u5668\uFF08\u5171\u7528\uFF09\u300D\u624D\u4F1A\u4F20\u5230 KV\uFF08\u540C\u6837\u7528\u4E0A\u9762\u8FD9\u628A\u7BA1\u7406\u5BC6\u94A5\uFF09\uFF0C\u4F20\u4E86\u522B\u4EBA\u6253\u5F00\u540C\u4E00\u5173\u5C31\u80FD\u770B\u5230\u3002</div>',
  "</div>",
  "",
  "<main>",
  "  <aside>",
  '    <div class="side-title">\u5173\u5361\uFF08\u6309\u6B7B\u4EA1\u6392\u5E8F\uFF09</div>',
  '    <div id="levels"></div>',
  "  </aside>",
  "",
  '  <section style="min-width:0">',
  '    <div id="kpis" class="kpis"></div>',
  '    <div class="tabs" id="tabs"></div>',
  '    <div id="content"></div>',
  "  </section>",
  "</main>",
  "",
  "<script>",
  "/* ================================================================ \u72B6\u6001 */",
  "",
  'const FALLBACK_API = "https://alittle-telemetry.lhy060303.workers.dev";',
  "const qs = new URLSearchParams(location.search);",
  'const servedFromHttp = location.protocol === "http:" || location.protocol === "https:";',
  "",
  "const TABS = [",
  '  ["heatmap", "\u70ED\u529B\u56FE"],',
  '  ["compare", "\u6A2A\u5411\u5BF9\u6BD4"],',
  '  ["deaths",  "\u6B7B\u4EA1"],',
  '  ["runs",    "\u65F6\u957F\u4E0E\u901A\u5173"],',
  '  ["recent",  "\u6700\u8FD1\u4E8B\u4EF6"],',
  '  ["dict",    "\u6570\u636E\u5B57\u5178"],',
  "];",
  "const TAB_KEYS = TABS.map(t => t[0]);",
  "",
  "const METRICS = {",
  '  deaths: { label: "\u6B7B\u4EA1\u6B21\u6570", get: c => c.deaths },',
  '  jumps:  { label: "\u8DF3\u8DC3\u6B21\u6570", get: c => c.jumps  },',
  '  dwell:  { label: "\u505C\u7559\u65F6\u957F", get: c => c.dwell  },',
  "};",
  "",
  "const P1_RGB = [255, 71, 87];",
  "const P2_RGB = [77, 163, 255];",
  "// \u4E24\u6761\u8DEF\u7EBF\u91CD\u5408\u7684\u683C\u5B50\u7528\u7D2B\u8272\uFF08\u7EA2 + \u84DD\u53E0\u51FA\u6765\u7684\uFF09\uFF0C\u548C\u5355\u8272\u65B9\u5757\u4E00\u6837\u5927",
  "const BOTH_RGB = [178, 110, 214];",
  "",
  "// \u4E00\u683C\u4EE3\u8868\u591A\u5C11\u4E16\u754C\u5355\u4F4D\uFF08\u65B0\u6570\u636E 0.5\uFF0C\u8001\u6570\u636E 1\uFF09\u3002\u80CC\u666F\u56FE\u5B58\u7684\u662F\u4E16\u754C\u5750\u6807\uFF0C\u6362\u7B97\u65F6\u8981\u9664\u5B83\u3002",
  "const cellSize = () => (state.sceneData && state.sceneData.cell_size) || 1;",
  "// \u683C\u5B50\u7D22\u5F15 -> \u4E16\u754C\u5750\u6807\uFF08\u7D22\u5F15\u7684\u5DE6\u4E0A\u89D2\uFF09",
  "const toWorld = (v) => Math.round(v * cellSize() * 100) / 100;",
  "",
  "const store = {",
  "  get(k, d) { try { const v = localStorage.getItem(k); return v === null ? d : v; } catch { return d; } },",
  "  set(k, v) { try { localStorage.setItem(k, v); } catch {} },",
  "};",
  "",
  "const state = {",
  '  api: servedFromHttp ? location.origin : (qs.get("api") || store.get("alittle_api", "") || FALLBACK_API),',
  '  key: qs.get("key") || store.get("alittle_key", ""),',
  '  admin: store.get("alittle_admin", ""),',
  '  range: qs.get("range") || "all",',
  '  scene: qs.get("scene") || null,',
  '  metric: METRICS[qs.get("metric")] ? qs.get("metric") : "deaths",',
  '  tab: TAB_KEYS.includes(qs.get("tab")) ? qs.get("tab") : "heatmap",',
  "  overview: null,",
  "  sceneData: null,",
  "  compare: null,",
  '  compareSort: "deaths_per_start",',
  '  showP1: qs.get("p1") !== "0",',
  '  showP2: qs.get("p2") !== "0",',
  '  showRoute: qs.get("route") !== "0",',
  '  routeClearOnly: qs.get("rc") !== "0",',
  '  routePick: qs.get("rp") || "latest",',
  "  bg: null,",
  "  bgShared: false,     // \u5F53\u524D\u8FD9\u5F20\u56FE\u662F\u4E0D\u662F\u5DF2\u7ECF\u4ECE\u670D\u52A1\u5668\u52A0\u8F7D\u7684\u5171\u7528\u56FE",
  "  bgIndex: {},         // scene -> \u670D\u52A1\u5668\u4E0A\u7684\u80CC\u666F\u56FE\u5143\u4FE1\u606F",
  "  bgEdit: false,",
  "  bgLock: true,",
  "  bgAlpha: 0.55,",
  "  bgBounds: null,",
  "  transform: null,",
  "  recent: null,",
  "  autoTimer: null,",
  "};",
  "",
  "/* ================================================================ \u5C0F\u5DE5\u5177 */",
  "",
  "const $ = id => document.getElementById(id);",
  'const esc = s => String(s === null || s === undefined ? "" : s)',
  '  .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");',
  "",
  "function fmtNum(v) {",
  '  if (v === null || v === undefined || Number.isNaN(v)) return "\u2013";',
  '  if (Math.abs(v) >= 10000) return (v / 1000).toFixed(1) + "k";',
  "  return (Math.round(v * 10) / 10).toString();",
  "}",
  'const fmtPct = v => (v === null || v === undefined ? "\u2013" : (v * 100).toFixed(1) + "%");',
  "function fmtSec(s) {",
  '  if (s === null || s === undefined) return "\u2013";',
  '  if (s < 60) return s.toFixed(1) + "s";',
  "  const m = Math.floor(s / 60), r = Math.round(s % 60);",
  '  if (m < 60) return m + "m" + String(r).padStart(2, "0") + "s";',
  '  return Math.floor(m / 60) + "h" + String(m % 60).padStart(2, "0") + "m";',
  "}",
  "function fmtAgo(ts) {",
  '  if (!ts) return "\u2013";',
  "  const d = (Date.now() - ts) / 1000;",
  '  if (d < 60) return "\u521A\u521A";',
  '  if (d < 3600) return Math.floor(d / 60) + " \u5206\u949F\u524D";',
  '  if (d < 86400) return Math.floor(d / 3600) + " \u5C0F\u65F6\u524D";',
  '  if (d < 86400 * 30) return Math.floor(d / 86400) + " \u5929\u524D";',
  '  return new Date(ts).toLocaleDateString("zh-CN");',
  "}",
  'const fmtTime = ts => new Date(ts).toLocaleString("zh-CN", { hour12: false });',
  "",
  "function sinceMs() {",
  '  if (state.range === "all") return 0;',
  '  const days = { "24h": 1, "7d": 7, "30d": 30 }[state.range] || 0;',
  "  return Date.now() - days * 86400000;",
  "}",
  "",
  "function setStatus(text, isErr) {",
  '  const el = $("status");',
  "  el.textContent = text;",
  '  el.className = "status" + (isErr ? " err" : "");',
  "}",
  "",
  "async function api(path, params) {",
  '  const u = new URL(state.api.replace(/\\/+$/, "") + path);',
  "  for (const [k, v] of Object.entries(params || {})) {",
  '    if (v !== undefined && v !== null && v !== "") u.searchParams.set(k, v);',
  "  }",
  "  const headers = {};",
  '  if (state.key) headers["X-Read-Key"] = state.key;',
  "  const res = await fetch(u.toString(), { headers });",
  "  const body = await res.json().catch(() => ({}));",
  '  if (!res.ok || body.ok === false) throw new Error(body.error || ("HTTP " + res.status));',
  "  return body;",
  "}",
  "",
  "/* ================================================================ \u80CC\u666F\u56FE\u5B58\u50A8\uFF08IndexedDB\uFF09 */",
  "",
  "const bgStore = {",
  "  db: null,",
  "  async open() {",
  "    if (this.db) return this.db;",
  "    this.db = await new Promise((resolve, reject) => {",
  '      const req = indexedDB.open("alittle-bg", 1);',
  "      req.onupgradeneeded = () => {",
  '        if (!req.result.objectStoreNames.contains("images")) {',
  '          req.result.createObjectStore("images", { keyPath: "scene" });',
  "        }",
  "      };",
  "      req.onsuccess = () => resolve(req.result);",
  "      req.onerror = () => reject(req.error);",
  "    });",
  "    return this.db;",
  "  },",
  "  async get(scene) {",
  "    const db = await this.open();",
  "    return new Promise((resolve, reject) => {",
  '      const tx = db.transaction("images", "readonly");',
  '      const req = tx.objectStore("images").get(scene);',
  "      req.onsuccess = () => resolve(req.result || null);",
  "      req.onerror = () => reject(req.error);",
  "    });",
  "  },",
  "  async put(rec) {",
  "    const db = await this.open();",
  "    return new Promise((resolve, reject) => {",
  '      const tx = db.transaction("images", "readwrite");',
  '      tx.objectStore("images").put(rec);',
  "      tx.oncomplete = () => resolve(true);",
  "      tx.onerror = () => reject(tx.error);",
  "    });",
  "  },",
  "  async del(scene) {",
  "    const db = await this.open();",
  "    return new Promise((resolve, reject) => {",
  '      const tx = db.transaction("images", "readwrite");',
  '      tx.objectStore("images").delete(scene);',
  "      tx.oncomplete = () => resolve(true);",
  "      tx.onerror = () => reject(tx.error);",
  "    });",
  "  },",
  "};",
  "",
  "function decodeImage(src) {",
  "  return new Promise((resolve, reject) => {",
  "    const img = new Image();",
  "    img.onload = () => resolve(img);",
  '    img.onerror = () => reject(new Error("\u56FE\u7247\u52A0\u8F7D\u5931\u8D25"));',
  "    img.src = src;",
  "  });",
  "}",
  "",
  "async function loadBg(scene) {",
  "  if (state.bg && state.bg.url) URL.revokeObjectURL(state.bg.url);",
  "  state.bg = null;",
  "  state.bgShared = false;",
  "  if (!scene) return;",
  "",
  "  // 1) \u672C\u673A\u8349\u7A3F\u4F18\u5148\uFF08\u81EA\u5DF1\u62D6\u8FC7\u4F4D\u7F6E\u7684\u90A3\u4EFD\uFF09",
  "  try {",
  "    const rec = await bgStore.get(scene);",
  "    if (rec && rec.blob) {",
  "      const url = URL.createObjectURL(rec.blob);",
  "      const img = await decodeImage(url);",
  "      state.bg = {",
  "        img, url, blob: rec.blob,",
  "        x: +rec.x || 0, y: +rec.y || 0, w: +rec.w || 1, h: +rec.h || 1,",
  "        alpha: rec.alpha === undefined ? 0.55 : +rec.alpha,",
  "      };",
  "      state.bgAlpha = state.bg.alpha;",
  "      return;",
  "    }",
  "  } catch (err) {",
  '    console.warn("\u80CC\u666F\u56FE\u8BFB\u53D6\u5931\u8D25", err);',
  "  }",
  "",
  "  // 2) \u672C\u673A\u6CA1\u6709\u5C31\u770B\u670D\u52A1\u5668\u4E0A\u6709\u6CA1\u6709\u4EBA\u4F20\u8FC7",
  "  const meta = state.bgIndex[scene];",
  "  if (!meta) return;",
  "  try {",
  '    const url = state.api.replace(/\\/+$/, "") + meta.url + "?v=" + (meta.updated_at || 0);',
  '    const res = await fetch(url, { cache: "no-store" });',
  "    if (!res.ok) return;",
  "    const blob = await res.blob();",
  "    const objUrl = URL.createObjectURL(blob);",
  "    const img = await decodeImage(objUrl);",
  "    state.bg = {",
  "      img, url: objUrl, blob,",
  "      x: +meta.px || 0, y: +meta.py || 0, w: +meta.pw || 1, h: +meta.ph || 1,",
  "      alpha: meta.alpha === undefined ? 0.55 : +meta.alpha,",
  "      remote: true,",
  "    };",
  "    state.bgAlpha = state.bg.alpha;",
  "    state.bgShared = true;",
  "  } catch (err) {",
  '    console.warn("\u670D\u52A1\u5668\u80CC\u666F\u56FE\u52A0\u8F7D\u5931\u8D25", err);',
  "  }",
  "}",
  "",
  "async function saveBg() {",
  "  if (!state.scene || !state.bg) return;",
  "  try {",
  "    await bgStore.put({",
  "      scene: state.scene,",
  "      blob: state.bg.blob,",
  "      x: state.bg.x, y: state.bg.y, w: state.bg.w, h: state.bg.h,",
  "      alpha: state.bg.alpha,",
  "      savedAt: Date.now(),",
  "    });",
  "  } catch (err) {",
  '    setStatus("\u80CC\u666F\u56FE\u4FDD\u5B58\u5931\u8D25\uFF08\u6D4F\u89C8\u5668\u4E0D\u5141\u8BB8\u672C\u5730\u5B58\u50A8\uFF1F\uFF09", true);',
  "    console.warn(err);",
  "  }",
  "}",
  "",
  "/* ------------------------------------------------ \u80CC\u666F\u56FE\u5171\u4EAB\uFF08\u4E0A\u4F20\u5230\u670D\u52A1\u5668\uFF09 */",
  "",
  "/** \u62C9\u4E00\u6B21\u670D\u52A1\u5668\u4E0A\u7684\u80CC\u666F\u56FE\u6E05\u5355\uFF08\u516C\u5F00\u63A5\u53E3\uFF0C\u4E0D\u9700\u8981\u5BC6\u94A5\uFF09 */",
  "async function loadBgIndex() {",
  "  try {",
  '    const body = await api("/v1/backgrounds");',
  "    const map = {};",
  "    for (const b of body.backgrounds || []) map[b.scene] = b;",
  "    state.bgIndex = map;",
  "  } catch (err) {",
  "    // \u6CA1\u7ED1 KV \u7684\u90E8\u7F72\u4F1A\u8FD4\u56DE 503\uFF0C\u8FD9\u91CC\u9759\u9ED8\u964D\u7EA7\u6210\u300C\u53EA\u80FD\u7528\u672C\u673A\u56FE\u300D",
  "    state.bgIndex = {};",
  "  }",
  "}",
  "",
  "/**",
  " * \u628A\u5F53\u524D\u80CC\u666F\u56FE\u538B\u5230\u5408\u9002\u5927\u5C0F\u518D\u4E0A\u4F20\uFF1A\u957F\u8FB9\u6700\u591A 1600px\uFF0C\u4F18\u5148 WebP\u3002",
  " * \u5173\u5361\u622A\u56FE\u52A8\u8F84 2~4 MB\uFF0C\u538B\u5B8C\u4E00\u822C 80~250 KB\uFF0CKV \u7684 1 GB \u80FD\u653E\u51E0\u5343\u5F20\u3002",
  " */",
  "async function encodeBgForUpload(blob) {",
  "  const img = await decodeImage(URL.createObjectURL(blob));",
  "  const maxSide = 1600;",
  "  const scale = Math.min(1, maxSide / Math.max(img.naturalWidth, img.naturalHeight));",
  "  const w = Math.max(1, Math.round(img.naturalWidth * scale));",
  "  const h = Math.max(1, Math.round(img.naturalHeight * scale));",
  "",
  '  const canvas = document.createElement("canvas");',
  "  canvas.width = w;",
  "  canvas.height = h;",
  '  const ctx = canvas.getContext("2d");',
  "  ctx.drawImage(img, 0, 0, w, h);",
  "",
  "  let quality = 0.85;",
  '  let data = canvas.toDataURL("image/webp", quality);',
  '  if (!data.startsWith("data:image/webp")) {',
  "    // \u8001\u6D4F\u89C8\u5668\u4E0D\u652F\u6301 WebP \u7F16\u7801\uFF0C\u9000\u56DE JPEG\uFF08\u80CC\u666F\u56FE\u4E0D\u9700\u8981\u900F\u660E\uFF09",
  '    data = canvas.toDataURL("image/jpeg", quality);',
  "  }",
  "  // \u8FD8\u662F\u592A\u5927\u5C31\u7EE7\u7EED\u964D\u8D28\u91CF\uFF0C\u6700\u591A\u8BD5 3 \u6B21",
  "  for (let i = 0; i < 3 && data.length > 2.5 * 1024 * 1024; i++) {",
  "    quality -= 0.2;",
  '    data = data.startsWith("data:image/webp")',
  '      ? canvas.toDataURL("image/webp", quality)',
  '      : canvas.toDataURL("image/jpeg", quality);',
  "  }",
  "  return { data, w, h };",
  "}",
  "",
  "async function uploadBg() {",
  '  if (!state.scene || !state.bg) { setStatus("\u5148\u9009\u4E00\u5F20\u80CC\u666F\u56FE", true); return; }',
  '  if (!state.admin) { setStatus("\u4E0A\u4F20\u9700\u8981\u5728 \u2699 \u91CC\u586B\u7BA1\u7406\u5BC6\u94A5\uFF08ADMIN_KEY\uFF09", true); return; }',
  '  setStatus("\u6B63\u5728\u538B\u7F29\u5E76\u4E0A\u4F20\u80CC\u666F\u56FE\u2026");',
  "  try {",
  "    const { data, w, h } = await encodeBgForUpload(state.bg.blob);",
  "    const res = await fetch(",
  '      state.api.replace(/\\/+$/, "") + "/v1/backgrounds/" + encodeURIComponent(state.scene),',
  "      {",
  '        method: "PUT",',
  '        headers: { "Content-Type": "application/json", "X-Admin-Key": state.admin },',
  "        body: JSON.stringify({",
  "          data, w, h,",
  "          px: state.bg.x, py: state.bg.y, pw: state.bg.w, ph: state.bg.h,",
  "          alpha: state.bg.alpha,",
  "        }),",
  "      }",
  "    );",
  "    const body = await res.json().catch(() => ({}));",
  '    if (!res.ok || body.ok === false) throw new Error(body.error || ("HTTP " + res.status));',
  "    state.bgShared = true;",
  "    await loadBgIndex();",
  "    renderBgBar();",
  "    setStatus(`\u80CC\u666F\u56FE\u5DF2\u4E0A\u4F20\uFF08${Math.round(body.background.bytes / 1024)} KB\uFF09\uFF0C\u540C\u4E00\u94FE\u63A5\u7684\u4EBA\u90FD\u80FD\u770B\u5230`);",
  "  } catch (err) {",
  '    setStatus("\u4E0A\u4F20\u5931\u8D25\uFF1A" + err.message, true);',
  "  }",
  "}",
  "",
  "async function deleteRemoteBg() {",
  "  if (!state.scene) return;",
  '  if (!state.admin) { setStatus("\u5220\u9664\u9700\u8981\u5728 \u2699 \u91CC\u586B\u7BA1\u7406\u5BC6\u94A5\uFF08ADMIN_KEY\uFF09", true); return; }',
  "  if (!confirm(`\u628A\u670D\u52A1\u5668\u4E0A\u300C${state.scene}\u300D\u7684\u80CC\u666F\u56FE\u5220\u6389\uFF1F\uFF08\u672C\u673A\u8FD9\u4EFD\u4E0D\u53D7\u5F71\u54CD\uFF09`)) return;",
  "  try {",
  "    const res = await fetch(",
  '      state.api.replace(/\\/+$/, "") + "/v1/backgrounds/" + encodeURIComponent(state.scene),',
  '      { method: "DELETE", headers: { "X-Admin-Key": state.admin } }',
  "    );",
  "    const body = await res.json().catch(() => ({}));",
  '    if (!res.ok || body.ok === false) throw new Error(body.error || ("HTTP " + res.status));',
  "    state.bgShared = false;",
  "    await loadBgIndex();",
  "    renderBgBar();",
  '    setStatus("\u670D\u52A1\u5668\u4E0A\u7684\u80CC\u666F\u56FE\u5DF2\u5220\u9664");',
  "  } catch (err) {",
  '    setStatus("\u5220\u9664\u5931\u8D25\uFF1A" + err.message, true);',
  "  }",
  "}",
  "",
  "/** \u80CC\u666F\u56FE\u90A3\u4E00\u884C\u7684\u6309\u94AE\u72B6\u6001 + \u300C\u672C\u5730\u8349\u7A3F / \u5DF2\u5171\u4EAB\u300D\u6807\u8BB0 */",
  "function renderBgBar() {",
  '  const badge = $("bgBadge");',
  "  if (!badge) return;",
  '  if (!state.scene) { badge.textContent = ""; return; }',
  "  const remote = state.bgIndex[state.scene];",
  "  if (state.bgShared) {",
  '    badge.textContent = "\u5DF2\u5171\u4EAB\uFF08\u6240\u6709\u4EBA\u90FD\u80FD\u770B\u5230\uFF09";',
  '    badge.className = "tag ok";',
  "  } else if (remote) {",
  '    badge.textContent = "\u670D\u52A1\u5668\u4E0A\u5DF2\u6709\u8FD9\u5F20\uFF08\u4F60\u672C\u5730\u6539\u8FC7\uFF09";',
  '    badge.className = "tag";',
  "  } else {",
  '    badge.textContent = "\u53EA\u5B58\u5728\u672C\u673A";',
  '    badge.className = "tag muted";',
  "  }",
  "}",
  "",
  "/* ================================================================ \u52A0\u8F7D */",
  "",
  "async function loadAll() {",
  '  setStatus("\u52A0\u8F7D\u4E2D\u2026");',
  "  try {",
  "    const since = sinceMs();",
  '    const ov = await api("/v1/stats/overview", { since });',
  "    state.overview = ov;",
  "",
  "    if (!state.scene && ov.scenes.length) state.scene = pickDefaultScene(ov.scenes);",
  "    if (state.scene && !ov.scenes.some(s => s.scene === state.scene)) {",
  "      state.scene = ov.scenes.length ? pickDefaultScene(ov.scenes) : null;",
  "    }",
  "",
  '    state.sceneData = state.scene ? await api("/v1/stats/scene", { scene: state.scene, since }) : null;',
  "    state.recent = null;",
  "    state.compare = null;",
  "    await loadBgIndex();",
  "",
  "    renderLevels();",
  "    renderKpis();",
  "    renderTabs();",
  "    renderContent();",
  '    setStatus("\u66F4\u65B0\u4E8E " + new Date().toLocaleTimeString("zh-CN", { hour12: false }));',
  "  } catch (err) {",
  '    setStatus("\u51FA\u9519\uFF1A" + err.message, true);',
  '    $("content").innerHTML = `<div class="card"><div class="empty">',
  "      \u8BFB\u4E0D\u5230\u6570\u636E\uFF1A${esc(err.message)}<br>",
  '      <span class="muted">\u68C0\u67E5\u7F51\u7EDC\u3001API \u5730\u5740\u548C\u53EA\u8BFB\u5BC6\u94A5\uFF08\u2699 \u91CC\u6539\uFF09</span>',
  "    </div></div>`;",
  "  }",
  "}",
  "",
  "function pickDefaultScene(scenes) {",
  "  const scored = scenes.slice().sort((a, b) => (b.deaths - a.deaths) || (b.starts - a.starts));",
  "  return (scored[0] || scenes[0]).scene;",
  "}",
  "",
  "async function loadScene() {",
  "  try {",
  '    state.sceneData = await api("/v1/stats/scene", { scene: state.scene, since: sinceMs() });',
  "  } catch (err) {",
  "    state.sceneData = null;",
  '    setStatus("\u5173\u5361\u6570\u636E\u51FA\u9519\uFF1A" + err.message, true);',
  "  }",
  "}",
  "",
  "async function loadCompare() {",
  "  if (state.compare) return state.compare;",
  '  state.compare = await api("/v1/stats/compare", { since: sinceMs() });',
  "  return state.compare;",
  "}",
  "",
  "/* ================================================================ \u5DE6\u4FA7\u5173\u5361 */",
  "",
  "function renderLevels() {",
  "  const ov = state.overview;",
  '  const box = $("levels");',
  "  if (!ov || !ov.scenes.length) {",
  '    box.innerHTML = `<div class="empty" style="padding:14px 8px">\u8FD8\u6CA1\u6709\u4EFB\u4F55\u5173\u5361\u6570\u636E</div>`;',
  "    return;",
  "  }",
  "  const sorted = ov.scenes.slice().sort((a, b) => (b.deaths - a.deaths) || (b.starts - a.starts));",
  "  const maxDeaths = Math.max(1, ...sorted.map(s => s.deaths));",
  "",
  "  box.innerHTML = sorted.map(s => {",
  '    const on = s.scene === state.scene ? " on" : "";',
  "    const heat = s.deaths / maxDeaths;",
  '    const dotColor = heat > 0.66 ? "#ff4757" : heat > 0.25 ? "#ffa940" : "#3a4757";',
  '    return `<button class="lvl${on}" data-scene="${esc(s.scene)}">',
  '      <span class="nm"><b>${esc(s.scene)}</b><em>${s.level_index === null ? "" : "#" + s.level_index}</em></span>',
  '      <span class="sub">',
  '        <span><i class="dot" style="background:${dotColor}"></i>\u6B7B ${s.deaths}</span>',
  "        <span>\u8FDB\u5165 ${s.starts}</span>",
  "        <span>\u901A\u5173 ${fmtPct(s.clear_rate)}</span>",
  "      </span>",
  "    </button>`;",
  '  }).join("");',
  "",
  '  box.querySelectorAll(".lvl").forEach(el => {',
  '    el.addEventListener("click", async () => {',
  "      state.scene = el.dataset.scene;",
  "      syncUrl();",
  "      renderLevels();",
  "      await Promise.all([loadScene(), loadBg(state.scene)]);",
  "      renderBgBar();",
  "      syncBgInputs();",
  "      renderKpis();",
  "      renderContent();",
  "    });",
  "  });",
  "}",
  "",
  "/* ================================================================ KPI */",
  "",
  "function renderKpis() {",
  "  const ov = state.overview;",
  "  const sd = state.sceneData;",
  "  const t = ov ? ov.totals : null;",
  '  if (!t) { $("kpis").innerHTML = ""; return; }',
  "",
  "  const items = [",
  '    [fmtNum(t.deaths), "\u603B\u6B7B\u4EA1"],',
  '    [fmtNum(t.jumps), "\u603B\u8DF3\u8DC3"],',
  '    [fmtNum(t.players), "\u73A9\u5BB6\u6570"],',
  '    [fmtNum(t.sessions), "\u6E38\u73A9\u5C40\u6570"],',
  '    [fmtSec(t.playtime_s), "\u7D2F\u8BA1\u6E38\u620F\u65F6\u957F"],',
  '    [fmtNum(sd ? sd.stats.starts : 0), `\u300C${sd ? sd.scene : "\u2013"}\u300D\u8FDB\u5165`],',
  '    [fmtNum(sd ? sd.stats.deaths : 0), "\u672C\u5173\u6B7B\u4EA1"],',
  '    [fmtPct(sd ? sd.stats.clear_rate : null), "\u672C\u5173\u901A\u5173\u7387"],',
  '    [fmtSec(sd ? sd.stats.avg_duration_s : null), "\u5E73\u5747\u5355\u6B21\u65F6\u957F"],',
  '    [fmtNum(sd ? sd.stats.cells : 0), "\u6709\u8BB0\u5F55\u7684\u683C\u5B50"],',
  "  ];",
  "",
  '  $("kpis").innerHTML = items.map(([v, l]) =>',
  '    `<div class="kpi"><div class="v">${esc(v)}</div><div class="l">${esc(l)}</div></div>`',
  '  ).join("");',
  "}",
  "",
  "/* ================================================================ \u6807\u7B7E\u9875 */",
  "",
  "function renderTabs() {",
  '  $("tabs").innerHTML = TABS.map(([k, label]) =>',
  '    `<button class="tab${state.tab === k ? " on" : ""}" data-tab="${k}">${label}</button>`',
  '  ).join("");',
  '  $("tabs").querySelectorAll(".tab").forEach(el => {',
  '    el.addEventListener("click", () => {',
  "      state.tab = el.dataset.tab;",
  "      syncUrl();",
  "      renderTabs();",
  "      renderContent();",
  "    });",
  "  });",
  "}",
  "",
  "function renderContent() {",
  '  const c = $("content");',
  '  if (state.tab === "heatmap") { c.innerHTML = viewHeatmap(); bindHeatmap(); return; }',
  '  if (state.tab === "compare") { c.innerHTML = `<div class="card"><div class="empty">\u52A0\u8F7D\u4E2D\u2026</div></div>`; renderCompare(); return; }',
  '  if (state.tab === "deaths") { c.innerHTML = viewDeaths(); return; }',
  '  if (state.tab === "runs") { c.innerHTML = viewRuns(); return; }',
  '  if (state.tab === "recent") { viewRecent(); return; }',
  '  if (state.tab === "dict") { c.innerHTML = viewDict(); return; }',
  "}",
  "",
  "/* ================================================================ \u70ED\u529B\u56FE */",
  "",
  "function metricValue(side, metric) {",
  '  if (metric === "deaths") return side.deaths;',
  '  if (metric === "jumps") return side.jumps;',
  "  return side.dwell;",
  "}",
  "",
  "function viewHeatmap() {",
  "  const sd = state.sceneData;",
  "  if (!sd || !sd.cells || !sd.cells.length) {",
  '    return `<div class="card"><div class="empty">',
  "      \u8FD9\u4E00\u5173\u8FD8\u6CA1\u6709\u683C\u5B50\u6570\u636E\u3002<br>",
  '      <span class="muted">\u70ED\u529B\u56FE\u6765\u81EA <code>cell</code> \u4E8B\u4EF6\uFF1A\u4E24\u4E2A\u89D2\u8272\u5404\u81EA\u6BCF\u8D70\u4E00\u6B65\u90FD\u4F1A\u5728 1\xD71 \u7684\u683C\u5B50\u91CC\u7D2F\u52A0\uFF0C<br>',
  "      \u6BCF 60 \u79D2\u6216\u5173\u5361\u7ED3\u675F\u65F6\u4E0A\u62A5\u4E00\u6B21\u589E\u91CF\u3002\u8DD1\u4E00\u5C40\u5C31\u6709\u4E86\u3002</span>",
  "    </div></div>`;",
  "  }",
  "",
  "  const b = sd.bounds;",
  "  return `",
  '  <div class="card">',
  '    <div class="toolbar">',
  '      <div class="seg" id="metricSeg">',
  "        ${Object.keys(METRICS).map(k =>",
  '          `<button data-metric="${k}" class="${state.metric === k ? "on" : ""}">${METRICS[k].label}</button>`',
  '        ).join("")}',
  "      </div>",
  '      <div class="sep"></div>',
  '      <label class="muted" style="font-size:12px;display:flex;align-items:center;gap:5px">',
  '        <input type="checkbox" id="p1Chk" ${state.showP1 ? "checked" : ""} style="accent-color:${rgbCss(P1_RGB)}">',
  "        \u73A9\u5BB6 1",
  "      </label>",
  '      <label class="muted" style="font-size:12px;display:flex;align-items:center;gap:5px">',
  '        <input type="checkbox" id="p2Chk" ${state.showP2 ? "checked" : ""} style="accent-color:${rgbCss(P2_RGB)}">',
  "        \u73A9\u5BB6 2",
  "      </label>",
  '      <div class="sep"></div>',
  '      <label class="muted" style="font-size:12px;display:flex;align-items:center;gap:5px">',
  '        <input type="checkbox" id="gridChk" ${state.showGrid !== false ? "checked" : ""} style="accent-color:#4da3ff"> \u7F51\u683C',
  "      </label>",
  '      <div class="sep"></div>',
  '      <label class="muted" style="font-size:12px;display:flex;align-items:center;gap:5px">',
  '        <input type="checkbox" id="routeChk" ${state.showRoute ? "checked" : ""} style="accent-color:#ffd166"> \u8DEF\u7EBF',
  "      </label>",
  '      <label class="muted" style="font-size:12px;display:flex;align-items:center;gap:5px">',
  '        <input type="checkbox" id="routeClearChk" ${state.routeClearOnly ? "checked" : ""} style="accent-color:#ffd166"> \u53EA\u770B\u901A\u5173',
  "      </label>",
  '      <select id="routePick" style="font-size:12px;padding:4px 6px">',
  '        <option value="latest" ${state.routePick === "latest" ? "selected" : ""}>\u6700\u8FD1\u4E00\u5C40</option>',
  '        <option value="all" ${state.routePick === "all" ? "selected" : ""}>\u5168\u90E8\u53E0\u52A0</option>',
  "        ${visibleRoutes().map((r, i) =>",
  '          `<option value="${i}" ${state.routePick === String(i) ? "selected" : ""}>#${(visibleRoutes().length - i)} \xB7 ${r.actor === "player2" ? "P2" : "P1"} \xB7 ${outcomeLabel(r.outcome)} \xB7 ${fmtSec(r.duration_s)} \xB7 ${r.cell_count} \u683C</option>`',
  '        ).join("")}',
  "      </select>",
  "    </div>",
  "",
  '    <div class="heatwrap">',
  '      <canvas id="heat"></canvas>',
  '      <div class="tip" id="tip"></div>',
  "    </div>",
  '    <div class="legend" id="legend"></div>',
  '    <div class="hint">',
  "      \u5750\u6807\u662F Unity \u4E16\u754C\u5750\u6807\uFF0C1 \u683C = ${cellSize()} \u4E2A\u4E16\u754C\u5355\u4F4D\u3002\u7EA2\u8272\u662F\u73A9\u5BB6 1\uFF0C\u84DD\u8272\u662F\u73A9\u5BB6 2\uFF0C\u4E24\u5C42\u5404\u81EA\u6309\u81EA\u5DF1\u7684\u6700\u5927\u503C\u5F52\u4E00\u5316\uFF08\u5426\u5219\u4EBA\u5C11\u7684\u4E00\u5C42\u4F1A\u88AB\u538B\u6CA1\uFF09\u3002",
  "      \u5E26\u9AD8\u4EAE\u63CF\u8FB9\u7684\u8272\u5757\u662F<b>\u8DEF\u7EBF</b>\uFF1A\u6BCF\u8D9F\u5C1D\u8BD5\u7ED3\u675F\uFF08\u901A\u5173 / \u6B7B\u4EA1 / \u4E2D\u9014\u9000\u51FA\uFF09\u90FD\u4F1A\u7559\u4E00\u6761\uFF0C\u9ED8\u8BA4\u53EA\u753B\u901A\u5173\u7684\uFF1B\u52FE\u6389\u300C\u53EA\u770B\u901A\u5173\u300D\u5C31\u80FD\u770B\u5230\u6B7B\u5728\u54EA\u513F\u3001\u534A\u8DEF\u9000\u51FA\u8D70\u5230\u54EA\u513F\u3002",
  "      P2 \u662F\u5EF6\u8FDF\u6A21\u4EFF P1\uFF0C\u4E24\u6761\u8DEF\u7EBF\u5E38\u5E38\u91CD\u5408\uFF0C\u91CD\u5408\u7684\u683C\u5B50\u753B\u6210<b>\u7D2B\u8272\u65B9\u5757</b>\uFF08\u7EA2+\u84DD\u53E0\u51FA\u6765\u7684\uFF09\uFF0C\u548C\u5355\u8272\u65B9\u5757\u4E00\u6837\u5927\uFF0C\u4E0D\u4F1A\u4E92\u76F8\u76D6\u4F4F\u3002",
  "      \u628A\u9F20\u6807\u653E\u5230\u683C\u5B50\u4E0A\u770B\u8BE5\u683C\u7684 P1 / P2 \u6570\u503C\u3002",
  "    </div>",
  "  </div>",
  "",
  '  <div class="card bgedit">',
  '    <h3>\u80CC\u666F\u56FE <small>\u6BCF\u5173\u4E00\u5F20\uFF1B\u4E0D\u4E0A\u4F20\u5C31\u53EA\u6709\u4F60\u81EA\u5DF1\u80FD\u770B\u5230</small> <span class="tag muted" id="bgBadge"></span></h3>',
  '    <div class="row">',
  '      <span class="filebtn"><button id="bgPick">\u9009\u62E9\u56FE\u7247\u2026</button><input type="file" id="bgFile" accept="image/*"></span>',
  '      <button id="bgDel">\u5220\u9664\u672C\u673A\u80CC\u666F</button>',
  '      <button id="bgEdit" class="${state.bgEdit ? "primary" : ""}">${state.bgEdit ? "\u7ED3\u675F\u8C03\u6574" : "\u8C03\u6574\u4F4D\u7F6E/\u5927\u5C0F"}</button>',
  '      <div class="sep"></div>',
  '      <button id="bgFit">\u94FA\u6EE1\u7F51\u683C</button>',
  '      <button id="bgCenter">\u5C45\u4E2D</button>',
  '      <button id="bgRatio">\u8FD8\u539F\u5BBD\u9AD8\u6BD4</button>',
  '      <button id="bgSnap">\u5BF9\u9F50\u5230\u683C\u5B50</button>',
  "    </div>",
  '    <div class="row">',
  '      <button class="primary" id="bgUpload">\u4E0A\u4F20\u5230\u670D\u52A1\u5668\uFF08\u5171\u7528\uFF09</button>',
  '      <button id="bgRemoteDel">\u5220\u9664\u670D\u52A1\u5668\u526F\u672C</button>',
  '      <button id="bgRemoteReload">\u4ECE\u670D\u52A1\u5668\u91CD\u65B0\u52A0\u8F7D</button>',
  '      <div class="sep"></div>',
  '      <label class="muted" style="font-size:11.5px;display:flex;align-items:center;gap:4px">',
  '        <input type="checkbox" id="bgLock" ${state.bgLock ? "checked" : ""} style="accent-color:#4da3ff"> \u9501\u5B9A\u5BBD\u9AD8\u6BD4',
  "      </label>",
  '      <label class="muted" style="font-size:11.5px">\u900F\u660E\u5EA6 <input type="range" id="bgAlpha" min="0.05" max="1" step="0.05"></label>',
  "    </div>",
  '    <div class="row">',
  '      <label class="muted" style="font-size:11.5px">\u5DE6 x <input type="number" id="bgX" step="0.1"></label>',
  '      <label class="muted" style="font-size:11.5px">\u4E0B y <input type="number" id="bgY" step="0.1"></label>',
  '      <label class="muted" style="font-size:11.5px">\u5BBD <input type="number" id="bgW" step="0.1"></label>',
  '      <label class="muted" style="font-size:11.5px">\u9AD8 <input type="number" id="bgH" step="0.1"></label>',
  "    </div>",
  '    <div class="hint" id="bgHint">\u8FD8\u6CA1\u6709\u80CC\u666F\u56FE\u3002\u9009\u4E00\u5F20\u5173\u5361\u622A\u56FE\uFF0C\u7136\u540E\u70B9\u300C\u8C03\u6574\u4F4D\u7F6E/\u5927\u5C0F\u300D\uFF0C\u5728\u753B\u5E03\u4E0A\u62D6\u52A8\u5C31\u80FD\u5BF9\u9F50\u7F51\u683C\uFF1B\u6EDA\u8F6E\u7F29\u653E\u3002</div>',
  "  </div>",
  "",
  '  <div class="grid2">',
  '    <div class="card">',
  '      <h3>\u6700\u70ED\u7684\u683C\u5B50 <small id="topLabel"></small></h3>',
  '      <div class="scroll"><table id="topCells"></table></div>',
  "    </div>",
  '    <div class="card">',
  "      <h3>\u672C\u5173\u6982\u51B5</h3>",
  "      <table>",
  "        <tbody>",
  '          <tr><td class="muted">\u8FDB\u5165\u6B21\u6570</td><td class="num">${fmtNum(sd.stats.starts)}</td></tr>',
  '          <tr><td class="muted">\u901A\u5173 / \u653E\u5F03</td><td class="num">${fmtNum(sd.stats.clears)} / ${fmtNum(sd.stats.quits)}</td></tr>',
  '          <tr><td class="muted">\u5E73\u5747\u5355\u6B21\u65F6\u957F</td><td class="num">${fmtSec(sd.stats.avg_duration_s)}</td></tr>',
  '          <tr><td class="muted">\u6B7B\u4EA1 \u603B\u6570 / P1 / P2</td><td class="num">${fmtNum(sd.stats.deaths)} / <span style="color:${rgbCss(P1_RGB)}">${fmtNum(sd.stats.p1_deaths)}</span> / <span style="color:${rgbCss(P2_RGB)}">${fmtNum(sd.stats.p2_deaths)}</span></td></tr>',
  '          <tr><td class="muted">\u8DF3\u8DC3 \u603B\u6570 / P1 / P2</td><td class="num">${fmtNum(sd.stats.jumps)} / <span style="color:${rgbCss(P1_RGB)}">${fmtNum(sd.stats.p1_jumps)}</span> / <span style="color:${rgbCss(P2_RGB)}">${fmtNum(sd.stats.p2_jumps)}</span></td></tr>',
  '          <tr><td class="muted">\u505C\u7559\u65F6\u957F\u5408\u8BA1</td><td class="num">${fmtSec(sd.stats.dwell_s)}</td></tr>',
  '          <tr><td class="muted">\u6709\u8BB0\u5F55\u7684\u683C\u5B50</td><td class="num">${fmtNum(sd.stats.cells)}</td></tr>',
  '          <tr><td class="muted">\u8303\u56F4</td><td class="num">x ${b.minX}~${b.maxX} \xB7 y ${b.minY}~${b.maxY}</td></tr>',
  "        </tbody>",
  "      </table>",
  "    </div>",
  "  </div>`;",
  "}",
  "",
  "const rgbCss = c => `rgb(${c[0]},${c[1]},${c[2]})`;",
  "",
  "function bindHeatmap() {",
  "  const sd = state.sceneData;",
  '  if (!sd || state.tab !== "heatmap") return;',
  '  const canvas = $("heat");',
  "  if (!canvas) return;",
  "",
  '  canvas.addEventListener("pointerdown", onBgPointerDown);',
  '  canvas.addEventListener("mousemove", onHeatMove);',
  '  canvas.addEventListener("mouseleave", () => { $("tip").style.display = "none"; });',
  '  canvas.addEventListener("wheel", onCanvasWheel, { passive: false });',
  "",
  '  $("metricSeg").querySelectorAll("button").forEach(btn => {',
  '    btn.addEventListener("click", () => {',
  "      state.metric = btn.dataset.metric;",
  "      syncUrl();",
  '      $("metricSeg").querySelectorAll("button").forEach(b =>',
  '        b.classList.toggle("on", b.dataset.metric === state.metric));',
  "      drawHeatmap();",
  "      renderTopCells();",
  "    });",
  "  });",
  "",
  '  $("p1Chk").addEventListener("change", e => { state.showP1 = e.target.checked; syncUrl(); drawHeatmap(); renderTopCells(); });',
  '  $("p2Chk").addEventListener("change", e => { state.showP2 = e.target.checked; syncUrl(); drawHeatmap(); renderTopCells(); });',
  '  $("gridChk").addEventListener("change", e => { state.showGrid = e.target.checked; drawHeatmap(); });',
  '  $("routeChk").addEventListener("change", e => { state.showRoute = e.target.checked; syncUrl(); drawHeatmap(); });',
  '  $("routeClearChk").addEventListener("change", e => {',
  "    state.routeClearOnly = e.target.checked;",
  '    state.routePick = "latest";',
  "    syncUrl();",
  "    renderContent();",
  "  });",
  '  $("routePick").addEventListener("change", e => { state.routePick = e.target.value; syncUrl(); drawHeatmap(); });',
  "",
  "  // \u80CC\u666F\u56FE",
  '  $("bgFile").addEventListener("change", async e => {',
  "    const f = e.target.files && e.target.files[0];",
  "    if (f) await setBgFromFile(f);",
  '    e.target.value = "";',
  "  });",
  '  $("bgDel").addEventListener("click", async () => {',
  "    if (!state.scene) return;",
  "    if (state.bg && state.bg.url) URL.revokeObjectURL(state.bg.url);",
  "    state.bg = null;",
  "    state.bgShared = false;",
  "    try { await bgStore.del(state.scene); } catch (err) { console.warn(err); }",
  "    renderBgBar();",
  "    syncBgInputs();",
  "    drawHeatmap();",
  "  });",
  '  $("bgUpload").addEventListener("click", uploadBg);',
  '  $("bgRemoteDel").addEventListener("click", deleteRemoteBg);',
  '  $("bgRemoteReload").addEventListener("click", async () => {',
  "    if (!state.scene) return;",
  "    // \u4E22\u6389\u672C\u673A\u8349\u7A3F\uFF0C\u91CD\u65B0\u4ECE\u670D\u52A1\u5668\u53D6\uFF08\u60F3\u770B\u522B\u4EBA\u6539\u8FC7\u7684\u7248\u672C\u65F6\u7528\uFF09",
  "    try { await bgStore.del(state.scene); } catch (err) { console.warn(err); }",
  "    await loadBgIndex();",
  "    await loadBg(state.scene);",
  "    renderBgBar();",
  "    syncBgInputs();",
  "    drawHeatmap();",
  '    setStatus(state.bg ? "\u5DF2\u4ECE\u670D\u52A1\u5668\u91CD\u65B0\u52A0\u8F7D\u80CC\u666F\u56FE" : "\u670D\u52A1\u5668\u4E0A\u6CA1\u6709\u8FD9\u4E00\u5173\u7684\u80CC\u666F\u56FE", !state.bg);',
  "  });",
  '  $("bgEdit").addEventListener("click", () => {',
  '    if (!state.bg) { setStatus("\u5148\u9009\u4E00\u5F20\u80CC\u666F\u56FE", true); return; }',
  "    state.bgEdit = !state.bgEdit;",
  '    $("bgEdit").textContent = state.bgEdit ? "\u7ED3\u675F\u8C03\u6574" : "\u8C03\u6574\u4F4D\u7F6E/\u5927\u5C0F";',
  '    $("bgEdit").classList.toggle("primary", state.bgEdit);',
  '    canvas.classList.toggle("editing", state.bgEdit);',
  "    drawHeatmap();",
  "  });",
  '  $("bgFit").addEventListener("click", () => {',
  "    const b = state.sceneData.bounds;",
  "    const cs = cellSize();",
  "    if (!state.bg) return;",
  "    Object.assign(state.bg, { x: b.minX * cs, y: b.minY * cs, w: b.width * cs, h: b.height * cs });",
  "    syncBgInputs(); drawHeatmap(); saveBg();",
  "  });",
  '  $("bgCenter").addEventListener("click", () => {',
  "    const b = state.sceneData.bounds;",
  "    const cs = cellSize();",
  "    if (!state.bg) return;",
  "    state.bg.x = b.minX * cs + (b.width * cs - state.bg.w) / 2;",
  "    state.bg.y = b.minY * cs + (b.height * cs - state.bg.h) / 2;",
  "    syncBgInputs(); drawHeatmap(); saveBg();",
  "  });",
  '  $("bgRatio").addEventListener("click", () => {',
  "    if (!state.bg) return;",
  "    state.bg.h = +(state.bg.w * state.bg.img.naturalHeight / state.bg.img.naturalWidth).toFixed(3);",
  "    syncBgInputs(); drawHeatmap(); saveBg();",
  "  });",
  '  $("bgSnap").addEventListener("click", () => {',
  "    if (!state.bg) return;",
  "    const cs = cellSize();",
  "    Object.assign(state.bg, {",
  "      x: Math.round(state.bg.x / cs) * cs,",
  "      y: Math.round(state.bg.y / cs) * cs,",
  "      w: Math.max(cs, Math.round(state.bg.w / cs) * cs),",
  "      h: Math.max(cs, Math.round(state.bg.h / cs) * cs),",
  "    });",
  "    syncBgInputs(); drawHeatmap(); saveBg();",
  "  });",
  "",
  '  ["bgX", "bgY", "bgW", "bgH"].forEach(id => {',
  '    $(id).addEventListener("change", () => {',
  "      if (!state.bg) return;",
  "      const v = parseFloat($(id).value);",
  "      if (!Number.isFinite(v)) return;",
  '      if (id === "bgW") {',
  "        const oldW = state.bg.w || 1;",
  "        state.bg.w = Math.max(0.05, v);",
  "        if (state.bgLock) state.bg.h = state.bg.h * (state.bg.w / oldW);",
  '      } else if (id === "bgH") {',
  "        const oldH = state.bg.h || 1;",
  "        state.bg.h = Math.max(0.05, v);",
  "        if (state.bgLock) state.bg.w = state.bg.w * (state.bg.h / oldH);",
  '      } else if (id === "bgX") {',
  "        state.bg.x = v;",
  "      } else {",
  "        state.bg.y = v;",
  "      }",
  "      syncBgInputs(); drawHeatmap(); saveBg();",
  "    });",
  "  });",
  '  $("bgLock").addEventListener("change", e => { state.bgLock = e.target.checked; });',
  '  $("bgAlpha").addEventListener("input", e => {',
  "    state.bgAlpha = parseFloat(e.target.value);",
  "    if (state.bg) { state.bg.alpha = state.bgAlpha; drawHeatmap(); }",
  "  });",
  '  $("bgAlpha").addEventListener("change", () => saveBg());',
  "",
  '  canvas.classList.toggle("editing", state.bgEdit);',
  "  syncBgInputs();",
  "  drawHeatmap();",
  "  renderTopCells();",
  "}",
  "",
  "async function setBgFromFile(file) {",
  "  if (!state.scene || !state.sceneData) return;",
  "  try {",
  "    const url = URL.createObjectURL(file);",
  "    const img = await decodeImage(url);",
  "    if (state.bg && state.bg.url) URL.revokeObjectURL(state.bg.url);",
  "    const b = state.sceneData.bounds;",
  "    const cs = cellSize();",
  "    let w = b.width * cs;",
  "    let h = w * img.naturalHeight / img.naturalWidth;",
  "    state.bg = { img, url, blob: file, x: b.minX * cs, y: b.minY * cs, w, h, alpha: state.bgAlpha };",
  "    state.bgShared = false;",
  "    renderBgBar();",
  "    syncBgInputs();",
  "    drawHeatmap();",
  "    await saveBg();",
  "  } catch (err) {",
  '    setStatus("\u56FE\u7247\u8BFB\u53D6\u5931\u8D25\uFF1A" + err.message, true);',
  "  }",
  "}",
  "",
  "function syncBgInputs() {",
  "  const has = !!state.bg;",
  '  ["bgX", "bgY", "bgW", "bgH", "bgAlpha", "bgLock"].forEach(id => { if ($(id)) $(id).disabled = !has; });',
  "  if (!has) {",
  '    if ($("bgHint")) $("bgHint").textContent = "\u8FD8\u6CA1\u6709\u80CC\u666F\u56FE\u3002\u9009\u4E00\u5F20\u5173\u5361\u622A\u56FE\uFF0C\u7136\u540E\u70B9\u300C\u8C03\u6574\u4F4D\u7F6E/\u5927\u5C0F\u300D\uFF0C\u5728\u753B\u5E03\u4E0A\u62D6\u52A8\u5C31\u80FD\u5BF9\u9F50\u7F51\u683C\uFF1B\u6EDA\u8F6E\u7F29\u653E\u3002\u5BF9\u597D\u4E4B\u540E\u70B9\u300C\u4E0A\u4F20\u5230\u670D\u52A1\u5668\u300D\uFF0C\u522B\u4EBA\u6253\u5F00\u540C\u4E00\u5173\u4E5F\u80FD\u770B\u5230\u3002";',
  "    return;",
  "  }",
  '  $("bgX").value = round2(state.bg.x);',
  '  $("bgY").value = round2(state.bg.y);',
  '  $("bgW").value = round2(state.bg.w);',
  '  $("bgH").value = round2(state.bg.h);',
  '  $("bgAlpha").value = state.bg.alpha;',
  '  $("bgLock").checked = state.bgLock;',
  "  const img = state.bg.img;",
  '  if ($("bgHint")) {',
  '    $("bgHint").textContent =',
  "      `\u5F53\u524D\u80CC\u666F\u56FE ${img.naturalWidth}\xD7${img.naturalHeight}px\uFF0C\u653E\u5728\u4E16\u754C\u5750\u6807 x=${round2(state.bg.x)} y=${round2(state.bg.y)}\uFF0C` +",
  "      `\u8986\u76D6 ${round2(state.bg.w)}\xD7${round2(state.bg.h)} \u4E2A\u4E16\u754C\u5355\u4F4D\u3002\u62D6\u52A8=\u4F4D\u79FB\uFF0C\u6EDA\u8F6E=\u7F29\u653E\u3002`;",
  "  }",
  "}",
  "",
  "const round2 = v => Math.round(v * 100) / 100;",
  "",
  "/* ------------------------------------------------ \u7ED8\u5236 */",
  "",
  "function fitCanvas() {",
  '  const canvas = $("heat");',
  "  if (!canvas || !state.sceneData) return null;",
  "  const wrap = canvas.parentElement;",
  "  const b = state.sceneData.bounds;",
  "  const padCells = 1.5;",
  "  const wCells = Math.max(1, b.width) + padCells * 2;",
  "  const hCells = Math.max(1, b.height) + padCells * 2;",
  "",
  "  const availW = Math.max(320, wrap.clientWidth - 2);",
  "  let cellPx = availW / wCells;",
  "  let cssH = cellPx * hCells;",
  "  const maxH = Math.min(760, Math.max(320, window.innerHeight * 0.62));",
  "  if (cssH > maxH) {",
  "    cellPx = maxH / hCells;",
  "    cssH = maxH;",
  "  }",
  "  const cssW = Math.min(availW, cellPx * wCells);",
  "",
  "  const dpr = Math.min(2, window.devicePixelRatio || 1);",
  '  canvas.style.width = cssW + "px";',
  '  canvas.style.height = cssH + "px";',
  "  canvas.width = Math.round(cssW * dpr);",
  "  canvas.height = Math.round(cssH * dpr);",
  "",
  "  const ox = (cssW - cellPx * wCells) / 2 + padCells * cellPx;",
  "  const oy = (cssH - cellPx * hCells) / 2 + padCells * cellPx;",
  "",
  "  state.transform = { ox, oy, cellPx, cssW, cssH, dpr, b };",
  "  return state.transform;",
  "}",
  "",
  "function drawHeatmap() {",
  "  const sd = state.sceneData;",
  '  const canvas = $("heat");',
  "  if (!sd || !canvas) return;",
  "  const t = fitCanvas();",
  "  if (!t) return;",
  '  const ctx = canvas.getContext("2d");',
  "  ctx.setTransform(t.dpr, 0, 0, t.dpr, 0, 0);",
  "  ctx.clearRect(0, 0, t.cssW, t.cssH);",
  "",
  "  drawBackground(ctx, t);",
  "",
  "  const b = t.b;",
  "  const metric = state.metric;",
  "  const cells = sd.cells;",
  "  const maxP1 = Math.max(1e-6, ...cells.map(c => metricValue(c.p1, metric)));",
  "  const maxP2 = Math.max(1e-6, ...cells.map(c => metricValue(c.p2, metric)));",
  "",
  '  // \u505C\u7559\u65F6\u957F\u6309 1 \u79D2\u4E00\u6863\u4E0A\u8272\uFF1A0~1 \u79D2\u4E00\u6863\u30011~2 \u79D2\u4E0B\u4E00\u6863\u2026\u2026\u8FD9\u6837\u989C\u8272\u6DF1\u6D45\u76F4\u63A5\u5BF9\u5E94"\u5F85\u4E86\u51E0\u79D2"\uFF0C',
  "  // \u4E0D\u4F1A\u88AB\u4E2A\u522B\u8D85\u957F\u505C\u7559\uFF08\u6302\u673A\u3001\u5361\u4F4F\uFF09\u628A\u5176\u5B83\u683C\u5B50\u538B\u6210\u540C\u4E00\u6863\u3002",
  "  const stepTop = (v, max) => {",
  '    if (metric !== "dwell") return 0.14 + 0.86 * Math.sqrt(v / max);',
  "    const level = Math.floor(v); // 1 \u79D2\u4E00\u6863",
  "    const levels = Math.max(1, Math.floor(max));",
  "    return 0.14 + 0.86 * (Math.min(level, levels) / levels);",
  "  };",
  "",
  "  // \u7F51\u683C",
  "  if (state.showGrid !== false) {",
  '    ctx.strokeStyle = "rgba(255,255,255,.055)";',
  "    ctx.lineWidth = 1;",
  "    for (let x = 0; x <= b.width; x++) {",
  "      const sx = Math.round(t.ox + x * t.cellPx) + 0.5;",
  "      ctx.beginPath(); ctx.moveTo(sx, t.oy); ctx.lineTo(sx, t.oy + b.height * t.cellPx); ctx.stroke();",
  "    }",
  "    for (let y = 0; y <= b.height; y++) {",
  "      const sy = Math.round(t.oy + y * t.cellPx) + 0.5;",
  "      ctx.beginPath(); ctx.moveTo(t.ox, sy); ctx.lineTo(t.ox + b.width * t.cellPx, sy); ctx.stroke();",
  "    }",
  "  }",
  "",
  "  // \u683C\u5B50\uFF1A\u5148 P1 \u540E P2\uFF0C\u4E24\u5C42\u53E0\u52A0",
  "  for (const cell of cells) {",
  "    const sx = t.ox + (cell.x - b.minX) * t.cellPx;",
  "    const sy = t.oy + (b.maxY - cell.y) * t.cellPx;",
  "    if (state.showP1) {",
  "      const v = metricValue(cell.p1, metric);",
  "      if (v > 0) {",
  "        const a = stepTop(v, maxP1);",
  "        ctx.fillStyle = `rgba(${P1_RGB[0]},${P1_RGB[1]},${P1_RGB[2]},${a.toFixed(3)})`;",
  "        ctx.fillRect(sx, sy, t.cellPx, t.cellPx);",
  "      }",
  "    }",
  "    if (state.showP2) {",
  "      const v = metricValue(cell.p2, metric);",
  "      if (v > 0) {",
  "        const a = stepTop(v, maxP2);",
  "        ctx.fillStyle = `rgba(${P2_RGB[0]},${P2_RGB[1]},${P2_RGB[2]},${a.toFixed(3)})`;",
  "        ctx.fillRect(sx, sy, t.cellPx, t.cellPx);",
  "      }",
  "    }",
  "  }",
  "",
  "  // \u901A\u5173\u8DEF\u7EBF\uFF08\u753B\u5728\u70ED\u529B\u683C\u5B50\u4E4B\u4E0A\uFF1A\u6DE1\u586B\u5145 + \u540C\u8272\u9AD8\u4EAE\u63CF\u8FB9\uFF0C\u65E2\u80FD\u770B\u6E05\u53C8\u4E0D\u4F1A\u76D6\u6389\u70ED\u529B\uFF09",
  "  if (state.showRoute !== false) drawRoutes(ctx, t);",
  "",
  "  // \u6570\u636E\u8303\u56F4\u6846",
  '  ctx.strokeStyle = "rgba(120,140,170,.35)";',
  "  ctx.lineWidth = 1;",
  "  ctx.strokeRect(t.ox + 0.5, t.oy + 0.5, b.width * t.cellPx - 1, b.height * t.cellPx - 1);",
  "",
  "  // \u8F74\u6807\u6CE8",
  '  ctx.fillStyle = "rgba(139,148,158,.85)";',
  '  ctx.font = "11px -apple-system,Segoe UI,sans-serif";',
  "  ctx.fillText(`x ${toWorld(b.minX)} \u2192 ${toWorld(b.maxX + 1)}`, t.ox, t.oy + b.height * t.cellPx + 15);",
  "  ctx.fillText(`y ${toWorld(b.maxY + 1)}`, Math.max(2, t.ox - 3), t.oy - 5);",
  "",
  "  renderLegend(maxP1, maxP2);",
  "}",
  "",
  "// \u300C\u53EA\u770B\u901A\u5173\u300D\u6253\u5F00\u65F6\u628A\u6B7B\u4EA1/\u4E2D\u65AD\u7684\u8DEF\u7EBF\u8FC7\u6EE4\u6389\uFF1B\u670D\u52A1\u7AEF\u6309\u65F6\u95F4\u5012\u5E8F\u7ED9\uFF0C\u6240\u4EE5\u987A\u5E8F\u4E0D\u53D8",
  "function visibleRoutes() {",
  "  const routes = (state.sceneData && state.sceneData.routes) || [];",
  "  if (!state.routeClearOnly) return routes;",
  '  return routes.filter((r) => (r.outcome || "clear") === "clear");',
  "}",
  "",
  "function outcomeLabel(outcome) {",
  '  if (outcome === "death") return "\u6B7B\u4EA1";',
  '  if (outcome === "quit") return "\u4E2D\u9014\u9000\u51FA";',
  '  return "\u901A\u5173";',
  "}",
  "",
  "// \u5F53\u524D\u8981\u5728\u56FE\u4E0A\u753B\u7684\u8DEF\u7EBF\uFF1Alatest = \u6700\u8FD1\u4E00\u8D9F\uFF08\u540C\u4E00\u5C40\u7684 P1/P2 \u5404\u53D6\u6700\u65B0\u4E00\u6761\uFF09\uFF0Call = \u5168\u90E8\u53E0\u52A0",
  "function routesToDraw() {",
  "  const routes = visibleRoutes();",
  "  if (!routes.length) return [];",
  "",
  '  // \u4E00\u6B21"\u5173\u5361\u8FDB\u5165"\uFF08attempt\uFF09\u91CC P1/P2 \u5404\u81EA\u53EF\u80FD\u6709\u591A\u6761\u8DEF\u7EBF\uFF08\u6BCF\u6B21\u6B7B\u4EA1\u90FD\u4F1A\u7ED9\u6B7B\u6389\u7684\u90A3\u4E2A\u89D2\u8272\u7559\u4E00\u6761\uFF09\uFF0C',
  "  // \u6240\u4EE5\u6309 attempt \u5206\u7EC4\u3001\u6BCF\u4E2A\u89D2\u8272\u53EA\u53D6\u6700\u65B0\u90A3\u6761\u3002\u6CE8\u610F\u4E0D\u80FD\u518D\u6309 ts \u76F8\u8FD1\uFF082 \u79D2\uFF09\u914D\u5BF9\uFF1A",
  "  // P1 \u6B7B\u4E86\u4E4B\u540E P2 \u53EF\u80FD\u9694\u597D\u51E0\u79D2\u624D\u505C\u4E0B\uFF0Cts \u5DEE\u5F97\u591A\uFF0C\u914D\u4E0D\u4E0A\u5C31\u53EA\u5269\u4E00\u6761\u8DEF\u7EBF \u2014\u2014",
  '  // \u8868\u73B0\u5C31\u662F"\u53EA\u5269\u7EA2\u8DEF\u7EBF\uFF0C\u84DD\u8DEF\u7EBF\u8981\u5F00\u5173\u4E00\u6B21\u53EA\u770B\u901A\u5173\u624D\u51FA\u6765"\u3002',
  "  const perActorLatest = (list) => {",
  "    const seen = new Set();",
  "    const out = [];",
  "    for (const r of list) {",
  '      const who = r.actor || "player1";',
  "      if (seen.has(who)) continue;",
  "      seen.add(who);",
  "      out.push(r);",
  "    }",
  "    return out;",
  "  };",
  "",
  '  if (state.routePick === "all") return routes;',
  '  if (state.routePick === "latest") {',
  "    const newest = routes[0];",
  "    const sameAttempt = routes.filter((r) => r.attempt === newest.attempt);",
  "    const picked = perActorLatest(sameAttempt.length ? sameAttempt : [newest]);",
  "    return picked.length ? picked : [newest];",
  "  }",
  "  const i = parseInt(state.routePick, 10);",
  "  if (Number.isFinite(i) && routes[i]) {",
  "    const picked = perActorLatest(routes.filter((r) => r.attempt === routes[i].attempt));",
  "    return picked.length ? picked : [routes[i]];",
  "  }",
  "  return [routes[0]];",
  "}",
  "",
  '// \u5BA2\u6237\u7AEF\u73B0\u5728\u662F"\u6BCF\u5E27\u8FDB\u5165\u65B0\u683C\u5B50\u5C31\u8BB0\u4E00\u683C"\uFF0C\u6B63\u5E38\u79FB\u52A8\u76F8\u90BB\u4E24\u683C\u4E00\u5B9A\u662F\u6328\u7740\u7684\u3002',
  "// \u53EA\u6709\u6781\u5FEB\u79FB\u52A8\uFF08\u4E0B\u843D\u51B2\u523A\uFF09\u6216\u65E7\u7248\u672C 0.1 \u79D2\u91C7\u6837\u7684\u6570\u636E\u624D\u4F1A\u9694\u5F00\u51E0\u683C\uFF1A",
  "// \u9694 1~4 \u683C\u8865\u6210\u76F4\u7EBF\u8FD8\u7B97\u5FE0\u5B9E\uFF08\u4E00\u5E27\u5185\u786E\u5B9E\u662F\u4ECE A \u76F4\u7740\u98DE\u5230 B\uFF09\uFF0C",
  '// \u9694\u5F97\u66F4\u591A\u5C31\u8BF4\u660E\u4E2D\u95F4\u90A3\u51E0\u4E2A\u683C\u5B50\u6CA1\u91C7\u5230\uFF0C\u786C\u8FDE\u4F1A\u753B\u51FA"\u659C\u7A7F\u5730\u56FE"\u7684\u5047\u8DEF\u7EBF \u2014\u2014 \u76F4\u63A5\u65AD\u5F00\u4E0D\u8FDE\u3002',
  "function densifyRoute(cells) {",
  "  const out = [];",
  "  const seen = new Set();",
  "  const push = (x, y) => {",
  '    const k = x + "," + y;',
  "    if (seen.has(k)) return;",
  "    seen.add(k);",
  "    out.push([x, y]);",
  "  };",
  "  for (let i = 0; i < cells.length; i++) {",
  "    const [x1, y1] = cells[i];",
  "    push(x1, y1);",
  "    const nxt = cells[i + 1];",
  "    if (!nxt) break;",
  "    const [x2, y2] = nxt;",
  "    let dx = Math.abs(x2 - x1);",
  "    let dy = Math.abs(y2 - y1);",
  "    if (dx + dy > 4) continue; // \u91C7\u96C6\u4E2D\u65AD / \u4F20\u9001 / \u91CD\u751F\uFF1A\u4E0D\u8FDE\uFF0C\u522B\u7F16\u9020\u8DEF\u5F84",
  "    const sx = x1 < x2 ? 1 : -1;",
  "    const sy = y1 < y2 ? 1 : -1;",
  "    let err = dx - dy;",
  "    let cx = x1;",
  "    let cy = y1;",
  "    while (cx !== x2 || cy !== y2) {",
  "      const e2 = 2 * err;",
  "      if (e2 > -dy) { err -= dy; cx += sx; }",
  "      if (e2 < dx) { err += dx; cy += sy; }",
  "      push(cx, cy);",
  "    }",
  "  }",
  "  return out;",
  "}",
  "",
  "function drawRoutes(ctx, t) {",
  "  const b = t.b;",
  "  // P2 \u662F\u5EF6\u8FDF\u6A21\u4EFF P1\uFF0C\u4E24\u6761\u8DEF\u7EBF\u7ECF\u5E38\u5B8C\u5168\u91CD\u5408\u3002\u505A\u6CD5\uFF1A",
  '  //   \u2460 \u5148\u6309\u683C\u5B50\u6536\u96C6"\u8FD9\u4E00\u683C\u8C01\u8D70\u8FC7"\uFF0C\u987A\u5E8F\u4E0D\u4F9D\u8D56\u63A5\u53E3\u8FD4\u56DE\uFF1B',
  "  //   \u2461 \u53EA\u6709\u4E00\u6761\u8D70\u8FC7 \u2192 \u6574\u683C\u753B\u8BE5\u89D2\u8272\u7684\u989C\u8272\uFF1B",
  "  //   \u2462 \u4E24\u6761\u90FD\u8D70\u8FC7 \u2192 \u540C\u4E00\u683C**\u5DE6\u53F3\u5BF9\u534A\u5206**\uFF08\u5DE6\u7EA2\u53F3\u84DD\uFF09\uFF0C\u4E24\u8FB9\u4E00\u6837\u5927\uFF0C",
  '  //      \u4E0D\u4F1A\u51FA\u73B0"\u540E\u753B\u7684\u628A\u5148\u753B\u7684\u6574\u5757\u76D6\u4F4F"\u6216"\u84DD\u65B9\u5757\u6BD4\u7EA2\u65B9\u5757\u5C0F"\u3002',
  "  const list = routesToDraw()",
  '    .filter((r) => (r.actor === "player2" ? state.showP2 !== false : state.showP1 !== false))',
  '    .sort((a, r) => (a.actor === "player2" ? 1 : 0) - (r.actor === "player2" ? 1 : 0));',
  "",
  '  const marks = new Map(); // "x,y" -> { p1: bool, p2: bool }',
  "  for (const r of list) {",
  '    const which = r.actor === "player2" ? "p2" : "p1";',
  "    for (const [x, y] of densifyRoute(r.cells)) {",
  "      if (x < b.minX || x > b.maxX || y < b.minY || y > b.maxY) continue;",
  '      const k = x + "," + y;',
  "      let m = marks.get(k);",
  "      if (!m) { m = { p1: false, p2: false }; marks.set(k, m); }",
  "      m[which] = true;",
  "    }",
  "  }",
  "",
  "  // \u6574\u683C\u5BBD\u5EA6\u3001\u5C0F\u5757\u7559\u4E00\u70B9\u7F1D\uFF0C\u4E0D\u540C\u683C\u5B50\u4E4B\u95F4\u624D\u770B\u5F97\u6E05\u8FB9\u754C",
  "  const inset = t.cellPx * 0.04;",
  "  const side = Math.max(1, t.cellPx - inset * 2);",
  "  const lw = Math.max(1, Math.min(2, t.cellPx * 0.16));",
  "  const rgbStr = (rgb, a) => `rgba(${rgb[0]},${rgb[1]},${rgb[2]},${a})`;",
  "",
  "  for (const [k, m] of marks) {",
  '    const [x, y] = k.split(",").map(Number);',
  "    const sx = t.ox + (x - b.minX) * t.cellPx + inset;",
  "    const sy = t.oy + (b.maxY - y) * t.cellPx + inset;",
  "    // \u586B\u5145\u538B\u5F97\u6BD4\u8F83\u6DE1\u3001\u8FB9\u6846\u7528\u540C\u8272\u9AD8\u4EAE\uFF1A\u8DEF\u7EBF\u770B\u5F97\u6E05\uFF0C\u5E95\u4E0B\u7684\u70ED\u529B\u683C\u5B50\u4E5F\u4E0D\u4F1A\u88AB\u5B8C\u5168\u76D6\u4F4F",
  "    if (m.p1 && m.p2) {",
  "      // \u4E24\u6761\u90FD\u8D70\u8FC7\uFF1A\u753B\u6210\u4E00\u4E2A\u7D2B\u8272\u65B9\u5757\uFF08\u7EA2+\u84DD\u53E0\u51FA\u6765\u7684\u989C\u8272\uFF09\uFF0C",
  '      // \u548C"\u53EA\u6709\u4E00\u6761\u8D70\u8FC7"\u7684\u65B9\u5757\u4E00\u6837\u5927\u3001\u4E00\u6837\u7C97\uFF0C\u4E0D\u4F1A\u50CF\u540C\u5FC3\u53CC\u73AF\u90A3\u6837\u8BA9\u84DD\u6846\u663E\u5F97\u66F4\u5C0F\u3002',
  "      ctx.fillStyle = rgbStr(BOTH_RGB, 0.45);",
  "      ctx.strokeStyle = rgbStr(BOTH_RGB, 0.95);",
  "      ctx.lineWidth = lw;",
  "      ctx.fillRect(sx, sy, side, side);",
  "      ctx.strokeRect(sx + 0.5, sy + 0.5, Math.max(1, side - 1), Math.max(1, side - 1));",
  "    } else {",
  "      const rgb = m.p1 ? P1_RGB : P2_RGB;",
  "      ctx.fillStyle = rgbStr(rgb, 0.28);",
  "      ctx.strokeStyle = rgbStr(rgb, 0.95);",
  "      ctx.lineWidth = lw;",
  "      ctx.fillRect(sx, sy, side, side);",
  "      ctx.strokeRect(sx + 0.5, sy + 0.5, Math.max(1, side - 1), Math.max(1, side - 1));",
  "    }",
  "  }",
  "}",
  "",
  "function drawBackground(ctx, t) {",
  "  const bg = state.bg;",
  "  const b = t.b;",
  "  ctx.save();",
  "  ctx.globalAlpha = state.bgAlpha;",
  "  if (bg && state.showBg !== false) {",
  "    const cs = cellSize();",
  "    const bx = bg.x / cs;",
  "    const by = bg.y / cs;",
  "    const bw = bg.w / cs;",
  "    const bh = bg.h / cs;",
  "    const wx = t.ox + (bx - b.minX) * t.cellPx;",
  "    const wy = t.oy + (b.maxY + 1 - (by + bh)) * t.cellPx;",
  "    const ww = bw * t.cellPx;",
  "    const wh = bh * t.cellPx;",
  "    try { ctx.drawImage(bg.img, wx, wy, ww, wh); } catch {}",
  "    if (state.bgEdit) {",
  "      ctx.globalAlpha = 1;",
  '      ctx.strokeStyle = "#2f81f7";',
  "      ctx.setLineDash([5, 4]);",
  "      ctx.lineWidth = 1.5;",
  "      ctx.strokeRect(wx, wy, ww, wh);",
  "      ctx.setLineDash([]);",
  '      ctx.fillStyle = "#2f81f7";',
  "      [[wx, wy], [wx + ww, wy], [wx, wy + wh], [wx + ww, wy + wh]].forEach(([px, py]) => {",
  "        ctx.fillRect(px - 3, py - 3, 6, 6);",
  "      });",
  "    }",
  "  }",
  "  ctx.restore();",
  "}",
  "",
  "function renderLegend(maxP1, maxP2) {",
  "  const label = METRICS[state.metric].label;",
  '  const isTime = state.metric === "dwell";',
  '  const unit = isTime ? "\u79D2" : "\u6B21";',
  "  const fmt = v => (isTime ? v.toFixed(0) : Math.round(v));",
  "  const g1 = `linear-gradient(90deg, rgba(${P1_RGB},.14), rgba(${P1_RGB},1))`;",
  "  const g2 = `linear-gradient(90deg, rgba(${P2_RGB},.14), rgba(${P2_RGB},1))`;",
  '  $("legend").innerHTML = `',
  "    <span>${esc(label)}\uFF1A</span>",
  '    <span><i class="grad" style="background:${g1}"></i>\u73A9\u5BB6 1 0 \u2192 ${fmt(maxP1)} ${unit}</span>',
  '    <span><i class="grad" style="background:${g2}"></i>\u73A9\u5BB6 2 0 \u2192 ${fmt(maxP2)} ${unit}</span>',
  '    <span class="muted">${isTime ? "\uFF08\u6BCF 1 \u79D2\u4E00\u6863\uFF0C\u4E24\u5C42\u5404\u81EA\u5F52\u4E00\u5316\uFF09" : "\uFF08\u4E24\u5C42\u5404\u81EA\u5F52\u4E00\u5316\uFF09"}</span>',
  "    ${visibleRoutes().length",
  '      ? `<span class="muted">\u63CF\u8FB9\u8272\u5757 = \u8DEF\u7EBF\uFF08${state.routeClearOnly ? "\u53EA\u753B\u901A\u5173" : "\u542B\u6B7B\u4EA1/\u4E2D\u9014\u9000\u51FA"}\uFF09</span>`',
  "      : ((state.sceneData && state.sceneData.routes) || []).length",
  '        ? `<span class="muted">\u8FD9\u4E00\u5173\u8FD8\u6CA1\u6709\u901A\u5173\u8DEF\u7EBF\uFF0C\u52FE\u6389\u300C\u53EA\u770B\u901A\u5173\u300D\u80FD\u770B\u5230\u6B7B\u4EA1 / \u4E2D\u9014\u9000\u51FA\u7684\u8DEF\u7EBF</span>`',
  '        : ""}`;',
  "}",
  "",
  "function worldFromEvent(ev) {",
  "  const t = state.transform;",
  "  if (!t) return null;",
  '  const rect = $("heat").getBoundingClientRect();',
  "  const px = ev.clientX - rect.left;",
  "  const py = ev.clientY - rect.top;",
  "  const x = Math.floor((px - t.ox) / t.cellPx) + t.b.minX;",
  "  const y = t.b.maxY - Math.floor((py - t.oy) / t.cellPx);",
  "  return { x, y };",
  "}",
  "",
  "function cellAt(x, y) {",
  "  const cells = state.sceneData ? state.sceneData.cells : [];",
  "  return cells.find(c => c.x === x && c.y === y) || null;",
  "}",
  "",
  "function onHeatMove(ev) {",
  "  if (state.bgEdit) return;",
  "  const p = worldFromEvent(ev);",
  "  if (!p) return;",
  '  const rect = $("heat").getBoundingClientRect();',
  '  const tip = $("tip");',
  "  const b = state.transform.b;",
  '  if (p.x < b.minX || p.x > b.maxX || p.y < b.minY || p.y > b.maxY) { tip.style.display = "none"; return; }',
  "  const cell = cellAt(p.x, p.y);",
  "  tip.innerHTML = cell",
  "    ? `<b>\u4E16\u754C (${toWorld(cell.x)}, ${toWorld(cell.y)}) \xB7 \u7B2C ${cell.x},${cell.y} \u683C</b><br>",
  '       <span style="color:${rgbCss(P1_RGB)}">P1</span> \u6B7B ${cell.p1.deaths} \xB7 \u8DF3 ${cell.p1.jumps} \xB7 \u505C ${cell.p1.dwell.toFixed(1)}s<br>',
  '       <span style="color:${rgbCss(P2_RGB)}">P2</span> \u6B7B ${cell.p2.deaths} \xB7 \u8DF3 ${cell.p2.jumps} \xB7 \u505C ${cell.p2.dwell.toFixed(1)}s',
  '       ${cell.causes && cell.causes.length ? `<br><span class="muted">\u6B7B\u56E0\uFF1A${esc(cell.causes.join(" / "))}</span>` : ""}`',
  '    : `<b>\u4E16\u754C (${toWorld(p.x)}, ${toWorld(p.y)})</b><br><span class="muted">\u6CA1\u6709\u6570\u636E</span>`;',
  '  tip.style.display = "block";',
  "  const tw = tip.offsetWidth, th = tip.offsetHeight;",
  "  let left = ev.clientX - rect.left + 14;",
  "  let top = ev.clientY - rect.top + 14;",
  "  if (left + tw > rect.width) left = ev.clientX - rect.left - tw - 12;",
  "  if (top + th > rect.height) top = ev.clientY - rect.top - th - 12;",
  '  tip.style.left = Math.max(2, left) + "px";',
  '  tip.style.top = Math.max(2, top) + "px";',
  "}",
  "",
  "/* ------------------------------------------------ \u80CC\u666F\u56FE\u62D6\u52A8/\u7F29\u653E */",
  "",
  "let dragState = null;",
  "",
  "function onBgPointerDown(ev) {",
  "  if (!state.bgEdit || !state.bg) return;",
  "  const t = state.transform;",
  "  if (!t) return;",
  "  ev.preventDefault();",
  '  $("heat").setPointerCapture(ev.pointerId);',
  "  dragState = { px: ev.clientX, py: ev.clientY, x: state.bg.x, y: state.bg.y };",
  "",
  "  const move = e2 => {",
  "    if (!dragState) return;",
  "    const cs = cellSize();",
  "    const dWx = (e2.clientX - dragState.px) / t.cellPx * cs;",
  "    const dWy = (e2.clientY - dragState.py) / t.cellPx * cs;",
  "    state.bg.x = round2(dragState.x + dWx);",
  "    state.bg.y = round2(dragState.y - dWy);",
  "    syncBgInputs();",
  "    drawHeatmap();",
  "  };",
  "  const up = () => {",
  "    dragState = null;",
  '    window.removeEventListener("pointermove", move);',
  '    window.removeEventListener("pointerup", up);',
  "    saveBg();",
  "  };",
  '  window.addEventListener("pointermove", move);',
  '  window.addEventListener("pointerup", up);',
  "}",
  "",
  "function onCanvasWheel(ev) {",
  "  if (!state.bgEdit || !state.bg) return;",
  "  ev.preventDefault();",
  "  const f = ev.deltaY < 0 ? 1.06 : 1 / 1.06;",
  "  const cx = state.bg.x + state.bg.w / 2;",
  "  const cy = state.bg.y + state.bg.h / 2;",
  "  state.bg.w = round2(Math.max(0.1, state.bg.w * f));",
  "  state.bg.h = round2(Math.max(0.1, state.bg.h * f));",
  "  state.bg.x = round2(cx - state.bg.w / 2);",
  "  state.bg.y = round2(cy - state.bg.h / 2);",
  "  syncBgInputs();",
  "  drawHeatmap();",
  "  clearTimeout(onCanvasWheel._t);",
  "  onCanvasWheel._t = setTimeout(saveBg, 400);",
  "}",
  "",
  "function renderTopCells() {",
  "  const sd = state.sceneData;",
  '  const tbl = $("topCells");',
  "  if (!sd || !tbl) return;",
  "  const metric = state.metric;",
  "  const val = c => metricValue(c.p1, metric) + metricValue(c.p2, metric);",
  "  const sorted = sd.cells.slice().sort((a, b) => val(b) - val(a)).slice(0, 15);",
  '  $("topLabel").textContent = "\u6309" + METRICS[metric].label + "\u6392\u5E8F";',
  '  const isTime = metric === "dwell";',
  '  const fmt = v => (isTime ? v.toFixed(1) + "s" : String(v));',
  "",
  "  tbl.innerHTML = `<thead><tr>",
  '      <th>\u683C\u5B50\uFF08\u4E16\u754C\u5750\u6807\uFF09</th><th class="num">${esc(METRICS[metric].label)}</th>',
  '      <th class="num">P1</th><th class="num">P2</th><th class="num">\u6B7B\u4EA1</th>',
  "    </tr></thead><tbody>` + sorted.map(c => `<tr>",
  "      <td>(${toWorld(c.x)}, ${toWorld(c.y)})</td>",
  '      <td class="num">${fmt(val(c))}</td>',
  '      <td class="num" style="color:${rgbCss(P1_RGB)}">${fmt(metricValue(c.p1, metric))}</td>',
  '      <td class="num" style="color:${rgbCss(P2_RGB)}">${fmt(metricValue(c.p2, metric))}</td>',
  '      <td class="num">${c.deaths}</td>',
  '    </tr>`).join("") + "</tbody>";',
  "}",
  "",
  "/* ================================================================ \u6A2A\u5411\u5BF9\u6BD4 */",
  "",
  "const COMPARE_ROWS = [",
  '  { key: "starts", label: "\u8FDB\u5165\u6B21\u6570", fmt: fmtNum, max: "starts", better: "high" },',
  '  { key: "clear_rate", label: "\u901A\u5173\u7387", fmt: fmtPct, max: "clear_rate", better: "high" },',
  '  { key: "deaths", label: "\u6B7B\u4EA1\u603B\u6570", fmt: fmtNum, max: "deaths", better: "low" },',
  '  { key: "p1_deaths", label: "\u6B7B\u4EA1 \xB7 P1", fmt: fmtNum, max: "deaths", better: "low", color: "p1" },',
  '  { key: "p2_deaths", label: "\u6B7B\u4EA1 \xB7 P2", fmt: fmtNum, max: "deaths", better: "low", color: "p2" },',
  '  { key: "deaths_per_start", label: "\u6BCF\u6B21\u8FDB\u5165\u6B7B\u4EA1", fmt: v => (v === null ? "\u2013" : v.toFixed(2)), max: "deaths_per_start", better: "low" },',
  '  { key: "avg_duration_s", label: "\u5E73\u5747\u5355\u6B21\u65F6\u957F", fmt: fmtSec, max: "avg_duration_s", better: "low" },',
  '  { key: "jumps", label: "\u8DF3\u8DC3\u603B\u6570", fmt: fmtNum, max: "jumps", better: "low" },',
  '  { key: "p1_jumps", label: "\u8DF3\u8DC3 \xB7 P1", fmt: fmtNum, max: "jumps", better: "low", color: "p1" },',
  '  { key: "p2_jumps", label: "\u8DF3\u8DC3 \xB7 P2", fmt: fmtNum, max: "jumps", better: "low", color: "p2" },',
  '  { key: "dwell_s", label: "\u505C\u7559\u65F6\u957F", fmt: fmtSec, max: "dwell_s", better: "low" },',
  '  { key: "cells", label: "\u6709\u8BB0\u5F55\u7684\u683C\u5B50", fmt: fmtNum, max: "cells", better: "low" },',
  "];",
  "",
  "async function renderCompare() {",
  '  const box = $("content");',
  "  let data;",
  "  try {",
  "    data = await loadCompare();",
  "  } catch (err) {",
  '    box.innerHTML = `<div class="card"><div class="empty">\u5BF9\u6BD4\u6570\u636E\u8BFB\u4E0D\u5230\uFF1A${esc(err.message)}</div></div>`;',
  "    return;",
  "  }",
  '  if (state.tab !== "compare") return;',
  "",
  "  if (!data.scenes.length) {",
  '    box.innerHTML = `<div class="card"><div class="empty">\u8FD8\u6CA1\u6709\u5173\u5361\u6570\u636E</div></div>`;',
  "    return;",
  "  }",
  "",
  "  const scenes = data.scenes.slice().sort((a, b) => numOr(b[state.compareSort], 0) - numOr(a[state.compareSort], 0));",
  "  const max = data.max;",
  "",
  "  const header = `<tr><th>\u6307\u6807</th>${scenes.map(s =>",
  '    `<th class="num" style="min-width:104px">${esc(s.scene)}<br><span class="muted" style="font-weight:400">#${s.level_index === null ? "\u2013" : s.level_index}</span></th>`',
  '  ).join("")}</tr>`;',
  "",
  "  const rows = COMPARE_ROWS.map(row => {",
  "    const values = scenes.map(s => s[row.key]);",
  '    const nums = values.filter(v => typeof v === "number" && Number.isFinite(v));',
  '    const best = row.better === "high" ? Math.max(...nums) : Math.min(...nums);',
  '    const worst = row.better === "high" ? Math.min(...nums) : Math.max(...nums);',
  "    const scale = Math.max(1e-9, max[row.max] || 0);",
  "",
  "    const tds = values.map((v, i) => {",
  '      const cls = ["num"];',
  '      if (nums.length > 1 && v === best) cls.push("best");',
  '      if (nums.length > 1 && v === worst) cls.push("worst");',
  "      const w = Math.max(0, Math.min(100, (Number(v || 0) / scale) * 100));",
  '      const barCls = row.color === "p1" ? "bar red" : row.color === "p2" ? "bar blue" : "bar";',
  '      return `<td class="${cls.join(" ")}" style="min-width:104px">',
  '        <div class="barcell">',
  '          <div class="${barCls}" style="flex:1"><i style="width:${w.toFixed(1)}%"></i></div>',
  '          <span class="barval">${esc(row.fmt(v))}</span>',
  "        </div>",
  "      </td>`;",
  '    }).join("");',
  "",
  '    return `<tr><td>${esc(row.label)}${row.color ? `<span class="tag ${row.color}">${row.color === "p1" ? "P1" : "P2"}</span>` : ""}</td>${tds}</tr>`;',
  '  }).join("");',
  "",
  "  box.innerHTML = `",
  '    <div class="card">',
  '      <div class="toolbar">',
  '        <h3 style="margin:0">\u6A2A\u5411\u5BF9\u6BD4 <small>\u6BCF\u4E00\u5217\u662F\u4E00\u5173\uFF0C\u6761\u5F62\u6309\u8FD9\u4E00\u884C\u7684\u6700\u5927\u503C\u7F29\u653E</small></h3>',
  '        <div class="spacer" style="flex:1"></div>',
  '        <label class="muted" style="font-size:11.5px">\u6392\u5E8F\u5217',
  '          <select id="cmpSort">',
  '            ${COMPARE_ROWS.map(r => `<option value="${r.key}" ${state.compareSort === r.key ? "selected" : ""}>${esc(r.label)}</option>`).join("")}',
  "          </select>",
  "        </label>",
  "      </div>",
  '      <div class="cmp scroll" style="max-height:none">',
  "        <table>${header}${rows}</table>",
  "      </div>",
  '      <div class="hint">',
  "        \u7EFF\u8272 = \u8FD9\u4E00\u884C\u6700\u597D\u7684\u4E00\u5173\uFF0C\u7EA2\u8272 = \u6700\u5DEE\u7684\u4E00\u5173\u3002\u300C\u6BCF\u6B21\u8FDB\u5165\u6B7B\u4EA1\u300D\u6BD4\u88F8\u6B7B\u4EA1\u6570\u516C\u5E73\uFF1A\u5B83\u6392\u9664\u4E86\u201C\u8FDB\u5F97\u591A\u6240\u4EE5\u6B7B\u5F97\u591A\u201D\u3002",
  "        \u8DF3\u8DC3\u6570\u548C\u505C\u7559\u65F6\u957F\u504F\u9AD8\u901A\u5E38\u610F\u5473\u7740\u8FD9\u4E00\u5173\u8981\u4E48\u96BE\u3001\u8981\u4E48\u8BA9\u4EBA\u539F\u5730\u72B9\u8C6B\u3002",
  "      </div>",
  "    </div>`;",
  "",
  '  $("cmpSort").addEventListener("change", e => {',
  "    state.compareSort = e.target.value;",
  "    renderCompare();",
  "  });",
  "}",
  "",
  'const numOr = (v, d) => (typeof v === "number" && Number.isFinite(v) ? v : d);',
  "",
  "/* ================================================================ \u6B7B\u4EA1 */",
  "",
  "function viewDeaths() {",
  "  const sd = state.sceneData;",
  '  if (!sd) return `<div class="card"><div class="empty">\u5148\u9009\u4E00\u5173</div></div>`;',
  "",
  "  const causes = {};",
  "  for (const d of sd.deaths) {",
  '    const k = d.cause || "\u672A\u77E5";',
  "    if (!causes[k]) causes[k] = { n: 0, p1: 0, p2: 0 };",
  "    causes[k].n++;",
  '    if (d.actor === "player2") causes[k].p2++; else causes[k].p1++;',
  "  }",
  "  const causeRows = Object.entries(causes).sort((a, b) => b[1].n - a[1].n);",
  "  const maxCause = Math.max(1, ...causeRows.map(([, v]) => v.n));",
  "",
  "  const hotCells = sd.cells",
  "    .filter(c => c.deaths > 0)",
  "    .sort((a, b) => b.deaths - a.deaths)",
  "    .slice(0, 8);",
  "  const maxHot = Math.max(1, ...hotCells.map(c => c.deaths));",
  "",
  "  const causeTable = causeRows.length ? causeRows.map(([k, v]) => `<tr>",
  "      <td>${esc(k)}</td>",
  '      <td class="num" style="min-width:150px">',
  '        <div class="barcell">',
  '          <div class="bar" style="flex:1"><i style="width:${(v.n / maxCause * 100).toFixed(1)}%"></i></div>',
  '          <span class="barval">${v.n}</span>',
  "        </div>",
  "      </td>",
  '      <td class="num" style="color:${rgbCss(P1_RGB)}">${v.p1}</td>',
  '      <td class="num" style="color:${rgbCss(P2_RGB)}">${v.p2}</td>',
  '    </tr>`).join("")',
  '    : `<tr><td colspan="4" class="muted">\u8FD9\u4E00\u5173\u8FD8\u6CA1\u6709\u6B7B\u4EA1\u8BB0\u5F55</td></tr>`;',
  "",
  "  const hotTable = hotCells.length ? hotCells.map(c => `<tr>",
  "      <td>(${c.x}, ${c.y})</td>",
  '      <td class="num" style="min-width:120px">',
  '        <div class="barcell">',
  '          <div class="bar" style="flex:1"><i style="width:${(c.deaths / maxHot * 100).toFixed(1)}%"></i></div>',
  '          <span class="barval">${c.deaths} \u6B21</span>',
  "        </div>",
  "      </td>",
  '      <td class="num" style="color:${rgbCss(P1_RGB)}">${c.p1.deaths}</td>',
  '      <td class="num" style="color:${rgbCss(P2_RGB)}">${c.p2.deaths}</td>',
  '      <td class="muted" style="max-width:320px;overflow:hidden;text-overflow:ellipsis">${esc(c.causes.join(", "))}</td>',
  '    </tr>`).join("")',
  '    : `<tr><td colspan="5" class="muted">\u6CA1\u6709\u6B7B\u4EA1\u8BB0\u5F55</td></tr>`;',
  "",
  "  const flow = sd.deaths.slice(0, 300).map(d => `<tr>",
  '      <td class="muted" title="${esc(fmtTime(d.ts))}">${esc(fmtAgo(d.ts))}</td>',
  '      <td><span class="badge ${d.actor === "player2" ? "p2" : "p1"}">${d.actor === "player2" ? "P2" : "P1"}</span></td>',
  '      <td>${esc(d.cause || "\u2013")}</td>',
  '      <td class="num">${d.x.toFixed(1)}, ${d.y.toFixed(1)}</td>',
  '      <td class="num">${fmtSec(d.survived_s)}</td>',
  '      <td class="num">${d.jumps_before_death}</td>',
  '      <td class="num">${d.deaths_in_level}</td>',
  '    </tr>`).join("");',
  "",
  "  return `",
  '  <div class="grid2">',
  '    <div class="card">',
  "      <h3>\u672C\u5173\u6B7B\u56E0 <small>\u6309\u89D2\u8272\u62C6\u5206</small></h3>",
  '      <table><thead><tr><th>\u6B7B\u56E0</th><th class="num">\u6B21\u6570</th>',
  '        <th class="num"><span style="color:${rgbCss(P1_RGB)}">P1</span></th>',
  '        <th class="num"><span style="color:${rgbCss(P2_RGB)}">P2</span></th></tr></thead>',
  "        <tbody>${causeTable}</tbody></table>",
  "    </div>",
  '    <div class="card">',
  "      <h3>\u6B7B\u4EA1\u6700\u96C6\u4E2D\u7684\u683C\u5B50</h3>",
  '      <table><thead><tr><th>\u683C\u5B50</th><th class="num">\u6B7B\u4EA1</th>',
  '        <th class="num">P1</th><th class="num">P2</th><th>\u6B7B\u56E0</th></tr></thead>',
  "        <tbody>${hotTable}</tbody></table>",
  "    </div>",
  "  </div>",
  '  <div class="card">',
  "    <h3>\u6B7B\u4EA1\u6D41\u6C34 <small>\u6700\u8FD1 ${sd.deaths.length} \u6761</small></h3>",
  '    <div class="scroll"><table>',
  '      <thead><tr><th>\u65F6\u95F4</th><th>\u89D2\u8272</th><th>\u6B7B\u56E0</th><th class="num">\u4F4D\u7F6E</th>',
  '        <th class="num">\u6D3B\u4E86</th><th class="num">\u6B7B\u524D\u8DF3\u8DC3</th><th class="num">\u672C\u5173\u7B2C\u51E0\u6B21\u6B7B</th></tr></thead>',
  '      <tbody>${flow || `<tr><td colspan="7" class="muted">\u6CA1\u6709\u6B7B\u4EA1\u8BB0\u5F55</td></tr>`}</tbody>',
  "    </table></div>",
  "  </div>`;",
  "}",
  "",
  "/* ================================================================ \u65F6\u957F\u4E0E\u901A\u5173 */",
  "",
  "function viewRuns() {",
  "  const sd = state.sceneData;",
  '  if (!sd) return `<div class="card"><div class="empty">\u5148\u9009\u4E00\u5173</div></div>`;',
  "  const runs = sd.runs;",
  "  if (!runs.length) {",
  '    return `<div class="card"><div class="empty">\u8FD9\u4E00\u5173\u8FD8\u6CA1\u6709\u5B8C\u6210/\u4E2D\u65AD\u8BB0\u5F55</div></div>`;',
  "  }",
  "",
  "  const durations = runs.map(r => r.duration_s).filter(v => v > 0);",
  "  const bins = histogram(durations, 12);",
  "  const maxBin = Math.max(1, ...bins.map(b => b.n));",
  '  const clearCount = runs.filter(r => r.type === "level_clear").length;',
  "",
  '  const chart = `<div style="display:flex; align-items:flex-end; gap:4px; height:150px; padding:6px 0">',
  '    ${bins.map(b => `<div style="flex:1; display:flex; flex-direction:column; justify-content:flex-end; align-items:center; height:100%"',
  '        title="${b.from.toFixed(1)}s ~ ${b.to.toFixed(1)}s\uFF1A${b.n} \u6B21">',
  '        <div style="width:100%; background:linear-gradient(180deg,#4da3ff,#2b6cb0); border-radius:3px 3px 0 0; height:${(b.n / maxBin * 100).toFixed(1)}%"></div>',
  '      </div>`).join("")}',
  "  </div>",
  '  <div style="display:flex; justify-content:space-between; color:#8b949e; font-size:10.5px">',
  "    <span>${bins[0].from.toFixed(0)}s</span><span>${bins[bins.length - 1].to.toFixed(0)}s</span>",
  "  </div>`;",
  "",
  "  const rows = runs.slice(0, 300).map(r => `<tr>",
  '      <td class="muted" title="${esc(fmtTime(r.ts))}">${esc(fmtAgo(r.ts))}</td>',
  '      <td><span class="badge ${r.type === "level_clear" ? "clear" : "quit"}">${r.type === "level_clear" ? "\u901A\u5173" : "\u4E2D\u65AD"}</span></td>',
  '      <td class="num">${fmtSec(r.duration_s)}</td>',
  '      <td class="num">${r.deaths}</td>',
  '      <td class="num">${r.jumps}</td>',
  '    </tr>`).join("");',
  "",
  "  return `",
  '  <div class="card">',
  "    <h3>\u5355\u6B21\u6E38\u73A9\u65F6\u957F\u5206\u5E03 <small>${runs.length} \u6B21\u8BB0\u5F55 \xB7 \u901A\u5173 ${clearCount} \u6B21</small></h3>",
  "    ${chart}",
  "  </div>",
  '  <div class="card">',
  "    <h3>\u6BCF\u6B21\u6E38\u73A9\u660E\u7EC6</h3>",
  '    <div class="scroll"><table>',
  '      <thead><tr><th>\u65F6\u95F4</th><th>\u7ED3\u679C</th><th class="num">\u7528\u65F6</th><th class="num">\u6B7B\u4EA1</th><th class="num">\u8DF3\u8DC3</th></tr></thead>',
  "      <tbody>${rows}</tbody>",
  "    </table></div>",
  "  </div>`;",
  "}",
  "",
  "function histogram(values, binCount) {",
  "  if (!values.length) return [{ from: 0, to: 0, n: 0 }];",
  "  const min = Math.min(...values), max = Math.max(...values);",
  "  const span = Math.max(1e-6, max - min);",
  "  const step = span / binCount;",
  "  const bins = Array.from({ length: binCount }, (_, i) => ({",
  "    from: min + i * step, to: min + (i + 1) * step, n: 0,",
  "  }));",
  "  for (const v of values) {",
  "    const i = Math.min(binCount - 1, Math.floor((v - min) / step));",
  "    bins[i].n++;",
  "  }",
  "  return bins;",
  "}",
  "",
  "/* ================================================================ \u6700\u8FD1\u4E8B\u4EF6 */",
  "",
  "async function viewRecent() {",
  '  const box = $("content");',
  '  box.innerHTML = `<div class="card"><div class="empty">\u52A0\u8F7D\u4E2D\u2026</div></div>`;',
  "  let data;",
  "  try {",
  '    data = await api("/v1/stats/recent", { since: sinceMs(), limit: 80 });',
  "  } catch (err) {",
  '    box.innerHTML = `<div class="card"><div class="empty">\u8BFB\u4E0D\u5230\uFF1A${esc(err.message)}</div></div>`;',
  "    return;",
  "  }",
  '  if (state.tab !== "recent") return;',
  "",
  '  const label = t => ({ death: ["death", "\u6B7B\u4EA1"], level_start: ["start", "\u8FDB\u5165\u5173\u5361"],',
  '    level_clear: ["clear", "\u901A\u5173"], level_quit: ["quit", "\u4E2D\u65AD"], session_start: ["start", "\u542F\u52A8"] }[t] || ["start", t]);',
  "",
  "  const summary = m => {",
  '    if (!m) return "";',
  '    if (m.cause) return `${esc(m.cause)}${m.actor === "player2" ? " \xB7 P2" : " \xB7 P1"} @ (${m.cell_x}, ${m.cell_y})`;',
  "    if (m.duration_s !== undefined) return `\u7528\u65F6 ${fmtSec(m.duration_s)} \xB7 \u6B7B ${m.deaths || 0} \u6B21`;",
  "    if (m.attempt !== undefined) return `\u7B2C ${m.attempt} \u6B21\u8FDB\u5165`;",
  '    if (m.platform) return `${esc(m.platform)} \xB7 v${esc(m.app_version || "?")}`;',
  '    return "";',
  "  };",
  "",
  '  box.innerHTML = `<div class="card">',
  "    <h3>\u6700\u8FD1\u4E8B\u4EF6 <small>\u6700\u65B0\u7684 ${data.events.length} \u6761\uFF0C\u7528\u6765\u786E\u8BA4\u6570\u636E\u771F\u7684\u5728\u8FDB\u6765</small></h3>",
  '    <div class="scroll"><table>',
  "      <thead><tr><th>\u65F6\u95F4</th><th>\u7C7B\u578B</th><th>\u573A\u666F</th><th>\u6458\u8981</th><th>\u4F1A\u8BDD</th></tr></thead>",
  "      <tbody>${data.events.map(e => {",
  "        const [cls, txt] = label(e.type);",
  "        return `<tr>",
  '          <td class="muted" title="${esc(fmtTime(e.ts))}">${esc(fmtAgo(e.ts))}</td>',
  '          <td><span class="badge ${cls}">${esc(txt)}</span></td>',
  '          <td>${esc((e.meta && e.meta.scene) || "\u2013")}</td>',
  "          <td>${summary(e.meta)}</td>",
  '          <td class="muted" style="font-size:11px">${esc(String(e.session || "").slice(-8))}</td>',
  "        </tr>`;",
  '      }).join("") || `<tr><td colspan="5" class="muted">\u8FD8\u6CA1\u6709\u4E8B\u4EF6</td></tr>`}</tbody>',
  "    </table></div>",
  "  </div>`;",
  "}",
  "",
  "/* ================================================================ \u6570\u636E\u5B57\u5178 */",
  "",
  "function viewDict() {",
  '  const t = (rows) => `<table style="margin-bottom:10px">${rows.map(([k, v]) =>',
  '    `<tr><td style="width:200px"><code>${esc(k)}</code></td><td class="muted" style="white-space:normal">${v}</td></tr>`).join("")}</table>`;',
  "",
  "  return `",
  '  <div class="card">',
  "    <h3>\u80FD\u770B\u54EA\u4E9B\u6570\u636E <small>\u8FD9\u5F20\u8868\u5C31\u662F\u5E93\u91CC\u5B58\u7684\u4E1C\u897F\uFF0C\u6CA1\u6709\u522B\u7684</small></h3>",
  '    <div class="hint" style="margin-bottom:12px">',
  "      \u6BCF\u6761\u4E8B\u4EF6\u90FD\u6709 <code>player_id</code>\uFF08\u968F\u673A UUID\uFF0C\u91CD\u88C5\u624D\u53D8\uFF09\u3001<code>session_id</code>\uFF08\u6BCF\u6B21\u542F\u52A8\u4E00\u4E2A\uFF09\u3001",
  "      <code>client_ts</code>\uFF08\u5BA2\u6237\u7AEF\u65F6\u95F4\u6233\uFF09\u3002\u9664\u6B64\u4E4B\u5916\u4E0D\u542B\u4EFB\u4F55\u8D26\u53F7\u3001IP\u3001\u8BBE\u5907\u6307\u7EB9\u3002",
  "      \u89D2\u8272\u5B57\u6BB5 <code>actor</code> \u53EA\u6709\u4E24\u4E2A\u503C\uFF1A<code>player1</code> / <code>player2</code>\uFF0C\u770B\u677F\u4E0A\u7EA2\u8272\u662F P1\u3001\u84DD\u8272\u662F P2\u3002",
  "    </div>",
  "",
  '    <h3 style="margin-top:14px">session_start <small>\u6BCF\u6B21\u542F\u52A8\u6E38\u620F\u4E00\u6B21</small></h3>',
  "    ${t([",
  '      ["scene", "\u542F\u52A8\u65F6\u5728\u54EA\u4E2A\u573A\u666F"],',
  '      ["app_version / unity_version", "\u6E38\u620F\u7248\u672C\u3001\u5F15\u64CE\u7248\u672C"],',
  '      ["platform / device / os / gpu", "\u8FD0\u884C\u5E73\u53F0\u3001\u663E\u5361\u3001\u7CFB\u7EDF\uFF08\u7528\u4E8E\u533A\u5206\u6D4B\u8BD5\u673A\uFF09"],',
  '      ["screen_w / screen_h / target_fps", "\u5206\u8FA8\u7387\u4E0E\u5E27\u7387\u8BBE\u7F6E"],',
  '      ["language", "\u7CFB\u7EDF\u8BED\u8A00"],',
  "    ])}",
  "",
  '    <h3 style="margin-top:14px">level_start <small>\u6BCF\u6B21\u8FDB\u5165\u4E00\u4E2A\u5173\u5361</small></h3>',
  "    ${t([",
  '      ["scene / level_index", "\u5173\u5361\u540D\u4E0E\u6784\u5EFA\u5E8F\u53F7"],',
  '      ["attempt", "\u672C\u6B21\u8FD0\u884C\u91CC\u7B2C\u51E0\u6B21\u8FDB\u8FD9\u4E00\u5173\uFF08\u53CD\u590D\u91CD\u8BD5\u4F1A\u7D2F\u52A0\uFF09"],',
  '      ["session_time_s", "\u8FDB\u5173\u65F6\u8DDD\u79BB\u542F\u52A8\u8FC7\u4E86\u591A\u4E45"],',
  "    ])}",
  "",
  '    <h3 style="margin-top:14px">death <small>\u6BCF\u6B21\u6B7B\u4EA1</small></h3>',
  "    ${t([",
  '      ["scene / attempt", "\u54EA\u4E2A\u5173\u5361\u3001\u7B2C\u51E0\u6B21\u5C1D\u8BD5"],',
  '      ["cause", "\u6B7B\u56E0\uFF1A<code>hazard:\u7269\u4F53\u540D</code> / <code>crush</code>\uFF08\u88AB\u5939\uFF09 / <code>manual_r</code>\uFF08\u81EA\u5DF1\u6309 R\uFF09"],',
  '      ["actor", "<code>player1</code> \u6216 <code>player2</code>\uFF08\u80FD\u770B\u51FA\u662F\u4E0D\u662F\u88AB P2 \u7684\u5EF6\u8FDF\u6A21\u4EFF\u5BB3\u6B7B\u7684\uFF09"],',
  '      ["x / y", "\u6B7B\u4EA1\u77AC\u95F4\u7684\u771F\u5B9E\u4E16\u754C\u5750\u6807\uFF08\u4F20\u9001\u56DE\u51FA\u751F\u70B9\u4E4B\u524D\u53D6\u7684\uFF09"],',
  '      ["cell_x / cell_y", "\u540C\u4E00\u4E2A\u4F4D\u7F6E\u7684\u683C\u5B50\u7F16\u53F7\uFF0C\u70ED\u529B\u56FE\u7528"],',
  '      ["survived_s", "\u8FD9\u6B21\u8FDB\u5173\u5230\u6B7B\u4EA1\u6491\u4E86\u591A\u4E45"],',
  '      ["deaths_in_level", "\u672C\u5173\u7D2F\u8BA1\u7B2C\u51E0\u6B21\u6B7B"],',
  '      ["jumps_before_death", "\u6B7B\u524D\u8DF3\u4E86\u591A\u5C11\u6B21\uFF08\u5224\u65AD\u662F\u4E0D\u662F\u88AB\u8DF3\u8DC3\u5361\u4F4F\uFF09"],',
  "    ])}",
  "",
  '    <h3 style="margin-top:14px">cell <small>\u6BCF 0.5\xD70.5 \u4E16\u754C\u5355\u4F4D\u683C\u5B50\u7684\u589E\u91CF\uFF08\u6BCF 60 \u79D2\u6216\u5173\u5361\u7ED3\u675F\u4E0A\u62A5\uFF09</small></h3>',
  "    ${t([",
  '      ["actor", "\u8FD9\u4E00\u683C\u662F P1 \u8FD8\u662F P2 \u7684\u6570\u636E\uFF08\u4E24\u4E2A\u89D2\u8272\u5404\u7B97\u5404\u7684\uFF09"],',
  '      ["cell_x / cell_y", "\u683C\u5B50\u5750\u6807\uFF1Bx/y \u662F\u683C\u5B50\u4E2D\u5FC3"],',
  '      ["cell_size", "\u683C\u5B50\u8FB9\u957F\uFF08\u4E16\u754C\u5355\u4F4D\uFF09\uFF0C0.5 = \u534A\u683C\u5BC6\u5EA6\uFF1B\u8001\u6570\u636E\u6CA1\u6709\u8FD9\u4E2A\u5B57\u6BB5\uFF0C\u6309 1 \u7B97"],',
  '      ["dwell_s", "\u5728\u8FD9\u4E2A\u683C\u5B50\u91CC\u5F85\u4E86\u591A\u5C11\u79D2\uFF08\u53EA\u4E0A\u62A5\u8D85\u8FC7 1 \u79D2\u7684\u683C\u5B50\uFF09"],',
  '      ["jumps", "\u5728\u8FD9\u4E2A\u683C\u5B50\u91CC\u8D77\u8DF3\u7684\u6B21\u6570"],',
  "    ])}",
  "",
  '    <h3 style="margin-top:14px">route <small>\u6BCF\u8D9F\u5C1D\u8BD5\u7ED3\u675F\u4E00\u6761\uFF0C\u7528\u6765\u753B\u8DEF\u7EBF</small></h3>',
  "    ${t([",
  '      ["actor / attempt", "\u54EA\u4E00\u5C40\u3001\u54EA\u4E2A\u89D2\u8272"],',
  '      ["outcome", "<code>clear</code> \u901A\u5173 / <code>death</code> \u6B7B\u5728\u8FD9\u4E00\u8D9F / <code>quit</code> \u4E2D\u9014\u9000\u51FA\u6216\u5207\u573A\u666F"],',
  '      ["cells", "\u8FD9\u4E00\u8D9F\u8D70\u8FC7\u7684\u683C\u5B50\u5E8F\u5217\uFF0C\u683C\u5F0F <code>x,y;x,y;\u2026</code>\uFF08\u6309\u65F6\u95F4\u987A\u5E8F\uFF09"],',
  '      ["cell_count", "\u4E00\u5171\u8D70\u4E86\u591A\u5C11\u4E2A\u683C\u5B50\uFF08\u6700\u591A 300\uFF0C\u8D85\u4E86\u622A\u65AD\uFF1B\u6B7B\u4EA1\u8DEF\u7EBF\u5C11\u4E8E 10 \u683C\u4E0D\u53D1\uFF09"],',
  '      ["duration_s", "\u8FD9\u4E00\u8D9F\u82B1\u4E86\u591A\u5C11\u79D2"],',
  '      ["cell_size", "\u683C\u5B50\u8FB9\u957F\uFF0C\u548C cell \u4E8B\u4EF6\u4E00\u81F4"],',
  "    ])}",
  "",
  '    <h3 style="margin-top:14px">level_clear / level_quit <small>\u6BCF\u6B21\u79BB\u5F00\u4E00\u4E2A\u5173\u5361</small></h3>',
  "    ${t([",
  '      ["scene / level_index / attempt", "\u54EA\u4E2A\u5173\u5361\u3001\u7B2C\u51E0\u6B21\u5C1D\u8BD5"],',
  '      ["duration_s", "\u8FD9\u4E00\u8D9F\u82B1\u4E86\u591A\u5C11\u79D2"],',
  '      ["deaths / jumps", "\u8FD9\u4E00\u8D9F\u6B7B\u4E86\u51E0\u6B21\u3001\u8DF3\u4E86\u51E0\u6B21"],',
  '      ["distance", "\u8FD9\u4E00\u8D9F\u79FB\u52A8\u7684\u6C34\u5E73\u8DDD\u79BB"],',
  '      ["session_time_s", "\u79BB\u5F00\u65F6\u8DDD\u542F\u52A8\u8FC7\u4E86\u591A\u4E45"],',
  "    ])}",
  "",
  '    <h3 style="margin-top:14px">\u770B\u677F\u672C\u8EAB</h3>',
  "    ${t([",
  '      ["P1 / P2 \u989C\u8272", "\u7EA2\u8272 = player1\uFF0C\u84DD\u8272 = player2\uFF1B\u4E24\u5C42\u5404\u81EA\u6309\u81EA\u5DF1\u7684\u6700\u5927\u503C\u5F52\u4E00\u5316"],',
  '      ["\u80CC\u666F\u56FE", "\u6BCF\u5173\u4E00\u5F20\u3002\u9ED8\u8BA4\u5B58\u5728\u4F60\u81EA\u5DF1\u6D4F\u89C8\u5668\u7684 IndexedDB \u91CC\uFF08\u62D6\u52A8/\u6EDA\u8F6E\u8C03\u4F4D\u7F6E\uFF09\uFF1B\u70B9\u300C\u4E0A\u4F20\u5230\u670D\u52A1\u5668\u300D\u4F1A\u538B\u5230\u957F\u8FB9 1600px \u540E\u5B58\u8FDB Workers KV\uFF0C\u4F4D\u7F6E\u4E5F\u4E00\u8D77\u5B58\uFF0C\u6240\u4EE5\u522B\u4EBA\u6253\u5F00\u540C\u4E00\u5173\u770B\u5230\u7684\u662F\u4F60\u5BF9\u9F50\u597D\u7684\u6837\u5B50\u3002\u5220\u9664\u670D\u52A1\u5668\u526F\u672C\u4E0D\u5F71\u54CD\u672C\u673A\u8FD9\u4EFD"],',
  '      ["\u6E05\u7A7A\u6570\u636E", "\u2699 \u91CC\u7684\u300C\u6E05\u7A7A\u672C\u5173 / \u6E05\u7A7A\u5168\u90E8\u300D\uFF0C\u9700\u8981\u670D\u52A1\u7AEF\u914D\u4E86 <code>ADMIN_KEY</code> \u5E76\u5728\u8FD9\u91CC\u586B\u540C\u4E00\u4E2A\u503C"],',
  '      ["\u70ED\u529B\u56FE\u6307\u6807", "\u6B7B\u4EA1 / \u8DF3\u8DC3 / \u505C\u7559\uFF08\u6CA1\u6709\u8DF3\u8DC3\u5931\u8D25\u7387\uFF0C\u90A3\u4E2A\u6307\u6807\u5BF9\u5C0F\u6837\u672C\u6CA1\u610F\u4E49\uFF0C\u5DF2\u53BB\u6389\uFF1B\u505C\u7559\u65F6\u957F\u53EA\u8BB0\u8D85\u8FC7 1 \u79D2\u7684\u683C\u5B50\uFF0C\u989C\u8272\u6BCF 1 \u79D2\u4E00\u6863\uFF09"],',
  '      ["\u901A\u5173\u8DEF\u7EBF", "\u5E26\u9AD8\u4EAE\u63CF\u8FB9\u7684\u8272\u5757\uFF0C\u662F\u8FD9\u4E00\u5173\u6700\u8FD1\u4E00\u6B21\u901A\u5173\u8D70\u8FC7\u7684\u683C\u5B50\uFF1B\u4E0B\u62C9\u53EF\u4EE5\u9009\u300C\u5168\u90E8\u53E0\u52A0\u300D\u6216\u67D0\u4E00\u5C40"],',
  '      ["\u7F51\u683C\u5BC6\u5EA6", "1 \u683C = 0.5 \u4E2A\u4E16\u754C\u5355\u4F4D\uFF08\u6A2A\u7AD6\u5404 2 \u683C = \u539F\u6765 1 \u683C\uFF09\uFF0C\u65B0\u6570\u636E\u624D\u6709\u8FD9\u4E2A\u5BC6\u5EA6"],',
  "    ])}",
  "  </div>`;",
  "}",
  "",
  "/* ================================================================ \u8BBE\u7F6E\u4E0E\u6E05\u5E93 */",
  "",
  "function openSettings() {",
  '  $("cfgApi").value = state.api;',
  '  $("cfgKey").value = state.key;',
  '  $("cfgAdmin").value = state.admin;',
  '  $("cfg").style.display = "block";',
  "}",
  "",
  "async function clearData(scope) {",
  '  const label = scope.all ? "\u5168\u90E8\u6570\u636E" : `\u5173\u5361\u300C${state.scene}\u300D\u7684\u6570\u636E`;',
  '  if (!state.scene && !scope.all) { setStatus("\u5148\u9009\u4E00\u5173", true); return; }',
  '  if (!state.admin) { openSettings(); setStatus("\u5148\u5728 \u2699 \u91CC\u586B\u7BA1\u7406\u5BC6\u94A5\uFF08ADMIN_KEY\uFF09", true); return; }',
  "  if (!confirm(`\u786E\u8BA4\u5220\u9664${label}\uFF1F\\n\\n\u8FD9\u4E2A\u64CD\u4F5C\u4E0D\u53EF\u64A4\u9500\u3002`)) return;",
  "",
  '  setStatus("\u6B63\u5728\u5220\u9664\u2026");',
  "  try {",
  '    const res = await fetch(state.api.replace(/\\/+$/, "") + "/v1/admin/clear", {',
  '      method: "POST",',
  '      headers: { "Content-Type": "application/json", "X-Admin-Key": state.admin },',
  "      body: JSON.stringify(scope),",
  "    });",
  "    const body = await res.json().catch(() => ({}));",
  "    if (!res.ok || body.ok === false) {",
  '      throw new Error(body.error === "admin_key_not_configured"',
  '        ? "\u670D\u52A1\u7AEF\u8FD8\u6CA1\u914D ADMIN_KEY\uFF08wrangler secret put ADMIN_KEY\uFF09"',
  '        : (body.error || ("HTTP " + res.status)));',
  "    }",
  "    setStatus(`\u5DF2\u5220\u9664 ${body.deleted} \u884C`);",
  "    state.compare = null;",
  "    await loadAll();",
  "  } catch (err) {",
  '    setStatus("\u5220\u9664\u5931\u8D25\uFF1A" + err.message, true);',
  "  }",
  "}",
  "",
  "function bindSettings() {",
  '  $("cfgBtn").addEventListener("click", () => {',
  '    $("cfg").style.display = $("cfg").style.display === "block" ? "none" : "block";',
  '    if ($("cfg").style.display === "block") openSettings();',
  "  });",
  '  $("cfgClose").addEventListener("click", () => { $("cfg").style.display = "none"; });',
  '  $("cfgSave").addEventListener("click", () => {',
  '    state.api = $("cfgApi").value.trim().replace(/\\/+$/, "") || FALLBACK_API;',
  '    state.key = $("cfgKey").value.trim();',
  '    state.admin = $("cfgAdmin").value.trim();',
  '    store.set("alittle_api", state.api);',
  '    store.set("alittle_key", state.key);',
  '    store.set("alittle_admin", state.admin);',
  '    $("cfg").style.display = "none";',
  "    state.compare = null;",
  "    loadAll();",
  "  });",
  '  $("clearScene").addEventListener("click", () => {',
  '    $("cfg").style.display = "none";',
  "    clearData({ scene: state.scene });",
  "  });",
  '  $("clearAll").addEventListener("click", () => {',
  '    $("cfg").style.display = "none";',
  "    clearData({ all: true });",
  "  });",
  "}",
  "",
  "/* ================================================================ \u542F\u52A8 */",
  "",
  "function syncUrl() {",
  "  const u = new URL(location.href);",
  '  u.searchParams.set("tab", state.tab);',
  '  u.searchParams.set("metric", state.metric);',
  '  u.searchParams.set("range", state.range);',
  '  if (state.scene) u.searchParams.set("scene", state.scene);',
  '  if (state.showRoute === false) u.searchParams.set("route", "0");',
  '  if (state.routeClearOnly === false) u.searchParams.set("rc", "0");',
  '  if (state.routePick !== "latest") u.searchParams.set("rp", state.routePick);',
  '  history.replaceState(null, "", u);',
  "}",
  "",
  "function bindHeader() {",
  '  $("range").value = state.range;',
  '  $("range").addEventListener("change", e => {',
  "    state.range = e.target.value;",
  "    syncUrl();",
  "    state.compare = null;",
  "    loadAll();",
  "  });",
  '  $("refresh").addEventListener("click", () => { state.compare = null; state.recent = null; loadAll(); });',
  '  $("auto").addEventListener("change", e => {',
  "    if (state.autoTimer) { clearInterval(state.autoTimer); state.autoTimer = null; }",
  "    if (e.target.checked) state.autoTimer = setInterval(() => { state.compare = null; loadAll(); }, 60000);",
  "  });",
  '  window.addEventListener("resize", () => {',
  '    if (state.tab === "heatmap" && state.sceneData && $("heat")) drawHeatmap();',
  "  });",
  "}",
  "",
  "bindHeader();",
  "bindSettings();",
  "loadAll().then(() => {",
  "  if (state.scene) loadBg(state.scene).then(() => {",
  "    renderBgBar();",
  "    syncBgInputs();",
  '    if (state.tab === "heatmap") drawHeatmap();',
  "  });",
  "});",
  "<\/script>",
  "</body>",
  "</html>",
  ""
].join("\n");

// src/index.js
var INGEST_PATH = "/v1/events";
var MAX_BODY_BYTES = 256 * 1024;
var MAX_EVENTS_PER_REQUEST = 200;
var MAX_METADATA_BYTES = 4096;
var BATCH_CHUNK = 50;
var MAX_ERRORS_REPORTED = 8;
var ID_RE = /^[A-Za-z0-9_-]{6,64}$/;
var TYPE_RE = /^[a-z][a-z0-9_]{0,47}$/;
var MIN_TS = Date.UTC(2020, 0, 1);
var MAX_CLOCK_SKEW_MS = 7 * 24 * 3600 * 1e3;
var INSERT_SQL = "INSERT INTO game_events (player_id, session_id, event_type, metadata, client_ts, server_ts) VALUES (?, ?, ?, ?, ?, ?)";
var encoder = new TextEncoder();
var byteLength = /* @__PURE__ */ __name((s) => encoder.encode(s).length, "byteLength");
function normalizeTs(value, now) {
  if (typeof value !== "number" || !Number.isFinite(value)) return null;
  let ms = value;
  if (ms < 1e11) ms = Math.round(ms * 1e3);
  ms = Math.round(ms);
  if (ms < MIN_TS || ms > now + MAX_CLOCK_SKEW_MS) return null;
  return ms;
}
__name(normalizeTs, "normalizeTs");
function normalizeMetadata(value) {
  let obj;
  if (value && typeof value === "object" && !Array.isArray(value)) {
    obj = value;
  } else if (value === void 0 || value === null) {
    obj = {};
  } else {
    obj = { value };
  }
  const text = JSON.stringify(obj);
  if (byteLength(text) > MAX_METADATA_BYTES) return null;
  return text;
}
__name(normalizeMetadata, "normalizeMetadata");
function validateEvent(raw, index, now) {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) {
    return { error: `#${index}:not_an_object` };
  }
  const playerId = raw.player_id;
  if (typeof playerId !== "string" || !ID_RE.test(playerId)) {
    return { error: `#${index}:player_id_invalid` };
  }
  const sessionId = raw.session_id;
  if (typeof sessionId !== "string" || !ID_RE.test(sessionId)) {
    return { error: `#${index}:session_id_invalid` };
  }
  const eventType = raw.event_type;
  if (typeof eventType !== "string" || !TYPE_RE.test(eventType)) {
    return { error: `#${index}:event_type_invalid` };
  }
  const clientTs = normalizeTs(raw.client_ts, now);
  if (clientTs === null) {
    return { error: `#${index}:client_ts_invalid` };
  }
  const metadata = normalizeMetadata(raw.metadata);
  if (metadata === null) {
    return { error: `#${index}:metadata_too_large` };
  }
  return { row: [playerId, sessionId, eventType, metadata, clientTs] };
}
__name(validateEvent, "validateEvent");
function splitIntoChunks(arr, size) {
  const out = [];
  for (let i = 0; i < arr.length; i += size) out.push(arr.slice(i, i + size));
  return out;
}
__name(splitIntoChunks, "splitIntoChunks");
async function handleIngest(request, env) {
  if (env.INGEST_KEY) {
    const provided = request.headers.get("x-telemetry-key") || "";
    if (provided !== env.INGEST_KEY) {
      return json({ ok: false, error: "unauthorized" }, 401);
    }
  }
  const declared = Number(request.headers.get("content-length") || 0);
  if (declared > MAX_BODY_BYTES) {
    return json({ ok: false, error: "body_too_large" }, 413);
  }
  let text;
  try {
    text = await request.text();
  } catch {
    return json({ ok: false, error: "body_read_failed" }, 400);
  }
  if (byteLength(text) > MAX_BODY_BYTES) {
    return json({ ok: false, error: "body_too_large" }, 413);
  }
  let payload;
  try {
    payload = JSON.parse(text);
  } catch {
    return json({ ok: false, error: "invalid_json" }, 400);
  }
  let list;
  if (Array.isArray(payload)) {
    list = payload;
  } else if (payload && typeof payload === "object" && Array.isArray(payload.events)) {
    list = payload.events;
  } else if (payload && typeof payload === "object" && "event_type" in payload) {
    list = [payload];
  } else {
    return json({ ok: false, error: "expected_event_or_array" }, 400);
  }
  if (list.length === 0) {
    return json({ ok: true, inserted: 0 });
  }
  if (list.length > MAX_EVENTS_PER_REQUEST) {
    return json({ ok: false, error: "too_many_events", limit: MAX_EVENTS_PER_REQUEST }, 413);
  }
  const now = Date.now();
  const rows = [];
  const errors = [];
  for (let i = 0; i < list.length; i++) {
    const result = validateEvent(list[i], i, now);
    if (result.error) {
      if (errors.length < MAX_ERRORS_REPORTED) errors.push(result.error);
    } else {
      rows.push(result.row);
    }
  }
  if (rows.length === 0) {
    return json({ ok: false, error: "no_valid_events", details: errors }, 400);
  }
  const stmt = env.DB.prepare(INSERT_SQL);
  let inserted = 0;
  try {
    for (const chunk of splitIntoChunks(rows, BATCH_CHUNK)) {
      const statements = chunk.map(
        (r) => stmt.bind(r[0], r[1], r[2], r[3], r[4], now)
      );
      await env.DB.batch(statements);
      inserted += chunk.length;
    }
  } catch (err) {
    console.error("d1_insert_failed", err && err.message);
    return json({ ok: false, error: "db_write_failed" }, 503);
  }
  return json({
    ok: true,
    inserted,
    rejected: list.length - rows.length,
    details: errors.length ? errors : void 0
  });
}
__name(handleIngest, "handleIngest");
var index_default = {
  async fetch(request, env) {
    try {
      const url = new URL(request.url);
      if (request.method === "OPTIONS") {
        return new Response(null, { status: 204, headers: CORS_HEADERS });
      }
      if (url.pathname === "/v1/stats" || url.pathname.startsWith("/v1/stats/")) {
        if (request.method !== "GET") {
          return json({ ok: false, error: "method_not_allowed" }, 405, {
            Allow: "GET, OPTIONS"
          });
        }
        return await handleStats(url, env, request);
      }
      if (url.pathname === "/v1/admin/clear" || url.pathname.startsWith("/v1/admin/")) {
        if (request.method !== "POST") {
          return json({ ok: false, error: "method_not_allowed" }, 405, {
            Allow: "POST, OPTIONS"
          });
        }
        return await handleAdminClear(url, env, request);
      }
      if (url.pathname === "/v1/backgrounds" || url.pathname.startsWith("/v1/backgrounds/")) {
        return await handleBackgrounds(url, env, request);
      }
      if (request.method === "GET") {
        if (url.pathname === "/health") {
          return json({ ok: true, service: "alittle-telemetry", now: Date.now() });
        }
        if (url.pathname === "/" || url.pathname === "/dashboard") {
          return html(dashboard_page_default);
        }
        if (url.pathname === "/v1") {
          return json({
            ok: true,
            service: "alittle-telemetry",
            endpoints: {
              dashboard: "GET /",
              ingest: `POST ${INGEST_PATH}`,
              health: "GET /health",
              overview: "GET /v1/stats/overview",
              compare: "GET /v1/stats/compare",
              scene: "GET /v1/stats/scene?scene=<\u573A\u666F\u540D>",
              recent: "GET /v1/stats/recent",
              clear: "POST /v1/admin/clear  {scene|all}\uFF08\u9700 X-Admin-Key\uFF09",
              backgrounds: "GET /v1/backgrounds  |  GET/PUT/DELETE /v1/backgrounds/<\u573A\u666F\u540D>\uFF08\u5199\u9700 X-Admin-Key\uFF09"
            }
          });
        }
        return json({ ok: false, error: "not_found" }, 404);
      }
      if (request.method !== "POST") {
        return json({ ok: false, error: "method_not_allowed" }, 405, {
          Allow: "GET, POST, OPTIONS"
        });
      }
      if (url.pathname !== INGEST_PATH && url.pathname !== "/") {
        return json({ ok: false, error: "not_found" }, 404);
      }
      return await handleIngest(request, env);
    } catch (err) {
      console.error("unhandled", err && err.stack);
      return json({ ok: false, error: "internal_error" }, 500);
    }
  }
};
export {
  index_default as default
};
//# sourceMappingURL=index.js.map
