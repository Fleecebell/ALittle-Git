using UnityEngine;

// ======================================================================
// WindArea —— 风幕 (一条竖直的分界线, 一侧全吹, 可被 Ground 墙实时遮挡)
// --------------------------------------------------------------------------
// 工作方式:
//   挂在一个空物体上, 空物体的位置就是"风幕"竖线的 x 坐标.
//   用 lineHeight 控制竖线在 y 方向的长度 (可调), 场景里用 Gizmos 画出来可视化.
//   风向为 Left/Right:
//     风往左吹 → 竖线左侧的玩家会被往左吹, 右侧不受影响.
//     风往右吹 → 竖线右侧的玩家会被往右吹, 左侧不受影响.
//   遮挡 (风幕式):
//     从竖线垂直(沿 y)的方向看, 沿 x 水平向目标发射多根射线,
//     射线会被 Tag 为 Ground 的碰撞体(Tilemap 地面/墙)阻挡.
//     玩家只有"被射到"——即存在一条从竖线出发、未被 Ground 挡住、
//     且落在玩家身体高度范围内的射线穿过——才会被吹动.
//     墙怎么移动都实时生效.
//
// 与 SandstormController 联动:
//   风力受 SandstormController.CurrentIntensity 控制.
//   没沙尘暴时(intensity=0)风完全不生效, 主角正常行走;
//   沙尘暴时风力随强度平滑增大.
//   如果场景没挂 SandstormController(旧场景), 默认强度=1.
// ======================================================================
public class WindArea : MonoBehaviour
{
    public enum WindDirection { Left, Right }

    [Header("风向")]
    [Tooltip("只支持左右风. 风往左吹 → 竖线左侧被吹; 往右 → 竖线右侧被吹")]
    [SerializeField] private WindDirection direction = WindDirection.Left;

    [Header("=== 竖线(风幕) ===")]
    [Tooltip("竖线中心在 y 上的偏移, 一般用物体自身 y, 不用改")]
    [SerializeField] private float lineCenterOffsetY = 0f;
    [Tooltip("竖线的长度(高度). 从中心向上下各延伸 lineHeight/2. 场景中会画出来便于观察")]
    [SerializeField] private float lineHeight = 10f;

    [Header("=== 风力 ===")]
    [SerializeField] private float speedP1 = 2f;
    [SerializeField] private float speedP2 = 3f;
    [Tooltip("施加给风滚草(Tumbleweed)的力. 风滚草很轻(Mass=0.1), 同样的力效果比玩家明显得多, 通常设玩家速度的 1.5~2 倍")]
    [SerializeField] private float tumbleweedForce = 4f;
    [Tooltip("没沙尘暴时风滚草的轻微滚动系数. 实际轻微力 = tumbleweedForce × 此系数, 方向仍跟风幕方向一致. 0 = 没风时静止")]
    [Range(0f, 1f)]
    [SerializeField] private float idleScrollCoeff = 0.15f;

    [Header("=== 遮挡 (风幕式) ===")]
    [Tooltip("挡风物体的 Tag. 玩家与竖线之间被带此 Tag 的碰撞体挡住时, 风不生效. 默认 Ground")]
    [SerializeField] private string occluderTag = "Ground";
    [Tooltip("是否检测遮挡. 关闭 = 不检测, 竖线一侧范围内全吹(旧的整块风区行为)")]
    [SerializeField] private bool useDynamicOcclusion = true;
    [Tooltip("沿玩家身体高度采样的射线数量. 覆盖玩家从脚到头, 任一透过就吹(全部被挡才不吹)")]
    [Range(1, 8)]
    [SerializeField] private int occluderSampleCount = 3;

    [Header("=== 调试 ===")]
    [Tooltip("在 Scene 视图绘制竖线(风幕)和遮挡射线. 勾选 = 显示, 取消 = 隐藏")]
    [SerializeField] private bool showDebugGizmos = true;

    // 用于在 Scene 视图绘制遮挡射线 (Update 里计算, Gizmos 里绘制)
    // item: 起点, 终点, 是否被挡
    private readonly System.Collections.Generic.List<DebugRay> debugRays =
        new System.Collections.Generic.List<DebugRay>();

    private struct DebugRay
    {
        public Vector2 start;
        public Vector2 end;
        public bool blocked;
    }

    // 竖线中心的 y
    private float LineCenterY => transform.position.y + lineCenterOffsetY;
    // 竖线 x
    private float LineX => transform.position.x;
    // 竖线底部/顶部 y
    private float LineBottomY => LineCenterY - lineHeight * 0.5f;
    private float LineTopY => LineCenterY + lineHeight * 0.5f;

