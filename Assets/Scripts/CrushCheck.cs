using UnityEngine;

/// <summary>
/// 四向夹缝检测: 玩家被两堵墙 (tag=Ground) 真正挤压时才判定死亡.
///
/// 判定逻辑:
///   向玩家四个方向各发一条射线, 测出"墙到玩家对应边缘"的距离.
///   当某一对相对方向的墙同时存在, 且两者缝隙被压到小于 crushThreshold 时,
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

    [Tooltip("射线探测距离: 从玩家中心向外探测墙的距离, 需覆盖到最远的那堵墙. 一般1~3足够")]
    public float rayLength = 2f;

    [Tooltip("只认这个 tag 的物体为墙, 其余(玩家等)忽略")]
    public string wallTag = "Ground";

    [Tooltip("调试: 在场景视图中画出四条探测射线")]
    public bool showDebug = false;

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
        Vector2 pos = transform.position;

        // 水平方向
        float dLeft = RaycastDistance(pos, Vector2.left);
        float dRight = RaycastDistance(pos, Vector2.right);
        if (dLeft > 0f && dRight > 0f && dLeft + dRight < crushThreshold)
            return true;

        // 垂直方向
        float dDown = RaycastDistance(pos, Vector2.down);
        float dUp = RaycastDistance(pos, Vector2.up);
        if (dDown > 0f && dUp > 0f && dDown + dUp < crushThreshold)
            return true;

        return false;
    }

    /// <summary>
    /// 从 origin 朝 direction 发一条射线, 返回最近 Ground 墙到玩家对应边缘的距离.
    /// 没有检测到墙返回 -1 (表示这一侧没有阻挡).
    /// </summary>
    private float RaycastDistance(Vector2 origin, Vector2 direction)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, rayLength);

        // 玩家体宽/体高 0.97, 半长为 0.485
        const float halfSize = 0.485f;

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null) continue;

            // 只认墙 (tag = Ground), 忽略玩家/其他物体
            if (!hit.collider.CompareTag(wallTag)) continue;

            // 射线命中点到玩家对应边缘的距离 = 命中距离 - 玩家半边长
            // (玩家是正方形, 边长0.97, 半长0.485)
            float distance = hit.distance - halfSize;
            if (distance < 0f) distance = 0f;

            if (showDebug)
                Debug.DrawLine(origin, hit.point, Color.red, Time.deltaTime);

            return distance;
        }

        return -1f;
    }
}
