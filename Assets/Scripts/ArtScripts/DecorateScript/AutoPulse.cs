using UnityEngine;

// ======================================================================
// AutoPulse —— 脉冲发光: 大小+亮度同步脉冲
// --------------------------------------------------------------------------
// 教学点: 同时改两个属性(scale + color). 这种"组合脉冲"是发光特效的套路.
//         SpriteRenderer.color 的 RGB 可以 >1, 用线性加法叠加发光感(配合 Bloom)
//
// 用途: 能量核心 / 传送门 / 宝箱光晕 / 收集品发光
// 注意: 要看 HDR/Bloom 才能看出亮度超过 1 的发光效果
//       没挂 Bloom 时, 亮度 >1 会显得"过曝变白", 也算一种风格
// ======================================================================
[RequireComponent(typeof(SpriteRenderer))]
public class AutoPulse : MonoBehaviour
{
    [Tooltip("亮度最小值(0=黑, 1=原色, 1.5=加亮 50%)")]
    public float minBrightness = 0.5f;

    [Tooltip("亮度最大值")]
    public float maxBrightness = 1.5f;

    [Tooltip("缩放幅度(0=不缩放, 0.1=±10% 缩放)")]
    public float scaleAmplitude = 0.1f;

    [Tooltip("脉冲速度(每秒几圈)")]
    public float speed = 1f;

    private SpriteRenderer sr;
    private Vector3 baseScale;
    private Color baseColor;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
        baseColor = sr.color;
    }

    void Update()
    {
        float t = 0.5f * (1f + Mathf.Sin(Time.time * speed * Mathf.PI * 2f));

        // 亮度: 在 minBrightness 和 maxBrightness 之间插值
        // Unity 的 Color 没有 .rgb, 要分别乘 r/g/b (不动 a)
        float brightness = Mathf.Lerp(minBrightness, maxBrightness, t);
        Color c = baseColor;
        c.r *= brightness;
        c.g *= brightness;
        c.b *= brightness;
        sr.color = c;

        // 缩放: 让 t=0.5 时缩放为 1(原大小), t=1 放大, t=0 缩小
        // (t - 0.5) * 2: 把 0~1 映射到 -1~1
        float s = 1f + scaleAmplitude * (t - 0.5f) * 2f;
        transform.localScale = baseScale * s;
    }
}
