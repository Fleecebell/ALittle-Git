using UnityEngine;

// ======================================================================
// ParallaxLayer —— 单层视差背景 (挂在每个背景层物体上)
// --------------------------------------------------------------------------
// 业内做法说明 (讲给新手):
//   "视差"本质 = 远处的东西跟着摄像机/玩家移动得慢, 近处的快,
//   产生层次感和深度错觉. 实现方式有多种:
//
//   1. 摄像机法 (用摄像机移动量驱动): 适合横版卷轴(摄像机跟主角走).
//      背景位移 = 摄像机位移 × 视差系数(0~1). 系数越小越"远"(动得慢).
//
//   2. 玩家位置法 (本脚本用这种): 适合"固定机位"关卡(摄像机不动,
//      但玩家在屏幕里左右移动). 背景跟着玩家位置反向缓慢移动,
//      玩家往右走 → 背景往左漂 (像透过车窗看远山).
//
//   3. 多层叠加: 远景(系数 0.1, 动很慢) + 中景(系数 0.3) + 前景(系数 0.6),
//      层次越远系数越小. 本脚本支持每层单独挂, 独立调系数.
//
//   本脚本采用"玩家位置法", 因为你的关卡是固定机位. 玩家移动时:
//     背景位移 = (玩家当前X - 玩家初始X) × 系数
//   系数为正 → 背景跟玩家同向移动 (近景常用, 玩家往右背景也往右)
//   系数为负 → 背景反向移动 (远景常用, 玩家往右背景往左, 像看远处风景)
//
// 死亡重置天然支持:
//   玩家位置被重置到 p1Pos(初始位置) → (当前X - 初始X) = 0 → 背景回到原位
//   不需要额外挂钩死亡重开逻辑.
//
// 使用方法:
//   1. 把每个背景层(远景/中景/...)分别做成一个 GameObject (建议空父物体+sprite子物体)
//   2. 给每个背景层物体挂本脚本
//   3. 配好 Parallax X / Parallax Y 系数 (远景小, 中景大)
//   4. 拖入 Player 引用 (默认自动找 Player tag)
// ======================================================================
public class ParallaxLayer : MonoBehaviour
{
    [Header("=== 视差系数 ===")]
    [Tooltip("水平视差系数. 远景建议 0.1~0.3(动得慢), 中景 0.3~0.6. 正数=背景跟玩家同向移动, 负数=反向. 0=不动")]
    [SerializeField] private float parallaxX = 0.2f;

    [Tooltip("垂直视差系数. 你说只要左右不要上下, 默认设 0. 如果想要轻微上下, 设 0.05~0.1")]
    [SerializeField] private float parallaxY = 0f;

    [Header("=== 移动范围限制 (防止背景移出屏幕) ===")]
    [Tooltip("水平最大移动距离(世界单位). 超过此距离不再移动. 0=不限制. 建议设背景图片宽度的 20%~40%")]
    [SerializeField] private float maxXOffset = 5f;

    [Tooltip("垂直最大移动距离. 0=不限制. 默认 0 因为不要上下")]
    [SerializeField] private float maxYOffset = 0f;

    [Header("=== 平滑 (缓动) ===")]
    [Tooltip("平滑时间(秒). 背景大约用多少秒追上目标位置.\n" +
             "重要: '缓慢'靠调小 Parallax X(位移幅度), 不是调大这里!\n" +
             "这里只管响应速度: 小=方向变化立即响应(灵敏), 大=有惯性(迟钝, 方向变化会过冲).\n" +
             "推荐 0.2~0.5 (方向灵敏, 只是位移幅度小所以看起来缓慢)")]
    [SerializeField, Range(0.05f, 10f)] private float smoothTime = 0.3f;

    [Tooltip("最大速度. 防止背景瞬移过快. 0=不限. 通常不用改")]
    [SerializeField] private float maxSpeed = 10f;

    [Header("=== 引用 ===")]
    [Tooltip("主玩家引用(只跟主玩家, 不跟影子 Player2). 建议手动拖入主 Player 物体确保正确. 不填则自动找 Tag=Player 的物体(不会找 Player2)")]
    [SerializeField] private Transform player;

    [Header("=== 调试 ===")]
    [SerializeField] private bool showDebug = false;

    // ===================== 内部状态 =====================
    private Vector3 startPosition;     // 本背景层的初始位置 (脚本启动时记录)
    private Vector3 playerStartPos;    // 玩家的初始位置 (作为基准, 玩家相对它的偏移驱动背景)
    private Vector3 targetPosition;    // 背景当前目标位置
    private Vector3 velocity = Vector3.zero;  // SmoothDamp 用的速度缓存 (产生加减速度)

    void Start()
    {
        // 记录本背景层的起始位置 (后续位移都基于这个基准)
        startPosition = transform.position;

        // 找玩家
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        // 记录玩家初始位置作为基准
        // 死亡重开时玩家位置被重置到这个点 → 偏移归零 → 背景自动回正
        if (player != null)
        {
            playerStartPos = player.position;
        }

        targetPosition = startPosition;
    }

    void Update()
    {
        if (player == null) return;

        // ---- 1. 计算玩家相对初始位置的偏移 ----
        Vector3 playerOffset = player.position - playerStartPos;

        // ---- 2. 算背景的目标位移 = 玩家偏移 × 视差系数 ----
        float offsetX = playerOffset.x * parallaxX;
        float offsetY = playerOffset.y * parallaxY;

        // ---- 3. 限制范围 (防止背景移出屏幕) ----
        if (maxXOffset > 0f)
            offsetX = Mathf.Clamp(offsetX, -maxXOffset, maxXOffset);
        if (maxYOffset > 0f)
            offsetY = Mathf.Clamp(offsetY, -maxYOffset, maxYOffset);

        // ---- 4. 目标位置 = 初始位置 + 位移 ----
        targetPosition = startPosition + new Vector3(offsetX, offsetY, 0f);

        // ---- 5. SmoothDamp 平滑 (带速度的缓动, 产生加减速度) ----
        // 比 Lerp 好: Lerp 是指数衰减(立刻开始移动, 永远到不了), 无缓动感
        // SmoothDamp 有速度变量, 起步慢→加速→减速到目标, 真实的"漂移"手感
        // smoothTime = 大约用多少秒追上目标, 语义比 0.05 这种小数直观
        float maxSpd = maxSpeed > 0f ? maxSpeed : Mathf.Infinity;
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime, maxSpd);

        if (showDebug)
        {
            Debug.Log($"[ParallaxLayer {gameObject.name}] 玩家偏移={playerOffset.x:F2} " +
                      $"背景位移={offsetX:F2} 目标={targetPosition.x:F2}");
        }
    }
}
