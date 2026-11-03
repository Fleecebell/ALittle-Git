using UnityEngine;

// ======================================================================
// AutoSway —— 摇摆: 绕 Z 轴 sin 波摆动 (像旗子/灯笼/吊坠)
// --------------------------------------------------------------------------
// 教学点: 改 localRotation, 不是 rotation. localRotation 基于父物体, 不会丢失
//         父物体本身的旋转(比如父物体是倾斜的柱子, 装饰品仍然能正确摆动).
//
//         要让物体绕"底部"摆(像草从地里长出), 在 Sprite Inspector 里
//         把 Sprite 的 Pivot 改成 Bottom(0, 0) 即可, 代码不用动.
//
// 用途: 旗子 / 灯笼 / 吊坠 / 风铃 / 装饰性摆动
// 注意: 真正的草摆动用 PlantSway.cs(支持互动+下雨), 本脚本是纯装饰摆动
// ======================================================================
public class AutoSway : MonoBehaviour
{
    [Tooltip("最大摆动角度(度). 10 = ±10度")]
    public float maxAngle = 10f;

    [Tooltip("摆动速度(每秒几圈)")]
    public float speed = 1f;

    [Tooltip("起始相位(度). 多个旗子错开摆: 0/90/180/270")]
    public float phase = 0f;

    private Quaternion baseRot;

    void Start()
    {
        // 记录用户在 Inspector 设置的初始旋转, 之后摆动在这基础上叠加
        baseRot = transform.localRotation;
    }

    void Update()
    {
        float angle = Mathf.Sin(Time.time * speed * Mathf.PI * 2f + phase * Mathf.Deg2Rad) * maxAngle;

        // 用 baseRot * Quaternion.Euler 叠加, 不用 transform.Rotate 累积(避免漂移)
        transform.localRotation = baseRot * Quaternion.Euler(0f, 0f, angle);
    }
}
