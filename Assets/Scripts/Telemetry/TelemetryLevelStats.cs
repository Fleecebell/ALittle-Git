using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 单关卡统计：驻留热力（按格子）、跳跃次数、死亡数、时长、位移。
///
/// 关键点：
///   1) P1（MoveFirst）和 P2（MoveLate）分开统计，同一个格子里两人各自一份数据，
///      上报时带 actor 字段，看板才能用红色画 P1、蓝色画 P2。
///   2) 跳跃和驻留是高频数据，客户端先按 CellSize（默认 0.5 世界单位，即半格）累加，
///      每 60 秒 / 关卡结束时只发"增量"，服务端 SUM 起来就是热力图。
///   3) 通关时额外发一条 route 事件，内容是这次通关走过的格子序列，
///      看板用它画红（P1）/ 蓝（P2）的通关路线。
/// </summary>
public class TelemetryLevelStats
{
    private const string ActorP1 = "player1";
    private const string ActorP2 = "player2";

    private class Cell
    {
        public float dwell;   // 待了多久（秒）
        public int jumps;     // 成功起跳次数
        public int fails;     // 按了没跳成次数
    }

    private readonly Telemetry owner;

    /// <summary>actor -> (格子 key -> 累计量)。两个角色各一张表，互相不污染。</summary>
    private readonly Dictionary<string, Dictionary<long, Cell>> cellsByActor =
        new Dictionary<string, Dictionary<long, Cell>>();
    private readonly Dictionary<string, int> attempts = new Dictionary<string, int>();

    /// <summary>actor -> 本次尝试走过的格子序列（按时间顺序，重复的连续格子会合并）</summary>
    private readonly Dictionary<string, List<long>> routeByActor = new Dictionary<string, List<long>>();
    private readonly Dictionary<string, long> routeLast = new Dictionary<string, long>();

    private MoveFirst player;
    private MoveLate player2;
    private string scene = "";
    private int levelIndex = -1;
    private int attempt;
    private bool levelOpen;
    private bool closed;
    private bool hasP2;

    private float duration;
    private float distance;
    private int deaths;
    private int jumps;
    private int fails;

    private float sampleTimer;
    private float flushTimer;
    private float findTimer;

    private Vector3 lastP1Pos;
    private Vector3 lastP2Pos;
    private bool hasLastP1;
    private bool hasLastP2;

    public TelemetryLevelStats(Telemetry owner)
    {
        this.owner = owner;
        cellsByActor[ActorP1] = new Dictionary<long, Cell>();
        cellsByActor[ActorP2] = new Dictionary<long, Cell>();
        routeByActor[ActorP1] = new List<long>();
        routeByActor[ActorP2] = new List<long>();
    }

    /// <summary>是否已经收到过场景（用来在 Start 里补首次场景）</summary>
    public bool HasScene
    {
        get { return !string.IsNullOrEmpty(scene); }
    }

    /// <summary>P1 位置（死亡埋点没给位置时的兜底）</summary>
    public Vector3 PlayerPosition()
    {
        return player != null ? player.transform.position : Vector3.zero;
    }

    // ============================================================
    // 场景切换
    // ============================================================

    public void EnterScene(string sceneName)
    {
        // 上一关没走正常通关流程（直接切场景/调试跳关）：把数据补发掉，别丢
        if (levelOpen && !closed)
        {
            CloseLevel("quit");
        }

        scene = sceneName;
        levelIndex = SceneManager.GetSceneByName(sceneName).buildIndex;
        levelOpen = false;
        closed = false;
        player = null;
        player2 = null;
        hasP2 = false;
        duration = 0f;
        distance = 0f;
        deaths = 0;
        jumps = 0;
        fails = 0;
        sampleTimer = 0f;
        flushTimer = 0f;
        findTimer = 0f;
        hasLastP1 = false;
        hasLastP2 = false;
        foreach (var table in cellsByActor.Values) table.Clear();
        ResetRoutes();

        if (!attempts.ContainsKey(scene)) attempts[scene] = 0;
        attempt = attempts[scene];
    }

