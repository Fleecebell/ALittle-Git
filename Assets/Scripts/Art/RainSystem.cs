using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;  // URP 的 Vignette 在这个命名空间

// ======================================================================
// RainSystem —— 下雨视觉效果总控 (雨林关卡)
// --------------------------------------------------------------------------
// 设计思路:
//   1. 挂在"从屏幕顶部往下落"的 ParticleSystem 同一物体上.
//   2. 一个 intensity(0=无雨, 1=大雨) 驱动所有效果: 粒子量/速度/大小、
//      PostProcessing Volume 暗度、所有 PlantSway 植物摆动.
//   3. 6 阶段状态机: Idle → FadeIn → Heavy → Transition → Light → Tail → Idle
//      每阶段独立计时, 拼出"淡入→大雨→过渡→小雨→拖尾→结束"的完整下雨过程.
//   4. intensity 曲线 (按阶段):
//        Idle=0  →  FadeIn: 0→1  →  Heavy=1  →  Transition: 1→0.4  →
//        Light=0.4  →  Tail: 0.4→0  →  Idle=0
//   5. 长短不一: Start Size Y 用 MinMaxCurve 在 [minLen, maxLen] 随机.
//   6. 一阵一阵/浓淡不一: Start Color alpha 用 MinMaxCurve 在 [minAlpha, maxAlpha]
//      随机, 每滴雨透明度不同, 视觉上有的浓有的淡, 不是全屏统一.
//   7. 粗细一样: Start Size X 固定值, 不随机.
//   8. 触发: 手动 StartRain()/StopRain(), 或开启 Scheduler 定时自动循环.
//   9. 提供 static Instance, 让其他脚本(如将来的雷电系统)能查当前强度.
// ======================================================================
[RequireComponent(typeof(ParticleSystem))]
public class RainSystem : MonoBehaviour
{
    // ===================== 单例 =====================
    public static RainSystem Instance { get; private set; }

    // ===================== 粒子模块引用 =====================
    private ParticleSystem ps;
    private ParticleSystem.EmissionModule emissionModule;
    private ParticleSystem.MainModule mainModule;

    [Header("=== PostProcessing (画面变暗) ===")]
    [Tooltip("场景里的 PostProcessing Volume (带 Vignette override 的). 脚本从它取出 Vignette 组件, 直接改 intensity.")]
    [SerializeField] private Volume postVolume;

    [Tooltip("无雨时 Vignette 暗角强度 (日常亮度).")]
    [SerializeField, Range(0f, 1f)] private float baseVignetteIntensity = 0.2f;

    [Tooltip("大雨时 Vignette 暗角强度 (最暗).")]
    [SerializeField, Range(0f, 1f)] private float stormVignetteIntensity = 0.7f;

    [Header("=== 植物联动 ===")]
    [Tooltip("场景里所有 PlantSway 植物. 下雨时调它们的 OnRain(intensity), 让植物摆动. 可以运行时动态查找, 也可以手动拖.")]
    [SerializeField] private List<PlantSway> plants = new List<PlantSway>();

    [Tooltip("勾选后, 不用手动拖 plants 列表, 脚本自动查找场景里所有 PlantSway. 推荐勾选.")]
    [SerializeField] private bool autoFindPlants = true;

    [Header("=== 雨滴粒子参数 ===")]
    [Tooltip("雨滴粗细 (Start Size X, 固定值). 所有雨滴粗细一样.")]
    [SerializeField] private float rainThickness = 0.05f;

    [Tooltip("雨滴最短长度 (Start Size Y 最小值).")]
    [SerializeField] private float minLength = 0.5f;

    [Tooltip("雨滴最长长度 (Start Size Y 最大值). 长短不一靠这个.")]
    [SerializeField] private float maxLength = 2f;

    [Tooltip("雨滴最淡透明度 (Start Color alpha 最小值). 一阵一阵浓淡不一靠这个.")]
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.3f;

    [Tooltip("雨滴最浓透明度 (Start Color alpha 最大值).")]
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 1f;

    [Header("=== 粒子: 大雨状态 (intensity=1) ===")]
    [Tooltip("大雨时每秒发射粒子数.")]
    [SerializeField] private float heavyEmission = 200f;

