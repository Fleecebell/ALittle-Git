using UnityEngine;

/// <summary>
/// 四向夹缝检测: 玩家被两堵墙 (tag=Ground) 真正挤压时才判定死亡.
///
/// 判定逻辑:
///   每个方向 (左/右/上/下) 各发 3 条射线: 分别从该方向两条边端点的延长线和中间发射,
///   用于检测玩家被墙挤压的任意位置 (含边角).
///   每条线从玩家中心位置(带垂直于主方向的偏移)沿主方向发射, 测出该偏移高度/宽度处到墙的距离.
///   对水平方向, 同一边角高度上的 "中心到左墙 + 中心到右墙" 即该高度处两堵墙之间的距离.
///   当某对相对方向的墙同时存在, 且任一位置的两墙距离被压到小于 crushThreshold 时 -> 死亡.
///
///   关键: 测的是"两堵墙之间的距离"(中心线从中心向两侧测), 而不是"墙到玩家边缘的距离".
///   这样玩家穿过一个刚好容纳自己的通道时, 两墙距离 = 玩家边长(0.97),
///   只要 0.97 >= crushThreshold 就不会误判死亡; 只有两墙缝隙真的被压到小于阈值才死.
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
    [Tooltip("相对方向的两堵墙之间的距离(缝隙)小于此值才判定被夹死. 玩家边长是0.97, 建议小于0.97. 越小越难夹死(挤得越狠才死)")]
    public float crushThreshold = 0.6f;

    [Tooltip("射线探测距离: 从玩家边缘向外探测墙的距离, 需覆盖到最远的那堵墙. 一般1~3足够")]
    public float rayLength = 2f;

    [Tooltip("只认这个 tag 的物体为墙, 其余(玩家等)忽略")]
    public string wallTag = "Ground";

    [Tooltip("调试: 在场景视图中画出全部探测射线")]
    public bool showDebug = false;

    // 玩家正方形边长 0.97, 半长为 0.485
    private const float halfSize = 0.485f;

    // 每方向 3 条线的偏移: 沿垂直于主方向的位置. 水平方向对应 下/中/上; 垂直方向对应 左/中/右.
    private readonly float[] offsets = { -halfSize, 0f, halfSize };

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
    /// 水平/垂直各检查 3 个位置(边角+中间), 任一位置两墙距离过窄即算被夹住.
    /// </summary>
    private bool IsCrushed()
    {
        // ---- 水平方向: 检查 下/中/上 三个高度处 左右两墙的距离 ----
        foreach (float offset in offsets)
        {
            float dLeft = RaycastToWall(Vector2.left, offset);
            float dRight = RaycastToWall(Vector2.right, offset);
            if (dLeft > 0f && dRight > 0f && dLeft + dRight < crushThreshold)
                return true;
        }

        // ---- 垂直方向: 检查 左/中/右 三个宽度处 上下两墙的距离 ----
        foreach (float offset in offsets)
        {
            float dDown = RaycastToWall(Vector2.down, offset);
            float dUp = RaycastToWall(Vector2.up, offset);
            if (dDown > 0f && dUp > 0f && dDown + dUp < crushThreshold)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 从玩家中心沿 direction 发一条射线, 起点在垂直于 direction 的方向上偏移 offset.
    /// offset = -half 取该方向边的一端, 0 取中间, +half 取另一端.
    /// 返回该偏移位置处, 沿 direction 到最近 Ground 墙的距离. 没检测到墙返回 -1.
    /// </summary>
    private float RaycastToWall(Vector2 direction, float offset)
    {
        // 垂直于 direction 的方向 (顺时针旋转90度)
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        Vector2 origin = (Vector2)transform.position + perpendicular * offset;

        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, rayLength);

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null) continue;

            // 只认墙 (tag = Ground), 忽略玩家/其他物体
            if (!hit.collider.CompareTag(wallTag)) continue;

            return hit.distance;
        }

        return -1f;
    }

    // ===================== 调试绘制 =====================
    /// <summary>
    /// 用 Gizmos 在 Scene 视图绘制全部探测射线.
    /// 每个方向 3 条, 绿色=命中 Ground 墙, 红色=未命中(画到最大探测距离).
    /// 勾选 showDebug 后, 确认 Scene 视图右上角 Gizmos 图标已开启.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!showDebug) return;

        DrawDir(Vector2.left);
        DrawDir(Vector2.right);
        DrawDir(Vector2.down);
        DrawDir(Vector2.up);
    }

    private void DrawDir(Vector2 direction)
    {
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);

        foreach (float offset in offsets)
        {
            Vector2 origin = (Vector2)transform.position + perpendicular * offset;
            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, rayLength);
            bool found = false;

            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == null) continue;
                if (!hit.collider.CompareTag(wallTag)) continue;

                Gizmos.color = Color.green;
                Gizmos.DrawLine(origin, hit.point);
                found = true;
                break;
            }

            if (!found)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(origin, origin + direction * rayLength);
            }
        }
    }
}
