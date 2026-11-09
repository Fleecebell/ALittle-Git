using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// 遥测核心（自动启动，跨场景常驻，不需要往任何场景里拖 Prefab）。
///
/// 设计要点：
///  1. 非阻塞：所有网络请求走协程 + UnityWebRequest，不占用主线程；断网不卡游戏。
///  2. 不丢数据：发失败的先写本地 spool 文件，下次启动 / 每隔 30s 自动补发。
///  3. 省额度：高频数据（跳跃、驻留）在客户端按格子聚合，不会一条一条往 D1 灌。
///  4. 零侵入：通过 RuntimeInitializeOnLoadMethod 自动创建，不改场景、不改 Prefab。
///
/// 对外入口（其它脚本只调用这些静态方法）：
///   Telemetry.Event(...)      自定义事件
///   Telemetry.NoteDeath(...)  + Telemetry.OnDeath()     死亡
///   Telemetry.OnJump(...) / Telemetry.OnJumpFail(...)   跳跃
///   Telemetry.OnLevelClear()  通关
///   Telemetry.OnPlayerFound() 关卡开始（内部自动调用）
/// </summary>
public class Telemetry : MonoBehaviour
{
    public static Telemetry Instance { get; private set; }

    private const string PrefPlayerId = "telemetry.player_id";
    private const string EndpointFileName = "telemetry_endpoint.txt";
    private const string SpoolFileName = "telemetry_spool.jsonl";

    private struct Queued
    {
        public string type;
        public string json; // 完整事件对象 JSON（含 player_id / session_id / client_ts / metadata）
        public long ts;
    }

    private readonly List<Queued> queue = new List<Queued>();
    private TelemetryLevelStats stats;

    private string playerId;
    private string sessionId;
    private string spoolPath;
    private long sessionStartMs;

    private bool sending;
    private bool sendOk;
    private long lastStatus;
    private bool warnedNoEndpoint;
    private bool quitHandled;
    private long loopTicks; // 调试用：主循环跑了多少拍

    // ============================================================
    // 自动启动
    // ============================================================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoBoot()
    {
        if (!TelemetryConfig.enabled) return;
        if (Instance != null) return;

        var go = new GameObject("[Telemetry]");
        DontDestroyOnLoad(go);
        go.AddComponent<Telemetry>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        LoadRuntimeOverrides();
        playerId = LoadPlayerId();
        sessionId = "s_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        sessionStartMs = NowMs();
        spoolPath = Path.Combine(Application.persistentDataPath, SpoolFileName);

        stats = new TelemetryLevelStats(this);

        SceneManager.sceneLoaded += OnSceneLoaded;

        if (TelemetryConfig.verbose)
        {
            Debug.Log($"[Telemetry] 启动 session={sessionId} player={playerId} endpoint={TelemetryConfig.endpoint}");
        }
    }

    private void Start()
    {
        // 主循环放 Start：StartCoroutine 会立刻把协程跑到第一个 yield，
        // 所以 session_start 必须先入队，否则第一条事件会变成 level_start。
        LoadSpoolIntoQueue();

        // 首个场景的 sceneLoaded 事件可能早于 Start，这里补一次，避免漏掉第一关
        if (!stats.HasScene)
        {
            stats.EnterScene(SceneManager.GetActiveScene().name);
        }

        // 会话开始：记录一次设备/版本信息，方便以后按版本对比数据
        Event("session_start", new TelemetryFields()
            .Str("scene", SceneManager.GetActiveScene().name)
            .Str("app_version", SafeString(Application.version))
            .Str("unity_version", SafeString(Application.unityVersion))
            .Str("platform", SafeString(Application.platform.ToString()))
            .Str("device", SafeString(SystemInfo.deviceModel))
            .Str("os", SafeString(SystemInfo.operatingSystem))
            .Str("gpu", SafeString(SystemInfo.graphicsDeviceName))
            .Int("screen_w", Screen.width)
            .Int("screen_h", Screen.height)
            .Int("target_fps", Application.targetFrameRate)
            .Str("language", SafeString(Application.systemLanguage.ToString())));

        StartCoroutine(MainLoop());
    }

