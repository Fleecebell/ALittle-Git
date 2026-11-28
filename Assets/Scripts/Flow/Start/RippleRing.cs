using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一圈“空心圆环”的 UI 图形（用网格直接画，不需要贴图）。
/// 半径变大时线宽保持固定，所以看起来是一直变大的细线涟漪，而不是被吹大的气球。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RippleRing : MaskableGraphic
{
    [SerializeField] private float radius = 50f;      // 外半径（UI 单位）
    [SerializeField] private float thickness = 6f;    // 线宽（UI 单位）
    [SerializeField] private int segments = 64;       // 圆的分段，越高越圆

    public void SetRing(float outerRadius, float lineWidth, int seg = 64)
    {
        radius = Mathf.Max(0.01f, outerRadius);
        thickness = Mathf.Max(0.01f, lineWidth);
        segments = Mathf.Clamp(seg, 8, 180);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        float outer = Mathf.Max(0.01f, radius);
        float inner = Mathf.Max(0f, outer - Mathf.Max(0.01f, thickness));
        int seg = Mathf.Clamp(segments, 8, 180);

        Color32 col = color;

        for (int i = 0; i <= seg; i++)
        {
            float a = (i / (float)seg) * Mathf.PI * 2f;
            float cos = Mathf.Cos(a);
            float sin = Mathf.Sin(a);

            vh.AddVert(new Vector3(cos * inner, sin * inner, 0f), col, Vector2.zero);
            vh.AddVert(new Vector3(cos * outer, sin * outer, 0f), col, Vector2.one);
        }

        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            vh.AddTriangle(b, b + 1, b + 3);
            vh.AddTriangle(b, b + 3, b + 2);
        }
    }
}
