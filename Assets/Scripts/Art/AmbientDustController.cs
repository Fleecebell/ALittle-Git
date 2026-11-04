using UnityEngine;

// ======================================================================
// AmbientDustController —— 悬浮灰尘粒子 (沙尘暴预示系统)
// --------------------------------------------------------------------------
// 设计思路 (独立脚本, 和 SandstormController 联动, 不耦合进沙尘暴脚本):
//   沙尘暴结束后 → 悬浮灰尘慢慢浮现(平静感) → 一段时间后灰尘渐隐, 日常风沙
//   粒子出现(紧张感渐增, 预示沙尘暴越来越近) → 沙尘暴触发, 两者都消失.
//
// 三阶段状态机:
//   1. DustOnly  (沙尘暴刚结束): 只有悬浮灰尘, 日常粒子隐藏. 持续 dustDuration 秒.
//   2. Ambient   (预示阶段):      灰尘渐隐, 日常粒子渐显. 持续 ambientDuration 秒.
//                                  之后保持"日常粒子"状态直到下次沙尘暴.
//   3. Hidden    (沙尘暴中):       灰尘和日常粒子都消失, 沙尘暴粒子接管.
//
// 联动方式:
//   - 查 SandstormController.Instance.CurrentIntensity 判断沙尘暴是否发生
//   - 设 SandstormController.Instance.ambientScale 控制日常粒子显隐 (0=隐藏, 1=显示)
//   - 本脚本只管自己的悬浮灰尘 ParticleSystem
//
// 悬浮灰尘视觉:
//   - 静止悬浮在空中(速度≈0), 靠 Noise 模块轻微飘动
//   - 半透明(alpha 0.3~0.6), 小颗粒(0.1~0.3)
//   - "自发光"感: 用亮色 + Additive 混合材质(在 Unity 里配材质, 见使用说明)
//
// 使用方法:
//   1. 建一个 ParticleSystem 物体, 挂本脚本
//   2. 配好悬浮灰尘的视觉参数(见下方字段说明)
//   3. 脚本自动查 SandstormController, 不需要手动拖引用
// ======================================================================
[RequireComponent(typeof(ParticleSystem))]
public class AmbientDustController : MonoBehaviour
{
    [Header("=== 三阶段持续时间 (秒) ===")]
    [Tooltip("悬浮灰尘阶段持续时间. 沙尘暴刚结束后, 只有灰尘慢慢浮现, 日常粒子隐藏. 推荐值 8~15")]
    [SerializeField] private float dustDuration = 10f;

    [Tooltip("日常粒子阶段渐变持续时间. 灰尘渐隐, 日常粒子渐显(预示沙尘暴临近). 推荐值 5~10. 之后保持日常粒子直到沙尘暴触发")]
    [SerializeField] private float ambientDuration = 7f;

    [Tooltip("判断沙尘暴是否在发生的强度阈值. intensity 超过此值=沙尘暴中, 灰尘消失. 推荐 0.1")]
    [SerializeField, Range(0.05f, 0.5f)] private float stormThreshold = 0.1f;

    [Header("=== 悬浮灰尘粒子参数 ===")]
    [Tooltip("灰尘最大发射量(浮现满时的每秒粒子数). 推荐值 5~15, 太多会像雾")]
    [SerializeField] private float maxEmission = 8f;

    [Tooltip("灰尘粒子最小大小")]
    [SerializeField] private float minSize = 0.1f;

    [Tooltip("灰尘粒子最大大小")]
    [SerializeField] private float maxSize = 0.3f;

    [Tooltip("灰尘最小透明度(alpha). 浮现满时的最低 alpha")]
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.3f;

    [Tooltip("灰尘最大透明度(alpha). 浮现满时的最高 alpha")]
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.6f;

    [Tooltip("浮现/消失过渡速度(每秒变化多少). 越大过渡越快. 推荐 0.5~1.5")]
    [SerializeField] private float transitionSpeed = 1f;

    [Header("=== 调试 ===")]
    [Tooltip("运行时打印当前阶段和强度, 方便调参")]
    [SerializeField] private bool showDebug = false;

    // ===================== 内部状态 =====================
    private enum DustPhase { Hidden, DustOnly, Ambient, Sustained }

    private DustPhase phase = DustPhase.Sustained; // 初始假设日常期(等沙尘暴来)
    private float phaseTimer = 0f;       // 当前阶段计时
    private float dustIntensity = 0f;    // 悬浮灰尘显隐强度 0~1 (自己的)
    private bool wasStorming = false;    // 上一帧沙尘暴是否发生(检测结束瞬间)

