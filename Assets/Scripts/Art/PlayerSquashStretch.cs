using UnityEngine;

/// <summary>
/// 玩家 Q 弹变形（纯视觉）。
///
/// 四种表现：
///   1. 普通落地（踩在地面 / 平台）—— 纵向压扁，**底边死死贴着脚底的碰撞体底边**，幅度随撞击速度；
///   2. 空中（下落 / 起跳）—— 纵向上方拉长，脚底不动；
///   3. 踩到另一个角色头上 —— **只横向拉宽，高度一点不变**。
///      为什么：碰撞体不会跟着视觉缩短，纵向一压，两个角色中间立刻出现一条缝，看着像上面那位在飘；
///   4. 被另一个角色踩到 —— **只横向拉宽，且一直保持到对方离开才回弹**。
///      同样不压高度，否则被踩的人自己会"缩起来腾空"，上面的方块也会跟着悬空。
///
/// 脚底对齐为什么这么做（★ 这里踩过两次坑，别改回去★）：
///   坑 1 —— 用 Collider2D.bounds 这类世界数据算偏移：本工程 Physics2D.autoSyncTransforms = false，
///          Start 那一帧 bounds 可能是没同步的脏值，算出来的偏移是天文数字，
///          表现是贴图飞到 1e17 外、Console 每帧刷 Invalid worldAABB（"主角飞出去"）。
///   坑 2 —— 只把"局部底边"钉住：方块是被物理转起来的（角阻尼很低、旋转没冻结），
///          歪着的时候接触地面的是它的"最低那个角"而不是底面中点，
///          碰撞体不缩放、精灵缩放，两者世界半高不再相等，于是歪着走就浮空/陷进去。
///   所以现在：用旋转角把碰撞体和精灵的"世界半高"都算出来，让精灵最低点贴碰撞体最低点。
///   全程是三角 + 加减乘，不读 bounds、不读世界缩放，任意角度都精确贴地。
///
/// 前置条件（用 Tools/角色工具/玩家视觉层级 一键生成）：
///   Player(Player2)          ← 物理体：Rigidbody2D + BoxCollider2D + 全部游戏脚本（不动）
///   ├─ Visual                ← 缩放节点：本脚本每帧把它的世界旋转抵消成 identity → 缩放轴 = 世界轴
///   │  └─ Art                ← 画面节点：本脚本每帧把它的世界旋转设回方块自身 → 贴图继续翻滚
///   │     ├─ SpriteRenderer
///   │     └─ Animator
///   └─ VFX ...               ← 粒子留在外面，不跟着变形
///
/// 为什么"压扁/拉宽"必须用世界轴，而不是方块自己的轴：
///   方块被物理转起来之后，"落地压扁"要贴着地面压（世界竖直）、"被踩拉宽"要贴着他脚下往两边摊
///   （世界水平）。用方块自己的轴的话，方块侧着时它的局部 X 就是世界竖直的，
///   拉宽会变成"往上下长"，方向直接就错了。
///   但一个节点的 localScale 只能表达一个坐标系，所以要把"缩放"和"画面旋转"拆成两层。
///
/// 为什么 Animator 必须跟 SpriteRenderer 在一起：
///   动画 clip 的绑定 path 是空字符串（= Animator 自己所在的物体），只搬渲染器会让跳跃换帧静默失效。
/// </summary>
[DisallowMultipleComponent]
public class PlayerSquashStretch : MonoBehaviour
{
    // ===================== 引用 =====================
    [Header("=== 引用 ===")]
    [Tooltip("缩放节点（Player 下的 Visual）。它的旋转每帧被抵消成世界对齐，所以缩放轴 = 世界轴")]
    [SerializeField] private Transform visual;

    [Tooltip("画面节点（Visual 下的 Art，里面有 SpriteRenderer + Animator）。它的旋转每帧被设回方块自身的旋转，所以贴图继续翻滚")]
    [SerializeField] private Transform art;

    [Tooltip("只用来复用落地方向判定（法线角 groundAngleThreshold）。可留空，留空用 45 度")]
    [SerializeField] private PlayerFeelSettings feelSettings;

    [Tooltip("角色主碰撞体。留空自动 GetComponent<Collider2D>()")]
    [SerializeField] private Collider2D bodyCollider;

    // ===================== 开关 =====================
    [Header("=== 开关 ===")]
    [Tooltip("普通落地（踩在地面/平台上）时纵向压扁")]
    [SerializeField] private bool enableLandingSquash = true;

    [Tooltip("下落 / 起跳时向上拉长。爬梯、游泳、冲刺因为 gravityScale=0 会自动排除")]
    [SerializeField] private bool enableAirStretch = true;

    [Tooltip("踩到另一个角色头上时横向拉宽（不压高度，所以不会出现缝）。勾上=禁用")]
    [SerializeField] private bool disableActorLanding = false;

