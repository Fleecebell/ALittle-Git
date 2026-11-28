-- ============================================================
-- ALittle 遥测表结构
-- 执行方式（远程）: npx wrangler d1 execute alittle-telemetry --remote --file=./schema.sql
-- 执行方式（本地）: npx wrangler d1 execute alittle-telemetry --local  --file=./schema.sql
-- 全部语句幂等，可以重复执行。
-- ============================================================

CREATE TABLE IF NOT EXISTS game_events (
  -- 注意：没有 AUTOINCREMENT。有它的话每次 INSERT 还要写一行 sqlite_sequence，
  -- 实测每条事件从 2 行写入变 3 行（D1 免费额度按写入行数算）。
  id         INTEGER PRIMARY KEY,
  player_id  TEXT    NOT NULL,              -- 客户端生成并持久化的 UUID
  session_id TEXT    NOT NULL,              -- 一次启动一个（关掉重开算新会话）
  event_type TEXT    NOT NULL,              -- session_start / level_start / level_clear / death / cell / ...
  metadata   TEXT    NOT NULL DEFAULT '{}', -- JSON 文本（JSON1 扩展可用，见 queries.sql）
  client_ts  INTEGER NOT NULL,              -- 客户端 UTC 毫秒时间戳
  server_ts  INTEGER NOT NULL               -- 服务端落库毫秒时间戳（可用来发现改系统时间）
);

-- 只留这一个索引。
-- 原因：D1 的免费额度是按「写入行数」算的，每建一个索引，每次 INSERT 就多写一行。
-- 实测：3 个索引时 1 条事件 = 5 行写入；砍到 1 个索引后 1 条 = 3 行（表 + 索引 + 自增序号），
-- 也就是说同样额度能多存 66% 的数据。按 session_id / player_id 过滤的查询目前没有，
-- 所以这两个索引只是白花写入额度。以后真要做"按玩家看长期行为"，再加回来。
CREATE INDEX IF NOT EXISTS idx_events_type_ts ON game_events (event_type, client_ts);

-- ============================================================
-- 事件约定（客户端实现，见 Assets/Scripts/Telemetry/）
-- ------------------------------------------------------------
-- session_start : {scene, app_version, unity_version, platform, screen_w, screen_h, language, cpu, gpu}
-- level_start   : {scene, attempt, session_time_s}
-- level_clear   : {scene, attempt, duration_s, deaths, jumps, jump_fails, distance}
-- level_quit    : {scene, attempt, duration_s, deaths, jumps, jump_fails, distance}  -- 中途退出/关游戏
-- death         : {scene, attempt, cause(hazard:xxx|crush|manual|unknown), actor(player1|player2),
--                  x, y, cell_x, cell_y, survived_s(本次进入关卡到死亡), deaths_in_level}
-- cell          : {scene, attempt, cell_x, cell_y, x, y, dwell_s, idle_s, jumps, jump_fails, part}
--                 ↑ 客户端每 60s / 关卡结束各上报一次增量，同一格子多行需 SUM
-- ============================================================
