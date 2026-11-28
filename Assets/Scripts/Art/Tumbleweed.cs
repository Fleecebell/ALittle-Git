using UnityEngine;

// ======================================================================
// Tumbleweed —— 风滚草单体
// --------------------------------------------------------------------------
// 设计思路:
//   1. 纯物理系统: Dynamic Rigidbody2D + CircleCollider2D, 不用动画.
//   2. 不自带滚动. 移动完全由 WindArea 的风幕驱动:
//      风往左 → 左滚, 风往右 → 右滚, 与玩家同方向. 没风时静止.
//   3. 限制最大速度, 防止无限加速.
//   4. 两种销毁方式:
//      - 滚出屏幕外 (x < destroyX)
//      - 存活时间到期 (lifetime)
//   5. 销毁时通过事件通知生成器, 生成器减计数 (保证数量上限有效).
//   6. 受 WindArea 影响: WindArea 识别 "Tumbleweed" tag, 按风向加力.
//      风滚草很轻 (Mass=0.1), 同样的力效果比玩家明显得多, 风暴时被吹飞.
//   7. 主角碰到会自然推开风滚草 (质量比 10:1), 主角几乎不减速.
// ======================================================================
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class Tumbleweed : MonoBehaviour
{
    [Header("=== 自动滚动 (不依赖沙尘暴) ===")]
    [Tooltip("开启后风滚草自带滚动, 不需要沙尘暴也会滚. 关闭则只在沙尘暴时被风吹动")]
    [SerializeField] private bool autoRoll = true;

    [Tooltip("自动滚动方向. Left=往左滚, Right=往右滚. 直接选, 不用管正负号")]
    [SerializeField] private RollDirection autoRollDirection = RollDirection.Left;

    [Tooltip("自动滚动目标速度. 风滚草会逐渐加速到这个速度并保持. 永远填正数! 方向由上面选")]
    [SerializeField] private float autoRollSpeed = 2f;

    [Header("=== 滚动 ===")]
    [Tooltip("最大水平速度限制, 防止无限加速. 超过此值就钳制. 永远填正数! 方向由风决定, 不是这里")]
    [SerializeField] private float maxSpeed = 5f;

    // 自动滚动方向枚举: 用户直接选左/右, 不用管正负号
    public enum RollDirection { Left, Right }

    [Header("=== 生命周期 ===")]
    [Tooltip("滚到屏幕左外这个 X 坐标时销毁 (防止无限滚下去)")]
    [SerializeField] private float destroyX = -15f;

    [Tooltip("最大存活时间(秒). 到期自动销毁, 防止卡在平台里堆积")]
    [SerializeField] private float lifetime = 15f;

    [Header("=== 滚动旋转 ===")]
    [Tooltip("是否手动驱动旋转 (推荐开). 关闭则交给物理引擎自动滚 (有时不转)")]
    [SerializeField] private bool manualRoll = true;

    [Tooltip("滚动旋转倍率. 1=按真实圆周滚 (速度÷半径×弧度). 2=转得更快, 视觉更明显. 永远填正数! 旋转方向由速度自动决定")]
    [SerializeField] private float rollMultiplier = 1f;

    [Header("=== 调试 ===")]
    [Tooltip("运行时显示当前速度, 方便调参 (发布前可关掉)")]
    [SerializeField] private bool showDebug = false;

    /// <summary>
    /// 销毁时触发, 生成器订阅它来减计数.
    /// 参数是本对象引用, 生成器可据此清理引用.
    /// </summary>
    public event System.Action<Tumbleweed> OnDestroyed;

    private Rigidbody2D rb;
    private CircleCollider2D circle;
    private float radius = 0.4f;  // 圆形碰撞体半径, 用于计算滚动角速度
    private bool notified = false;  // 防止销毁事件重复触发

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        circle = GetComponent<CircleCollider2D>();
    }

    void Start()
    {
        // 在 Start 里算 radius (此时 spawner 已在 Instantiate 后设好随机 scale, Awake 时还没设)
        // radius = 碰撞体半径 × X 缩放, 用于手动驱动旋转的角速度计算
        if (circle != null) radius = Mathf.Max(0.01f, circle.radius * transform.localScale.x);
        // 定时自毁保险: 不管在哪, 到时间就销毁
        Destroy(gameObject, lifetime);
    }

    void FixedUpdate()
    {
        // 安全防护: maxSpeed 和 rollMultiplier 永远取绝对值
        // (用户误填负数会导致速度方向反转, 草往反方向滚)
        float safeMaxSpeed = Mathf.Abs(maxSpeed);
        float safeRollMul = Mathf.Abs(rollMultiplier);

        // 自动滚动: 直接设置 velocity, 不依赖 WindArea 或沙尘暴
        // 用 MoveTowards 平滑加速, 避免瞬间达到目标速度 (有惯性加速感)
        // 比起 AddForce 更稳定可靠, 刚生成就立即有速度
        if (autoRoll)
        {
            int dirSign = (autoRollDirection == RollDirection.Left) ? -1 : 1;
            float safeAutoSpeed = Mathf.Abs(autoRollSpeed);
            float targetVx = dirSign * safeAutoSpeed;
            // 平滑加速到目标速度 (加速度 = safeAutoSpeed * 2 每秒, 0.5秒达到目标)
            float newVx = Mathf.MoveTowards(rb.velocity.x, targetVx,
                                            safeAutoSpeed * 2f * Time.fixedDeltaTime);
            rb.velocity = new Vector2(newVx, rb.velocity.y);
        }

        // 限速: 防止无限加速 (风暴时风力会叠加, 不限制会越滚越快)
        // 用 Mathf.Sign 保留原始方向, 只限制大小
        Vector2 v = rb.velocity;
        if (Mathf.Abs(v.x) > safeMaxSpeed)
        {
            rb.velocity = new Vector2(Mathf.Sign(v.x) * safeMaxSpeed, v.y);
        }

        // 手动驱动旋转: 根据水平速度算角速度, 强制让它转起来
        // 2D 物理圆圈滚动有时不稳定 (摩擦不够/贴地不实), 手动驱动最可靠
        // 公式: 角速度(度/秒) = -velocity.x / 半径 × 弧度转角度 × 倍率
        // 负号: 往左滚(velocity.x<0)应顺时针看是逆时针(Unity正Z旋转), 即正值
        if (manualRoll)
        {
            float angularVel = -v.x / radius * Mathf.Rad2Deg * safeRollMul;
            rb.angularVelocity = angularVel;
        }

        // 出屏幕销毁: 根据滚动方向判断
        // 往左滚 → 滚到 destroyX(负数) 销毁; 往右滚 → 滚到 destroyX(正数) 销毁
        // destroyX 的符号自动匹配方向: 负数=左边界, 正数=右边界
        if (destroyX < 0 && transform.position.x < destroyX)
        {
            NotifyAndDestroy();
        }
        else if (destroyX > 0 && transform.position.x > destroyX)
        {
            NotifyAndDestroy();
        }

        if (showDebug)
        {
            Debug.Log($"[Tumbleweed] 速度={rb.velocity.magnitude:F2} 角速度={rb.angularVelocity:F1} 位置={transform.position}");
        }
    }

    /// <summary>
    /// 通知生成器并销毁. 用 notified flag 保证只通知一次
    /// (主动销毁 + OnDestroy + Destroy(lifetime) 可能重叠触发).
    /// </summary>
    private void NotifyAndDestroy()
    {
        if (notified) return;
        notified = true;
        OnDestroyed?.Invoke(this);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        // 场景切换/游戏停止/Destroy 调用时兜底通知 (保证计数准确)
        if (!notified)
        {
            notified = true;
            OnDestroyed?.Invoke(this);
        }
    }
}