    private ParticleSystem ps;
    private ParticleSystem.EmissionModule emissionModule;
    private ParticleSystem.MainModule mainModule;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        emissionModule = ps.emission;
        mainModule = ps.main;

        // 初始: 灰尘隐藏
        emissionModule.enabled = true;
        emissionModule.rateOverTime = 0f;
        dustIntensity = 0f;

        // 设粒子大小随机区间
        mainModule.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
    }

    void Update()
    {
        // ---- 1. 查询沙尘暴状态 ----
        float stormIntensity = 0f;
        bool isStorming = false;
        if (SandstormController.Instance != null)
        {
            stormIntensity = SandstormController.Instance.CurrentIntensity;
            isStorming = stormIntensity > stormThreshold;
        }

        // ---- 2. 检测沙尘暴"开始"和"结束"的瞬间 ----
        if (isStorming && !wasStorming)
        {
            // 沙尘暴刚开始 → 进入 Hidden
            phase = DustPhase.Hidden;
            phaseTimer = 0f;
            if (showDebug) Debug.Log("[AmbientDust] 沙尘暴开始 → Hidden (灰尘消失)");
        }
        else if (!isStorming && wasStorming)
        {
            // 沙尘暴刚结束 → 进入 DustOnly (悬浮灰尘浮现)
            phase = DustPhase.DustOnly;
            phaseTimer = 0f;
            if (showDebug) Debug.Log("[AmbientDust] 沙尘暴结束 → DustOnly (灰尘浮现)");
        }
        wasStorming = isStorming;

        // ---- 3. 状态机计时和切换 ----
        float targetDust = 0f;       // 悬浮灰尘目标强度
        float targetAmbient = 1f;    // 日常粒子目标显隐(给 SandstormController.ambientScale)

        switch (phase)
        {
            case DustPhase.Hidden:
                // 沙尘暴中: 灰尘和日常粒子都消失
                targetDust = 0f;
                targetAmbient = 0f;
                break;

            case DustPhase.DustOnly:
                // 沙尘暴刚结束: 灰尘浮现到满, 日常粒子保持隐藏
                targetDust = 1f;
                targetAmbient = 0f;
                phaseTimer += Time.deltaTime;
                if (phaseTimer >= dustDuration)
                {
                    phase = DustPhase.Ambient;
                    phaseTimer = 0f;
                    if (showDebug) Debug.Log("[AmbientDust] DustOnly 结束 → Ambient (灰尘渐隐, 日常粒子渐显)");
                }
                break;

            case DustPhase.Ambient:
                // 预示阶段: 灰尘渐隐, 日常粒子渐显
                float progress = Mathf.Clamp01(phaseTimer / ambientDuration);
                targetDust = 1f - progress;       // 1→0
                targetAmbient = progress;         // 0→1
                phaseTimer += Time.deltaTime;
                if (phaseTimer >= ambientDuration)
                {
                    phase = DustPhase.Sustained;
                    phaseTimer = 0f;
                    if (showDebug) Debug.Log("[AmbientDust] Ambient 结束 → Sustained (纯日常粒子, 等待沙尘暴)");
                }
                break;

            case DustPhase.Sustained:
                // 日常期: 灰尘消失, 日常粒子显示, 等待下次沙尘暴
                targetDust = 0f;
                targetAmbient = 1f;
                break;
        }

        // 沙尘暴中强制覆盖 (即使状态机还没切到 Hidden, 沙尘暴来了也要立刻消失)
        if (isStorming)
        {
            targetDust = 0f;
            targetAmbient = 0f;
        }

        // ---- 4. 平滑过渡 ----
        dustIntensity = Mathf.MoveTowards(dustIntensity, targetDust, transitionSpeed * Time.deltaTime);

        // 设给 SandstormController 控制日常粒子
        if (SandstormController.Instance != null)
        {
            SandstormController.Instance.ambientScale = Mathf.MoveTowards(
                SandstormController.Instance.ambientScale, targetAmbient, transitionSpeed * Time.deltaTime);
        }

        // ---- 5. 应用到悬浮灰尘粒子 ----
        // 发射量随 dustIntensity
        emissionModule.rateOverTime = maxEmission * dustIntensity;

        // 透明度随 dustIntensity (alpha 在 minAlpha~maxAlpha 间)
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, dustIntensity);
        Color c = mainModule.startColor.color;
        c.a = alpha;
        mainModule.startColor = c;

        if (showDebug)
        {
            Debug.Log($"[AmbientDust] 阶段={phase} 灰尘强度={dustIntensity:F2} " +
                      $"日常显隐={(SandstormController.Instance != null ? SandstormController.Instance.ambientScale : 0):F2} " +
                      $"沙尘暴={stormIntensity:F2}");
        }
    }
}
