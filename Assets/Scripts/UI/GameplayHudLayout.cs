using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas), typeof(CanvasScaler))]
public sealed class GameplayHudLayout : MonoBehaviour
{
    [FormerlySerializedAs("topRightButtons")]
    [SerializeField, Tooltip("按各自的左上或右上锚点贴合关卡画框")]
    private RectTransform[] topCornerItems = System.Array.Empty<RectTransform>();
    private Canvas hudCanvas;
    private CanvasScaler scaler;
    private (bool left, Vector2 offset, Vector3 scale)[] authored;
    private Rect lastFrame;
    private float lastScale;

    private void Awake()
    {
        hudCanvas = GetComponent<Canvas>();
        scaler = GetComponent<CanvasScaler>();
        authored = new (bool, Vector2, Vector3)[topCornerItems.Length];
        for (int i = 0; i < topCornerItems.Length; i++)
        {
            RectTransform item = topCornerItems[i];
            if (item) authored[i] = (item.anchorMax.x < 0.5f, item.anchoredPosition, item.localScale);
        }
    }

    internal static Rect NormalizedPlayArea()
    {
        float width = Screen.width;
        float height = Screen.height;
        if (width <= 0f || height <= 0f) return new Rect(0f, 0f, 1f, 1f);

        // 横屏构图以高度为基准。9101 的画框内部为 16:9，占相机高度的 90%。
        // 窄屏会裁掉地图两侧；只在实际可见且避开刘海的范围内布置按钮。
        float frameHeight = height * 0.9f;
        float frameWidth = frameHeight * (16f / 9f);
        float left = (width - frameWidth) * 0.5f;
        float bottom = (height - frameHeight) * 0.5f;
        Rect safe = Screen.safeArea;
        return Rect.MinMaxRect(
            Mathf.Max(safe.xMin, left) / width,
            Mathf.Max(safe.yMin, bottom) / height,
            Mathf.Min(safe.xMax, width - left) / width,
            Mathf.Min(safe.yMax, height - bottom) / height);
    }

    private void LateUpdate()
    {
        float width = Screen.width;
        float height = Screen.height;
        if (width <= 0f || height <= 0f) return;

        // SetCanvas 的原始偏移和尺寸以电脑画面为基准。超宽屏两侧多出的区域不属于画框。
        Vector2 reference = scaler.referenceResolution;
        float pixelsPerUnit = Mathf.Min(width / reference.x, height / reference.y);
        float sideMargin = (width - reference.x * pixelsPerUnit) * 0.5f;
        Rect safe = Screen.safeArea;
        Rect frame = Rect.MinMaxRect(Mathf.Max(sideMargin, safe.xMin) / width, safe.yMin / height,
            Mathf.Min(width - sideMargin, safe.xMax) / width, safe.yMax / height);
        float scale = pixelsPerUnit / hudCanvas.scaleFactor;
        if (frame == lastFrame && Mathf.Approximately(scale, lastScale)) return;
        lastFrame = frame;
        lastScale = scale;

        for (int i = 0; i < topCornerItems.Length; i++)
        {
            RectTransform item = topCornerItems[i];
            if (!item) continue;
            var layout = authored[i];
            item.anchorMin = item.anchorMax = new Vector2(layout.left ? frame.xMin : frame.xMax, frame.yMax);
            item.localScale = layout.scale * scale;
            Vector2 offset = layout.offset;
            offset.x = layout.left
                ? Mathf.Max(offset.x, -item.rect.xMin * layout.scale.x)
                : Mathf.Min(offset.x, -item.rect.xMax * layout.scale.x);
            offset.y = Mathf.Min(offset.y, -item.rect.yMax * layout.scale.y);
            item.anchoredPosition = offset * scale;
        }
    }
}
