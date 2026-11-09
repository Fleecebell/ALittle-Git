-- ============================================================
-- 常用查询（D1 支持 JSON1 扩展，metadata 里的字段用 json_extract 取）
-- 用法示例:
--   npx wrangler d1 execute alittle-telemetry --remote --command="<这里的一条 SQL>"
--   npx wrangler d1 execute alittle-telemetry --remote --file=./queries/one.sql
-- 注意: --command 里不要有换行；长 SQL 建议存成文件用 --file 执行。
-- ============================================================

-- 1) 每关死得多不多（死亡数 / 进入次数 / 平均每次进入死几次）
SELECT
  json_extract(metadata, '$.scene')                AS scene,
  COUNT(*)                                         AS deaths,
  COUNT(DISTINCT session_id)                       AS sessions,
  ROUND(COUNT(*) * 1.0 / COUNT(DISTINCT session_id), 2) AS deaths_per_session
FROM game_events
WHERE event_type = 'death'
GROUP BY scene
ORDER BY deaths DESC;

-- 2) 死亡热点：把死亡坐标按 1 单位格子聚合成热力图
--    ⚠️ 这是"在哪里死得最多"的核心查询，结果可直接丢进表格画热力图
SELECT
  json_extract(metadata, '$.scene')  AS scene,
  json_extract(metadata, '$.cell_x') AS cell_x,
  json_extract(metadata, '$.cell_y') AS cell_y,
  COUNT(*)                           AS deaths,
  GROUP_CONCAT(DISTINCT json_extract(metadata, '$.cause')) AS causes
FROM game_events
WHERE event_type = 'death'
GROUP BY scene, cell_x, cell_y
ORDER BY deaths DESC
LIMIT 50;

-- 3) 空格按得最多的位置（jumps = 成功起跳次数，jump_fails = 按了没跳成）
--    失败率高 = 这个地方手感差/判定苛刻
SELECT
  json_extract(metadata, '$.scene')   AS scene,
  json_extract(metadata, '$.cell_x')  AS cell_x,
  json_extract(metadata, '$.cell_y')  AS cell_y,
  SUM(json_extract(metadata, '$.jumps'))      AS jumps,
  SUM(json_extract(metadata, '$.jump_fails')) AS jump_fails,
  ROUND(
    SUM(json_extract(metadata, '$.jump_fails')) * 1.0 /
    NULLIF(SUM(json_extract(metadata, '$.jumps')) + SUM(json_extract(metadata, '$.jump_fails')), 0),
    3
  ) AS fail_rate
FROM game_events
WHERE event_type = 'cell'
GROUP BY scene, cell_x, cell_y
HAVING jumps > 0
ORDER BY jumps DESC
LIMIT 50;

-- 4) 待的时间最长的地方（玩家在哪卡住；客户端只上报停留超过 1 秒的格子）
SELECT
  json_extract(metadata, '$.scene')  AS scene,
  json_extract(metadata, '$.cell_x') AS cell_x,
  json_extract(metadata, '$.cell_y') AS cell_y,
  ROUND(SUM(json_extract(metadata, '$.dwell_s')), 1) AS dwell_s
FROM game_events
WHERE event_type = 'cell'
GROUP BY scene, cell_x, cell_y
HAVING dwell_s > 5
ORDER BY dwell_s DESC
LIMIT 50;

-- 5) 每关花了多少时间（只统计真正通关的）
SELECT
  json_extract(metadata, '$.scene')                      AS scene,
  COUNT(*)                                               AS clears,
  ROUND(AVG(json_extract(metadata, '$.duration_s')), 1)   AS avg_seconds,
  ROUND(MIN(json_extract(metadata, '$.duration_s')), 1)   AS best_seconds,
  ROUND(MAX(json_extract(metadata, '$.duration_s')), 1)   AS worst_seconds,
  ROUND(AVG(json_extract(metadata, '$.deaths')), 1)       AS avg_deaths
FROM game_events
WHERE event_type = 'level_clear'
GROUP BY scene
ORDER BY scene;

-- 6) 通关率 = 通关次数 / 进入次数（哪些关在劝退玩家）
SELECT
  s.scene,
  s.enters,
  COALESCE(c.clears, 0) AS clears,
  ROUND(COALESCE(c.clears, 0) * 1.0 / s.enters, 3) AS clear_rate
