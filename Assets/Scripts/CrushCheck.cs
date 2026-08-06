using UnityEngine;

/// <summary>
/// 四向夹缝检测: 玩家被两堵墙 (tag=Ground) 真正挤压时才判定死亡.
///
/// 判定逻辑:
///   每个方向 (左/右/上/下) 各发 3 条射线: 分别在角色该方向边的两个端点延长线和中间.
///   用每条线的"墙到玩家边缘距离"代表该位置的缝隙宽度.
///   当某一对相对方向的墙同时存在, 且两者中间线的缝隙被压到小于 crushThreshold 时,
///   认为玩家被夹扁 -> 触发死亡.
///
/// 死亡触发: 设置 MoveLate.isR = true (静态), 与按 R 完全一致.
/// 因为 DeathRespawnVFX.TriggerDeath() 统一处理 P1+P2, 所以任意角色被夹死,
/// 两个角色都会一起走死亡重生流程.
///
/// 挂载: 同时挂在 P1 和 P2 两个角色上.
/// </summary>
public class CrushCheck : MonoBehaviour
{
    [Header("=== 夹缝阈值 ===")]
    [Tooltip("相对方向的两堵墙之间缝隙小于此值才判定被夹死. 玩家边长是0.97, 建议小于0.97. 越小越难夹死(挤得越狠才死)")]
    public float crushThreshold = 0.6f;

    [Tooltip("射线探测距离: 从玩家边缘向外探测墙的距离, 需覆盖到最远的那堵墙. 一般1~3足够")]
    public float rayLength = -0.1f;

    [Tooltip("只认这个 tag 的物体为墙, 其余(玩家等)忽略")]
    public string wallTag = "Ground";

    [Tooltip("调试: 在场景视图中画出全部探测射线")]
    public bool showDebug = false;

    // 玩家正方形边长 0.97, 半长为 0.485
    private const float halfSize = 0.485f;

    private void Update()
    {
        // 死亡/重生动画播放期间不检测, 避免刚重生又被夹判定
        if (DeathRespawnVFX.isDead) return;

        if (IsCrushed())
        {
            // 与按 R 效果一致: 交给 MoveLate.CheckReset 下一帧触发 TriggerDeath()
            MoveLate.isR = true;
        }
    }

    /// <summary>
    /// 判断玩家是否被真正挤压 (任一对相对方向的墙缝隙过窄即算).
    /// </summary>
    private bool IsCrushed()
    {
        // ---- 水平方向: 左 3 条, 右 3 条 ----
        // 缝隙宽度 = 中间线距离 (左中 + 右中), 因为中间线测的是玩家中心高度的真实缝隙.
        float dLeftMid = RaycastDistance(Vector2.left, 0f);
        float dRightMid = RaycastDistance(Vector2.right, 0f);
        if (dLeftMid > 0f && dRightMid > 0f && dLeftMid + dRightMid < crushThreshold)
            return true;

        // ---- 垂直方向: 下 3 条, 上 3 条 ----
        float dDownMid = RaycastDistance(Vector2.down, 0f);
        float dUpMid = RaycastDistance(Vector2.up, 0f);
        if (dDownMid > 0f && dUpMid > 0f && dDownMid + dUpMid < crushThreshold)
            return true;

        return false;
    }

    /// <summary>
    /// 沿 direction 方向发一条射线, 起点在角色边缘并带有垂直于主方向的偏移.
    /// offset = -halfSize 取该方向边的下端/左端, 0 取中间, +halfSize 取上端/右端.
    /// 返回最近 Ground 墙到玩家对应边缘的距离, 没检测到墙返回 -1.
    /// </summary>
    private float RaycastDistance(Vector2 direction, float offset)
    {
        // 主方向上的边缘起点 (从中心偏移半边长到对应边)
        Vector2 origin = (Vector2)transform.position + direction * halfSize;
        // 垂直方向偏移 (offset 沿垂直于 direction 的方向)
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        origin += perpendicular * offset;

        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, rayLength);

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null) continue;

            // 只认墙 (tag = Ground), 忽略玩家/其他物体
            if (!hit.collider.CompareTag(wallTag)) continue;

            // 起点已在玩家边缘, hit.distance 即缝隙宽度
            float distance = hit.distance;
            if (distance < 0f) distance = 0f;

            return distance;
        }

        return -1f;
    }

    // ===================== 调试绘制 =====================
    /// <summary>
    /// 用 Gizmos 在 Scene 视图绘制全部探测射线, 保证 showDebug 一定能看到.
    /// 勾选 Inspector 的 showDebug 后, 在 Scene 视图右上角 Gizmos 图标里确认没有关闭.
    /// 绿色=检测到墙(有效), 红色=未检测到墙/被忽略.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!showDebug) return;

        // 每个方向 3 条: 下端/左端(-half), 中间(0), 上端/右端(+half)
        float[] offsets = { -halfSize, 0f, halfSize };

        DrawDir(Vector2.left, offsets);
        DrawDir(Vector2.right, offsets);
        DrawDir(Vector2.down, offsets);
        DrawDir(Vector2.up, offsets);
    }

    private void DrawDir(Vector2 direction, float[] offsets)
    {
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);

        foreach (float offset in offsets)
        {
            Vector2 origin = (Vector2)transform.position + direction * halfSize + perpendicular * offset;
            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, rayLength);
            bool found = false;

            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == null) continue;
                if (!hit.collider.CompareTag(wallTag)) continue;

                // 命中 Ground 墙
                Gizmos.color = Color.green;
                Gizmos.DrawLine(origin, hit.point);
                found = true;
                break;
            }

            if (!found)
            {
                // 这一侧没有 Ground 墙, 画红色线到最大探测距离
                Gizmos.color = Color.red;
                Gizmos.DrawLine(origin, origin + direction * rayLength);
            }
        }
    }
}
