using UnityEngine;

// ======================================================================
// AutoOrbit —— 轨道环绕: 绕一个中心点做圆周运动
// --------------------------------------------------------------------------
// 教学点: 三角函数应用. cos(angle) = X 偏移, sin(angle) = Y 偏移
//         越往后越会用到这个套路 (AutoBob / AutoWave 也是 sin/cos)
//
// 用途: 行星绕恒星 / 精灵环绕主角 / 能量球围绕敌人 / 飘动的宝物
// 注意: center 为空时, 默认用物体自身 Start 时的位置当中心
// ======================================================================
public class AutoOrbit : MonoBehaviour
{
    [Tooltip("环绕中心(世界坐标). 不填=用自身初始位置当中心")]
    public Transform center;

    [Tooltip("轨道半径(世界单位)")]
    public float radius = 1.5f;

    [Tooltip("角速度(度/秒). 正=顺时针, 负=逆时针. 90 = 每4秒1圈")]
    public float speed = 90f;

    [Tooltip("起始相位(度). 多个物体错开排布时设不同相位, 例如 0/90/180/270")]
    public float startAngle = 0f;

    private Vector3 centerPos;
    private float angle;

    void Start()
    {
        // 没指定 center 就用自己当前的位置当圆心(原地绕圈)
        centerPos = (center != null) ? center.position : transform.position;
        angle = startAngle;
    }

    void Update()
    {
        angle += speed * Time.deltaTime;

        // Mathf.Deg2Rad = π/180, 把度换算成弧度(三角函数要弧度)
        float rad = angle * Mathf.Deg2Rad;

        // 圆周运动公式: P = center + (cos*r, sin*r)
        Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;
        transform.position = centerPos + offset;
    }
}
