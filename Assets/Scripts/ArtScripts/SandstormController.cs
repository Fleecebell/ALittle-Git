using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;            // Volume / VolumeProfile 在这个命名空间
using UnityEngine.Rendering.Universal;  // URP 的 Vignette 在这个命名空间（Built-in 管线才在 Rendering 下，少了这句会报 CS0246）

// ======================================================================
// SandstormController —— 沙尘暴画面效果总控
// --------------------------------------------------------------------------
// 设计思路:
//   1. 挂在"屏幕外发射、横扫屏幕"的 ParticleSystem 同一物体上.
//   2. 用 ParticleSystem 的 Trigger 模块 + 所有 WindArea 的 Collider2D
//      做"风区遮罩": 粒子飞到风瓦片涂了的地方(风区内)变亮,
//      飞到没涂的地方(风区外)变淡 —— 实现"被挡住的地方粒子变少但不是没有".
//   3. 一个 intensity(0=日常, 1=强沙尘暴) 控制所有参数, 用 MoveTowards
//      做 lerp 过渡, 所以"淡入淡出"是自动的.
//   4. 支持两种触发: 手动调用 StartStorm()/StopStorm(), 或开启定时器自动循环.
//   5. URP Post Processing: 通过 Volume 里的 Vignette(暗角) 做"变暗",
//      同样跟着 intensity 一起过渡.
//   6. 提供 static Instance 单例, 让 WindArea 能查询当前强度,
//      实现"没沙尘暴时风不影响主角行走".
//   7. 日常(intensity=0)时粒子不分风区内/外, 到处透明度一样;
//      只有沙尘暴时才开始区分风区内亮、风区外淡.
//   8. 粒子透明度变化有时间过渡(MoveTowards), 不会瞬变.
//   9. 沙尘暴时粒子大小在 [stormMinSize, stormSize] 之间随机, 大小都有不单调.
// ======================================================================
[RequireComponent(typeof(ParticleSystem))]
public class SandstormController : MonoBehaviour
{
    // ===================== 单例: 让 WindArea 能查到当前强度 =====================
    // 场景里只有一个 SandstormController, WindArea 通过 Instance 查询 CurrentIntensity.
    // 如果场景没挂这个脚本(旧场景), WindArea 里 Instance==null 时默认强度=1, 兼容旧行为.
    public static SandstormController Instance { get; private set; }

    // ===================== 粒子模块引用(运行时获取) =====================
    private ParticleSystem ps;
    private ParticleSystem.EmissionModule emissionModule; // 控制发射量
    private ParticleSystem.MainModule mainModule;         // 控制大小等
    private ParticleSystem.VelocityOverLifetimeModule velModule; // 控制粒子飞行方向和速度(单向吹关键)

    [Header("=== 风区遮罩 (WindArea 列表) ===")]
    [Tooltip("把场景里所有 WindArea 拖进来. 粒子飞到这些风区内会变亮, 风区外会变淡")]
    [SerializeField] private List<WindArea> windAreas = new List<WindArea>();

    [Header("=== 粒子: 日常状态 ===")]
    [Tooltip("日常每秒发射粒子数(稀疏, 表现沙漠微风)")]
    [SerializeField] private float baseEmission = 8f;
    [Tooltip("日常粒子大小(单一值, 没有随机)")]
    [SerializeField] private float baseSize = 0.25f;
    [Tooltip("日常粒子初始飞行速度(发射时给的速度)")]
    [SerializeField] private float baseSpeed = 2f;
    [Tooltip("日常粒子存活时间(秒), 决定能飞多远")]
    [SerializeField] private float baseLifetime = 5f;

    [Header("=== 粒子: 沙尘暴状态 ===")]
    [Range(0f, 1f)]
    [Tooltip("沙尘暴等级: 0.3=轻沙, 0.7=中沙, 1.0=强沙尘暴")]
    [SerializeField] private float stormLevel = 1f;
    [Tooltip("沙尘暴时每秒发射粒子数(浓密)")]
    [SerializeField] private float stormEmission = 80f;
    [Tooltip("沙尘暴时粒子最大大小")]
    [SerializeField] private float stormSize = 0.5f;
    [Tooltip("沙尘暴时粒子最小小大小 —— 最强时大小在[stormMinSize, stormSize]间随机, 大小都有不单调")]
    [SerializeField] private float stormMinSize = 0.15f;
    [Tooltip("沙尘暴时粒子初始飞行速度(通常比日常快, 表现风更猛)")]
    [SerializeField] private float stormSpeed = 5f;
    [Tooltip("沙尘暴时粒子存活时间(秒, 通常比日常长, 保证浓密粒子能横扫屏幕)")]
    [SerializeField] private float stormLifetime = 8f;

