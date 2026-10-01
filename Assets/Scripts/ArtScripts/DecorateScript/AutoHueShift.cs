using UnityEngine;

// ======================================================================
// AutoHueShift —— 色相循环: HSV 色相旋转, 遍历彩虹色
// --------------------------------------------------------------------------
// 教学点: Color.HSVToRGB(h, s, v) 是 Unity 内置 HSV→RGB 函数, 不用自己写
//         hue 0~1 = 红→黄→绿→青→蓝→紫→红 一圈
//
// 用途: 彩虹 / 霓虹灯 / 魔法特效 / 变色装饰
// 注意: 色相循环不保留原色, 整个 sprite 会变彩虹. 想保留原色亮度变化用 AutoPulse
// ======================================================================
[RequireComponent(typeof(SpriteRenderer))]
public class AutoHueShift : MonoBehaviour
{
    [Tooltip("循环速度(1 = 每秒一圈彩虹)")]
    public float speed = 0.2f;

    [Tooltip("饱和度(0=灰, 1=纯色)")]
    [Range(0f, 1f)] public float saturation = 1f;

    [Tooltip("明度(0=黑, 1=纯, >1 不会更亮, 用 AutoPulse 才会发光)")]
    public float value = 1f;

    private SpriteRenderer sr;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        // Time.time * speed 一直增长, % 1f 取小数部分, 让 hue 循环 0~1
        float hue = (Time.time * speed) % 1f;

        // HSVToRGB 返回 0~1 范围的 RGB, 直接赋给 color
        Color c = Color.HSVToRGB(hue, saturation, value);
        // 保留原 alpha 不被覆盖(原 sprite 可能是半透明)
        c.a = sr.color.a;
        sr.color = c;
    }
}