    [Tooltip("被另一个角色踩到时横向拉宽。勾上=禁用")]
    [SerializeField] private bool disableSteppedOnWiden = false;

    [Tooltip("贴墙时让精灵那条边贴住墙面（不穿进墙、也不留缝）。勾上=禁用")]
    [SerializeField] private bool disableWallHug = false;

    // 说明：这两个开关故意做成"勾上=禁用"，因为它们是后加字段。
    // 如果老序列化数据里没有这个键、Unity 又没套用字段初始值，bool 会变成 false = 功能开启，
    // 行为仍然正确；反过来写成 enableXxx=true 的话就可能被吃成 false，功能静默失效。

    // ===================== 落地压扁（纵向） =====================
    [Header("=== 普通落地：纵向压扁（幅度随撞击速度） ===")]
    [Tooltip("低于这个撞击速度不弹。0.2 格小台阶落地约 4.9，所以默认 5 刚好卡掉走路下台阶")]
    [SerializeField, Range(1f, 20f)] private float minImpactSpeed = 5f;

    [Tooltip("撞击速度到这里就饱和（满跳落地约 15.3，从 3 格高跳下约 18.8）")]
    [SerializeField, Range(2f, 40f)] private float maxImpactSpeed = 18f;

    [Tooltip("最大压扁量。0.2 = 最竖着压掉 20%（方块角色已经很显眼，别再大）")]
    [SerializeField, Range(0.02f, 0.5f)] private float maxSquash = 0.2f;

    [Tooltip("纵向压扁时横向跟着变宽的比例。0=只变矮，1=面积守恒。方块角色建议 0.6~0.8")]
    [SerializeField, Range(0f, 1f)] private float axisShare = 0.7f;

    [Tooltip("撞击速度 → 幅度 的响应曲线。横轴 0=刚到阈值，1=饱和；纵轴 0~1")]
    [SerializeField] private AnimationCurve impactResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    // ===================== 空中拉伸 =====================
    [Header("=== 空中：向上拉长（脚底不动） ===")]
    [Tooltip("空中最大拉伸量。0.12 = 向上抬高 12%")]
    [SerializeField, Range(0f, 0.4f)] private float maxAirStretch = 0.12f;

    [Tooltip("竖直速度到这个值时拉伸饱和")]
    [SerializeField, Range(1f, 40f)] private float stretchRefSpeed = 18f;

    [Tooltip("起跳(向上)的拉伸相对下落的比例。起跳是瞬间给速度，别拉太狠")]
    [SerializeField, Range(0f, 1f)] private float riseStretchScale = 0.6f;

    // ===================== 踩在另一个角色头上 =====================
    [Header("=== 踩在另一个角色头上：只横向拉宽 ===")]
    [Tooltip("踩到别人头上时的最大横向拉宽量（高度完全不变，所以两个方块不会分开）")]
    [SerializeField, Range(0f, 0.4f)] private float maxActorLandingWiden = 0.12f;

    // ===================== 被踩 =====================
    [Header("=== 被采：只横向拉宽（高度不变） ===")]
    [Tooltip("被踩瞬间横向弹开的幅度")]
    [SerializeField, Range(0f, 0.4f)] private float steppedOnWidenKick = 0.12f;

    [Tooltip("对方一直站着时保持的拉宽量。设 0 = 只在被踩瞬间弹一下，不保持")]
    [SerializeField, Range(0f, 0.3f)] private float steppedOnHoldWiden = 0.08f;

    [Tooltip("勾上后：对方站着期间保持拉宽，对方离开才回弹恢复（\"起来才恢复\"）")]
    [SerializeField] private bool holdWhileSteppedOn = true;

    // ===================== 弹簧 =====================
    [Header("=== 弹簧（Q 弹本身的速度） ===")]
    [Tooltip("★Q 弹速度总开关★ 1 = 默认；2 = 回弹快一倍（约 0.13 秒回中）；0.5 = 慢一倍（软绵绵）。" +
             "只改快慢、不改幅度 —— 内部按 刚度×速度²、阻尼×速度 等比缩放，所以过冲次数和形状保持不变")]
    [SerializeField, Range(0.2f, 3f)] private float bounceSpeed = 1f;

    [Tooltip("弹簧刚度（进阶）。想统一改快慢请用上面的 bounceSpeed，别直接动这个")]
    [SerializeField, Range(20f, 600f)] private float stiffness = 220f;

    [Tooltip("阻尼（进阶）。14 约等于 0.47 阻尼比（会过冲 1~2 次，就是 Q 弹的来源）")]
    [SerializeField, Range(1f, 60f)] private float damping = 14f;

    [Tooltip("积分最大步长（秒）。掉帧时防止弹簧炸开，与 ObjectFloat 一致用 1/120")]
    [SerializeField] private float maxSubStep = 1f / 120f;

