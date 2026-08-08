using UnityEngine;

// ======================================================================
// RainAudioController —— 雨声音频控制器
// --------------------------------------------------------------------------
// 设计思路:
//   1. 不需要改 RainSystem.cs! 本脚本通过 RainSystem.Instance.CurrentIntensity
//      实时读取当前雨强度(0~1), 被动跟随, 完全解耦.
//   2. 只需要两个音频: 大雨声 + 小雨声(都是无缝循环). 音量跟着 intensity 过渡:
//        intensity = 0   → 大雨 volume=0,   小雨 volume=0    (静音)
//        intensity = 0.4 → 大雨 volume=0,   小雨 volume=1    (纯小雨)
//        intensity = 1   → 大雨 volume=1,   小雨 volume=0.3  (大雨为主, 小雨作细节)
//        中间值            → 两者按比例平滑混合
//   3. RainSystem 的 6 阶段(淡入→大雨→过渡→小雨→拖尾)会自动驱动音频过渡,
//      因为 intensity 本身就是用 MoveTowards 平滑变化的, 所以音频天然有淡入淡出,
//      无论阶段时间怎么调, 音频都会自动对应, 不需要额外处理.
//   4. 音量为0时自动 Stop() 省性能, 音量>0时自动 Play().
//
// 挂载位置: 任意位置都行(靠单例读 intensity). 推荐挂在 RainSystem 物体本身上,
//           两个 AudioSource 放在 RainSystem 的子物体里.
// ======================================================================
public class RainAudioController : MonoBehaviour
{
    [Header("=== 音频源 ===")]
    [Tooltip("大雨声 AudioSource (拖入带 AudioSource 的物体). 必须 Loop 勾选, Play On Awake 取消")]
    [SerializeField] private AudioSource heavyRainAudio;

    [Tooltip("小雨声 AudioSource (拖入带 AudioSource 的物体). 必须 Loop 勾选, Play On Awake 取消")]
    [SerializeField] private AudioSource lightRainAudio;

    [Header("=== 音量映射 ===")]
    [Tooltip("大雨最大音量. 1=原音量, 0.8=稍小. 根据素材响度调整, 两段素材响度差异大时在这里平衡")]
    [SerializeField, Range(0f, 1f)] private float heavyMaxVolume = 0.8f;

    [Tooltip("小雨最大音量. 1=原音量, 0.6=稍小")]
    [SerializeField, Range(0f, 1f)] private float lightMaxVolume = 0.6f;

    [Tooltip("大雨时小雨的保留音量. 0.3=大雨时小雨声保留30%(作细节点缀), 0=大雨时完全听不到小雨")]
    [SerializeField, Range(0f, 1f)] private float lightVolumeDuringHeavy = 0.3f;

    [Header("=== 调试 ===")]
    [Tooltip("运行时显示当前强度和两个音量, 方便调参")]
    [SerializeField] private bool showDebug = false;

    // intensity 的分界点 (对应 RainSystem 的小雨强度 0.4)
    // intensity 0→0.4 = 小雨阶段; 0.4→1 = 大雨阶段
    private const float lightSplit = 0.4f;

    void Update()
    {
        // 从 RainSystem 单例获取当前强度
        if (RainSystem.Instance == null)
        {
            if (showDebug) Debug.LogWarning("[RainAudioController] 找不到 RainSystem 单例! 确保场景里有 RainSystem 物体");
            return;
        }

        float intensity = RainSystem.Instance.CurrentIntensity;

        // ---- 计算两个音量 ----
        // 小雨音量: intensity 0→0.4 时 0→lightMaxVolume, intensity 0.4→1 时 lightMaxVolume→lightMaxVolume*lightVolumeDuringHeavy
        // (大雨起来后小雨声音被盖过一部分, 但保留一点作细节)
        float lightVol;
        if (intensity <= lightSplit)
        {
            lightVol = Mathf.Lerp(0f, lightMaxVolume, intensity / lightSplit);
        }
        else
        {
            lightVol = Mathf.Lerp(lightMaxVolume, lightMaxVolume * lightVolumeDuringHeavy,
                                  (intensity - lightSplit) / (1f - lightSplit));
        }

        // 大雨音量: intensity 0→0.4 时 0(还没到大雨), intensity 0.4→1 时 0→heavyMaxVolume
        float heavyVol;
        if (intensity <= lightSplit)
        {
            heavyVol = 0f;
        }
        else
        {
            heavyVol = Mathf.Lerp(0f, heavyMaxVolume, (intensity - lightSplit) / (1f - lightSplit));
        }

        // ---- 应用音量 + 控制播放/停止 ----
        ApplyAudio(heavyRainAudio, heavyVol, "大雨");
        ApplyAudio(lightRainAudio, lightVol, "小雨");

        if (showDebug)
        {
            Debug.Log($"[RainAudioController] 强度={intensity:F2} 大雨音量={heavyVol:F2} 小雨音量={lightVol:F2}");
        }
    }

    /// <summary>
    /// 应用音量到 AudioSource, 并控制播放/停止.
    /// 音量>0.01 时播放(如果没在播), 音量<=0.01 时停止(如果在播).
    /// 这样无雨时两个 AudioSource 都 Stop, 省性能.
    /// </summary>
    private void ApplyAudio(AudioSource audio, float volume, string name)
    {
        if (audio == null) return;

        audio.volume = volume;

        if (volume > 0.01f && !audio.isPlaying)
        {
            audio.Play();
            if (showDebug) Debug.Log($"[RainAudioController] {name} 开始播放");
        }
        else if (volume <= 0.01f && audio.isPlaying)
        {
            audio.Stop();
            if (showDebug) Debug.Log($"[RainAudioController] {name} 停止播放");
        }
    }
}
