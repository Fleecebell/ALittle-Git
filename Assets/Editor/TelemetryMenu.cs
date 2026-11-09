using UnityEditor;
using UnityEngine;

/// <summary>
/// 遥测的小工具菜单（只在编辑器里存在，不会打进包里）。
/// 菜单：Tools / Telemetry
/// </summary>
public static class TelemetryMenu
{
    [MenuItem("Tools/Telemetry/打开数据目录", false, 10)]
    private static void OpenDataFolder()
    {
        string path = Application.persistentDataPath;
        if (!System.IO.Directory.Exists(path))
        {
            System.IO.Directory.CreateDirectory(path);
        }
        EditorUtility.RevealInFinder(path + "/");
        Debug.Log("[Telemetry] 数据目录: " + path);
    }

    [MenuItem("Tools/Telemetry/发送一条测试事件", false, 20)]
    private static void SendTestEvent()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Telemetry] 先进入 Play 模式再发测试事件。");
            return;
        }
        Telemetry.Event("test_ping", new TelemetryFields()
            .Str("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            .Num("session_time_s", Telemetry.SessionSeconds()));
        Debug.Log("[Telemetry] 已入队一条 test_ping（最多 8 秒内发出；也可调用 Telemetry 的 flush 逻辑等下一次定时上报）。");
    }

    [MenuItem("Tools/Telemetry/打印当前配置", false, 30)]
    private static void PrintConfig()
    {
        Debug.Log(
            "[Telemetry] 配置\n" +
            "  开关: " + TelemetryConfig.enabled + "\n" +
            "  地址: " + TelemetryConfig.endpoint + "\n" +
            "  已配置: " + TelemetryConfig.HasEndpoint + "\n" +
            "  详细日志: " + TelemetryConfig.verbose + "\n" +
            "  数据目录: " + Application.persistentDataPath + "\n" +
            "  离线缓存: " + System.IO.Path.Combine(Application.persistentDataPath, "telemetry_spool.jsonl"));
    }

    [MenuItem("Tools/Telemetry/开关详细日志", false, 40)]
    private static void ToggleVerbose()
    {
        TelemetryConfig.verbose = !TelemetryConfig.verbose;
        Debug.Log("[Telemetry] 详细日志 = " + TelemetryConfig.verbose);
    }

    [MenuItem("Tools/Telemetry/清空本地离线缓存", false, 50)]
    private static void ClearSpool()
    {
        string path = System.IO.Path.Combine(Application.persistentDataPath, "telemetry_spool.jsonl");
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
            Debug.Log("[Telemetry] 已删除本地缓存: " + path);
        }
        else
        {
            Debug.Log("[Telemetry] 本地没有缓存文件。");
        }
    }
}
