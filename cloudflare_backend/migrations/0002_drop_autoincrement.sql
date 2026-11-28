-- 去掉 AUTOINCREMENT：省掉每次 INSERT 对 sqlite_sequence 的那一行写入。
-- 实测：1 个索引时，AUTOINCREMENT 让每条事件从 2 行变 3 行（+50% 写入额度消耗）。
-- id 用 INTEGER PRIMARY KEY（rowid 别名）即可，删除行后 id 可能复用，对这张只追加的表没有影响。
--
-- 执行：npx wrangler d1 execute alittle-telemetry --remote --file=./migrations/0002_drop_autoincrement.sql

PRAGMA foreign_keys = OFF;

CREATE TABLE IF NOT EXISTS game_events_new (
  id         INTEGER PRIMARY KEY,
  player_id  TEXT    NOT NULL,
  session_id TEXT    NOT NULL,
  event_type TEXT    NOT NULL,
  metadata   TEXT    NOT NULL DEFAULT '{}',
  client_ts  INTEGER NOT NULL,
  server_ts  INTEGER NOT NULL
);

INSERT INTO game_events_new (id, player_id, session_id, event_type, metadata, client_ts, server_ts)
SELECT id, player_id, session_id, event_type, metadata, client_ts, server_ts FROM game_events;

DROP TABLE game_events;
ALTER TABLE game_events_new RENAME TO game_events;

CREATE INDEX IF NOT EXISTS idx_events_type_ts ON game_events (event_type, client_ts);

-- 注：sqlite_sequence 是 SQLite 内部表，D1 不允许 DROP（会报 `table sqlite_sequence may not be dropped`）。
-- 留着无妨：表上没有 AUTOINCREMENT 列以后，它不会再被写入。