    // bounceSpeed 统一换算成等效刚度/阻尼（等比缩放，阻尼比不变 → 形状不变、只改快慢）
    private float BounceMult => Mathf.Max(0.01f, bounceSpeed);
    private float EffStiffness => Mathf.Max(1f, stiffness) * BounceMult * BounceMult;
    private float EffDamping => Mathf.Max(0f, damping) * BounceMult;

    // ===================== 调试 =====================
    [Header("=== 调试 ===")]
    [SerializeField] private bool showDebug = false;

    // ===================== 内部状态 =====================
    private Rigidbody2D rb;
    private MoveFirst moveFirst;
    private MoveLate moveLate;
    private SpriteRenderer spriteRenderer;

    // 纵向变形：>0 向上拉长，<0 压扁
    private float deformV;
    private float deformVVel;
    // 横向变形：>0 拉宽（高度不变），<0 收窄
    private float deformH;
    private float deformHVel;

    // 视觉节点原始缩放 / 位置（Q 弹在它们基础上叠加，不覆写用户在 Inspector 里的设置）
    private Vector3 baseVisualScale = Vector3.one;
    private Vector3 baseVisualPos;

    // ---- 脚底对齐用的几何常量（全部只算一次，运行时只是三角 + 加减乘） ----
    // 说明：方块是被物理转起来的（Rigidbody2D 角阻尼很低、旋转没冻结），
    // 所以"贴地"不能按局部底边算 —— 方块歪着的时候，真正接触地面的是它的"最低那个角"。
    // 这里把碰撞体和精灵都按"旋转后的世界半高"来算，再让精灵最低点去贴碰撞体最低点。
    private float colliderHalfX = 0.488f;   // BoxCollider2D.size.x * 0.5
    private float colliderHalfY = 0.484f;   // BoxCollider2D.size.y * 0.5
    private Vector2 colliderOffset;         // BoxCollider2D.offset
    private Vector2 spriteSize = Vector2.one;   // 精灵自身尺寸（世界单位，未缩放）
    private Vector2 spriteCenter;               // 精灵中心相对 pivot 的偏移（居中时为 0）
    // 静止时"精灵底边 - 碰撞体底边"的差值（美术/尺寸差异），对齐时始终把这个差还回去
    private float restBottomOffset;
    // 安全阀只报一次，避免刷屏
    private bool warnedOffsetClamped;

    // 版本横幅：只打一次，专门用来确认"跑的是不是最新编译出来的程序集"
    private static bool loggedVersionBanner;

    // 空中最后一帧的竖直速度（落地瞬间接触求解已把速度清零，只能靠它兜底）
    private float lastAirborneVy;

    // 被踩状态（FixedUpdate 每帧先清空，再由 OnCollisionStay2D 置位；与 MoveLate 的 isSteppedOn 同款写法）
    private bool steppedOnThisFrame;
    // 左右两侧是否有墙（同样每帧清空再由 OnCollisionStay2D 置位）。
    // 从碰撞法线免费得到，不用额外射线：法线朝 +x = 墙在我左边，朝 -x = 墙在我右边（法线由对方指向我）。
    private bool wallLeftThisFrame;
    private bool wallRightThisFrame;
    // 被踩冲量的小去抖，防止接触反复成立导致连弹
    private float steppedOnCooldown;

    // 梯子触发器计数（双保险：gravityScale 已经能排除爬梯，这里再兜一层）
    private int ladderTriggerCount;

    private float groundAngleThreshold => feelSettings != null ? feelSettings.groundAngleThreshold : 45f;

    // ===================== 生命周期 =====================
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        moveFirst = GetComponent<MoveFirst>();
        moveLate = GetComponent<MoveLate>();
        if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();

        // 自动找子物体：即使编辑器工具漏接字段也能跑
        if (visual == null) visual = transform.Find("Visual");
        if (visual != null && art == null) art = visual.Find("Art");

        // 渲染器可能挂在 Visual 上（老结构）或 Art 上（新结构）
        if (spriteRenderer == null)
        {
            if (art != null) spriteRenderer = art.GetComponent<SpriteRenderer>();
            if (spriteRenderer == null && visual != null) spriteRenderer = visual.GetComponent<SpriteRenderer>();
        }

        if (visual != null)
        {
            baseVisualScale = visual.localScale;
            baseVisualPos = visual.localPosition;
        }