    /// <summary>
    /// 每帧驱动统计。**必须放在 Update 里**：协程是 0.1 秒一跳，DeltaTime 却是"一帧"的长度，
    /// 用 Time.unscaledDeltaTime 累计会只算到真实时间的 1/6（表现就是"一关只玩了 1 秒"）。
    /// </summary>
    private void Update()
    {
        if (stats == null) return;

        float dt = Time.unscaledDeltaTime;
        if (dt > 0.5f) dt = 0.5f; // 编辑器断点/卡顿后的异常大 dt，别污染驻留统计
        stats.Tick(dt);
    }

    // ============================================================
    // 主循环：定时上报 + 离线补发（采样在 Update 里，这里只管网络）
    // ============================================================

    private IEnumerator MainLoop()
    {
        float sinceFlush = 0f;
        float sinceSpool = 0f;
        float lastRealtime = Time.realtimeSinceStartup;

        while (true)
        {
            bool wantFlush = false;
            bool wantSpool = false;

            // 单拍异常不能把整个上报循环打死（否则遥测静默停摆，最难查）
            // 注意：yield 不能写在带 catch 的 try 里，所以这里只算标志位，yield 放到外面
            try
            {
                // 用真实时间算间隔：协程自己是 0.1 秒一跳，不能再拿一帧的 DeltaTime 当时钟
                float now = Time.realtimeSinceStartup;
                float elapsed = now - lastRealtime;
                lastRealtime = now;
                if (elapsed < 0f) elapsed = 0f;
                if (elapsed > 5f) elapsed = 5f;

                loopTicks++;

                sinceFlush += elapsed;
                sinceSpool += elapsed;

                wantFlush = !sending && queue.Count > 0 &&
                            (queue.Count >= TelemetryConfig.FlushAtCount ||
                             sinceFlush >= TelemetryConfig.FlushIntervalSeconds);

                if (wantFlush)
                {
                    sinceFlush = 0f;
                }
                else if (!sending && sinceSpool >= TelemetryConfig.SpoolRetryIntervalSeconds && queue.Count == 0)
                {
                    sinceSpool = 0f;
                    wantSpool = HasSpoolData();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Telemetry] 主循环异常（已忽略，继续运行）: " + e);
            }

            if (wantFlush)
            {
                yield return FlushQueue();
            }
            else if (wantSpool)
            {
                yield return ReplaySpool();
            }

            yield return new WaitForSecondsRealtime(0.1f);
        }
    }

    // ============================================================
    // 上报
    // ============================================================

    private IEnumerator FlushQueue()
    {
        if (queue.Count == 0) yield break;

        int n = Mathf.Min(TelemetryConfig.MaxBatch, queue.Count);
        string payload = BuildPayload(n);

        sending = true;
        yield return PostJson(payload);
        sending = false;

        if (sendOk)
        {
            queue.RemoveRange(0, n);
            if (TelemetryConfig.verbose) Debug.Log($"[Telemetry] 上报成功 {n} 条");
        }
        else if (IsPermanentRejection(lastStatus))
        {
            // 服务端明确说"这批数据不合法"（400/413/422），重试多少次都不会成功，
            // 丢掉它们，否则会永远堵在 spool 里，把后面的好数据一起卡死。
            queue.RemoveRange(0, n);
            Debug.LogWarning($"[Telemetry] 服务端拒收 HTTP {lastStatus}，已丢弃这 {n} 条");
        }
        else
        {
            // 失败就整批落盘，下个会话/30 秒后补发，避免内存无限涨
            var failed = queue.GetRange(0, n);
            queue.RemoveRange(0, n);
            AppendSpool(failed);
            if (TelemetryConfig.verbose) Debug.LogWarning($"[Telemetry] 上报失败，已存本地 {n} 条");
        }
    }

    /// <summary>4xx 里只有超时/限流是可重试的，其余都是"数据本身有问题"。</summary>
    private static bool IsPermanentRejection(long status)
    {
        return status >= 400 && status < 500 && status != 408 && status != 429;
    }

