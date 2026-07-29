using UnityEngine;

// ======================================================================
// Tumbleweed —— 风滚草单体
// --------------------------------------------------------------------------
// 设计思路:
//   1. 纯物理系统: Dynamic Rigidbody2D + CircleCollider2D, 不用动画.
//   2. 每帧 FixedUpdate 加往左的力, 自然滚动 (圆形 collider + 摩擦 = 滚动).
//   3. 限制最大速度, 防止无限加速.
//   4. 两种销毁方式:
//      - 滚出屏幕外 (x < destroyX)
//      - 存活时间到期 (lifetime)
//   5. 销毁时通过事件通知生成器, 生成器减计数 (保证数量上限有效).
//   6. 受 WindArea 影响: WindArea 识别 "Tumbleweed" tag, 风暴时加风吹力.
//      风滚草很轻 (Mass=0.1), 同样的力效果比玩家明显得多, 风暴时被吹飞.
//   7. 主角碰到会自然推开风滚草 (质量比 10:1), 主角几乎不减速.
// ======================================================================
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class Tumbleweed : MonoBehaviour
{
    [Header("=== 滚动 ===")]
    [Tooltip("每帧施加的往左推力. 力越大滚得越快, 太小会停, 太大会飞出")]
    [SerializeField] private float scrollForce = 2f;

    [Tooltip("最大水平速度限制, 防止无限加速. 超过此值就钳制")]
    [SerializeField] private float maxSpeed = 5f;

    [Header("=== 生命周期 ===")]
    [Tooltip("滚到屏幕左外这个 X 坐标时销毁 (防止无限滚下去)")]
    [SerializeField] private float destroyX = -15f;

    [Tooltip("最大存活时间(秒). 到期自动销毁, 防止卡在平台里堆积")]
    [SerializeField] private float lifetime = 15f;

    [Header("=== 滚动旋转 ===")]
    [Tooltip("是否手动驱动旋转 (推荐开). 关闭则交给物理引擎自动滚 (有时不转)")]
    [SerializeField] private bool manualRoll = true;

    [Tooltip("滚动旋转倍率. 1=按真实圆周滚 (速度÷半径×弧度). 2=转得更快, 视觉更明显")]
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
        // 持续往左加力, 让风滚草自然滚动
        rb.AddForce(Vector2.left * scrollForce, ForceMode2D.Force);

        // 限速: 防止无限加速 (风暴时风力会叠加, 不限制会越滚越快)
        Vector2 v = rb.velocity;
        if (Mathf.Abs(v.x) > maxSpeed)
        {
            rb.velocity = new Vector2(Mathf.Sign(v.x) * maxSpeed, v.y);
        }

        // 手动驱动旋转: 根据水平速度算角速度, 强制让它转起来
        // 2D 物理圆圈滚动有时不稳定 (摩擦不够/贴地不实), 手动驱动最可靠
        // 公式: 角速度(度/秒) = -velocity.x / 半径 × 弧度转角度 × 倍率
        // 负号: 往左滚(velocity.x<0)应顺时针看是逆时针(Unity正Z旋转), 即正值
        if (manualRoll)
        {
            float angularVel = -v.x / radius * Mathf.Rad2Deg * rollMultiplier;
            rb.angularVelocity = angularVel;
        }

        // 出屏幕销毁
        if (transform.position.x < destroyX)
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
