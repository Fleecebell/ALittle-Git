using UnityEngine;

// ======================================================================
// AutoWave —— 波浪运动: 单个 sprite 沿水平方向移动 + 垂直 sin 波动
// --------------------------------------------------------------------------
// 教学点: 把 AutoDrift(水平 PingPong) + AutoBob(垂直 sin) 组合在一个脚本里
//         真正的"sprite 内部像素扭曲"(像水面波纹)要 Shader 做出 UV 偏移,
//         那个用 DecoratePattern.shader 调 _ScrollSpeed + _PatternScale 实现
//
// 用途: 水面浮叶 / 漂浮的小船 / 海面装饰物
// 注意: 多个 sprite 排成一线, 给每个不同的 phase, 整体像波浪传播
//       真正的水面纹理扭曲用 DecoratePattern.shader
// ======================================================================
[RequireComponent(typeof(SpriteRenderer))]
public class AutoWave : MonoBehaviour
{
    [Tooltip("水平移动速度(0=不水平移动)")]
    public float xSpeed = 0.5f;

    [Tooltip("垂直波浪幅度")]
    public float yAmplitude = 0.1f;

    [Tooltip("波浪频率(每秒几圈)")]
    public float yFrequency = 2f;

    [Tooltip("水平移动范围(到边反弹)")]
    public float xRange = 5f;

    [Tooltip("波浪起始相位(度). 多个 sprite 排成一线错开用")]
    public float phase = 0f;

    private Vector3 basePos;
    private float t;

    void Start()
    {
        basePos = transform.localPosition;
    }

    void Update()
    {
        // 水平: PingPong 在 ±xRange 之间反弹
        t += xSpeed * Time.deltaTime;
        float xOffset = Mathf.PingPong(t, xRange * 2f) - xRange;

        // 垂直: sin 波在 ±yAmplitude 之间波动
        float yOffset = Mathf.Sin(Time.time * yFrequency * Mathf.PI * 2f + phase * Mathf.Deg2Rad) * yAmplitude;

        Vector3 p = basePos;
        p.x += xOffset;
        p.y += yOffset;
        transform.localPosition = p;
    }
}
