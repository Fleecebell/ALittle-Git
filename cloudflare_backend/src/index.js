/**
 * ALittle 遥测接收端
 * ---------------------------------------------------------------
 * 职责只有三件事：校验 → 批量写 D1 → 返回结果。
 * 不做任何统计/聚合（聚合放在客户端做，D1 只当"事件表"用），
 * 这样每次请求的 CPU 时间极短，稳在免费版 10ms CPU 限制内。
 *
 * 路由：
 *   OPTIONS /                     CORS 预检
 *   GET     /health               健康检查
 *   GET     /                     看板页面（热力图等可视化）
 *   GET     /v1                   服务信息（JSON）
 *   GET     /v1/stats/*           只读统计（见 stats.js）
 *   POST    /v1/events            上报（单条或数组）
 *
 * 上报体格式（两种都支持）：
 *   单条: { "player_id": "...", "session_id": "...", "event_type": "death",
 *           "client_ts": 1762000000000, "metadata": { ... } }
 *   批量: { "events": [ {...}, {...} ] }
 *   也接受裸数组 [ {...}, {...} ]
 */

import { json, html, CORS_HEADERS } from "./http.js";
import { handleStats } from "./stats.js";
import DASHBOARD_HTML from "./dashboard-page.js";

const INGEST_PATH = "/v1/events";

// ---- 硬上限（防止恶意/异常请求把 CPU 与写入额度打爆）----
const MAX_BODY_BYTES = 256 * 1024; // 单次请求体
const MAX_EVENTS_PER_REQUEST = 200; // 单次最多事件数
const MAX_METADATA_BYTES = 4096; // 单条 metadata 上限
const BATCH_CHUNK = 50; // 每批 50 条语句，避免单批过大
const MAX_ERRORS_REPORTED = 8;

// ---- 字段规则 ----
const ID_RE = /^[A-Za-z0-9_-]{6,64}$/; // player_id / session_id
const TYPE_RE = /^[a-z][a-z0-9_]{0,47}$/; // event_type
const MIN_TS = Date.UTC(2020, 0, 1); // 2020-01-01，早于此视为脏数据
const MAX_CLOCK_SKEW_MS = 7 * 24 * 3600 * 1000; // 允许未来 7 天的时钟偏差

const INSERT_SQL =
  "INSERT INTO game_events (player_id, session_id, event_type, metadata, client_ts, server_ts) " +
  "VALUES (?, ?, ?, ?, ?, ?)";

const encoder = new TextEncoder();
const byteLength = (s) => encoder.encode(s).length;

/** 时间戳归一化：< 1e11 认为是秒，统一成毫秒 */
function normalizeTs(value, now) {
  if (typeof value !== "number" || !Number.isFinite(value)) return null;
  let ms = value;
  if (ms < 1e11) ms = Math.round(ms * 1000);
  ms = Math.round(ms);
  if (ms < MIN_TS || ms > now + MAX_CLOCK_SKEW_MS) return null;
  return ms;
}

/** metadata 只接受对象；字符串/数字会被包一层，保证存进库的一定是合法 JSON */
function normalizeMetadata(value) {
  let obj;
  if (value && typeof value === "object" && !Array.isArray(value)) {
    obj = value;
  } else if (value === undefined || value === null) {
    obj = {};
  } else {
    obj = { value };
  }
  const text = JSON.stringify(obj);
  if (byteLength(text) > MAX_METADATA_BYTES) return null;
  return text;
}

/**
 * 校验单条事件。通过返回 { row }，失败返回 { error }。
 * 错误信息只带字段名和原因，不带原值（避免日志里出现 user data）。
 */
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

function splitIntoChunks(arr, size) {
  const out = [];
  for (let i = 0; i < arr.length; i += size) out.push(arr.slice(i, i + size));
  return out;
}

async function handleIngest(request, env) {
  // 可选鉴权：配置了 INGEST_KEY 才校验（只是挡一下乱发，不是真正的安全边界）
  if (env.INGEST_KEY) {
    const provided = request.headers.get("x-telemetry-key") || "";
    if (provided !== env.INGEST_KEY) {
      return json({ ok: false, error: "unauthorized" }, 401);
    }
  }

  // 先用 content-length 快速拒绝，再按真实长度兜底
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
    // 单条裸对象 { player_id, event_type, ... }
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

  // 一条都不合法 → 直接 400，便于客户端发现问题（而不是静默丢弃）
  if (rows.length === 0) {
    return json({ ok: false, error: "no_valid_events", details: errors }, 400);
  }

  // 批量写入：同一个 prepared statement 反复 bind，CPU 最省
  const stmt = env.DB.prepare(INSERT_SQL);
  let inserted = 0;
  try {
    for (const chunk of splitIntoChunks(rows, BATCH_CHUNK)) {
      const statements = chunk.map((r) =>
        stmt.bind(r[0], r[1], r[2], r[3], r[4], now)
      );
      await env.DB.batch(statements);
      inserted += chunk.length;
    }
  } catch (err) {
    console.error("d1_insert_failed", err && err.message);
    // 503 让客户端进重试队列（宁可重复几条，也不要静默丢数据）
    return json({ ok: false, error: "db_write_failed" }, 503);
  }

  return json({
    ok: true,
    inserted,
    rejected: list.length - rows.length,
    details: errors.length ? errors : undefined,
  });
}

export default {
  async fetch(request, env) {
    try {
      const url = new URL(request.url);

      // CORS 预检
      if (request.method === "OPTIONS") {
        return new Response(null, { status: 204, headers: CORS_HEADERS });
      }

      // 只读统计接口（看板用）
      if (url.pathname === "/v1/stats" || url.pathname.startsWith("/v1/stats/")) {
        if (request.method !== "GET") {
          return json({ ok: false, error: "method_not_allowed" }, 405, {
            Allow: "GET, OPTIONS",
          });
        }
        return await handleStats(url, env, request);
      }

      if (request.method === "GET") {
        if (url.pathname === "/health") {
          return json({ ok: true, service: "alittle-telemetry", now: Date.now() });
        }
        // 根路径直接给看板，省得另外托管静态站点
        if (url.pathname === "/" || url.pathname === "/dashboard") {
          return html(DASHBOARD_HTML);
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
              scene: "GET /v1/stats/scene?scene=<场景名>",
              recent: "GET /v1/stats/recent",
            },
          });
        }
        return json({ ok: false, error: "not_found" }, 404);
      }

      if (request.method !== "POST") {
        return json({ ok: false, error: "method_not_allowed" }, 405, {
          Allow: "GET, POST, OPTIONS",
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
  },
};