    [Tooltip("大雨时雨滴下落速度.")]
    [SerializeField] private float heavySpeed = 15f;

    [Header("=== 粒子: 小雨状态 (intensity=0.4) ===")]
    [Tooltip("小雨时每秒发射粒子数 (Transition/Light 阶段用).")]
    [SerializeField] private float lightEmission = 50f;

    [Tooltip("小雨时雨滴下落速度.")]
    [SerializeField] private float lightSpeed = 8f;

    [Header("=== 阶段持续时间 (秒) ===")]
    [Tooltip("FadeIn 阶段: 从无雨渐变到大雨的时间.")]
    [SerializeField] private float fadeInDuration = 3f;

    [Tooltip("Heavy 阶段: 大雨持续时间.")]
    [SerializeField] private float heavyDuration = 15f;

    [Tooltip("Transition 阶段: 大雨过渡到小雨的时间.")]
    [SerializeField] private float transitionDuration = 4f;

    [Tooltip("Light 阶段: 小雨持续时间.")]
    [SerializeField] private float lightDuration = 8f;

    [Tooltip("Tail 阶段: 拖尾淅淅沥沥渐变到无雨的时间.")]
    [SerializeField] private float tailDuration = 6f;

    [Header("=== 手动触发 ===")]
    [Tooltip("勾选=开始下雨 (走完整流程: 淡入→大雨→过渡→小雨→拖尾). 取消勾选=立刻进入拖尾结束. 和定时器互不干扰, 都能触发.")]
    [SerializeField] private bool startRain = false;

    [Header("=== 定时触发 ===")]
    [Tooltip("勾选后自动定时下雨. 不勾则只能手动触发.")]
    [SerializeField] private bool enableScheduler = false;

    [Tooltip("两次下雨之间的间隔时间 (秒). 从上一场雨结束到下一场雨开始.")]
    [SerializeField] private float rainInterval = 30f;

    [Header("=== 调试 ===")]
    [Tooltip("运行时显示当前阶段和强度, 方便调参.")]
    [SerializeField] private bool showDebug = false;

    // ===================== 内部状态 =====================
    // 6 个阶段
    private enum RainPhase { Idle, FadeIn, Heavy, Transition, Light, Tail }
    private RainPhase phase = RainPhase.Idle;

    private float phaseTimer = 0f;      // 当前阶段已过时间
    private float currentIntensity = 0f; // 当前强度 (0~1), 驱动所有效果

    // 粒子参数缓存 (避免每帧 new MinMaxCurve)
    private ParticleSystem.MinMaxCurve sizeCurve;
    private ParticleSystem.MinMaxCurve colorCurve;
    private ParticleSystem.MinMaxCurve speedCurve;

    void Awake()
    {
        // 单例赋值
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RainSystem] 场景里已有 RainSystem, 删除多余的.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ps = GetComponent<ParticleSystem>();
        emissionModule = ps.emission;
        mainModule = ps.main;

        // 初始: 无雨, 粒子发射量为 0
        emissionModule.enabled = true;
        emissionModule.rateOverTime = 0f;
        currentIntensity = 0f;

