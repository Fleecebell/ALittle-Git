using UnityEngine;

// ======================================================================
// AutoBlink —— 闪烁: 透明度循环变化
// --------------------------------------------------------------------------
// 教学点: 只动 color.a 一个属性, 不动 RGB.
//         新手常犯的错: 直接改 color.a 忘了先把 color 整体保存, 把 RGB 也覆盖了.
//         正确做法: Start 里记 baseColor, 每帧 c = baseColor; c.a = ...
//
// 用途: 星星眨眼 / 警示灯节奏 / 屏幕装饰闪烁
// 注意: 想做"不规则闪烁"用 AutoFlicker, 本组件是规则 sin 波
// ======================================================================
[RequireComponent(typeof(SpriteRenderer))]
public class AutoBlink : MonoBehaviour
{
    [Tooltip("最小透明度")]
    [Range(0f, 1f)] public float minAlpha = 0.3f;

    [Tooltip("最大透明度")]
    [Range(0f, 1f)] public float maxAlpha = 1f;

    [Tooltip("闪烁速度(每秒几圈)")]
    public float speed = 2f;

    private SpriteRenderer sr;
    private Color baseColor;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        baseColor = sr.color;
    }

    void Update()
    {
        float t = 0.5f * (1f + Mathf.Sin(Time.time * speed * Mathf.PI * 2f));

        // 复制 baseColor 再改 alpha, 不要直接写 sr.color.a = ...
        // 否则如果同时别的脚本改了 RGB 会冲突
        Color c = baseColor;
        c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
        sr.color = c;
    }
}