    [Header("=== 风区透明度 (遮罩效果) ===")]
    [Range(0f, 1f)]
    [Tooltip("日常状态下所有粒子的透明度(到处一样, 不分风区内/外)")]
    [SerializeField] private float baseAlpha = 1f;
    [Range(0f, 1f)]
    [Tooltip("沙尘暴时, 粒子在风瓦片涂了的地方(风区内)的透明度")]
    [SerializeField] private float insideAlpha = 1f;
    [Range(0f, 1f)]
    [Tooltip("沙尘暴时, 粒子飞到没涂风瓦片的地方(风区外)的透明度 —— 这就是'变淡但不消失'")]
    [SerializeField] private float outsideAlpha = 0.25f;
    [Tooltip("粒子透明度过渡速度: 粒子从风区走到无风区时, alpha 每秒变化多少(0~1). 数值越大变化越快, 0.5=2秒变完")]
    [SerializeField] private float alphaTransitionSpeed = 2f;

    [Header("=== 后期: Vignette 暗角 (即你说的灯光环境氛围) ===")]
    [Tooltip("挂你场景里的 Global Volume (带 Vignette override 的)")]
    [SerializeField] private Volume postVolume;
    [Tooltip("日常状态暗角强度")]
    [SerializeField] private float baseVignetteIntensity = 0.35f;
    [Tooltip("沙尘暴状态暗角强度")]
    [SerializeField] private float stormVignetteIntensity = 0.75f;

    [Header("=== 过渡 ===")]
    [Tooltip("沙尘暴淡入/淡出时间(秒). 数值越大过渡越慢越柔和")]
    [SerializeField] private float transitionDuration = 3f;

    [Header("=== 定时触发 ===")]
    [Tooltip("开启后按间隔自动触发沙尘暴; 关闭则只能手动调用 StartStorm()/StopStorm()")]
    [SerializeField] private bool enableScheduler = false;
    [Tooltip("两次沙尘暴之间的间隔时间(秒)")]
    [SerializeField] private float interval = 20f;
    [Tooltip("每次沙尘暴持续时间(秒)")]
    [SerializeField] private float duration = 8f;

    // ===================== 内部状态 =====================
    private float currentIntensity = 0f;   // 当前强度 0~1 (会逐帧 lerp)
    private float targetIntensity = 0f;    // 目标强度
    private bool isStorming = false;       // 是否处于沙尘暴中
    private Vignette vignette;             // 从 Volume 取出的暗角组件
    private Coroutine schedulerCo;         // 定时器协程句柄
    private bool schedulerRunning = false; // 定时器是否在跑(用于 Inspector 实时开关)

    // OnParticleTrigger 用的缓存 List, 避免每帧 new 产生 GC 垃圾
    private List<ParticleSystem.Particle> insideParticles = new List<ParticleSystem.Particle>();
    private List<ParticleSystem.Particle> outsideParticles = new List<ParticleSystem.Particle>();

    // 对外只读属性, 方便别的脚本查询状态
    public bool IsStorming => isStorming;
    public float CurrentIntensity => currentIntensity;

    void Awake()
    {
        // 注册单例, 让 WindArea 能找到我
        Instance = this;

        ps = GetComponent<ParticleSystem>();
        emissionModule = ps.emission;
        mainModule = ps.main;
        // 获取 Velocity over Lifetime 模块并强制启用 —— 粒子单向飞的关键
        // 这个模块每帧设置粒子速度, 会覆盖 Start Speed, 所以速度必须在这里控制
        velModule = ps.velocityOverLifetime;
        velModule.enabled = true;
        velModule.space = ParticleSystemSimulationSpace.World; // 世界空间, 不受粒子系统自身旋转影响
    }

    void Start()
    {
        // 1. 配置粒子 Trigger 模块: 用 WindArea 的 Collider2D 当触发器
        SetupTrigger();

        // 2. 从 Volume 取 Vignette 引用 (后续每帧改它的 intensity)
        if (postVolume != null && postVolume.profile != null)
        {
            postVolume.profile.TryGet(out vignette);
            // 保险: 即使 Inspector 里 Vignette override 忘记勾选激活(active=0),
            // 这里也强制打开. 否则脚本改 intensity 也不会显示效果.
            if (vignette != null) vignette.active = true;
        }

        // 3. 初始化为日常状态
        ApplyIntensity(currentIntensity);

        // 4. 如果开了定时器, 启动
        if (enableScheduler)
        {
            StartScheduler();
            schedulerRunning = true;
        }
    }

