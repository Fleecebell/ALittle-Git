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
  // 真 D1 的 .run() 返回 { success, meta: { changes } }，这里照内存 SQLite 的 changes 模拟
  run() {
    const info = this.db.prepare(this.sql).run(...this.args);
    return { success: true, meta: { changes: Number(info.changes || 0) } };
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

/* ------------------------------------------------ 假 KV（背景图用） */

function makeFakeKV() {
  const map = new Map();
  return {
    async put(key, value, opts) {
      map.set(key, { value, metadata: opts && opts.metadata });
    },
    async getWithMetadata(key) {
      const e = map.get(key);
      return e ? { value: e.value, metadata: e.metadata } : { value: null, metadata: null };
    },
    async delete(key) {
      map.delete(key);
    },
    async list({ prefix } = {}) {
      const keys = [...map.entries()]
        .filter(([k]) => !prefix || k.startsWith(prefix))
        .map(([name, v]) => ({ name, metadata: v.metadata }));
      return { keys, list_complete: true };
    },
  };
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
      // 客户端只上报停留超过 1 秒的格子，这里也照着造，预览才和线上一致
      dwell: rf(1.0, 3.5) * w,
      jumps: 0,
      fails: 0,
    });

  // 地面
  for (let x = config.x0; x <= config.x1; x++) push(x, config.groundY, 1);
  // 悬空平台
  for (const [px0, px1, py] of config.platforms) {
    for (let x = px0; x <= px1; x++) push(x, py, 1);
  }
  // 深坑底部：掉下去立刻死，等不到 1 秒就不上报，所以这里不造格子（和线上一致）
  return cells;
}

function seedDatabase(db) {
  const players = ["player_7f3a91", "player_c2e84b", "player_19ad05"];
  let ts = NOW - 48 * HOUR;
  const tick = () => (ts += ri(300, 9000));

  // 新客户端是 0.5 世界单位一格 → 假数据也用同样密度（索引 = 世界坐标 × 2）
  const GRID = 2;
  const CS = 0.5;
  const toCell = (v) => Math.round(v * GRID);

  // 假通关路线：沿地面走，遇到坑就跳个弧线
  function makeRoute(lv, dxCells) {
    const path = [];
    for (let x = lv.x0; x <= lv.x1; x++) {
      if (x >= lv.pitX0 && x <= lv.pitX1) {
        path.push([x, lv.groundY + 1], [x, lv.groundY + 2]);
      } else {
        path.push([x, lv.groundY]);
      }
    }
    return path.map(([x, y]) => [toCell(x) + dxCells, toCell(y)]);
  }

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
          cell_x: toCell(dx),
          cell_y: toCell(dy),
          cell_size: CS,
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
            cell_x: toCell(c.x),
            cell_y: toCell(c.y),
            x: toCell(c.x) * CS,
            y: toCell(c.y) * CS,
            dwell_s: +(c.dwell * rf(0.6, 1.2)).toFixed(2),
            jumps: jumps,
            jump_fails: jumps > 0 ? ri(0, Math.min(2, jumps)) : 0,
            cell_size: CS,
          });
        }
      }

      const duration = rf(18, 95);
      const cleared = i < lv.clears;

      // 每趟尝试结束一条路线：通关发 clear，没通关的发 death（死在半路，只画到死点）
      if (cleared) {
        const p1Path = makeRoute(lv, 0);
        addEvent(db, player, session, "route", tick(), {
          scene: lv.scene,
          level_index: lv.index,
          attempt: 1,
          actor: "player1",
          outcome: "clear",
          cells: p1Path.map(([x, y]) => `${x},${y}`).join(";"),
          cell_count: p1Path.length,
          duration_s: +duration.toFixed(1),
          cell_size: CS,
        });
        if (rnd() < 0.6) {
          // P2 是延迟模仿 P1，实际游戏里两条路线几乎完全重合 —— 预览数据也照这个来，
          // 好检查"重合时红蓝都看得见"（drawRoutes 重合处画紫色方块）
          const p2Path = makeRoute(lv, 0);
          addEvent(db, player, session, "route", tick(), {
            scene: lv.scene,
            level_index: lv.index,
            attempt: 1,
            actor: "player2",
            outcome: "clear",
            cells: p2Path.map(([x, y]) => `${x},${y}`).join(";"),
            cell_count: p2Path.length,
            duration_s: +(duration + rf(0.3, 2)).toFixed(1),
            cell_size: CS,
          });
        }
      } else {
        const p1Path = makeRoute(lv, 0).slice(0, Math.max(10, Math.floor(lv.width * 0.5)));
        addEvent(db, player, session, "route", tick(), {
          scene: lv.scene,
          level_index: lv.index,
          attempt: 1,
          actor: "player1",
          outcome: "death",
          cells: p1Path.map(([x, y]) => `${x},${y}`).join(";"),
          cell_count: p1Path.length,
          duration_s: +duration.toFixed(1),
          cell_size: CS,
        });
      }

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
const kv = makeFakeKV();

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

  // 本地预览默认不设 ADMIN_KEY（清库按钮会返回 403，和线上没配密钥时一致）；
  // 想在本地点清库 / 上传背景图：ADMIN_KEY=devkey node tools/preview-dashboard.mjs
  const response = await worker.fetch(request, {
    DB: d1,
    BG: kv,
    INGEST_KEY: "",
    ADMIN_KEY: process.env.ADMIN_KEY || "",
  });

  res.statusCode = response.status;
  response.headers.forEach((v, k) => res.setHeader(k, v));
  res.end(Buffer.from(await response.arrayBuffer()));
});

server.listen(port, "127.0.0.1", () => {
  const total = db.prepare("SELECT COUNT(*) AS n FROM game_events").get().n;
  console.log(`看板预览: http://127.0.0.1:${port}/   （假数据 ${total} 条事件）`);
  console.log(`接口自检: http://127.0.0.1:${port}/v1/stats/overview`);
});
