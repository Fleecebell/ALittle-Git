/**
 * 本地看板预览：不部署也能看到看板的真实样子。
 *
 * 用真的 Worker 代码（src/index.js：托管 HTML + /v1/stats/*）跑在一个本地 http
 * 服务上，只把 D1 换成 node:sqlite 的内存库，并灌一份"像真的一局游戏"的假数据。
 * 这样看到的就是线上会看到的东西，不存在"预览版和线上版不一致"。
 *
 *   node tools/preview-dashboard.mjs            # 默认 127.0.0.1:8792
 *   node tools/preview-dashboard.mjs 9000       # 换端口
 */

import http from "node:http";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import worker from "../src/index.js";

const here = dirname(fileURLToPath(import.meta.url));
const schema = readFileSync(join(here, "..", "schema.sql"), "utf8");

/* ------------------------------------------------ D1 适配器（同测试里那套） */

class Statement {
  constructor(db, sql, args = []) {
    this.db = db;
    this.sql = sql;
    this.args = args;
  }
  bind(...args) {
    return new Statement(this.db, this.sql, args);
  }
  all() {
    return { results: this.db.prepare(this.sql).all(...this.args) };
  }
}

class FakeD1 {
  constructor(db) {
    this.db = db;
  }
  prepare(sql) {
    return new Statement(this.db, sql);
  }
  async batch(statements) {
    return statements.map((s) => ({ success: true, results: s.all().results }));
  }
}

/* ------------------------------------------------ 假数据（确定性随机） */

let seedState = 20261109;
function rnd() {
  seedState = (seedState * 1103515245 + 12345) & 0x7fffffff;
  return seedState / 0x7fffffff;
}
const ri = (a, b) => a + Math.floor(rnd() * (b - a + 1));
const rf = (a, b) => +(a + rnd() * (b - a)).toFixed(2);

const NOW = Date.now();
const HOUR = 3600_000;

function makeDb() {
  const db = new DatabaseSync(":memory:");
  db.exec(schema);
  return db;
}

function addEvent(db, player, session, type, ts, meta) {
  db.prepare(
    "INSERT INTO game_events (player_id, session_id, event_type, metadata, client_ts, server_ts) VALUES (?, ?, ?, ?, ?, ?)"
  ).run(player, session, type, JSON.stringify(meta), ts, ts);
}

/** 一关的地形：路面 + 平台 + 深坑 + 尖刺 + 夹子 */
function levelCells(config) {
  const cells = [];
  const push = (x, y, w) =>
    cells.push({
      x,
      y,
      dwell: rf(0.4, 1.2) * w,
      idle: rf(0, 0.6) * w,
      jumps: 0,
      fails: 0,
    });

  // 地面
  for (let x = config.x0; x <= config.x1; x++) push(x, config.groundY, 1);
  // 悬空平台
  for (const [px0, px1, py] of config.platforms) {
    for (let x = px0; x <= px1; x++) push(x, py, 1);
  }
  // 深坑底部（掉下去就死，所以停留时间极短）
  for (let x = config.pitX0; x <= config.pitX1; x++) {
    cells.push({ x, y: config.pitBottomY, dwell: rf(0.1, 0.5), idle: 0, jumps: 0, fails: 0 });
  }
  return cells;
}