    // -------- 配置 Trigger 模块 --------
    void SetupTrigger()
    {
        var trigger = ps.trigger;
        trigger.enabled = true;
        // inside / outside 都设为 Callback, 这样两种粒子我们都能拿到并改透明度
        // 注意: Unity 2022+ 把这个 enum 从 ParticleSystemTriggerAction 改名成了
        //       ParticleSystemOverlapAction, 旧名字会报 CS0103 找不到类型
        trigger.inside  = ParticleSystemOverlapAction.Callback;
        trigger.outside = ParticleSystemOverlapAction.Callback;

        // 把所有 WindArea 的 Collider2D 逐个添加为触发器
        // 注意: Unity 2022 的 TriggerModule 没有 SetColliders(数组) 方法,
        //       只有 AddCollider(单个) / SetCollider(index, 单个), 所以要循环添加
        foreach (var wind in windAreas)
        {
            if (wind == null) continue;
            // WindArea 有 [RequireComponent(typeof(Collider2D))], 一定拿得到
            var col = wind.GetComponent<Collider2D>();
            if (col != null) trigger.AddCollider(col);
        }
    }

    void Update()
    {
        // ---- 强度 lerp 过渡 ----
        if (!Mathf.Approximately(currentIntensity, targetIntensity))
        {
            // MoveTowards 按固定速度逼近, transitionDuration 决定速度
            float speed = 1f / Mathf.Max(0.01f, transitionDuration);
            currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, speed * Time.deltaTime);
            ApplyIntensity(currentIntensity);
        }