    // ============================================================
    // 每帧
    // ============================================================

    public void Tick(float dt)
    {
        if (closed) return;

        if (!levelOpen)
        {
            findTimer -= dt;
            if (findTimer > 0f) return;

            findTimer = 0.25f;
            player = Object.FindObjectOfType<MoveFirst>();
            player2 = Object.FindObjectOfType<MoveLate>();
            if (player == null) return; // 菜单场景没有玩家，不计时也不上报

            BeginLevel();
        }

        if (player == null)
        {
            // 玩家被销毁（换场景的瞬间等）：暂停采样，等新场景 EnterScene 重置
            return;
        }

        // 死亡动画期间不采样：避免把"停在原地"算成驻留、把传送算成位移
        if (DeathRespawnVFX.isDead)
        {
            hasLastP1 = false;
            hasLastP2 = false;
            return;
        }

        duration += dt;

        // 位移：两个角色各自累加，跳变（传送/重置）不计入
        Vector3 p1 = player.transform.position;
        if (hasLastP1)
        {
            float dx = Mathf.Abs(p1.x - lastP1Pos.x);
            if (dx < 5f) distance += dx;
        }
        lastP1Pos = p1;
        hasLastP1 = true;

        hasP2 = player2 != null;
        if (hasP2)
        {
            Vector3 p2 = player2.transform.position;
            if (hasLastP2)
            {
                float dx = Mathf.Abs(p2.x - lastP2Pos.x);
                if (dx < 5f) distance += dx;
            }
            lastP2Pos = p2;
            hasLastP2 = true;
        }

        // 路线每帧记：只在"进入新格子"时真正 append，代价是一次取整 + 一次字典比较，
        // 比 0.1 秒采样准确得多（0.1 秒能跑 1~2 个格子，采样会漏掉转弯，画出来就是斜穿）。
        TrackRoute(ActorP1, p1);
        if (hasP2) TrackRoute(ActorP2, player2.transform.position);

        sampleTimer += dt;
        if (sampleTimer >= TelemetryConfig.SampleInterval)
        {
            float sample = sampleTimer;
            sampleTimer = 0f;
            AccumulateDwell(ActorP1, p1, sample);
            if (hasP2)
            {
                AccumulateDwell(ActorP2, player2.transform.position, sample);
            }
        }

        flushTimer += dt;
        if (flushTimer >= TelemetryConfig.CellFlushIntervalSeconds)
        {
            flushTimer = 0f;
            FlushCells();
        }
    }

    private void BeginLevel()
    {
        levelOpen = true;
        attempt++;
        attempts[scene] = attempt;

        duration = 0f;
        distance = 0f;
        deaths = 0;
        jumps = 0;
        fails = 0;
        sampleTimer = 0f;
        flushTimer = 0f;
        hasLastP1 = false;
        hasLastP2 = false;

        ResetRoutes();

        owner.Enqueue("level_start", new TelemetryFields()
            .Str("scene", scene)
            .Int("level_index", levelIndex)
            .Int("attempt", attempt)
            .Num("session_time_s", Telemetry.SessionSeconds())
            .Bool("has_p2", player2 != null)
            .Build(), Telemetry.NowMs());

        if (TelemetryConfig.verbose)
        {
            Debug.Log($"[Telemetry] 关卡开始 {scene} (第 {attempt} 次) P2={(player2 != null)}");
        }
    }

    // ============================================================
    // 采样
    // ============================================================

    private void AccumulateDwell(string actor, Vector3 position, float seconds)
    {
        Cell cell = GetCell(actor, position);
        cell.dwell += seconds;
    }

    private static long CellKeyOf(Vector3 worldPos)
    {
        int cx = Mathf.FloorToInt(worldPos.x / TelemetryConfig.CellSize);
        int cy = Mathf.FloorToInt(worldPos.y / TelemetryConfig.CellSize);
        return ((long)cx << 32) ^ (uint)cy;
    }

