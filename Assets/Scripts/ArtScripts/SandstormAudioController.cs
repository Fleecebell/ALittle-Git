using UnityEngine;

// ======================================================================
// SandstormAudioController —— 沙尘暴音频控制器
// --------------------------------------------------------------------------
// 设计思路:
//   1. 不需要改 SandstormController.cs! 本脚本通过 SandstormController.Instance.CurrentIntensity
//      实时读取当前沙尘暴强度(0~1), 被动跟随, 完全解耦.
//   2. 只需要一个音频: 持续沙尘暴声(无缝循环). 音量跟着 intensity 过渡:
//        intensity = 0 → volume=0    (静音, 无沙尘暴)
//        intensity = 1 → volume=max  (强沙尘暴)
//        中间值       → 按比例平滑过渡
//   3. SandstormController 的 intensity 用 MoveTowards 平滑过渡(transitionDuration控制),
//      所以音频天然有淡入淡出, 沙尘暴开始/停止时音频自动对应, 不需要额外处理.
//   4. 音量为0时自动 Stop() 省性能, 音量>0时自动 Play().
//
// 挂载位置: 任意位置都行(靠单例读 intensity). 推荐挂在 SandstormController 物体本身上,
//           AudioSource 放在 SandstormController 的子物体里.
// ======================================================================
public class SandstormAudioController : MonoBehaviour
{
    [Header("=== 音频源 ===")]
    [Tooltip("沙尘暴声 AudioSource (拖入带 AudioSource 的物体). 必须 Loop 勾选, Play On Awake 取消")]
    [SerializeField] private AudioSource stormAudio;

    [Header("=== 音量映射 ===")]
    [Tooltip("沙尘暴最大音量. 1=原音量, 0.7=稍小. 根据素材响度调整")]
    [SerializeField, Range(0f, 1f)] private float maxVolume = 0.7f;

    [Tooltip("音量过渡速度. 数值越大音量变化越快越直接, 越小越柔和. 0.5=2秒变完. 默认2配合SandstormController的transitionDuration=3, 让音频比视觉略快一点点起势")]
    [SerializeField, Range(0.1f, 5f)] private float volumeSmoothSpeed = 2f;

    [Header("=== 调试 ===")]
    [Tooltip("运行时显示当前强度和音量, 方便调参")]
    [SerializeField] private bool showDebug = false;

    // 当前实际应用的音量 (用 MoveTowards 平滑, 避免强度跳变时音频也跳变)
    private float currentVolume = 0f;

    void Update()
    {
        // 从 SandstormController 单例获取当前强度
        if (SandstormController.Instance == null)
        {
            if (showDebug) Debug.LogWarning("[SandstormAudioController] 找不到 SandstormController 单例! 确保场景里有 SandstormController 物体");
            return;
        }

        float intensity = SandstormController.Instance.CurrentIntensity;

        // 目标音量 = 强度 * 最大音量
        // intensity 0→1 对应 volume 0→maxVolume
        float targetVolume = intensity * maxVolume;

        // 用 MoveTowards 平滑过渡, 避免强度突变(比如手动 StartStorm)时音频瞬间变大
        // volumeSmoothSpeed 控制过渡速度
        currentVolume = Mathf.MoveTowards(currentVolume, targetVolume,
                                          volumeSmoothSpeed * Time.deltaTime);

        // 应用音量 + 控制播放/停止
        ApplyAudio(stormAudio, currentVolume);

        if (showDebug)
        {
            Debug.Log($"[SandstormAudioController] 强度={intensity:F2} 目标音量={targetVolume:F2} 实际音量={currentVolume:F2}");
        }
    }

    /// <summary>
    /// 应用音量到 AudioSource, 并控制播放/停止.
    /// 音量>0.01 时播放(如果没在播), 音量<=0.01 时停止(如果在播).
    /// 这样无沙尘暴时 AudioSource 停止, 省性能.
    /// 最终音量会乘以 MusicManager 的全局音效音量(若无 MusicManager 则按原音量).
    /// </summary>
    private void ApplyAudio(AudioSource audio, float volume)
    {
        if (audio == null) return;

        // 受全局音效音量控制 (沙尘暴声属于音效)
        if (MusicManager.Instance != null)
            volume *= MusicManager.Instance.SFXVolume;

        audio.volume = volume;

        if (volume > 0.01f && !audio.isPlaying)
        {
            audio.Play();
            if (showDebug) Debug.Log("[SandstormAudioController] 沙尘暴音频开始播放");
        }
        else if (volume <= 0.01f && audio.isPlaying)
        {
            audio.Stop();
            if (showDebug) Debug.Log("[SandstormAudioController] 沙尘暴音频停止播放");
        }
    }
}