function seedDatabase(db) {
  const players = ["player_7f3a91", "player_c2e84b", "player_19ad05"];
  let ts = NOW - 48 * HOUR;
  const tick = () => (ts += ri(300, 9000));

  const levels = [
    {
      scene: "9101",
      index: 1,
      starts: 24,
      clears: 11,
      quits: 13,
      x0: 0,
      x1: 31,
      groundY: -2,
      platforms: [
        [6, 11, 2],
        [24, 27, 5],
      ],
      pitX0: 14,
      pitX1: 17,
      pitBottomY: -7,
      // [格子x, 格子y, 死亡权重]
      hotDeaths: [
        [15, -7, 5],
        [16, -7, 4],
        [21, -2, 3],
        [9, 2, 1],
        [26, 5, 1],
      ],
      jumpSpots: [
        [12, -2],
        [13, -2],
        [18, -2],
        [7, 2],
        [25, 5],
      ],
    },
    {
      scene: "S1",
      index: 8,
      starts: 9,
      clears: 5,
      quits: 4,
      x0: 0,
      x1: 18,
      groundY: 0,
      platforms: [[4, 8, 3]],
      pitX0: 12,
      pitX1: 14,
      pitBottomY: -5,
      hotDeaths: [
        [13, -5, 3],
        [9, 0, 2],
      ],
      jumpSpots: [
        [10, 0],
        [11, 0],
      ],
    },
    {
      scene: "S0",
      index: 4,
      starts: 7,
      clears: 6,
      quits: 1,
      x0: 0,
      x1: 12,
      groundY: 0,
      platforms: [],
      pitX0: 9,
      pitX1: 10,
      pitBottomY: -4,
      hotDeaths: [[10, -4, 1]],
      jumpSpots: [[5, 0]],
    },
  ];

  for (const lv of levels) {
    const cells = levelCells(lv);
    const jumpsPerCell = new Map();
    for (const [x, y] of lv.jumpSpots) jumpsPerCell.set(`${x},${y}`, ri(4, 14));

    for (let i = 0; i < lv.starts; i++) {
      const player = players[i % players.length];
      const session = `s_2026110${ri(7, 9)}_${ri(10, 22)}00_${player.slice(7)}`;
      const startTs = ts;
      addEvent(db, player, session, "level_start", tick(), {
        scene: lv.scene,
        level_index: lv.index,
        attempt: 1,
        session_time_s: ri(1, 40),
      });

      // 这一趟死了几次
      const deaths = ri(0, 3);
      for (let d = 0; d < deaths; d++) {
        const [dx, dy] = weightedPick(lv.hotDeaths);
        addEvent(db, player, session, "death", tick(), {
          scene: lv.scene,
          level_index: lv.index,
          attempt: 1,
          cause: pickCause(dx, dy, lv),
          actor: rnd() < 0.8 ? "player1" : "player2",
          x: dx + rf(0.1, 0.9),
          y: dy + rf(0.1, 0.9),
          cell_x: dx,
          cell_y: dy,
          survived_s: rf(3, 30),
          deaths_in_level: d + 1,
          jumps_before_death: ri(0, 6),
        });
      }

      // 格子增量：分 2 次上报，模拟"每 60s 刷一次"
      for (let pass = 0; pass < 2; pass++) {
        for (const c of cells) {
          if (rnd() < 0.25) continue; // 有些格子这趟没去过
          const jumps = Math.round((jumpsPerCell.get(`${c.x},${c.y}`) || 0) * rf(0.1, 0.3));
          addEvent(db, player, session, "cell", tick(), {
            scene: lv.scene,
            attempt: 1,
            cell_x: c.x,
            cell_y: c.y,
            x: c.x + 0.5,
            y: c.y + 0.5,
            dwell_s: +(c.dwell * rf(0.3, 0.7)).toFixed(2),
            idle_s: +(c.idle * rf(0.2, 0.8)).toFixed(2),
            jumps: jumps,
            jump_fails: jumps > 0 ? ri(0, Math.min(2, jumps)) : 0,
          });
        }
      }

      const duration = rf(18, 95);
      const cleared = i < lv.clears;
      addEvent(
        db,
        player,
        session,
        cleared ? "level_clear" : "level_quit",
        tick(),
        {
          scene: lv.scene,
          level_index: lv.index,
          attempt: 1,
          duration_s: +duration.toFixed(1),
          deaths,
          jumps: ri(3, 24),
          jump_fails: ri(0, 5),
          distance: rf(20, 90),
          session_time_s: +duration.toFixed(1),
        }
      );
      ts = startTs + duration * 1000 + ri(500, 4000);
      if (ts > NOW - 60_000) ts = NOW - 60_000 - ri(0, 30) * 60_000;
    }
  }

  for (const player of players) {
    addEvent(db, player, `s_20261107_2000_${player.slice(7)}`, "session_start", NOW - 50 * HOUR, {
      scene: "Start",
      app_version: "1.0",
      unity_version: "2022.3.55f1c1",
      platform: player === players[2] ? "WindowsPlayer" : "WindowsEditor",
      device: "DESKTOP-ALITTLE",
      os: "Windows 11  (10.0.22631) 64bit",
      gpu: "NVIDIA GeForce RTX 3060",
      screen_w: 2560,
      screen_h: 1440,
      target_fps: 60,
      language: "zh-Hans",
    });
  }
}

function weightedPick(list) {
  const total = list.reduce((s, it) => s + it[2], 0);
  let r = rnd() * total;
  for (const it of list) {
    r -= it[2];
    if (r <= 0) return [it[0], it[1]];
  }
  return [list[0][0], list[0][1]];
}

function pickCause(dx, dy, lv) {
  if (dx >= lv.pitX0 && dx <= lv.pitX1 && dy === lv.pitBottomY) {
    return rnd() < 0.7 ? "hazard:Lava" : "hazard:Spike";
  }
  if (rnd() < 0.15) return "crush";
  if (rnd() < 0.05) return "manual_r";
  const names = ["Spike", "Lava", "Saw", "Thorn"];
  return "hazard:" + names[ri(0, names.length - 1)];
}

/* ------------------------------------------------ HTTP 服务 */

const db = makeDb();
seedDatabase(db);
const d1 = new FakeD1(db);

const port = Number(process.argv[2] || 8792);

const server = http.createServer(async (req, res) => {
  const chunks = [];
  for await (const c of req) chunks.push(c);
  const body = Buffer.concat(chunks);

  const url = `http://127.0.0.1:${port}${req.url}`;
  const headers = new Headers();
  for (const [k, v] of Object.entries(req.headers)) {
    if (typeof v === "string") headers.set(k, v);
  }

  const request = new Request(url, {
    method: req.method,
    headers,
    body: ["GET", "HEAD"].includes(req.method) ? undefined : body,
  });

  const response = await worker.fetch(request, { DB: d1, INGEST_KEY: "" });

  res.statusCode = response.status;
  response.headers.forEach((v, k) => res.setHeader(k, v));
  res.end(Buffer.from(await response.arrayBuffer()));
});

server.listen(port, "127.0.0.1", () => {
  const total = db.prepare("SELECT COUNT(*) AS n FROM game_events").get().n;
  console.log(`看板预览: http://127.0.0.1:${port}/   （假数据 ${total} 条事件）`);
  console.log(`接口自检: http://127.0.0.1:${port}/v1/stats/overview`);
});