    /// <summary>记录路线：只在进入新格子时追加，重复格子不重复记（路线是一串格子，不关心停留多久）</summary>
    private void TrackRoute(string actor, Vector3 position)
    {
        long key = CellKeyOf(position);
        List<long> route;
        if (!routeByActor.TryGetValue(actor, out route)) return;

        long last;
        if (routeLast.TryGetValue(actor, out last) && last == key) return;

        routeLast[actor] = key;
        if (route.Count >= TelemetryConfig.RouteMaxCells) return; // 超长就截断，不丢已记录的
        route.Add(key);
    }

    private void ResetRoutes()
    {
        foreach (var list in routeByActor.Values) list.Clear();
        routeLast.Clear();
    }

    private void ResetRoute(string actor)
    {
        List<long> route;
        if (routeByActor.TryGetValue(actor, out route)) route.Clear();
        routeLast.Remove(actor);
    }

    /// <summary>
    /// 把两个角色走过的格子序列各发一条 route 事件（看板用来画红/蓝路线）。
    /// outcome：clear = 通关、death = 死在这一趟、quit = 中途退出/切场景。
    /// </summary>
    private void FlushRoutes(string outcome)
    {
        foreach (var actor in routeByActor.Keys) FlushRouteFor(actor, outcome, 2);
    }

    /// <summary>单个角色的路线；minCells 用来过滤太短的（死亡路线防止刷量）</summary>
    private void FlushRouteFor(string actor, string outcome, int minCells)
    {
        List<long> route;
        if (!routeByActor.TryGetValue(actor, out route)) return;
        if (route.Count < minCells) return;

        var sb = new System.Text.StringBuilder(route.Count * 8);
        for (int i = 0; i < route.Count; i++)
        {
            int cx = (int)(route[i] >> 32);
            int cy = (int)(uint)route[i];
            if (i > 0) sb.Append(';');
            sb.Append(cx).Append(',').Append(cy);
        }

        owner.Enqueue("route", new TelemetryFields()
            .Str("scene", scene)
            .Int("level_index", levelIndex)
            .Int("attempt", attempt)
            .Str("actor", actor)
            .Str("outcome", outcome)
            .Str("cells", sb.ToString())
            .Int("cell_count", route.Count)
            .Num("duration_s", duration)
            .Num("cell_size", TelemetryConfig.CellSize)
            .Build(), Telemetry.NowMs());
    }

    private Cell GetCell(string actor, Vector3 worldPos)
    {
        int cx = Mathf.FloorToInt(worldPos.x / TelemetryConfig.CellSize);
        int cy = Mathf.FloorToInt(worldPos.y / TelemetryConfig.CellSize);
        long key = ((long)cx << 32) ^ (uint)cy;

        Dictionary<long, Cell> table;
        if (!cellsByActor.TryGetValue(actor, out table))
        {
            table = new Dictionary<long, Cell>();
            cellsByActor[actor] = table;
        }

        Cell cell;
        if (!table.TryGetValue(key, out cell))
        {
            cell = new Cell();
            table[key] = cell;
        }
        return cell;
    }

    // ============================================================
    // 埋点入口
    // ============================================================

    public void RecordJump(string actor, Vector3 position, bool success)
    {
        if (closed) return;

        if (string.IsNullOrEmpty(actor)) actor = ActorP1;

        Cell cell = GetCell(actor, position);
        if (success)
        {
            jumps++;
            cell.jumps++;
        }
        else
        {
            fails++;
            cell.fails++;
        }
    }

