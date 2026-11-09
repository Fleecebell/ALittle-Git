using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 单关卡统计：驻留热力（按格子）、跳跃次数、死亡数、时长、位移。
///
/// 为什么要在客户端聚合：
///   跳跃和驻留是高频数据（一局几百上千次），如果每次都发一条，D1 的免费写入额度很快见底。
///   所以这里按 1×1 世界单位的格子累加，每 60 秒 / 关卡结束时上报"增量"，
///   服务端只负责存，分析时按 (scene, cell_x, cell_y) SUM 即可。
/// </summary>
public class TelemetryLevelStats
{
    private class Cell
    {
        public float dwell;   // 待了多久（秒）
        public float idle;    // 其中完全没输入的时间（卡住/发呆）
        public int jumps;     // 成功起跳次数
        public int fails;     // 按了没跳成次数
    }

    private readonly Telemetry owner;
    private readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
    private readonly Dictionary<string, int> attempts = new Dictionary<string, int>();

    private MoveFirst player;
    private string scene = "";
    private int levelIndex = -1;
    private int attempt;
    private bool levelOpen;
    private bool closed;

    private float duration;
    private float distance;
    private int deaths;
    private int jumps;
    private int fails;

    private float sampleTimer;
    private float flushTimer;
    private float findTimer;
    private Vector3 lastSamplePos;
    private bool hasLastPos;

    public TelemetryLevelStats(Telemetry owner)
    {
        this.owner = owner;
    }

    /// <summary>是否已经收到过场景（用来在 Start 里补首次场景）</summary>
    public bool HasScene
    {
        get { return !string.IsNullOrEmpty(scene); }
    }

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
        duration = 0f;
        distance = 0f;
        deaths = 0;
        jumps = 0;
        fails = 0;
        sampleTimer = 0f;
        flushTimer = 0f;
        findTimer = 0f;
        hasLastPos = false;
        cells.Clear();

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
            hasLastPos = false;
            return;
        }

        duration += dt;

        if (hasLastPos)
        {
            float dx = Mathf.Abs(player.transform.position.x - lastSamplePos.x);
            if (dx < 5f) distance += dx; // 传送/重置产生的巨大跳变不计入
        }
        lastSamplePos = player.transform.position;
        hasLastPos = true;

        sampleTimer += dt;
        if (sampleTimer >= TelemetryConfig.SampleInterval)
        {
            float sample = sampleTimer;
            sampleTimer = 0f;
            AccumulateDwell(sample);
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
        hasLastPos = false;

        owner.Enqueue("level_start", new TelemetryFields()
            .Str("scene", scene)
            .Int("level_index", levelIndex)
            .Int("attempt", attempt)
            .Num("session_time_s", Telemetry.SessionSeconds())
            .Build(), Telemetry.NowMs());

        if (TelemetryConfig.verbose)
        {
            Debug.Log($"[Telemetry] 关卡开始 {scene} (第 {attempt} 次)");
        }
    }

    // ============================================================
    // 采样
    // ============================================================

    private void AccumulateDwell(float seconds)
    {
        Cell cell = GetCell(player.transform.position);
        cell.dwell += seconds;
        if (IsIdle()) cell.idle += seconds;
    }

    /// <summary>完全没有操作（不推方向、不按跳/下/冲刺）视为"发呆"</summary>
    private static bool IsIdle()
    {
        if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f) return false;
        if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W)) return false;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.E)) return false;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return false;
        return true;
    }

    private Cell GetCell(Vector3 worldPos)
    {
        int cx = Mathf.FloorToInt(worldPos.x / TelemetryConfig.CellSize);
        int cy = Mathf.FloorToInt(worldPos.y / TelemetryConfig.CellSize);
        long key = ((long)cx << 32) ^ (uint)cy;

        Cell cell;
        if (!cells.TryGetValue(key, out cell))
        {
            cell = new Cell();
            cells[key] = cell;
        }
        return cell;
    }

    // ============================================================
    // 埋点入口
    // ============================================================

    public void RecordJump(Vector3 position, bool success)
    {
        if (closed) return;

        Cell cell = GetCell(position);
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
            .Build(), Telemetry.NowMs());

        if (TelemetryConfig.verbose)
        {
            Debug.Log($"[Telemetry] 关卡结束 {scene} ({type}) 时长={duration:0.0}s 死亡={deaths} 跳跃={jumps}");
        }
    }

    // ============================================================
    // 格子增量上报
    // ============================================================

    private void FlushCells()
    {
        if (!levelOpen || cells.Count == 0) return;

        int sent = 0;
        foreach (var kv in cells)
        {
            if (sent >= TelemetryConfig.CellFlushMaxEvents) break;

            Cell cell = kv.Value;
            if (cell.dwell < TelemetryConfig.MinDwellToReport && cell.jumps == 0 && cell.fails == 0)
            {
                continue; // 只是路过，不值得占一行
            }

            int cx = (int)(kv.Key >> 32);
            int cy = (int)(uint)kv.Key;

            owner.Enqueue("cell", new TelemetryFields()
                .Str("scene", scene)
                .Int("attempt", attempt)
                .Int("cell_x", cx)
                .Int("cell_y", cy)
                .Num("x", (cx + 0.5f) * TelemetryConfig.CellSize)
                .Num("y", (cy + 0.5f) * TelemetryConfig.CellSize)
                .Num("dwell_s", cell.dwell)
                .Num("idle_s", cell.idle)
                .Int("jumps", cell.jumps)
                .Int("jump_fails", cell.fails)
                .Build(), Telemetry.NowMs());

            // 只清"已上报的量"，格子本身保留（下一段增量继续累加）
            cell.dwell = 0f;
            cell.idle = 0f;
            cell.jumps = 0;
            cell.fails = 0;
            sent++;
        }
    }
}
