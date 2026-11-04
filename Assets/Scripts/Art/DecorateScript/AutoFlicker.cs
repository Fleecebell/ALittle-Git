using UnityEngine;

// ======================================================================
// AutoFlicker —— 不规则闪烁: 随机亮度抖动 (像火焰/坏灯)
// --------------------------------------------------------------------------
// 教学点: 不像 AutoBlink 是规则 sin 波, Flicker 用 Random.Range 生成随机数
//         每 1/frequency 秒变一次亮度, 制造"忽明忽暗"的不规则感
//
//         重要: 用 Random.InitState(seed) 固定随机种子, 否则每次运行效果不一样
//         调试时想要可复现, 给每个物体不同的 seed
//
// 用途: 火焰 / 坏掉的灯 / 闪电 / 能量场不稳定 / 鬼火
// 注意: AutoBlink 是温柔规律的呼吸闪烁, AutoFlicker 是急促不规则闪烁
// ======================================================================
[RequireComponent(typeof(SpriteRenderer))]
public class AutoFlicker : MonoBehaviour
{
    [Tooltip("亮度抖动强度(0=不抖, 1=从0到2倍亮度)")]
    [Range(0f, 1f)] public float intensity = 0.3f;

    [Tooltip("抖动频率(每秒变化几次). 15 = 每秒变15次, 像火焰")]
    public float frequency = 15f;

    [Tooltip("随机种子. 同场景多个物体给不同值, 错开抖动节奏")]
    public int seed = 0;

    private SpriteRenderer sr;
    private Color baseColor;
    private float nextFlickTime;
    private float currentBrightness = 1f;

    // 保存当前 Random 状态, 防止 InitState 影响其他脚本
    private UnityEngine.Random.State prevRandomState;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        baseColor = sr.color;
        nextFlickTime = 0f;

        // 保存当前 Random 状态 -> 改种子 InitState -> 还原
        // 这样不影响其他脚本的随机数
        prevRandomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(seed);
        UnityEngine.Random.state = prevRandomState;
    }

    void Update()
    {
        // Time.time = 游戏开始后的秒数
        // 累积到下次闪烁时间了 -> 换一个新亮度
        if (Time.time >= nextFlickTime)
        {
            nextFlickTime = Time.time + 1f / frequency;
            // 亮度 = 1 ± intensity 的随机值
            currentBrightness = 1f + Random.Range(-intensity, intensity);
        }

        // 应用亮度. Unity 的 Color 没有 .rgb, 要分别乘 r/g/b (不动 a)
        Color c = baseColor;
        c.r *= currentBrightness;
        c.g *= currentBrightness;
        c.b *= currentBrightness;
        sr.color = c;
    }
}
