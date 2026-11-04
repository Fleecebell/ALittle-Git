using UnityEngine;

// ======================================================================
// WindArea —— 风幕 (一条分界线, 一侧全吹, 可被 Ground 墙实时遮挡)
// --------------------------------------------------------------------------
// 工作方式:
//   挂在一个空物体上, 空物体的位置就是"风幕"分界线的坐标.
//
//   风向为 Left/Right (左右风):
//     分界线是一条竖直的线(沿 y 延伸, 用 lineHeight 控制长度).
//     风往左吹 → 竖线左侧的玩家会被往左吹, 右侧不受影响.
//     风往右吹 → 竖线右侧的玩家会被往右吹, 左侧不受影响.
//
//   风向为 Up/Down (上下风):
//     分界线是一条水平的线(沿 x 延伸, 同样用 lineHeight 控制长度).
//     风往上吹 → 横线上方的玩家会被往上吹, 下方不受影响.
//     风往下吹 → 横线下方的玩家会被往下吹, 上方不受影响.
//
//   遮挡 (风幕式):
//     沿分界线的垂直方向(左右风沿 x, 上下风沿 y)向目标发射多根射线,
//     射线会被 Tag 为 Ground 的碰撞体(Tilemap 地面/墙)阻挡.
//     玩家只有"被射到"——即存在一条从分界线出发、未被 Ground 挡住、
//     且落在玩家身体尺寸范围内的射线穿过——才会被吹动.
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
    public enum WindDirection { Left, Right, Up, Down }

    [Header("风向")]
    [Tooltip("左右风: 分界线是竖线, 沿 x 检测遮挡, 风向侧被吹.\n" +
             "向上风: 分界线是横线, 沿 y 检测遮挡, 横线上方被往上吹.\n" +
             "向下风: 分界线是横线, 沿 y 检测遮挡, 横线下方被往下吹.")]
    [SerializeField] private WindDirection direction = WindDirection.Left;

    [Header("=== 分界线(风幕) ===")]
    [Tooltip("分界线中心在纵坐标上的偏移, 一般用物体自身 y, 不用改")]
    [SerializeField] private float lineCenterOffsetY = 0f;
    [Tooltip("分界线的长度. 左右风=竖线高度(沿 y), 上下风=横线宽度(沿 x). 从中心向两侧各延伸一半. 场景中会画出来便于观察")]
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
    [Tooltip("是否检测遮挡. 关闭 = 不检测, 分界线一侧范围内全吹(旧的整块风区行为)")]
    [SerializeField] private bool useDynamicOcclusion = true;
    [Tooltip("沿玩家身体高度采样的射线数量. 覆盖玩家从脚到头, 任一透过就吹(全部被挡才不吹)")]
    [Range(1, 8)]
    [SerializeField] private int occluderSampleCount = 3;

    [Header("=== 调试 ===")]
    [Tooltip("在 Scene 视图绘制分界线(风幕)和遮挡射线. 勾选 = 显示, 取消 = 隐藏")]
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

    // ---- 分界线几何 ----
    // 分界线中心点 (左右风: 竖线中心; 上下风: 横线中心)
    private Vector2 LineCenter => new Vector2(transform.position.x, transform.position.y + lineCenterOffsetY);
    // 分界线中心 x / y
    private float LineX => LineCenter.x;
    private float LineY => LineCenter.y;
    // 分界线长度
    private float LineLength => lineHeight;
    // 左右风: 竖线底部/顶部 y
    private float LineBottomY => LineY - LineLength * 0.5f;
    private float LineTopY => LineY + LineLength * 0.5f;
    // 上下风: 横线左端/右端 x
    private float LineLeftX => LineX - LineLength * 0.5f;
    private float LineRightX => LineX + LineLength * 0.5f;

    // 是否上下风 (分界线是横线)
    private bool IsVertical => direction == WindDirection.Up || direction == WindDirection.Down;
    // 风向: 左右风: -1=左 1=右; 上下风: -1=下 1=上 (用于射线方向和侧判断)
    private int WindDirSign =>
        direction == WindDirection.Right || direction == WindDirection.Up ? 1 : -1;

    private void Update()
    {
        // 每帧清空调试射线缓存, 只保留本帧计算过的
        debugRays.Clear();

        // 沙尘暴强度: 没沙尘暴为 0, 有则取当前强度. 没有 SandstormController(旧场景)默认 1.
        float intensity = (SandstormController.Instance != null)
            ? SandstormController.Instance.CurrentIntensity
            : 1f;
        bool hasStorm = intensity >= 0.01f;

        int dir = WindDirSign;

        // 用一个盒形检测抓取分界线两侧附近的对象.
        // 左右风: 盒沿 x 宽、沿 y 覆盖分界线高度;
        // 上下风: 盒沿 y 高、沿 x 覆盖分界线长度.
        float boxWidth = IsVertical ? LineLength : 60f;   // 上下风盒宽=横线长度, 左右风=覆盖范围
        float boxHeight = IsVertical ? 60f : LineLength;  // 左右风盒高=竖线高度, 上下风=覆盖范围
        Vector2 boxCenter = LineCenter;
        Vector2 boxSize = new Vector2(boxWidth, boxHeight);
        Collider2D[] hits = Physics2D.OverlapBoxAll(boxCenter, boxSize, 0f);

        foreach (Collider2D col in hits)
        {
            if (col == null || col.attachedRigidbody == null) continue;
            bool isPlayer = col.CompareTag("Player") || col.CompareTag("Player2");
            bool isWeed = col.CompareTag("Tumbleweed");
            if (!isPlayer && !isWeed) continue;

            // 侧判断: 目标中心在分界线的哪一侧. 上下风用 y, 左右风用 x.
            float side;
            if (IsVertical)
                side = Mathf.Sign(col.bounds.center.y - LineY);
            else
                side = Mathf.Sign(col.bounds.center.x - LineX);
            if (side != dir) continue;

            // 尺寸必须在分界线范围内 (被风幕扫到):
            // 左右风看 y 是否落在竖线高度内; 上下风看 x 是否落在横线长度内.
            if (IsVertical)
            {
                if (col.bounds.max.x < LineLeftX || col.bounds.min.x > LineRightX) continue;
            }
            else
            {
                if (col.bounds.min.y > LineTopY || col.bounds.max.y < LineBottomY) continue;
            }

            // 遮挡检测: 被 Ground 完全挡住 → 不吹 (风滚草轻微动也遵守, 被挡就不动)
            if (IsFullyBlocked(col, dir)) continue;

            // 风向单位向量 (左右风水平, 上下风垂直)
            Vector2 windDirV = IsVertical ? Vector2.up * dir : Vector2.right * dir;

            // 没沙尘暴时: 玩家完全不受影响, 但风滚草仍轻微滚动(方向跟风幕方向一致)
            if (!hasStorm)
            {
                if (isWeed)
                {
                    col.attachedRigidbody.AddForce(
                        windDirV * tumbleweedForce * idleScrollCoeff,
                        ForceMode2D.Force);
                }
                continue;
            }

            if (isWeed)
            {
                // 风滚草: 用 AddForce, 风力随沙尘暴强度增强
                col.attachedRigidbody.AddForce(windDirV * tumbleweedForce * intensity, ForceMode2D.Force);
            }
            else
            {
                // 玩家: 把风力写入玩家移动脚本的外部风字段,
                // 由玩家移动脚本叠加到自己的速度上.
                // 不能直接改 rb.velocity: 玩家的移动控制器(MoveFirst/MoveLate)
                // 每帧会覆盖 velocity, 直接改会被清掉(P1)或导致一卡一卡(P2).
                float speed = col.CompareTag("Player") ? speedP1 : speedP2;
                float windSpeed = dir * speed * intensity;

                var rb = col.attachedRigidbody;
                var mf = rb.GetComponent<MoveFirst>();
                var ml = rb.GetComponent<MoveLate>();
                if (IsVertical)
                {
                    // 上下风 → 垂直风力写入 externalWindVy
                    if (mf != null) mf.externalWindVy = windSpeed;
                    if (ml != null) ml.externalWindVy = windSpeed;
                }
                else
                {
                    // 左右风 → 水平风力写入 externalWindVx
                    if (mf != null) mf.externalWindVx = windSpeed;
                    if (ml != null) ml.externalWindVx = windSpeed;
                }
            }
        }
    }

    // ──────────────────────────────────────────────────────────────
    // 遮挡检测 (风幕式): 判断分界线与目标之间是否被 Ground 完全挡住.
    // 沿目标身体尺寸采样 N 个点, 从分界线出发沿垂直风向向目标发射线,
    // 只要有一根透过(Ground 没挡住) → 玩家"被射到" → 可吹 (返回 false).
    // 全部被挡 → 完全挡死 → 不吹 (返回 true).
    // useDynamicOcclusion = false → 不检测, 直接认为可吹 (返回 false).
    // ──────────────────────────────────────────────────────────────
    private bool IsFullyBlocked(Collider2D target, int windDir)
    {
        if (!useDynamicOcclusion) return false;
        if (target == null) return false;

        // 目标碰撞体范围, 决定采样范围
        Bounds b = target.bounds;

        // 左右风: 分界线是竖线(沿 y), 沿 x 吹, 采样点在目标的 y 方向(竖直)上取.
        // 上下风: 分界线是横线(沿 x), 沿 y 吹, 采样点在目标的 x 方向(水平)上取.
        // 采样的那一维 = 分界线延伸的方向.
        float sampleMin = IsVertical ? b.min.x : b.min.y;
        float sampleMax = IsVertical ? b.max.x : b.max.y;

        // 分界线的覆盖范围 (左右风: y 上下; 上下风: x 左右)
        float coverMin = IsVertical ? LineLeftX : LineBottomY;
        float coverMax = IsVertical ? LineRightX : LineTopY;

        // 目标靠近分界线的一侧在"吹风方向"上的坐标 (射线终点)
        float targetEdge = IsVertical
            ? (windDir > 0 ? b.max.y : b.min.y)
            : (windDir > 0 ? b.max.x : b.min.x);
        // 分界线在"吹风方向"上的起始坐标 (射线起点)
        float lineOrigin = IsVertical ? LineY : LineX;
        float dist = Mathf.Abs(targetEdge - lineOrigin);
        if (dist <= 0f) return false;

        for (int i = 0; i < occluderSampleCount; i++)
        {
            float t = (occluderSampleCount == 1) ? 0.5f : (float)i / (occluderSampleCount - 1);
            float sample = Mathf.Lerp(sampleMin, sampleMax, t);

            // 该采样点超出风幕(分界线)覆盖范围 → 这根射线不属于风幕, 跳过
            if (sample < coverMin || sample > coverMax) continue;

            // 起点: 在分界线上, 沿分界线方向取 sample 对应的坐标
            Vector2 origin = IsVertical
                ? new Vector2(sample, LineY)
                : new Vector2(LineX, sample);
            // 射线方向 = 吹风方向
            Vector2 rayDir = IsVertical ? Vector2.up * windDir : Vector2.right * windDir;
            // 终点 = 目标靠近分界线的一侧边缘
            Vector2 rayEnd = IsVertical
                ? new Vector2(sample, targetEdge)
                : new Vector2(targetEdge, sample);

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

            // 记录到调试缓存, 供 Scene 视图绘制 (起点 → 目标边缘)
            if (showDebugGizmos)
            {
                debugRays.Add(new DebugRay
                {
                    start = origin,
                    end = rayEnd,
                    blocked = blockedThisRay
                });
            }

            // 这一根射线透过(Ground 没挡) → 风能射到玩家 → 可吹
            if (!blockedThisRay) return false;
        }

        // 所有在风幕覆盖范围内的采样射线都被 Ground 挡住 → 完全挡死, 不吹
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        // 绘制分界线(风幕), 便于在 Scene 视图调整 lineHeight 和位置
        Color c = Color.red;
        c.a = 0.9f;
        Gizmos.color = c;

        if (IsVertical)
        {
            // 上下风: 横线 (左端 → 右端)
            Vector2 left = new Vector2(LineLeftX, LineY);
            Vector2 right = new Vector2(LineRightX, LineY);
            Gizmos.DrawLine(left, right);
            Gizmos.DrawSphere(left, 0.15f);
            Gizmos.DrawSphere(right, 0.15f);
        }
        else
        {
            // 左右风: 竖线 (底部 → 顶部)
            Vector2 top = new Vector2(LineX, LineTopY);
            Vector2 bottom = new Vector2(LineX, LineBottomY);
            Gizmos.DrawLine(bottom, top);
            Gizmos.DrawSphere(top, 0.15f);
            Gizmos.DrawSphere(bottom, 0.15f);
        }

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
