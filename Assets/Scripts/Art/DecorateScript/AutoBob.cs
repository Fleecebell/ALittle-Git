using UnityEngine;

// ======================================================================
// AutoBob —— 上下浮动: sin 波垂直运动
// --------------------------------------------------------------------------
// 教学点: 改 localPosition.y, 不是 position.y. localPosition 基于父物体, 安全.
//         如果物体是某个移动平台/镜头的子物体, 改 position 会被父物体的移动覆盖.
//
// 用途: 气泡上升 / 浮空岛 / 幽灵飘 / 水母游动 / 装饰性浮空元素
// 注意: 想做水平飘移用 AutoDrift; 想做波浪用 AutoWave
// ======================================================================
public class AutoBob : MonoBehaviour
{
    [Tooltip("浮动幅度(世界单位). 0.2 = 上下各 0.2 格")]
    public float amplitude = 0.2f;

    [Tooltip("浮动速度(每秒几圈)")]
    public float speed = 1f;

    [Tooltip("起始相位(度). 多个物体错开排布: 0/90/180/270 形成此起彼伏")]
    public float phase = 0f;

    private Vector3 basePos;

    void Start()
    {
        // 记录初始本地位置, 之后浮动都基于这个位置偏移
        basePos = transform.localPosition;
    }

    void Update()
    {
        // sin 波 0~0~0 振荡幅度, 让 y 在 basePos.y ± amplitude 之间波动
        float offset = Mathf.Sin(Time.time * speed * Mathf.PI * 2f + phase * Mathf.Deg2Rad) * amplitude;

        Vector3 p = basePos;
        p.y += offset;
        transform.localPosition = p;
    }
}
