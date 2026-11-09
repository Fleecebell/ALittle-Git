/**
 * 关卡背景图接口的测试：用假 KV 跑，不需要网络也不需要 Cloudflare 账号。
 * 重点覆盖：没配 ADMIN_KEY 一律 403、写错密钥 401、图片本体能原样取回、清单/删除、
 * 以及坏输入（不是 data URL、太大、场景名非法）。
 */

import test from "node:test";
import assert from "node:assert/strict";
import worker from "../src/index.js";

const PNG_1X1 =
  "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==";

class FakeKV {
  constructor() {
    this.map = new Map();
  }
  async put(key, value, opts) {
    this.map.set(key, { value, metadata: opts && opts.metadata });
  }
  async getWithMetadata(key) {
    const e = this.map.get(key);
    if (!e) return { value: null, metadata: null };
    return { value: e.value, metadata: e.metadata };
  }
  async delete(key) {
    this.map.delete(key);
  }
  async list({ prefix, cursor } = {}) {
    const keys = [...this.map.entries()]
      .filter(([k]) => (prefix ? k.startsWith(prefix) : true))
      .map(([k, v]) => ({ name: k, metadata: v.metadata }));
    return { keys, list_complete: true, cursor: undefined };
  }
}

const env = (extra = {}) => ({ BG: new FakeKV(), ADMIN_KEY: "s3cret", ...extra });

const call = (path, init = {}, e = env()) =>
  worker.fetch(new Request("https://example.com" + path, init), e);

const sceneUrl = (scene) => "/v1/backgrounds/" + encodeURIComponent(scene);

const putInit = (body, headers = { "x-admin-key": "s3cret" }) => ({
  method: "PUT",
  headers: { "Content-Type": "application/json", ...headers },
  body: JSON.stringify(body),
});

test("清单：一开始是空的", async () => {
  const res = await call("/v1/backgrounds");
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.deepEqual(body.backgrounds, []);
});

test("没配 ADMIN_KEY：写和删都 403（安全默认）", async () => {
  const e = env({ ADMIN_KEY: "" });
  const res = await call(sceneUrl("9101"), putInit({ data: PNG_1X1 }), e);
  assert.equal(res.status, 403);
  assert.equal((await res.json()).error, "admin_key_not_configured");

  const del = await call(sceneUrl("9101"), { method: "DELETE" }, e);
  assert.equal(del.status, 403);
});

test("密钥不对：401", async () => {
  const res = await call(
    sceneUrl("9101"),
    putInit({ data: PNG_1X1 }, { "x-admin-key": "nope" })
  );
  assert.equal(res.status, 401);
  assert.equal((await res.json()).error, "unauthorized");
});

test("上传 → 清单 → 取回 → 删除", async () => {
  const e = env();

  const put = await call(
    sceneUrl("9101"),
    putInit({ data: PNG_1X1, w: 1600, h: 900, px: -8.5, py: -4, pw: 17, ph: 9.5, alpha: 0.4 }),
    e
  );
  assert.equal(put.status, 200);
  const putBody = await put.json();
  assert.equal(putBody.background.scene, "9101");
  assert.equal(putBody.background.mime, "image/png");
  assert.equal(putBody.background.w, 1600);
  assert.equal(putBody.background.h, 900);
  assert.equal(putBody.background.px, -8.5);
  assert.equal(putBody.background.pw, 17);
  assert.equal(putBody.background.alpha, 0.4);
  assert.ok(putBody.background.bytes > 0);
  assert.ok(putBody.background.updated_at > 0);

  const list = await (await call("/v1/backgrounds", {}, e)).json();
  assert.equal(list.backgrounds.length, 1);
  assert.equal(list.backgrounds[0].scene, "9101");
  assert.equal(list.backgrounds[0].url, "/v1/backgrounds/9101");

  const got = await call(sceneUrl("9101"), {}, e);
  assert.equal(got.status, 200);
  assert.equal(got.headers.get("content-type"), "image/png");
  const bytes = new Uint8Array(await got.arrayBuffer());
  assert.ok(bytes.length > 8);
  assert.equal(bytes[0], 0x89); // PNG magic
  assert.equal(bytes[1], 0x50);

  const del = await call(
    sceneUrl("9101"),
    { method: "DELETE", headers: { "x-admin-key": "s3cret" } },
    e
  );
  assert.equal(del.status, 200);
  assert.equal((await del.json()).deleted, "9101");
  const after = await call(sceneUrl("9101"), {}, e);
  assert.equal(after.status, 404);
  assert.equal((await after.json()).error, "not_found");
});

test("POST 也能传（省得前端只能写 PUT）", async () => {
  const e = env();
  const res = await call(sceneUrl("S1"), {
    method: "POST",
    headers: { "Content-Type": "application/json", "x-admin-key": "s3cret" },
    body: JSON.stringify({ data: PNG_1X1 }),
  }, e);
  assert.equal(res.status, 200);
  const list = await (await call("/v1/backgrounds", {}, e)).json();
  assert.equal(list.backgrounds[0].scene, "S1");
});

test("坏输入：不是 data URL / 类型不支持 / 场景名非法", async () => {
  const e = env();

  const bad = await call(sceneUrl("9101"), putInit({ data: "http://x/y.png" }), e);
  assert.equal(bad.status, 400);
  assert.equal((await bad.json()).error, "invalid_image_data_url");

  const svg = await call(
    sceneUrl("9101"),
    putInit({ data: "data:image/svg+xml;base64,PHN2Zy8+" }),
    e
  );
  assert.equal(svg.status, 400);

  const scene = await call("/v1/backgrounds/" + encodeURIComponent("a/b"), {
    method: "GET",
  }, e);
  assert.equal(scene.status, 400);
  assert.equal((await scene.json()).error, "invalid_scene");
});

test("太大：413 且带上上限", async () => {
  const e = env();
  const huge = "data:image/png;base64," + "A".repeat(5 * 1024 * 1024);
  const res = await call(sceneUrl("9101"), putInit({ data: huge }), e);
  assert.equal(res.status, 413);
  const body = await res.json();
  assert.equal(body.error, "image_too_large");
  assert.equal(body.max_bytes, 3 * 1024 * 1024);
});

test("没绑定 BG 的部署：503 而不是崩掉", async () => {
  const res = await call("/v1/backgrounds", {}, { ADMIN_KEY: "" });
  assert.equal(res.status, 503);
  assert.equal((await res.json()).error, "bg_not_configured");
});

test("CORS 允许 PUT/DELETE 和 X-Admin-Key", async () => {
  const res = await call("/v1/backgrounds");
  assert.equal(res.headers.get("access-control-allow-origin"), "*");
  const allow = res.headers.get("access-control-allow-methods");
  assert.ok(allow.includes("PUT"));
  assert.ok(allow.includes("DELETE"));
  assert.ok(res.headers.get("access-control-allow-headers").includes("X-Admin-Key"));
});