    private void Update()
    {
        // 每帧清空调试射线缓存, 只保留本帧计算过的
        debugRays.Clear();

        // 沙尘暴强度: 没沙尘暴为 0, 有则取当前强度. 没有 SandstormController(旧场景)默认 1.
        float intensity = (SandstormController.Instance != null)
            ? SandstormController.Instance.CurrentIntensity
            : 1f;
        bool hasStorm = intensity >= 0.01f;

        int dir = direction == WindDirection.Right ? 1 : -1;

        // 水平风只作用于横跨竖线的物体 (在竖线的风向侧才可能被吹)
        // 用一个盒形检测抓取竖线两侧附近的对象
        Vector2 boxCenter = new Vector2(LineX, LineCenterY);
        // 盒子的高度 = 竖线高度, 宽度 = 风速覆盖到的水平范围
        float width = 60f; // 一个够大的覆盖范围, 具体受风与否由射线遮挡决定
        Vector2 boxSize = new Vector2(width, lineHeight);
        Collider2D[] hits = Physics2D.OverlapBoxAll(boxCenter, boxSize, 0f);

        foreach (Collider2D col in hits)
        {
            if (col == null || col.attachedRigidbody == null) continue;
            bool isPlayer = col.CompareTag("Player") || col.CompareTag("Player2");
            bool isWeed = col.CompareTag("Tumbleweed");
            if (!isPlayer && !isWeed) continue;

            // 风向往左时, 只有竖线左侧的对象才被吹
            float side = Mathf.Sign(col.bounds.center.x - LineX);
            if (side != dir) continue;

            // 高度必须在竖线范围内 (被风幕扫到)
            if (col.bounds.min.y > LineTopY || col.bounds.max.y < LineBottomY) continue;

            // 遮挡检测: 被 Ground 完全挡住 → 不吹 (风滚草轻微动也遵守, 被挡就不动)
            if (IsFullyBlocked(col, dir)) continue;

            // 没沙尘暴时: 玩家完全不受影响, 但风滚草仍轻微滚动(方向跟风幕一致)
            if (!hasStorm)
            {
                if (isWeed)
                {
                    col.attachedRigidbody.AddForce(
                        Vector2.right * dir * tumbleweedForce * idleScrollCoeff,
                        ForceMode2D.Force);
                }
                continue;
            }

            if (isWeed)
            {
                // 风滚草: 用 AddForce, 风力随沙尘暴强度增强
                col.attachedRigidbody.AddForce(Vector2.right * dir * tumbleweedForce * intensity, ForceMode2D.Force);
            }
            else
            {
                // 玩家: 把风力写入玩家移动脚本的 externalWindVx,
                // 由玩家移动脚本叠加到自己的水平速度上.
                // 不能直接改 rb.velocity: 玩家的移动控制器(MoveFirst/MoveLate)
                // 每帧会覆盖 velocity, 直接改会被清掉(P1)或导致一卡一卡(P2).
                float speed = col.CompareTag("Player") ? speedP1 : speedP2;
                float windVx = dir * speed * intensity;

                var rb = col.attachedRigidbody;
                var mf = rb.GetComponent<MoveFirst>();
                var ml = rb.GetComponent<MoveLate>();
                if (mf != null) mf.externalWindVx = windVx;
                if (ml != null) ml.externalWindVx = windVx;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 遮挡检测 (风幕式): 判断竖线与目标之间是否被 Ground 完全挡住.
    // 沿玩家身体高度采样 N 个 y 点, 从竖线出发沿水平方向向目标发射线,
    // 只要有一根透过(Ground 没挡住) → 玩家"被射到" → 可吹 (返回 false).
    // 全部被挡 → 完全挡死 → 不吹 (返回 true).
    // useDynamicOcclusion = false → 不检测, 直接认为可吹 (返回 false).
    // ──────────────────────────────────────────────────────────────
    private bool IsFullyBlocked(Collider2D target, int windDir)
    {
        if (!useDynamicOcclusion) return false;
        if (target == null) return false;

        // 目标碰撞体范围, 决定采样高度
        Bounds b = target.bounds;

        // 采样点: 从目标底部到顶部均匀取 N 个点(含两端), 但只保留在竖线高度内的
        for (int i = 0; i < occluderSampleCount; i++)
        {
            float t = (occluderSampleCount == 1) ? 0.5f : (float)i / (occluderSampleCount - 1);
            float sampleY = Mathf.Lerp(b.min.y, b.max.y, t);

            // 该采样点超出风幕(竖线)高度 → 这根射线不属于风幕, 跳过
            if (sampleY < LineBottomY || sampleY > LineTopY) continue;

            Vector2 origin = new Vector2(LineX, sampleY);
            Vector2 rayDir = Vector2.right * windDir;
            float targetX = windDir > 0 ? b.max.x : b.min.x;
            float dist = Mathf.Abs(targetX - LineX);
            if (dist <= 0f) continue;

            // 打全部命中, 只认带 occluderTag 的碰撞体 (Ground 墙)
            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, rayDir, dist);
            bool blockedThisRay = false;
            foreach (RaycastHit2D h in hits)
            {
                if (h.collider != null && h.collider.CompareTag(occluderTag))
                {
                    blockedThisRay = true;
                    break;
                }
            }

            // 记录到调试缓存, 供 Scene 视图绘制 (起点 → 目标边缘 x)
            if (showDebugGizmos)
            {
                debugRays.Add(new DebugRay
                {
                    start = origin,
                    end = new Vector2(targetX, sampleY),
                    blocked = blockedThisRay
                });
            }

            // 这一根射线透过(Ground 没挡) → 风能射到玩家 → 可吹
            if (!blockedThisRay) return false;
        }

        // 所有在风幕高度内的采样射线都被 Ground 挡住 → 完全挡死, 不吹
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        // 绘制竖线(风幕), 便于在 Scene 视图调整 lineHeight 和位置
        Color c = Color.red;
        c.a = 0.9f;
        Gizmos.color = c;
        Vector2 top = new Vector2(LineX, LineTopY);
        Vector2 bottom = new Vector2(LineX, LineBottomY);
        Gizmos.DrawLine(bottom, top);

        // 两端画小圆点, 更醒目
        Gizmos.DrawSphere(top, 0.15f);
        Gizmos.DrawSphere(bottom, 0.15f);

        // 绘制遮挡射线 (Update 里缓存的, 本帧计算过的)
        // 被挡的射线画黄色, 透过(能吹到)的画绿色
        foreach (DebugRay r in debugRays)
        {
            Gizmos.color = r.blocked ? Color.yellow : Color.green;
            Gizmos.DrawLine(r.start, r.end);
            // 在末端画个小圆点表示射线终点
            Gizmos.DrawSphere(r.end, 0.06f);
        }
    }
#endif
}