        // ---- 响应 Inspector 里实时勾选 enableScheduler ----
        if (enableScheduler && !schedulerRunning)
        {
            StartScheduler();
            schedulerRunning = true;
        }
        else if (!enableScheduler && schedulerRunning)
        {
            StopScheduler();
            schedulerRunning = false;
        }
    }

    // -------- 根据 intensity 下发所有参数 --------
    void ApplyIntensity(float t)
    {
        // 粒子发射量: rateOverTime 支持 lerp, 过渡平滑
        emissionModule.rateOverTime = Mathf.Lerp(baseEmission, stormEmission, t);

        // 粒子大小: 用 MinMaxCurve 实现"沙尘暴时大小随机, 日常单一值".
        // t=0 时 min=max=baseSize(无随机); t=1 时 min=stormMinSize, max=stormSize(大小都有).
        // 注意 startSize 只对"新发射"的粒子生效, 老粒子不变,
        // 过渡期新老粒子大小不一的混叠可接受(反而显得自然)
        float minSize = Mathf.Lerp(baseSize, stormMinSize, t);
        float maxSize = Mathf.Lerp(baseSize, stormSize, t);
        mainModule.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);

        // 粒子飞行速度和方向: 用 Velocity over Lifetime 模块控制(不是 startSpeed!).
        // 重要: Velocity over Lifetime 每帧覆盖粒子速度, 所以 Start Speed 调了也没用,
        //       速度必须在这里设置才会生效.
        // X=正数往右吹, X=负数往左吹; Y=0 保证不上下飘; Z=0 保证在2D平面内.
        // 日常速度慢(baseSpeed), 沙尘暴速度快(stormSpeed), 跟 intensity 平滑过渡.
        float windSpeed = Mathf.Lerp(baseSpeed, stormSpeed, t);
        velModule.x = new ParticleSystem.MinMaxCurve(windSpeed);
        velModule.y = new ParticleSystem.MinMaxCurve(0f);
        velModule.z = new ParticleSystem.MinMaxCurve(0f);

        // 粒子存活时间: 跟着 intensity 过渡, 沙尘暴时活更久(保证浓密粒子能横扫整个屏幕)
        mainModule.startLifetime = Mathf.Lerp(baseLifetime, stormLifetime, t);

        // Vignette 暗角: 直接改 intensity, 跟着 intensity 一起过渡
        if (vignette != null)
        {
            vignette.intensity.value = Mathf.Lerp(baseVignetteIntensity, stormVignetteIntensity, t);
        }
    }

    // ======================================================================
    // OnParticleTrigger: 风区内变亮 / 风区外变淡
    // ----------------------------------------------------------------------
    // 这个回调由 Unity 自动调用(因为上面设置了 inside/outside = Callback).
    // 它把"在触发器内的粒子"和"在触发器外的粒子"分别给我们.
    //
    // 关键设计(满足你的需求):
    //   1. 日常(intensity=0)时, 风区内外的目标 alpha 都是 baseAlpha,
    //      即日常粒子到处透明度一样, 不分风区.
    //   2. 沙尘暴(intensity=1)时, 风区内目标=insideAlpha, 风区外目标=outsideAlpha.
    //   3. 用 MoveTowards 让粒子当前 alpha 逐渐逼近目标, 有时间过渡, 不会瞬变.
    //      这就是"粒子从风区走到无风区时透明度随时间逐渐改变".
    // ======================================================================
    void OnParticleTrigger()
    {
        int numInside  = ps.GetTriggerParticles(ParticleSystemTriggerEventType.Inside,  insideParticles);
        int numOutside = ps.GetTriggerParticles(ParticleSystemTriggerEventType.Outside, outsideParticles);

        // 计算当前帧风区内/外的"目标 alpha":
        // 日常时(intensity=0)两个都是 baseAlpha(到处一样);
        // 沙尘暴时(intensity=1)风区内=insideAlpha, 风区外=outsideAlpha;
        // 过渡期在两者间 lerp.
        float targetInsideAlpha  = Mathf.Lerp(baseAlpha, insideAlpha,  currentIntensity);
        float targetOutsideAlpha = Mathf.Lerp(baseAlpha, outsideAlpha, currentIntensity);

        // 每帧 alpha 变化量(用 deltaTime 保证过渡速度不受帧率影响)
        float delta = alphaTransitionSpeed * Time.deltaTime;

        // 风区内粒子: alpha 逐渐逼近 targetInsideAlpha
        for (int i = 0; i < numInside; i++)
        {
            var p = insideParticles[i];
            Color c = p.startColor;
            // MoveTowards: 从当前 alpha 平滑过渡到目标, 不会瞬变
            c.a = Mathf.MoveTowards(c.a, targetInsideAlpha, delta);
            p.startColor = c;
            insideParticles[i] = p;
        }

        // 风区外粒子: alpha 逐渐逼近 targetOutsideAlpha
        for (int i = 0; i < numOutside; i++)
        {
            var p = outsideParticles[i];
            Color c = p.startColor;
            c.a = Mathf.MoveTowards(c.a, targetOutsideAlpha, delta);
            p.startColor = c;
            outsideParticles[i] = p;
        }

        // 写回(只写有变化的, 避免无谓开销)
        // 注意: Unity 2022 的 SetTriggerParticles 只有 2 参数重载 (类型, List),
        //       没有 3 参数带 count 的版本, 旧写法会报 CS1501 参数不匹配
        if (numInside > 0)
            ps.SetTriggerParticles(ParticleSystemTriggerEventType.Inside, insideParticles);
        if (numOutside > 0)
            ps.SetTriggerParticles(ParticleSystemTriggerEventType.Outside, outsideParticles);
    }

    // ======================================================================
    // 手动开关 (可被别的脚本调用, 也可在 Inspector 右键调用)
    // ======================================================================
    [ContextMenu("开始沙尘暴")]
    public void StartStorm()
    {
        StartStorm(stormLevel);
    }

    // 带等级参数的重载: 想触发不同强度时用, 比如 StartStorm(0.5f) 中等沙
    public void StartStorm(float level)
    {
        isStorming = true;
        targetIntensity = Mathf.Clamp01(level);
    }

    [ContextMenu("停止沙尘暴")]
    public void StopStorm()
    {
        isStorming = false;
        targetIntensity = 0f;
    }

    // ======================================================================
    // 定时器
    // ======================================================================
    public void StartScheduler()
    {
        if (schedulerCo != null) StopCoroutine(schedulerCo);
        schedulerCo = StartCoroutine(SchedulerLoop());
    }

    public void StopScheduler()
    {
        if (schedulerCo != null)
        {
            StopCoroutine(schedulerCo);
            schedulerCo = null;
        }
    }

    IEnumerator SchedulerLoop()
    {
        while (true)
        {
            // 等间隔时间
            yield return new WaitForSeconds(interval);
            // 触发沙尘暴(用 stormLevel 等级)
            StartStorm(stormLevel);
            // 持续 duration 秒
            yield return new WaitForSeconds(duration);
            // 停止(开始淡出)
            StopStorm();
            // 循环回到 while 顶, 再等 interval 秒
        }
    }
}
