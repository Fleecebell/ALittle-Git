// 本地假接收端（只用于开发自测，不参与部署）。
//
// 作用：在不联网、没有 Cloudflare 账号的情况下，验证 Unity 端到底把什么 JSON 发了出去。
// 用法：
//   node tools/mock-receiver.mjs            # 监听 127.0.0.1:8791
//   然后在 Unity 里让 game 往 http://127.0.0.1:8791/v1/events 上报
//   收到的原始请求体逐行写进 tools/received.jsonl
//
// 注意：它不校验、不入库，只是"照单全收"，用来肉眼检查 payload。

import http from "node:http";
import fs from "node:fs";
import path from "node:path";

const PORT = Number(process.env.PORT || 8791);
const OUT = path.join(import.meta.dirname, "received.jsonl");

const server = http.createServer((req, res) => {
  if (req.method === "OPTIONS") {
    res.writeHead(204, {
      "access-control-allow-origin": "*",
      "access-control-allow-headers": "content-type,x-telemetry-key",
      "access-control-allow-methods": "POST,GET,OPTIONS",
    });
    res.end();
    return;
  }

  if (req.method === "GET" && (req.url === "/health" || req.url === "/")) {
    res.writeHead(200, { "content-type": "application/json" });
    res.end(JSON.stringify({ ok: true, mock: true, port: PORT }));
    return;
  }

  if (req.method === "POST") {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      const body = Buffer.concat(chunks).toString("utf8");
      let events = 0;
      try {
        const parsed = JSON.parse(body);
        events = Array.isArray(parsed?.events) ? parsed.events.length : 1;
      } catch {
        events = -1;
      }

      fs.appendFileSync(OUT, JSON.stringify({ at: new Date().toISOString(), bytes: body.length, events, body }) + "\n");
      console.log(`[mock] POST ${req.url} ${body.length}B events=${events}`);

      res.writeHead(200, { "content-type": "application/json" });
      res.end(JSON.stringify({ ok: true, inserted: Math.max(events, 0), rejected: 0 }));
    });
    return;
  }

  res.writeHead(404, { "content-type": "application/json" });
  res.end(JSON.stringify({ ok: false, error: "not_found" }));
});

server.listen(PORT, "127.0.0.1", () => {
  console.log(`[mock] listening on http://127.0.0.1:${PORT}`);
  console.log(`[mock] 收到的 payload 写入 ${OUT}`);
});
