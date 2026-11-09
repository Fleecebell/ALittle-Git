/**
 * 关卡背景图：存在 Workers KV 里，让同一个链接的人都能看到同一张截图。
 *
 * 为什么用 KV 而不是 D1：
 *   - D1 单条 SQL 语句上限 100 KB，一张截图 base64 之后基本都会超；
 *   - D1 免费版整库上限 500 MB，图是二进制大对象，塞进去会拖慢统计查询；
 *   - KV 免费额度 1 GB 存储 / 10 万次读一天 / 1000 次写一天，一张图一次写，完全够。
 *
 * 键：`bg:<scene>`，值就是图片原始字节，metadata 里放 mime/宽/高/大小/更新时间。
 * 写入需要 ADMIN_KEY（和清库同一把），读取公开。
 */

import { CORS_HEADERS, json } from "./http.js";

const PREFIX = "bg:";
const MAX_BYTES = 3 * 1024 * 1024; // 3 MB，够 1600px 宽的 WebP 了
const ALLOWED = new Set(["image/webp", "image/png", "image/jpeg", "image/gif"]);
const SCENE_RE = /^[A-Za-z0-9 _.\-]{1,64}$/;

function keyOf(scene) {
  return PREFIX + scene;
}

function authOk(env, url, request) {
  const need = env.ADMIN_KEY;
  if (!need) return { ok: false, status: 403, error: "admin_key_not_configured" };
  const given = request.headers.get("x-admin-key") || url.searchParams.get("key") || "";
  if (given !== need) return { ok: false, status: 401, error: "unauthorized" };
  return { ok: true };
}

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
    alpha: m.alpha === undefined ? 0.55 : Number(m.alpha),
    url: `/v1/backgrounds/${encodeURIComponent(scene)}`,
  };
}

function clampNum(v, min, max, fallback) {
  const n = Number(v);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, n));
}

/** GET /v1/backgrounds  →  所有关卡背景图的清单（不含图片本体） */
async function listBackgrounds(env) {
  if (!env.BG) return json({ ok: false, error: "bg_not_configured" }, 503);
  const out = [];
  let cursor;
  do {
    const page = await env.BG.list({ prefix: PREFIX, cursor });
    for (const k of page.keys) {
      out.push(metaOf(k.name.slice(PREFIX.length), k.metadata));
    }
    cursor = page.list_complete ? undefined : page.cursor;
  } while (cursor);
  out.sort((a, b) => String(a.scene).localeCompare(String(b.scene)));
  return json({ ok: true, backgrounds: out });
}

/** GET /v1/backgrounds/<scene>  →  图片本体 */
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
      ...CORS_HEADERS,
    },
  });
}

/** PUT/POST /v1/backgrounds/<scene>  →  body { data: "data:image/webp;base64,...", w, h } */
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
    w: clampNum(body.w, 0, 20000, 0),
    h: clampNum(body.h, 0, 20000, 0),
    bytes: parsed.bytes.byteLength,
    updated_at: Date.now(),
    px: Math.round(clampNum(body.px, -1e6, 1e6, 0) * 100) / 100,
    py: Math.round(clampNum(body.py, -1e6, 1e6, 0) * 100) / 100,
    pw: Math.round(clampNum(body.pw, 0, 1e6, 0) * 100) / 100,
    ph: Math.round(clampNum(body.ph, 0, 1e6, 0) * 100) / 100,
    alpha: Math.round(clampNum(body.alpha, 0.05, 1, 0.55) * 100) / 100,
  };
  await env.BG.put(keyOf(scene), parsed.bytes, { metadata: meta });
  return json({ ok: true, background: metaOf(scene, meta) });
}

async function deleteBackground(env, request, scene, url) {
  if (!env.BG) return json({ ok: false, error: "bg_not_configured" }, 503);
  const auth = authOk(env, url, request);
  if (!auth.ok) return json({ ok: false, error: auth.error }, auth.status);
  await env.BG.delete(keyOf(scene));
  return json({ ok: true, deleted: scene });
}

/** /v1/backgrounds 下的所有路由 */
export async function handleBackgrounds(url, env, request) {
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
