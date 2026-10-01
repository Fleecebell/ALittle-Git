using UnityEngine;

// ======================================================================
// AutoScale —— 呼吸缩放: 大小循环变化
// --------------------------------------------------------------------------
// 教学点: sin 波从 -1~1 映射到 0~1, 用 Lerp 在 min/max 之间插值
//         baseScale 必须在 Start 里记一次, 不能每帧从 transform.localScale 读,
//         否则会让效果叠加错误 (因为 scale 被自己改了)
//
// 用途: 心脏跳动 / 光环呼吸 / 警示标记脉冲 / 装饰品缓慢缩放
// 注意: 不要在同一个物体上同时挂 AutoScale + AutoPulse, 两者都改 localScale 会冲突
// ======================================================================
public class AutoScale : MonoBehaviour
{
    [Tooltip("最小缩放倍数(相对初始大小). 0.9 = 缩到90%")]
    public float minScale = 0.9f;

    [Tooltip("最大缩放倍数. 1.1 = 放大到110%")]
    public float maxScale = 1.1f;

    [Tooltip("呼吸速度(每秒呼吸几圈). 1 = 每秒一次完整呼吸")]
    public float speed = 1f;

    [Tooltip("起始相位(度). 同场景多个物体错开呼吸节奏用")]
    public float phase = 0f;

    private Vector3 baseScale;

    void Start()
    {
        // 记录用户在 Inspector 里设置的初始大小, 之后所有缩放都在这基础上叠加
        baseScale = transform.localScale;
    }

    void Update()
    {
        // Time.time = 游戏开始到现在的秒数
        // Mathf.PI * 2 = 2π, 让 speed=1 时正好每秒 1 圈
        // (sin + 1) * 0.5: sin 范围 -1~1 -> 加 1 -> 0~2 -> 乘 0.5 -> 0~1
        float t = 0.5f * (1f + Mathf.Sin(Time.time * speed * Mathf.PI * 2f + phase * Mathf.Deg2Rad));

        // Lerp(a, b, t) = a + (b-a)*t, 在 a/b 之间按 t 插值
        float s = Mathf.Lerp(minScale, maxScale, t);
        transform.localScale = baseScale * s;
    }
}