    public void RecordDeath(string cause, string actor, Vector3 position)
    {
        if (closed || !levelOpen) return;

        deaths++;
        // 这一趟到此为止：先把死者走过的路线发出去（太短的不发，避免刷数据），再清掉重来
        FlushRouteFor(actor, "death", TelemetryConfig.RouteMinCellsOnDeath);
        ResetRoute(actor);

        int cx = Mathf.FloorToInt(position.x / TelemetryConfig.CellSize);
        int cy = Mathf.FloorToInt(position.y / TelemetryConfig.CellSize);

        owner.Enqueue("death", new TelemetryFields()
            .Str("scene", scene)
            .Int("level_index", levelIndex)
            .Int("attempt", attempt)
            .Str("cause", cause)
            .Str("actor", actor)
            .Num("x", position.x)
            .Num("y", position.y)
            .Int("cell_x", cx)
            .Int("cell_y", cy)
            .Num("cell_size", TelemetryConfig.CellSize)
            .Num("survived_s", duration)          // 这次进关到死亡活了多久
            .Int("deaths_in_level", deaths)
            .Int("jumps_before_death", jumps)
            .Build(), Telemetry.NowMs());

        if (TelemetryConfig.verbose)
        {
            Debug.Log($"[Telemetry] 死亡 {scene} @({position.x:0.0},{position.y:0.0}) 原因={cause} 活了{duration:0.0}s");
        }
    }

    /// <summary>关卡结束：reason = clear（通关）/ quit（退出、切场景、暂停）</summary>
    public void CloseLevel(string reason)
    {
        if (!levelOpen || closed) return;

        closed = true;
        FlushCells();

        string type = reason == "clear" ? "level_clear" : "level_quit";

        // 每趟尝试结束都留一条路线：通关（clear）/ 退出或切场景（quit）
        FlushRoutes(reason == "clear" ? "clear" : "quit");

        owner.Enqueue(type, new TelemetryFields()
            .Str("scene", scene)
            .Int("level_index", levelIndex)
            .Int("attempt", attempt)
            .Num("duration_s", duration)
            .Int("deaths", deaths)
            .Int("jumps", jumps)
            .Int("jump_fails", fails)
            .Num("distance", distance)
            .Num("session_time_s", Telemetry.SessionSeconds())
            .Bool("has_p2", hasP2)
            .Build(), Telemetry.NowMs());

        if (TelemetryConfig.verbose)
        {
            Debug.Log($"[Telemetry] 关卡结束 {scene} ({type}) 时长={duration:0.0}s 死亡={deaths} 跳跃={jumps}");
        }
    }

    // ============================================================
    // 格子增量上报（两个角色分别发）
    // ============================================================

    private void FlushCells()
    {
        if (!levelOpen) return;

        int sent = 0;
        foreach (var pair in cellsByActor)
        {
            string actor = pair.Key;
            foreach (var kv in pair.Value)
            {
                if (sent >= TelemetryConfig.CellFlushMaxEvents) return;

                Cell cell = kv.Value;
                // 停留超过 1 秒才值得记一条；只跳过路过的格子（有跳跃的格子仍然保留）
                if (cell.dwell <= TelemetryConfig.MinDwellToReport && cell.jumps == 0 && cell.fails == 0)
                {
                    continue;
                }

                int cx = (int)(kv.Key >> 32);
                int cy = (int)(uint)kv.Key;

                owner.Enqueue("cell", new TelemetryFields()
                    .Str("scene", scene)
                    .Int("attempt", attempt)
                    .Str("actor", actor)
                    .Int("cell_x", cx)
                    .Int("cell_y", cy)
                    .Num("x", (cx + 0.5f) * TelemetryConfig.CellSize)
                    .Num("y", (cy + 0.5f) * TelemetryConfig.CellSize)
                    .Num("dwell_s", cell.dwell)
                    .Int("jumps", cell.jumps)
                    .Int("jump_fails", cell.fails)
                    .Num("cell_size", TelemetryConfig.CellSize)
                    .Build(), Telemetry.NowMs());

                // 只清"已上报的量"，格子本身保留（下一段增量继续累加）
                cell.dwell = 0f;
                cell.jumps = 0;
                cell.fails = 0;
                sent++;
            }
        }
    }
}
