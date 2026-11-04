using UnityEngine;

// ======================================================================
// AutoOrbit —— 公转绕圈: 绕一个中心点做圆周运动 (朝向跟不跟着转可选)
// --------------------------------------------------------------------------
// 教学点: 三角函数应用. cos(angle) = X 偏移, sin(angle) = Y 偏移.
//         "朝向跟随" = 把同一个 angle 写进 rotation.z —— 效果像一根
//         看不见的杆把物体焊在中心上转; 不勾则朝向保持不变(船舱式).
//
// 用途: 行星绕恒星 / 精灵环绕主角 / 能量球围绕敌人 / 飘动的宝物 /
//       绕固定点转动的指针/风车装饰 (勾"朝向跟随")
// 注意: center 为空时, 默认用物体自身 Start 时的位置当中心;
//       center 运动的话圆心不会跟 (Start 时缓存了位置) —— 需要再说
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

    [Tooltip("朝向跟随 (勾=像一根杆焊在中心上转, 自身朝向跟着轨道角走; 不勾=朝向保持不变)")]
    public bool faceAround = false;

    private Vector3 centerPos;
    private float angle;
    private float facingOffset;   // 初始朝向与轨道角的角度差

    void Start()
    {
        // 没指定 center 就用自己当前的位置当圆心(原地绕圈)
        centerPos = (center != null) ? center.position : transform.position;
        angle = startAngle;

        // 记录"初始朝向 - 轨道角"的差值; 朝向跟随时保持这个差值,
        // 不管物体一开始摆成什么角度, 那根"杆"的相对角度都锁死
        facingOffset = transform.eulerAngles.z - angle;
    }

    void Update()
    {
        angle += speed * Time.deltaTime;

        // Mathf.Deg2Rad = π/180, 把度换算成弧度(三角函数要弧度)
        float rad = angle * Mathf.Deg2Rad;

        // 圆周运动公式: P = center + (cos*r, sin*r)
        Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;
        transform.position = centerPos + offset;

        // 朝向跟随: 同一个角度写进 rotation.z, 物体相对中心的角度差恒定
        if (faceAround)
        {
            transform.rotation = Quaternion.Euler(0f, 0f, angle + facingOffset);
        }
    }
}