FROM (
  SELECT json_extract(metadata, '$.scene') AS scene, COUNT(*) AS enters
  FROM game_events WHERE event_type = 'level_start' GROUP BY scene
) AS s
LEFT JOIN (
  SELECT json_extract(metadata, '$.scene') AS scene, COUNT(*) AS clears
  FROM game_events WHERE event_type = 'level_clear' GROUP BY scene
) AS c ON c.scene = s.scene
ORDER BY clear_rate ASC;

-- 7) 死亡原因分布（岩浆 vs 夹死 vs 自己按 R）
SELECT
  json_extract(metadata, '$.cause') AS cause,
  COUNT(*)                          AS deaths
FROM game_events
WHERE event_type = 'death'
GROUP BY cause
ORDER BY deaths DESC;

-- 8) 还原某一次会话的完整时间线（把 session_id 换成你要看的）
SELECT
  datetime(client_ts / 1000, 'unixepoch') AS at_utc,
  event_type,
  metadata
FROM game_events
WHERE session_id = 'REPLACE_WITH_SESSION_ID'
ORDER BY client_ts
LIMIT 200;

-- 9) 每个玩家玩了多少（判断测试者是否真的玩了）
SELECT
  player_id,
  COUNT(DISTINCT session_id)      AS sessions,
  MIN(datetime(client_ts/1000, 'unixepoch')) AS first_seen,
  MAX(datetime(client_ts/1000, 'unixepoch')) AS last_seen,
  COUNT(*)                        AS events
FROM game_events
GROUP BY player_id
ORDER BY events DESC;

-- 10) 每关 P1 / P2 各死多少次（看板用红色画 P1、蓝色画 P2）
--     老数据没有 actor 字段，一律算 player1
SELECT
  json_extract(metadata, '$.scene') AS scene,
  COALESCE(json_extract(metadata, '$.actor'), 'player1') AS actor,
  COUNT(*)                          AS deaths
FROM game_events
WHERE event_type = 'death'
GROUP BY scene, actor
ORDER BY scene, actor;

-- 11) 单个格子里 P1 / P2 的停留、跳跃（热力图取数用这个）
SELECT
  json_extract(metadata, '$.scene')  AS scene,
  json_extract(metadata, '$.cell_x') AS cell_x,
  json_extract(metadata, '$.cell_y') AS cell_y,
  COALESCE(json_extract(metadata, '$.actor'), 'player1') AS actor,
  ROUND(SUM(json_extract(metadata, '$.dwell_s')), 1) AS dwell_s,
  SUM(json_extract(metadata, '$.jumps'))             AS jump_count
FROM game_events
WHERE event_type = 'cell'
GROUP BY scene, cell_x, cell_y, actor
ORDER BY dwell_s DESC
LIMIT 200;

-- 12) 每关的平均单次时长（通关 + 中途退出都算，比只算通关更接近真实体验）
SELECT
  json_extract(metadata, '$.scene') AS scene,
  COUNT(*)                          AS runs,
  ROUND(AVG(json_extract(metadata, '$.duration_s')), 1) AS avg_run_seconds,
  ROUND(AVG(json_extract(metadata, '$.deaths')), 2)     AS avg_run_deaths
FROM game_events
WHERE event_type IN ('level_clear', 'level_quit')
GROUP BY scene
ORDER BY avg_run_seconds DESC;

-- 13) 通关路线（每格 0.5 世界单位；老数据 cell_size 缺省按 1，索引 ×2 归一化）
--     cells 是一串 "x,y" 用分号连起来，直接拿去脚本里画图就行
SELECT
  json_extract(metadata, '$.scene')             AS scene,
  COALESCE(json_extract(metadata, '$.actor'), 'player1') AS actor,
  json_extract(metadata, '$.attempt')           AS attempt,
  json_extract(metadata, '$.cell_count')        AS cell_count,
  ROUND(json_extract(metadata, '$.duration_s'), 1) AS run_seconds,
  json_extract(metadata, '$.cells')             AS cells,
  datetime(client_ts / 1000, 'unixepoch', 'localtime') AS at
FROM game_events
WHERE event_type = 'route'
ORDER BY client_ts DESC
LIMIT 50;