        // 兼容老序列化数据：下面这几个是"后加"的数值字段。
        // 如果 prefab 里存的是旧版本组件（没有这些键），Unity 有可能给到 0 —— 那就补回设计默认值。
        // 想彻底关掉对应表现，请用 disableActorLanding / disableSteppedOnWiden 开关，而不是把它们设 0。
        if (maxActorLandingWiden <= 0.0001f) maxActorLandingWiden = 0.12f;
        if (steppedOnWidenKick <= 0.0001f) steppedOnWidenKick = 0.12f;
        if (steppedOnHoldWiden < 0f) steppedOnHoldWiden = 0.08f;
        if (bounceSpeed <= 0.01f) bounceSpeed = 1f;
    }

    private void Start()
    {
        if (visual == null || spriteRenderer == null) return;

        // 注意：这里全是"纯数学"，一次都不读 Collider2D.bounds。
        // 本工程 Physics2D.autoSyncTransforms = false，Collider2D.bounds 在 Start 那一帧
        // 可能还是没同步的脏值；一旦拿它算偏移，贴图会被推到天文数字坐标上，
        // 表现就是"主角飞出去"、Console 每帧刷 Invalid worldAABB。
        if (bodyCollider is BoxCollider2D box)
        {
            colliderHalfX = Mathf.Abs(box.size.x) * 0.5f;
            colliderHalfY = Mathf.Abs(box.size.y) * 0.5f;
            colliderOffset = box.offset;
        }

        if (spriteRenderer.sprite != null)
        {
            spriteSize = spriteRenderer.sprite.bounds.size;
            spriteCenter = spriteRenderer.sprite.bounds.center;
        }

        // 静止时（旋转 0、缩放原始值）精灵底边与碰撞体底边的差值，之后每帧都把它还回去。
        // 这里刻意只用"原始局部位置 + 精灵自身尺寸"，不读 Collider2D.bounds（见上面的坑）。
        float restSpriteBottom = baseVisualPos.y + spriteCenter.y * baseVisualScale.y
                                - spriteSize.y * 0.5f * baseVisualScale.y;
        float restColliderBottom = colliderOffset.y - colliderHalfY;
        restBottomOffset = restSpriteBottom - restColliderBottom;

        // 版本横幅：看到这行才说明跑的是最新编译出来的程序集
        if (!loggedVersionBanner)
        {
            loggedVersionBanner = true;
            Debug.Log($"[Squash] v8 已加载（世界轴缩放 + 贴墙对齐，art={(art != null ? "OK" : "缺失→需重跑菜单")}）", this);
        }
    }

    private void OnDisable()
    {
        // 组件被关掉时把视觉还原，别把角色留在压扁状态
        deformV = 0f;
        deformVVel = 0f;
        deformH = 0f;
        deformHVel = 0f;
        ApplyVisual();
    }

    private void FixedUpdate()
    {
        // 与 MoveFirst/MoveLate 的 isGrounded 同一套机制：每步物理开始先清空，
        // 再由 OnCollisionStay2D 在"头上确实有人"时置位，Update 里读到的就是本帧状态。
        // 不清空的话，被踩过一次就会永远保持拉宽。
        steppedOnThisFrame = false;
        wallLeftThisFrame = false;
        wallRightThisFrame = false;
    }

    private void Update()
    {
        if (visual == null || spriteRenderer == null) return;

        // 死亡 / 重生期间角色在缩放 + 传送，直接归位，别和 DeathRespawnVFX 的 DOTween 打架
        if (DeathRespawnVFX.isDead)
        {
            deformV = 0f; deformVVel = 0f;
            deformH = 0f; deformHVel = 0f;
            ApplyVisual();
            return;
        }

        if (steppedOnCooldown > 0f) steppedOnCooldown -= Time.deltaTime;

        // ---- 1. 算这一帧的两个弹簧目标 ----
        bool airborne = IsAirborne();
        bool steppedOn = !disableSteppedOnWiden && holdWhileSteppedOn && steppedOnThisFrame;

        float targetV = 0f;      // 纵向：空中才拉长，其余时候回中
        float targetH = 0f;      // 横向：被踩期间保持拉宽，其余时候回中

        if (airborne && enableAirStretch && rb != null)
        {
            float vy = rb.velocity.y;
            float refSpeed = Mathf.Max(1f, stretchRefSpeed);
            if (vy < 0f)
                targetV = maxAirStretch * Mathf.Clamp01(-vy / refSpeed);                  // 下落 → 向上拉长
            else
                targetV = maxAirStretch * riseStretchScale * Mathf.Clamp01(vy / refSpeed);  // 上升 → 拉长一点点
        }
        else if (steppedOn)
        {
            // 被踩：只加宽，不加高（加高会让下面的方块自己缩起来、上面的方块悬空）
            targetH = steppedOnHoldWiden;
        }

        // 空中时持续记录竖直速度，供落地瞬间取"求解前速度"；
        // 一落地就清零，否则站一会儿再蹭到别的东西时会拿很久以前的旧值去算撞击。
        if (rb != null)
        {
            if (airborne) lastAirborneVy = rb.velocity.y;
            else lastAirborneVy = 0f;
        }

        // ---- 2. 两个弹簧各自积分（分步，掉帧也不炸） ----
        StepSpring(ref deformV, ref deformVVel, targetV, Time.deltaTime);
        StepSpring(ref deformH, ref deformHVel, targetH, Time.deltaTime);

        // ---- 3. 夹紧 + 应用到视觉节点 ----
        deformV = Mathf.Clamp(deformV,
            -Mathf.Max(maxSquash, maxActorLandingWiden) * 1.4f,
            Mathf.Max(0.0001f, maxAirStretch) * 1.5f);
        deformH = Mathf.Clamp(deformH,
            -Mathf.Max(0.05f, maxActorLandingWiden) * 0.6f,
            Mathf.Max(maxActorLandingWiden, steppedOnWidenKick, steppedOnHoldWiden) * 1.6f);

        ApplyVisual();

        if (showDebug)
            Debug.Log($"[Squash] {name} airborne={airborne} steppedOn={steppedOn} " +
                      $"vy={(rb != null ? rb.velocity.y : 0f):F2} " +
                      $"V={deformV:F3}({targetV:F3}) H={deformH:F3}({targetH:F3})", this);
    }

    // ===================== 弹簧 =====================
    // 阻尼弹簧追目标：目标为 0 时就是"回中 + 过冲"的 Q 弹；落地时另外给一个冲量。
    private void StepSpring(ref float value, ref float velocity, float target, float dt)
    {
        dt = Mathf.Clamp(dt, 0f, 0.1f);       // 切场景/卡顿的首帧不参与
        float step = Mathf.Max(0.0005f, maxSubStep);

        while (dt > 0f)
        {
            float h = Mathf.Min(dt, step);
            dt -= h;
            velocity += ((target - value) * EffStiffness - velocity * EffDamping) * h;
            value += velocity * h;
        }
    }

    // ===================== 应用视觉 =====================
    private void ApplyVisual()
    {
        if (visual == null || spriteRenderer == null) return;

        // 纵向：deformV 作用在世界竖直；横向：deformH 只加宽、不影响高度。
        // 关键点：这两条都必须是"世界轴"，不是方块自己的轴。
        //   · 被踩时"拉宽"的视觉含义是贴着他脚下往两边摊开 —— 世界水平；方块侧着时它的局部 X 是世界竖直的，
        //     用局部轴就会变成"往上下长"，方向就错了。
        //   · 落地压扁要贴着地面压 —— 也是世界竖直。
        // 但贴图本身还要继续跟着方块翻滚，所以靠两层节点：
        //   Visual（本脚本抵消旋转 → 缩放轴 = 世界轴） → Art（把方块旋转还回去 → 画面继续翻滚）
        float scaleY = Mathf.Max(0.05f, 1f + deformV);
        float scaleX = Mathf.Max(0.05f, 1f - deformV * axisShare + deformH);

        float rad = transform.eulerAngles.z * Mathf.Deg2Rad;
        float sinA = Mathf.Sin(rad), cosA = Mathf.Cos(rad);
        float absSin = Mathf.Abs(sinA), absCos = Mathf.Abs(cosA);

        float sxWorld, syWorld;   // 实际生效的"世界轴"缩放
        if (art != null)
        {
            // 抵消方块旋转 → 缩放轴变成世界轴；再把旋转还给画面节点 → 贴图继续翻滚
            visual.rotation = Quaternion.identity;
            art.rotation = transform.rotation;

            visual.localScale = new Vector3(
                baseVisualScale.x * scaleX,
                baseVisualScale.y * scaleY,
                baseVisualScale.z);

            // 世界轴缩放：精灵先绕 θ 转，再按世界轴缩放 → 竖直半高只跟 sy 有关
            sxWorld = baseVisualScale.x * scaleX;
            syWorld = baseVisualScale.y * scaleY;
        }
        else
        {
            // 兼容老结构（渲染器还直接在 Visual 上、没拆出 Art）：保持"方块自身轴"，方向会随方块转
            visual.localScale = new Vector3(
                baseVisualScale.x * scaleX,
                baseVisualScale.y * scaleY,
                baseVisualScale.z);
            sxWorld = baseVisualScale.x * scaleX;
            syWorld = baseVisualScale.y * scaleY;
        }

        // ---- 脚底对齐：贴"旋转后的最低那个角" ----
        // 方块被物理转起来之后，接触地面的是最低的角。碰撞体不跟着缩放、精灵跟着缩放，
        // 两者的"世界半高"不再相等；只把底边钉住，歪着时就会浮空或陷进去。
        //
        //   碰撞体世界半高 = hx·|sinθ| + hy·|cosθ|
        //   精灵世界半高   = sy·(W/2·|sinθ| + H/2·|cosθ|)        （世界轴缩放）
        //                 = (W/2·sx·|sinθ| + H/2·sy·|cosθ|)      （方块自身轴缩放）
        float colliderCenterY = colliderOffset.x * sinA + colliderOffset.y * cosA;
        float colliderHalfHeight = colliderHalfX * absSin + colliderHalfY * absCos;
        float colliderBottomY = transform.position.y + colliderCenterY - colliderHalfHeight;

        float spriteHalfHeight, spriteCenterY;
        if (art != null)
        {
            spriteHalfHeight = syWorld * (spriteSize.x * 0.5f * absSin + spriteSize.y * 0.5f * absCos);
            spriteCenterY = syWorld * (spriteCenter.x * sinA + spriteCenter.y * cosA);
        }
        else
        {
            spriteHalfHeight = spriteSize.x * 0.5f * sxWorld * absSin + spriteSize.y * 0.5f * syWorld * absCos;
            spriteCenterY = spriteCenter.x * sxWorld * sinA + spriteCenter.y * syWorld * cosA;
        }

        float targetWorldY = colliderBottomY + restBottomOffset + spriteHalfHeight - spriteCenterY;

        // 安全阀：正常值只在方块中心附近 ±1 内浮动；万一算出脏数据也绝不让贴图飞走
        float deltaFromBody = targetWorldY - transform.position.y;
        if (deltaFromBody > 2f || deltaFromBody < -2f)
        {
            if (!warnedOffsetClamped)
            {
                warnedOffsetClamped = true;
                Debug.LogWarning($"[Squash] {name} 脚底对齐算出异常位移 {deltaFromBody:F3}，已夹紧到安全范围。" +
                                 $"rotZ={transform.eulerAngles.z:F1} sx={sxWorld:F3} sy={syWorld:F3} " +
                                 $"restBottomOffset={restBottomOffset:F3}", this);
            }
            targetWorldY = transform.position.y + Mathf.Clamp(deltaFromBody, -2f, 2f);
        }

        // 直接写世界位置：父级是旋转过的方块，写 localPosition 会让"下移"变成斜着移动
        Vector3 wp = visual.position;
        wp.y = targetWorldY;

        // ---- 贴墙：让精灵那条边贴住碰撞体的那个面 ----
        // 压扁会横向鼓起、空中拉伸会横向收窄（碰撞体永远不变），贴着墙时就会
        // 一会儿穿进墙里、一会儿和墙之间留条缝。这里沿 x 把精灵整体挪一下，
        // 让它靠墙那条边和碰撞体的面重合。只在单侧有墙时处理（两边都夹住就居中）。
        if (!disableWallHug && (wallLeftThisFrame ^ wallRightThisFrame))
        {
            float colliderHalfW = colliderHalfX * absCos + colliderHalfY * absSin;
            float colliderCenterX = transform.position.x + (colliderOffset.x * cosA - colliderOffset.y * sinA);

            float spriteHalfW, spriteCenterOffX;
            if (art != null)
            {
                // 世界轴缩放：水平半宽只跟 sx 有关
                spriteHalfW = sxWorld * (spriteSize.x * 0.5f * absCos + spriteSize.y * 0.5f * absSin);
                spriteCenterOffX = sxWorld * (spriteCenter.x * cosA - spriteCenter.y * sinA);
            }
            else
            {
                // 方块自身轴缩放
                spriteHalfW = spriteSize.x * 0.5f * sxWorld * absCos + spriteSize.y * 0.5f * syWorld * absSin;
                spriteCenterOffX = spriteCenter.x * sxWorld * cosA - spriteCenter.y * syWorld * sinA;
            }

            float spriteCenterX = wp.x + spriteCenterOffX;
            float dx = wallLeftThisFrame
                ? (colliderCenterX - colliderHalfW) - (spriteCenterX - spriteHalfW)   // 贴左墙：左边对齐
                : (colliderCenterX + colliderHalfW) - (spriteCenterX + spriteHalfW);  // 贴右墙：右边对齐

            wp.x += Mathf.Clamp(dx, -1f, 1f);   // 夹紧，防止脏数据把精灵甩出去
        }

        visual.position = wp;
    }

    // ===================== 落地 / 被踩判定 =====================
    // 用碰撞回调而不是轮询 isGrounded：relativeVelocity 是求解前的真实撞击速度，轮询只能拿到 0。
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (visual == null) return;
        if (DeathRespawnVFX.isDead) return;
        if (!enableLandingSquash && disableActorLanding && disableSteppedOnWiden) return;

        Collider2D other = collision.collider;
        if (other == null) return;

        bool otherIsGround = other.CompareTag("Ground");
        bool otherIsActor = other.CompareTag("Player") || other.CompareTag("Player2");
        if (!otherIsGround && !otherIsActor) return;

        float upDotMin = Mathf.Cos(groundAngleThreshold * Mathf.Deg2Rad);
        float myCenterY = transform.position.y;   // 不用 Collider2D.bounds：autoSyncTransforms=false 时它可能是脏值

        bool landedOnSomething = false;   // 我踩在别人/地面上面（法线朝上）
        bool someoneOnMe = false;         // 别人踩在我头上（法线朝下 + 接触点在中线以上）

        foreach (ContactPoint2D contact in collision.contacts)
        {
            float upDot = Vector2.Dot(contact.normal, Vector2.up);
            if (upDot >= upDotMin) landedOnSomething = true;
            else if (upDot <= -upDotMin && contact.point.y > myCenterY + 0.05f) someoneOnMe = true;
        }

        // 撞击速度的两个来源：
        //   · relativeVelocity —— 求解前的真实相对速度，互相"拍"在一起时会更大（互踩正是这种）
        //   · lastAirborneVy   —— 自己空中最后一帧的竖直速度（落地帧 rb.velocity.y 已经是 0，读不到）
        // 落地只用"下落"的缓存值：起跳上冲时蹭到平台边缘不该被判成重摔。
        float relativeImpact = Mathf.Abs(collision.relativeVelocity.y);

        if (landedOnSomething && lastAirborneVy <= 0.01f)
        {
            float impactSpeed = Mathf.Max(relativeImpact, -lastAirborneVy);

            if (otherIsActor)
            {
                // 踩在另一个角色头上：只横向拉宽。纵向一压，碰撞体不会跟着缩短，
                // 两个角色中间必然出现一条缝，看起来像上面那位在飘。
                if (!disableActorLanding)
                    ApplyHorizontalKick(impactSpeed, maxActorLandingWiden);
            }
            else if (enableLandingSquash)
            {
                ApplyVerticalImpact(impactSpeed);
            }
        }
        else if (someoneOnMe && !disableSteppedOnWiden && steppedOnCooldown <= 0f)
        {
            // 被踩：同样只拉宽（纵向一压，自己被"缩起来"会腾空，上面的方块也跟着悬空）
            ApplyHorizontalKick(relativeImpact, steppedOnWidenKick);
            steppedOnCooldown = 0.06f;
        }
    }

    // 持续接触时做两件事：
    //   1. 记录左右两侧有没有墙（免费，不用额外射线）—— 贴墙时精灵那条边要贴住墙面；
    //   2. （可选）判断"我头上是不是站着另一个角色"，用于被压时的保持拉宽。
    private void OnCollisionStay2D(Collision2D collision)
    {
        if (visual == null) return;
        if (DeathRespawnVFX.isDead) return;

        Collider2D other = collision.collider;
        if (other == null) return;

        // ---- 1. 左右墙检测（法线由对方指向我：+x = 墙在我左边）----
        if (!disableWallHug)
        {
            foreach (ContactPoint2D contact in collision.contacts)
            {
                float nx = contact.normal.x;
                if (nx > 0.7f) wallLeftThisFrame = true;
                else if (nx < -0.7f) wallRightThisFrame = true;
            }
        }

        // ---- 2. 被踩检测 ----
        if (disableSteppedOnWiden || !holdWhileSteppedOn) return;
        if (!other.CompareTag("Player") && !other.CompareTag("Player2")) return;

        float myCenterY = transform.position.y;   // 不用 Collider2D.bounds：autoSyncTransforms=false 时它可能是脏值
        float upDotMin = Mathf.Cos(groundAngleThreshold * Mathf.Deg2Rad);

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (Vector2.Dot(contact.normal, Vector2.up) > -upDotMin) continue;
            if (contact.point.y > myCenterY + 0.05f)
            {
                steppedOnThisFrame = true;
                return;
            }
        }
    }

    // 普通落地：纵向压扁冲量
    private void ApplyVerticalImpact(float impactSpeed)
    {
        float amount = EvaluateAmount(impactSpeed, maxSquash);
        if (amount <= 0.0001f) return;

        // 冲量 → 初始速度。弹簧峰值位移约等于 kick/ω，所以取 kick = amount*ω 时
        // 峰值正好是 amount，改 stiffness 不会改变观感幅度（只改回弹快慢）。
        float omega = Mathf.Sqrt(Mathf.Max(1f, EffStiffness));
        deformVVel -= amount * omega;

        if (showDebug)
            Debug.Log($"[Squash] {name} 普通落地 speed={impactSpeed:F2} amount={amount:F3}", this);
    }

    // 踩在别人头上 / 被别人踩：横向拉宽冲量（高度不动）
    private void ApplyHorizontalKick(float impactSpeed, float maxAmount)
    {
        float amount = EvaluateAmount(impactSpeed, maxAmount);
        if (amount <= 0.0001f) return;

        float omega = Mathf.Sqrt(Mathf.Max(1f, EffStiffness));
        deformHVel += amount * omega;

        if (showDebug)
            Debug.Log($"[Squash] {name} 横向拉宽 impact={impactSpeed:F2} amount={amount:F3}", this);
    }

    // 撞击速度 → 变形量（含响应曲线）
    private float EvaluateAmount(float impactSpeed, float maxAmount)
    {
        if (impactSpeed < minImpactSpeed || maxAmount <= 0.0001f) return 0f;

        float t = Mathf.InverseLerp(minImpactSpeed, Mathf.Max(minImpactSpeed + 0.01f, maxImpactSpeed), impactSpeed);
        if (impactResponse != null && impactResponse.length > 0)
            t = Mathf.Clamp01(impactResponse.Evaluate(t));

        return maxAmount * t;
    }

    // ===================== 状态判定 =====================
    // 是否"真正在空中"：着地状态 + 重力真的在起作用 + 不在梯子上 + 不在水里。
    // 爬梯/游泳/冲刺都会把 gravityScale 置 0，所以这一条就排除了所有匀速竖直运动
    // （爬梯是匀速的，不该跟着拉长）。
    private bool IsAirborne()
    {
        if (IsGrounded()) return false;
        if (rb != null && rb.gravityScale <= 0.01f) return false;
        if (ladderTriggerCount > 0) return false;
        if (moveFirst != null && moveFirst.currentWaterPool != null) return false;
        if (moveLate != null && moveLate.currentWaterPool != null) return false;
        return true;
    }

    // 与 PlayerVFX 同款：P1 读 MoveFirst，P2 读 MoveLate
    private bool IsGrounded()
    {
        if (moveFirst != null) return moveFirst.isGrounded;
        if (moveLate != null) return moveLate.isGrounded;
        return false;
    }

    // 梯子触发器计数（梯子的 Tag 是 Ladder，与 MoveFirst/MoveLate 的判定一致）
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other != null && other.CompareTag("Ladder")) ladderTriggerCount++;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other != null && other.CompareTag("Ladder"))
            ladderTriggerCount = Mathf.Max(0, ladderTriggerCount - 1);
    }

    // ===================== 编辑器辅助 =====================
    [ContextMenu("手动触发一次最大压扁")]
    private void TestMaxSquash()
    {
        float omega = Mathf.Sqrt(Mathf.Max(1f, EffStiffness));
        deformVVel -= maxSquash * omega;
        deformHVel += maxActorLandingWiden * omega;
    }

    /// <summary>
    /// 调幅度曲线时的"作弊条"：在 Inspector 里右键组件 → 打印幅度映射表。
    /// 改完 impactResponse / minImpactSpeed / maxImpactSpeed 点一下，就能看到几档典型落地速度
    /// 各自压多少，不用进 Play 盲目试。
    /// （曲线"改了没感觉"的最常见原因：实际撞击速度都挤在 t=0.8~1.0 的尾巴上。）
    /// </summary>
    [ContextMenu("打印幅度映射表（改完曲线点这里）")]
    private void PrintImpactTable()
    {
        float[] samples = { minImpactSpeed, 7f, 10.9f, 13f, 15.34f, 18.8f, 24f };
        string[] notes = { "阈值(刚好不弹)", "小跳", "1格高落下", "中跳", "满跳落回", "3格高落下", "很高" };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Squash] 幅度映射（min={minImpactSpeed:F1} max={maxImpactSpeed:F1} " +
                      $"maxSquash={maxSquash:F2} bounceSpeed={bounceSpeed:F2}）");
        sb.AppendLine("   撞击速度   t(曲线横轴)   压扁量    说明");

        foreach (float v in System.Linq.Enumerable.Distinct(samples))
        {
            float t = Mathf.InverseLerp(minImpactSpeed, Mathf.Max(minImpactSpeed + 0.01f, maxImpactSpeed), v);
            float shaped = (impactResponse != null && impactResponse.length > 0)
                ? Mathf.Clamp01(impactResponse.Evaluate(t)) : t;
            float amount = maxSquash * shaped;

            string note = "";
            for (int i = 0; i < samples.Length; i++)
                if (Mathf.Abs(samples[i] - v) < 0.001f) { note = notes[i]; break; }

            sb.AppendLine($"   {v,7:F2}   {t,8:F3}    {amount,7:F3}    {note}");
        }

        sb.AppendLine("   提示：这一列压扁量差异不明显，就把 maxImpactSpeed 调大（比如 26），让常用落地速度落在曲线中段。");
        Debug.Log(sb.ToString(), this);
    }

    private void OnDrawGizmosSelected()
    {
        if (bodyCollider == null) return;
        Gizmos.color = Color.cyan;
        Bounds b = bodyCollider.bounds;
        Gizmos.DrawWireCube(b.center, b.size);                    // 碰撞体：变形不该动它
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawLine(new Vector3(b.min.x, b.min.y, 0f), new Vector3(b.max.x, b.min.y, 0f)); // 脚底锚定线
    }
}
