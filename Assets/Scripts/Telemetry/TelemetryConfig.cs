using UnityEngine;

/// <summary>
/// 遥测总开关与调参。改这里就够了，不用动其他脚本。
/// </summary>
public static class TelemetryConfig
{
    // ★★★ 部署完 Worker 后改这一行 ★★★
    //   npx wrangler deploy 会打印形如
    //   https://alittle-telemetry.xxxxx.workers.dev
    //   在末尾加 /v1/events
    public const string DefaultEndpoint =
        "https://alittle-telemetry.lhy060303.workers.dev/v1/events";

    // 与 wrangler secret put INGEST_KEY 对应的值；留空 = 服务端也不校验
    public const string IngestKey = "";

    /// <summary>实际使用的地址（可被本地配置覆盖，见 Telemetry.LoadRuntimeOverrides）</summary>
    public static string endpoint = DefaultEndpoint;

    /// <summary>总开关：打包前想临时关掉就设 false</summary>
    public static bool enabled = true;

    /// <summary>打开后在 Console 打印每次上报的结果，排查用</summary>
    public static bool verbose = false;

    // ---------------- 网络与队列 ----------------
    /// <summary>单次 POST 最多带多少条事件（服务端上限 200）</summary>
    public const int MaxBatch = 100;

    /// <summary>内存队列上限，超过就丢最旧的（防止断网时内存涨爆）</summary>
    public const int MaxQueue = 2000;

    /// <summary>攒够多少条立刻发，不等定时器</summary>
    public const int FlushAtCount = 25;

    /// <summary>定时上报间隔（秒），非阻塞</summary>
    public const float FlushIntervalSeconds = 8f;

    public const float RequestTimeoutSeconds = 10f;

    /// <summary>退出游戏时同步发最后一批的最长等待（秒）</summary>
    public const float QuitFlushTimeoutSeconds = 2f;

    /// <summary>离线补发：本地最多存多少条未上报事件</summary>
    public const int SpoolMaxEvents = 3000;

    /// <summary>离线补发重试间隔（秒）</summary>
    public const float SpoolRetryIntervalSeconds = 30f;

    // ---------------- 关卡统计 ----------------
    /// <summary>热力图格子边长（世界单位）。1 = 一个 Unity 单位</summary>
    public const float CellSize = 1f;

    /// <summary>驻留采样间隔（秒）。0.1 = 每秒 10 次</summary>
    public const float SampleInterval = 0.1f;

    /// <summary>关卡中途增量上报间隔（秒），防崩溃丢数据</summary>
    public const float CellFlushIntervalSeconds = 60f;

    /// <summary>一次最多上报多少个格子，超出的下次再发</summary>
    public const int CellFlushMaxEvents = 300;

    /// <summary>驻留不足这个秒数的格子不上报（过滤路过）</summary>
    public const float MinDwellToReport = 0.25f;

    /// <summary>地址是否已正确配置（没配就只落盘、不发网络，避免刷屏报错）</summary>
    public static bool HasEndpoint
    {
        get
        {
            if (string.IsNullOrEmpty(endpoint)) return false;
            if (!endpoint.StartsWith("http", System.StringComparison.OrdinalIgnoreCase)) return false;
            if (endpoint.Contains("YOUR-SUBDOMAIN")) return false;
            return true;
        }
    }
}