    private IEnumerator PostJson(string json)
    {
        sendOk = false;

        if (!TelemetryConfig.HasEndpoint)
        {
            if (!warnedNoEndpoint)
            {
                warnedNoEndpoint = true;
                Debug.LogWarning("[Telemetry] 还没配置上报地址（TelemetryConfig.DefaultEndpoint），" +
                                 "数据会先存在本地 spool 文件里。");
            }
            yield break;
        }

        UnityWebRequest request = null;
        try
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            request = new UnityWebRequest(TelemetryConfig.endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(TelemetryConfig.IngestKey))
            {
                request.SetRequestHeader("X-Telemetry-Key", TelemetryConfig.IngestKey);
            }
            request.timeout = Mathf.Max(1, Mathf.RoundToInt(TelemetryConfig.RequestTimeoutSeconds));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Telemetry] 构造请求失败: " + e.Message);
            if (request != null) request.Dispose();
            yield break;
        }

        yield return request.SendWebRequest();

        lastStatus = request.responseCode;
        sendOk = request.result == UnityWebRequest.Result.Success &&
                 request.responseCode >= 200 && request.responseCode < 300;

        if (!sendOk && TelemetryConfig.verbose)
        {
            Debug.LogWarning($"[Telemetry] HTTP {request.responseCode} {request.error} {request.downloadHandler?.text}");
        }

        request.Dispose();
    }

    private string BuildPayload(int count)
    {
        var sb = new StringBuilder(128 + count * 200);
        sb.Append("{\"game\":\"alittle\",\"v\":1,\"events\":[");
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(queue[i].json);
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // ============================================================
    // 事件入队（其它脚本通过静态方法进来）
    // ============================================================

    /// <summary>发一条自定义事件（游戏逻辑里想加新埋点就用这个，不用改 Telemetry 本身）</summary>
    public static void Event(string eventType, TelemetryFields fields)
    {
        if (Instance == null) return;
        Instance.Enqueue(eventType, fields == null ? "{}" : fields.Build(), NowMs());
    }

    internal void Enqueue(string eventType, string metaJson, long ts)
    {
        string json = BuildEventJson(eventType, metaJson, ts);
        if (json == null) return;

        queue.Add(new Queued { type = eventType, json = json, ts = ts });

        // 内存上限：扔最旧的（说明一直在失败，spool 已经兜住了）
        if (queue.Count > TelemetryConfig.MaxQueue)
        {
            queue.RemoveRange(0, queue.Count - TelemetryConfig.MaxQueue);
        }
    }

    private string BuildEventJson(string eventType, string metaJson, long ts)
    {
        if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(sessionId)) return null;

        var sb = new StringBuilder(128 + (metaJson == null ? 0 : metaJson.Length));
        sb.Append("{\"player_id\":\"").Append(playerId).Append('"')
          .Append(",\"session_id\":\"").Append(sessionId).Append('"')
          .Append(",\"event_type\":\"").Append(eventType).Append('"')
          .Append(",\"client_ts\":").Append(ts)
          .Append(",\"metadata\":").Append(string.IsNullOrEmpty(metaJson) ? "{}" : metaJson)
          .Append('}');
        return sb.ToString();
    }

    // ============================================================
    // 场景 / 关卡
    // ============================================================

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        stats.EnterScene(scene.name);
    }

    // ============================================================
    // 静态埋点 API（供游戏代码调用）
    // ============================================================

    private static string pendingCause = "unknown";
    private static string pendingActor = "player1";
    private static Vector3 pendingPos;
    private static bool hasPendingPos;

    /// <summary>记录死因（在死亡真正触发前调用；位置用死亡物体的位置，留空则用 P1 位置）</summary>
    public static void NoteDeath(string cause, string actor)
    {
        pendingCause = string.IsNullOrEmpty(cause) ? "unknown" : cause;
        pendingActor = string.IsNullOrEmpty(actor) ? "player1" : actor;
        hasPendingPos = false;
    }

    public static void NoteDeath(string cause, string actor, Vector3 position)
    {
        NoteDeath(cause, actor);
        pendingPos = position;
        hasPendingPos = true;
    }

    /// <summary>死亡真正发生时调用（放 DeathRespawnVFX.TriggerDeath 里，此时还没被传送走）</summary>
    public static void OnDeath()
    {
        if (Instance == null) return;

        Vector3 pos = hasPendingPos
            ? pendingPos
            : (Instance.stats != null ? Instance.stats.PlayerPosition() : Vector3.zero);

        Instance.stats.RecordDeath(pendingCause, pendingActor, pos);

        pendingCause = "unknown";
        pendingActor = "player1";
        hasPendingPos = false;
    }

    /// <summary>成功起跳（actor 决定热力图上是红 P1 还是蓝 P2）</summary>
    public static void OnJump(string actor, Vector3 position)
    {
        if (Instance == null) return;
        Instance.stats.RecordJump(actor, position, true);
    }

    /// <summary>按了跳但没跳成（用于发现手感差/判定苛刻的位置）</summary>
    public static void OnJumpFail(string actor, Vector3 position, string reason)
    {
        if (Instance == null) return;
        Instance.stats.RecordJump(actor, position, false);
    }

    /// <summary>通关（在 GoToNextLevel 里调用）</summary>
    public static void OnLevelClear()
    {
        if (Instance == null) return;
        Instance.stats.CloseLevel("clear");
    }

    /// <summary>排查用：把内部状态拼成一行（编辑器桥接 /eval 调用即可看到）</summary>
    public static string DebugState()
    {
        if (Instance == null) return "instance=null";
        return "queue=" + Instance.queue.Count
             + " loopTicks=" + Instance.loopTicks
             + " sending=" + Instance.sending
             + " sendOk=" + Instance.sendOk
             + " spool=" + Instance.HasSpoolData()
             + " playerId=" + (string.IsNullOrEmpty(Instance.playerId) ? "<empty>" : Instance.playerId)
             + " sessionId=" + (string.IsNullOrEmpty(Instance.sessionId) ? "<empty>" : Instance.sessionId)
             + " endpoint=" + TelemetryConfig.endpoint
             + " hasEndpoint=" + TelemetryConfig.HasEndpoint;
    }

    // ============================================================
    // 退出 / 暂停
    // ============================================================

    private void OnApplicationPause(bool paused)
    {
        if (!paused || Instance == null) return;

        // 手机上随时可能被系统杀掉：先把内存里的事件落到本地，零阻塞
        stats.CloseLevel("quit");
        SpoolAll();
    }

    private void OnApplicationQuit()
    {
        if (quitHandled) return;
        quitHandled = true;

        stats.CloseLevel("quit");

        // 退出时给最后一批一次同步机会（最多 2 秒，玩家已经点关闭了，感知不到）
        FlushBlockingAtQuit();

        if (queue.Count > 0) SpoolAll();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// 退出时的最后一批上报。
    ///
    /// 为什么不用 UnityWebRequest：它在主线程上由玩家循环推进，而退出（尤其是编辑器里点停止）
    /// 时主循环马上就停了，经常"请求发出去了但永远 isDone=false"，于是最后一批（含 level_quit）
    /// 只能在下次启动时才补发 —— 表现就是"退出后服务器上看不到这一局"。
    /// 这里改成后台线程 + HttpWebRequest 同步发送，不依赖 Unity 的帧循环。
    /// </summary>
    private void FlushBlockingAtQuit()
    {
        if (queue.Count == 0) return;
        if (!TelemetryConfig.HasEndpoint) return;

        long deadline = NowMs() + (long)(TelemetryConfig.QuitFlushTimeoutSeconds * 1000f);

        while (queue.Count > 0)
        {
            int n = Mathf.Min(TelemetryConfig.MaxBatch, queue.Count);
            string json = BuildPayload(n);

            string error = null;
            var thread = new System.Threading.Thread(() => { error = PostJsonSync(json); });
            thread.IsBackground = true;
            thread.Start();

            long remain = deadline - NowMs();
            if (remain <= 0)
            {
                Debug.LogWarning("[Telemetry] 退出时上报超时（已存本地，下次启动补发）");
                break;
            }
            if (!thread.Join((int)remain))
            {
                Debug.LogWarning("[Telemetry] 退出时上报超时（已存本地，下次启动补发）");
                break; // 超时：这批留在队列里，交给 SpoolAll()
            }

            if (error != null)
            {
                Debug.LogWarning("[Telemetry] 退出时上报失败（已存本地，下次启动补发）: " + error);
                break;
            }

            queue.RemoveRange(0, n);
        }
    }

    /// <summary>同步 POST（可在后台线程调用）。成功返回 null，失败返回错误信息。</summary>
    private string PostJsonSync(string json)
    {
        try
        {
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(TelemetryConfig.endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.KeepAlive = false;
            // 单次请求超时略短于总预算，让 3.5 秒预算里还有一点线程调度余量
            request.Timeout = Mathf.Max(500, Mathf.RoundToInt(TelemetryConfig.QuitFlushTimeoutSeconds * 1000f) - 500);
            request.ReadWriteTimeout = request.Timeout;
            if (!string.IsNullOrEmpty(TelemetryConfig.IngestKey))
            {
                request.Headers["X-Telemetry-Key"] = TelemetryConfig.IngestKey;
            }

            byte[] body = Encoding.UTF8.GetBytes(json);
            request.ContentLength = body.Length;

            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(body, 0, body.Length);
            }

            using (var response = (System.Net.HttpWebResponse)request.GetResponse())
            {
                int code = (int)response.StatusCode;
                if (code < 200 || code >= 300) return "HTTP " + code;
            }

            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    // ============================================================
    // 本地 spool（离线补发）
    // ============================================================

    private void SpoolAll()
    {
        if (queue.Count == 0) return;
        AppendSpool(queue);
        queue.Clear();
    }

    private void AppendSpool(List<Queued> items)
    {
        try
        {
            int skip = Mathf.Max(0, items.Count - TelemetryConfig.SpoolMaxEvents);
            var sb = new StringBuilder(items.Count * 200);
            for (int i = skip; i < items.Count; i++)
            {
                sb.Append(items[i].json).Append('\n');
            }
            if (sb.Length == 0) return;
            File.AppendAllText(spoolPath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Telemetry] 写本地缓存失败: " + e.Message);
        }
    }

    private bool HasSpoolData()
    {
        try
        {
            return File.Exists(spoolPath) && new FileInfo(spoolPath).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void LoadSpoolIntoQueue()
    {
        try
        {
            if (!File.Exists(spoolPath)) return;

            string[] lines = File.ReadAllLines(spoolPath);
            File.Delete(spoolPath);

            if (lines.Length == 0) return;

            int start = Mathf.Max(0, lines.Length - TelemetryConfig.SpoolMaxEvents);
            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                queue.Add(new Queued { type = "spool", json = line, ts = 0 });
            }

            // 超出内存上限的旧数据直接丢弃
            if (queue.Count > TelemetryConfig.MaxQueue)
            {
                queue.RemoveRange(0, queue.Count - TelemetryConfig.MaxQueue);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Telemetry] 读取本地缓存失败: " + e.Message);
        }
    }

    private IEnumerator ReplaySpool()
    {
        string[] lines = null;
        try
        {
            if (!File.Exists(spoolPath)) yield break;
            lines = File.ReadAllLines(spoolPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Telemetry] 读取本地缓存失败: " + e.Message);
            yield break;
        }

        if (lines == null || lines.Length == 0)
        {
            yield break;
        }

        int n = Mathf.Min(TelemetryConfig.MaxBatch, lines.Length);
        var sb = new StringBuilder(128 + n * 200);
        sb.Append("{\"game\":\"alittle\",\"v\":1,\"events\":[");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(lines[i].Trim());
        }
        sb.Append("]}");

        sending = true;
        yield return PostJson(sb.ToString());
        sending = false;

        if (!sendOk) yield break;

        try
        {
            if (n >= lines.Length)
            {
                File.Delete(spoolPath);
            }
            else
            {
                var rest = new List<string>(lines.Length - n);
                for (int i = n; i < lines.Length; i++) rest.Add(lines[i]);
                File.WriteAllLines(spoolPath, rest);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Telemetry] 清理本地缓存失败: " + e.Message);
        }
    }

    // ============================================================
    // 工具
    // ============================================================

    private void LoadRuntimeOverrides()
    {
        TelemetryConfig.endpoint = TelemetryConfig.DefaultEndpoint;

        // 允许不重新打包就改上报地址：把一个纯文本文件放到 persistentDataPath 里
        // 内容就一行，比如 http://127.0.0.1:8787/v1/events
        try
        {
            string path = Path.Combine(Application.persistentDataPath, EndpointFileName);
            if (File.Exists(path))
            {
                string custom = File.ReadAllText(path).Trim();
                if (custom.Length > 0) TelemetryConfig.endpoint = custom;
            }
        }
        catch
        {
            // 读不到就用默认地址，不影响游戏
        }
    }

    private static string LoadPlayerId()
    {
        string id = PlayerPrefs.GetString(PrefPlayerId, string.Empty);
        if (string.IsNullOrEmpty(id) || id.Length < 6)
        {
            id = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(PrefPlayerId, id);
            PlayerPrefs.Save();
        }
        return id;
    }

    private static string SafeString(string s)
    {
        return string.IsNullOrEmpty(s) ? "-" : s;
    }

    /// <summary>UTC 毫秒时间戳（不依赖 DateTimeOffset 的 API 可用性）</summary>
    public static long NowMs()
    {
        return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }

    public static float SessionSeconds()
    {
        if (Instance == null) return 0f;
        return (NowMs() - Instance.sessionStartMs) / 1000f;
    }
}