        // 从 Volume 取 Vignette 引用 (后续每帧直接改它的 intensity, 不用 volume.weight)
        if (postVolume != null && postVolume.profile != null)
        {
            postVolume.profile.TryGet(out vignette);
            // 保险: 即使 Inspector 里 Vignette override 忘记勾选激活, 这里强制激活
            if (vignette != null) vignette.active = true;
        }
    }

    // 定时器协程句柄, 用于运行时开关
    private Coroutine schedulerRoutine;
    // 记录上一次 enableScheduler 的值, 检测 Inspector 变化
    private bool lastEnableScheduler;
    // 记录上一次 startRain 的值, 检测 Inspector 变化
    private bool lastStartRain;

    // 从 Volume 取出的 Vignette 组件引用 (直接改它的 intensity, 不用 volume.weight)
    private Vignette vignette;

    void Start()
    {
        // 自动查找所有植物
        if (autoFindPlants)
        {
            plants.Clear();
            plants.AddRange(FindObjectsOfType<PlantSway>());
        }

        // 开启定时器
        lastEnableScheduler = enableScheduler;
        if (enableScheduler)
        {
            schedulerRoutine = StartCoroutine(SchedulerCoroutine());
        }

        // 记录手动触发初始状态
        lastStartRain = startRain;
        // 如果一开始就勾选了 startRain, 立刻开始下雨
        if (startRain)
        {
            StartRain();
        }
    }

    void Update()
    {
        // ---- 检测手动触发开关 (Inspector 勾选/取消) ----
        if (startRain != lastStartRain)
        {
            if (startRain)
                StartRain();      // 勾选 → 开始下雨
            else
                StopRain();       // 取消勾选 → 进入拖尾结束
            lastStartRain = startRain;
        }

        // 检测 Inspector 里 enableScheduler 是否被运行时修改
        if (enableScheduler != lastEnableScheduler)
        {
            if (enableScheduler && schedulerRoutine == null)
            {
                schedulerRoutine = StartCoroutine(SchedulerCoroutine());
                if (showDebug) Debug.Log("[RainSystem] 定时器已开启 (运行时)");
            }
            else if (!enableScheduler && schedulerRoutine != null)
            {
                StopCoroutine(schedulerRoutine);
                schedulerRoutine = null;
                if (showDebug) Debug.Log("[RainSystem] 定时器已关闭 (运行时)");
            }
            lastEnableScheduler = enableScheduler;
        }

        UpdatePhase();

        // 根据当前阶段算 intensity
        float targetIntensity = CalculateIntensityFromPhase();

        // intensity 平滑过渡 (每帧向目标值靠近, 避免阶段切换瞬间跳变)
        currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, 2f * Time.deltaTime);

        // 应用 intensity 到所有效果
        ApplyIntensity();

        if (showDebug)
        {
            Debug.Log($"[RainSystem] 阶段={phase} 强度={currentIntensity:F2} 计时={phaseTimer:F1}");
        }
    }

    // ===================== 阶段计时 =====================
    private void UpdatePhase()
    {
        if (phase == RainPhase.Idle) return;

        phaseTimer += Time.deltaTime;

        // 检查是否该进入下一阶段
        switch (phase)
        {
            case RainPhase.FadeIn:
                if (phaseTimer >= fadeInDuration) EnterPhase(RainPhase.Heavy);
                break;
            case RainPhase.Heavy:
                if (phaseTimer >= heavyDuration) EnterPhase(RainPhase.Transition);
                break;
            case RainPhase.Transition:
                if (phaseTimer >= transitionDuration) EnterPhase(RainPhase.Light);
                break;
            case RainPhase.Light:
                if (phaseTimer >= lightDuration) EnterPhase(RainPhase.Tail);
                break;
            case RainPhase.Tail:
                if (phaseTimer >= tailDuration) EnterPhase(RainPhase.Idle);
                break;
        }
    }

    // 进入新阶段, 重置计时器
    private void EnterPhase(RainPhase newPhase)
    {
        phase = newPhase;
        phaseTimer = 0f;
        if (showDebug) Debug.Log($"[RainSystem] 进入阶段: {newPhase}");
    }

    // ===================== 根据阶段算目标 intensity =====================
    private float CalculateIntensityFromPhase()
    {
        switch (phase)
        {
            case RainPhase.Idle:
                return 0f;

            case RainPhase.FadeIn:
                // 0 → 1, 线性
                return Mathf.Clamp01(phaseTimer / fadeInDuration);

            case RainPhase.Heavy:
                return 1f;

            case RainPhase.Transition:
                // 1 → 0.4, 线性
                return Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(phaseTimer / transitionDuration));

            case RainPhase.Light:
                return 0.4f;

            case RainPhase.Tail:
                // 0.4 → 0, 线性
                return Mathf.Lerp(0.4f, 0f, Mathf.Clamp01(phaseTimer / tailDuration));

            default:
                return 0f;
        }
    }

    // ===================== 应用 intensity 到所有效果 =====================
    private void ApplyIntensity()
    {
        // ---- 粒子发射量: intensity=0 → 0, intensity=0.4 → lightEmission, intensity=1 → heavyEmission ----
        float emission;
        if (currentIntensity <= 0.4f)
            emission = Mathf.Lerp(0f, lightEmission, currentIntensity / 0.4f);
        else
            emission = Mathf.Lerp(lightEmission, heavyEmission, (currentIntensity - 0.4f) / 0.6f);
        emissionModule.rateOverTime = emission;

        // ---- 粒子速度: 同上插值 ----
        float speed;
        if (currentIntensity <= 0.4f)
            speed = Mathf.Lerp(lightSpeed * 0.5f, lightSpeed, currentIntensity / 0.4f);
        else
            speed = Mathf.Lerp(lightSpeed, heavySpeed, (currentIntensity - 0.4f) / 0.6f);
        mainModule.startSpeed = speed;

        // ---- 粒子大小: X 固定粗细, Y 长短随机 (2D 粒子用 separateAxes 分别设 X/Y) ----
        mainModule.startSizeX = rainThickness;                          // 粗细固定
        mainModule.startSizeY = new ParticleSystem.MinMaxCurve(minLength, maxLength); // 长短随机
        mainModule.startSizeZ = rainThickness;

        // ---- 粒子颜色: alpha 随机 (一阵阵浓淡不一) ----
        // 用 startColor 的 MinMaxCurve 让每滴雨 alpha 在 [minAlpha, maxAlpha] 随机
        Color minColor = new Color(1f, 1f, 1f, minAlpha);
        Color maxColor = new Color(1f, 1f, 1f, maxAlpha);
        mainModule.startColor = new ParticleSystem.MinMaxGradient(minColor, maxColor);

        // ---- Vignette 暗角: 直接改 intensity, 跟着 intensity 一起过渡 ----
        // 无雨时 = baseVignetteIntensity (日常亮度), 大雨时 = stormVignetteIntensity (最暗)
        if (vignette != null)
        {
            vignette.intensity.value = Mathf.Lerp(baseVignetteIntensity, stormVignetteIntensity, currentIntensity);
        }

        // ---- 植物摆动 ----
        for (int i = 0; i < plants.Count; i++)
        {
            if (plants[i] != null)
            {
                if (currentIntensity > 0.01f)
                    plants[i].OnRain(currentIntensity);
                else
                    plants[i].OnRainStop();
            }
        }
    }

    // ===================== 公开接口: 手动触发 =====================

    /// <summary>
    /// 立刻开始一场雨 (从 FadeIn 开始). 如果已经在下雨则忽略.
    /// </summary>
    public void StartRain()
    {
        if (phase != RainPhase.Idle)
        {
            if (showDebug) Debug.Log("[RainSystem] 已经在下雨了, 忽略 StartRain.");
            return;
        }
        EnterPhase(RainPhase.FadeIn);
        if (showDebug) Debug.Log("[RainSystem] 手动触发下雨.");
    }

    /// <summary>
    /// 立刻停止下雨 (强制进入 Tail 拖尾阶段, 淅淅沥沥渐变到无雨).
    /// 不是瞬间停, 而是走 Tail 阶段平滑结束.
    /// </summary>
    public void StopRain()
    {
        if (phase == RainPhase.Idle) return;
        EnterPhase(RainPhase.Tail);
        if (showDebug) Debug.Log("[RainSystem] 手动触发停止 (走拖尾).");
    }

    /// <summary>
    /// 当前强度 (0~1). 其他脚本可查询, 比如雷电系统只在 intensity>0.5 时打闪.
    /// </summary>
    public float CurrentIntensity { get { return currentIntensity; } }

    // ===================== 定时器协程 =====================
    private IEnumerator SchedulerCoroutine()
    {
        while (true)
        {
            // 等待间隔
            yield return new WaitForSeconds(rainInterval);

            // 如果当前没下雨, 开始一场
            if (phase == RainPhase.Idle)
            {
                StartRain();
            }

            // 等待这场雨完全结束 (回到 Idle)
            while (phase != RainPhase.Idle)
            {
                yield return null;
            }
        }
    }
}
