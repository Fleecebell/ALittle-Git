using UnityEngine;

// ======================================================================
// AutoSpin —— 自转: 绕 Z 轴匀速旋转
// --------------------------------------------------------------------------
// 教学点: 最简单的 Auto 组件. 一个参数 speed, Update 里调用 transform.Rotate.
//
// 用途: 风车 / 齿轮 / 光环 / 魔法阵 / 旋转的太阳
// 叠加示例: AutoSpin + AutoPulse = 旋转的能量核心
// 要"绕着别的物体转圈" → 用 AutoOrbit (它的"朝向跟随"开关=跟着转的效果)
// ======================================================================
public class AutoSpin : MonoBehaviour
{
    [Tooltip("旋转速度(度/秒). 正=顺时针, 负=逆时针. 90 = 每4秒转1圈")]
    public float speed = 90f;

    [Tooltip("旋转轴(本地空间). 默认绕 Z 轴(2D 平面旋转)")]
    public Vector3 axis = new Vector3(0, 0, 1);

    void Update()
    {
        // Time.deltaTime = 上一帧到这一帧的时间(秒)
        // 乘上 deltaTime 的作用: 让旋转速度按"度/秒"算, 不受帧率影响
        transform.Rotate(axis, speed * Time.deltaTime, Space.Self);
    }
}
