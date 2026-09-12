using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro; // TextMeshPro 命名空间

// 兼容 SpriteRenderer 光圈模式: 避免 UGUI Mask 的 Stencil 污染导致屏幕闪烁
// 用 SpriteRenderer + 2D Sprite Mask 实现"剪贴蒙版"效果, 完全脱离 UGUI Canvas

// ======================================================================
// BouncyButton —— Q弹按钮: 多层结构 + 悬停颜色变化 + Q弹压缩 + 按下下沉
// --------------------------------------------------------------------------
// 已实现:
//   第1步: 三层结构(边框/背板/阴影) + 悬停颜色变化(边框+文字)
//   第2步: Q弹压缩(悬停时 Y方向压缩 + X方向膨胀)
//   第3步: 按下下沉(鼠标按下时边框+背板向下沉, 松开回弹)
//
// 后续步骤: 缩放立体感 / 光圈扩散 / 随机倾斜
//
// 结构要求(Unity Hierarchy):
//   Button(挂本脚本)
//     ├─ Shadow    (Image, 阴影层, 按下时不沉)
//     ├─ Backplate (Image, 背板层, 按下时下沉)
//     ├─ Frame     (Image, 外边框层, 按下时下沉)
//     └─ Title     (TMP_Text, 按钮文字)
//
// 下沉原理: 按下时 frameImage 和 backplateImage 的 localPosition.y 减小(向下移动)
//          松开时回到初始位置, 阴影不动(制造"按钮被按下"的立体感)
// ======================================================================
public class BouncyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                                                IPointerDownHandler, IPointerUpHandler
{
    [Header("=== 三层结构引用 ===")]
    [Tooltip("外边框 Image(最上层, 显示边框颜色). 拖拽 Frame 子对象到这")]
    public Image frameImage;
    [Tooltip("背板 Image(中间层, 按钮主体背景). 拖拽 Backplate 子对象到这")]
    public Image backplateImage;
    [Tooltip("阴影 Image(最下层, 按钮投影). 拖拽 Shadow 子对象到这")]
    public Image shadowImage;
    [Tooltip("按钮文字(TMP_Text). 兼容 TextMeshPro. 拖拽 Title 子对象到这")]
    public TMP_Text titleText;

    [Header("=== 边框颜色(悬停变化) ===")]
    [Tooltip("正常状态边框颜色")]
    public Color normalFrameColor = new Color(1f, 0.85f, 0.4f, 1f); // 暖黄
    [Tooltip("悬停状态边框颜色")]
    public Color hoverFrameColor = new Color(1f, 1f, 0.6f, 1f);      // 亮黄
    [Tooltip("颜色过渡速度(越大越快). 10 = 快速平滑过渡")]
    public float colorTransitionSpeed = 10f;

    [Header("=== 文字颜色(悬停变化) ===")]
    [Tooltip("正常状态文字颜色")]
    public Color normalTextColor = new Color(0.2f, 0.15f, 0.1f, 1f); // 深棕
    [Tooltip("悬停状态文字颜色")]
    public Color hoverTextColor = new Color(0.1f, 0.05f, 0f, 1f);    // 更深

    [Header("=== 第2步: Q弹压缩(悬停时变形) ===")]
    [Tooltip("正常缩放(默认 1,1 即原大小). 通常不用改")]
    public Vector2 normalScale = new Vector2(1f, 1f);
    [Tooltip("悬停时缩放: Y 减小(压扁), X 增大(变宽). 例如 (1.1, 0.92) = 宽10% 扁8%")]
    public Vector2 hoverScale = new Vector2(1.1f, 0.92f);
    [Tooltip("缩放过渡速度(越大越快). 15 = 快速Q弹, 5 = 慢速柔软")]
    public float scaleTransitionSpeed = 15f;

    [Header("=== 第3步: 按下下沉(边框+背板下沉) ===")]
    [Tooltip("按下时下沉距离(正值=向下沉). 8 = 下沉8单位, 制造按钮被按下去的感觉")]
    public float pressSinkDistance = 8f;
    [Tooltip("下沉/回弹过渡速度(越大越快). 30 = 快速按下回弹, 10 = 慢速松软")]
    public float pressTransitionSpeed = 30f;
    [Tooltip("按下时是否暂停Q弹压缩(避免下沉+压扁叠加看起来乱). 勾选=按下时只下沉不压扁")]
    public bool pauseSquishOnPress = true;

    [Header("=== 第4步: 点击缩放立体感(瞬间变小再回弹) ===")]
    [Tooltip("点击瞬间整体缩小到这个比例(0.9 = 缩小10%). 制造'被压进去'的立体感")]
    [Range(0.5f, 1f)] public float clickPunchScale = 0.9f;
    [Tooltip("缩放冲击回弹速度(越大回弹越快). 20 = 快速回弹Q弹感, 8 = 慢速松软")]
    public float punchRecoverSpeed = 20f;
    [Tooltip("缩放冲击影响 X 和 Y 的比例(通常和 clickPunchScale 一致). 想只缩小Y改这里")]
    public float clickPunchScaleY = 0.9f;

    [Header("=== 第6步: 进入随机倾斜(悬停时绕Z轴倾斜) ===")]
    [Tooltip("进入按钮时随机倾斜的最大角度(度). 5 = 倾斜 ±5度, 制造活泼感")]
    public float randomTiltAngle = 5f;
    [Tooltip("倾斜过渡速度(越大越快). 15 = 快速倾斜到位, 5 = 慢速柔软")]
    public float tiltTransitionSpeed = 15f;
    [Tooltip("按下时是否取消倾斜(按下时回正, 松开恢复倾斜). 勾选避免下沉+倾斜叠加乱")]
    public bool pauseTiltOnPress = true;

    [Header("=== 第5步: 点击光圈扩散(从点击位置向外扩散) ===")]
    [Tooltip("光圈 Image(UGUI 模式, 一个圆形 Sprite 的 Image). 点击时显示, 从小扩散到大并淡出. 拖拽 Ripple 子对象到这")]
    public Image rippleImage;
    [Tooltip("光圈 CanvasGroup(推荐配合 Mask 用). 用 CanvasGroup 控制 alpha 而不是 Image.color, 避免每帧改 color 触发 Canvas Stencil 重绘闪烁. 拖拽 Ripple 上的 CanvasGroup 到这")]
    public CanvasGroup rippleCanvasGroup;
    [Tooltip("光圈 SpriteRenderer(2D 模式, 推荐用这个避免 UGUI Mask 闪烁). 拖拽 Ripple2D 子对象到这. 二选一: 优先用 rippleRenderer")]
    public SpriteRenderer rippleRenderer;
    [Tooltip("光圈最终缩放(相对初始大小). 3 = 放大到3倍后消失")]
    public float rippleMaxScale = 3f;
    [Tooltip("光圈扩散持续时间(秒). 0.4 = 0.4秒扩散完并淡出")]
    public float rippleDuration = 0.4f;
    [Tooltip("光圈颜色. 建议亮黄/白配淡黄边框")]
    public Color rippleColor = new Color(1f, 1f, 0.6f, 1f);

    // 当前是否悬停(鼠标在按钮上)
    private bool isHovered = false;
    // 当前是否按下(鼠标按住按钮)
    private bool isPressed = false;
    // 点击冲击缩放计时器(>0 表示处于冲击中, 期间强制缩小, 到期回弹)
    private float punchTimer = 0f;
    // 点击冲击持续时间(秒, 期间保持缩小状态)
    private float punchDuration = 0.08f;
    // 当前倾斜角度(度, 绕 Z 轴, 用于平滑过渡)
    private float currentTilt = 0f;
    // 目标倾斜角度(进入时随机生成, 离开时归零)
    private float targetTilt = 0f;
    // 光圈扩散计时器(>0 表示光圈正在扩散, 到期淡出)
    private float rippleTimer = 0f;
    // 光圈初始 localScale(从0开始扩散, 记录以便平滑)
    private Vector3 rippleInitialScale;
    // 光圈 SpriteRenderer 模式的初始 scale
    private Vector3 rippleRendererInitialScale;
    // 按钮初始 localScale(用户在 Inspector 设置的, 脚本不覆写, 在这基础上叠加倍率)
    private Vector3 baseScale;
    // 当前实际边框颜色(用于平滑过渡)
    private Color currentFrameColor;
    // 当前实际文字颜色
    private Color currentTextColor;
    // 当前实际缩放(用于平滑过渡)
    private Vector2 currentScale;

    // 边框和背板的初始 localPosition(用于下沉时基于此偏移)
    private Vector3 frameInitialLocalPos;
    private Vector3 backplateInitialLocalPos;
    // 当前边框和背板的目标 localPosition(按下=下沉, 松开=回原位)
    private Vector3 currentFramePos;
    private Vector3 currentBackplatePos;

    void Start()
    {
        // 初始化颜色和缩放为正常状态
        currentFrameColor = normalFrameColor;
        currentTextColor = normalTextColor;
        currentScale = normalScale;

        // 记录用户在 Inspector 设置的初始 scale 作为基础大小
        // 之后所有悬停/按下/点击的缩放都是在这基础上叠加倍率, 不覆写用户设的大小
        baseScale = transform.localScale;

        // 记录边框和背板的初始位置(下沉基于这个位置偏移)
        if (frameImage != null) frameInitialLocalPos = frameImage.transform.localPosition;
        if (backplateImage != null) backplateInitialLocalPos = backplateImage.transform.localPosition;
        currentFramePos = frameInitialLocalPos;
        currentBackplatePos = backplateInitialLocalPos;

        // 初始化光圈: 用 CanvasGroup alpha=0 隐藏(不改 Image.color, 避免每帧触发 Canvas Stencil 重绘闪烁)
        if (rippleImage != null)
        {
            rippleInitialScale = rippleImage.transform.localScale;
            // 设置光圈颜色(只设一次, 后续不再改 Image.color, 减少闪烁)
            rippleImage.color = rippleColor;
            // 用 CanvasGroup 控制 alpha(如果没配 CanvasGroup 就回退到改 Image.color)
            if (rippleCanvasGroup != null)
            {
                rippleCanvasGroup.alpha = 0f;
                rippleCanvasGroup.blocksRaycasts = false; // 光圈不拦截鼠标
            }
            else
            {
                Color hideColor = rippleColor;
                hideColor.a = 0f;
                rippleImage.color = hideColor;
            }
        }
        // SpriteRenderer 模式初始化: 隐藏, 记录初始 scale
        if (rippleRenderer != null)
        {
            rippleRendererInitialScale = rippleRenderer.transform.localScale;
            Color hideColor = rippleColor;
            hideColor.a = 0f;
            rippleRenderer.color = hideColor;
        }

        ApplyColors();
        ApplyScale();
        ApplyPressPos();
    }

    void Update()
    {
        // === 平滑过渡颜色 ===
        Color targetFrame = isHovered ? hoverFrameColor : normalFrameColor;
        Color targetText = isHovered ? hoverTextColor : normalTextColor;
        float colorStep = colorTransitionSpeed * Time.deltaTime;
        currentFrameColor = MoveColorTowards(currentFrameColor, targetFrame, colorStep);
        currentTextColor = MoveColorTowards(currentTextColor, targetText, colorStep);

        // === 点击冲击缩放计时器递减 ===
        if (punchTimer > 0f)
        {
            punchTimer -= Time.deltaTime;
            if (punchTimer < 0f) punchTimer = 0f;
        }

        // === 平滑过渡缩放(Q弹压缩 + 点击冲击) ===
        // 优先级: 点击冲击 > 按下 > 悬停 > 正常
        Vector2 targetScale;
        if (punchTimer > 0f)
        {
            // 冲击期间: 强制缩小到 clickPunchScale(制造瞬间被压扁的立体感)
            targetScale = new Vector2(clickPunchScale, clickPunchScaleY);
        }
        else if (isPressed && pauseSquishOnPress)
        {
            targetScale = normalScale; // 按下时不压扁, 只下沉
        }
        else
        {
            targetScale = isHovered ? hoverScale : normalScale;
        }

        // 冲击期间用更快的速度回弹(让缩小→回弹过程更Q弹)
        float scaleStep = (punchTimer > 0f ? punchRecoverSpeed : scaleTransitionSpeed) * Time.deltaTime;
        currentScale = MoveVector2Towards(currentScale, targetScale, scaleStep);

        // === 平滑过渡边框/背板位置(按下下沉) ===
        // 目标位置 = 初始位置 + 下沉偏移(按下时 Y 减小下沉, 松开时回原位)
        Vector3 targetFramePos = frameInitialLocalPos;
        Vector3 targetBackplatePos = backplateInitialLocalPos;
        if (isPressed)
        {
            // 按下: 边框和背板都向下沉(pressSinkDistance)
            targetFramePos.y = frameInitialLocalPos.y - pressSinkDistance;
            targetBackplatePos.y = backplateInitialLocalPos.y - pressSinkDistance;
        }
        float posStep = pressTransitionSpeed * Time.deltaTime;
        currentFramePos = MoveVector3Towards(currentFramePos, targetFramePos, posStep);
        currentBackplatePos = MoveVector3Towards(currentBackplatePos, targetBackplatePos, posStep);

        // === 平滑过渡倾斜角度(随机倾斜) ===
        // 按下时如果 pauseTiltOnPress 勾选, 则归零(避免下沉+倾斜叠加看起来乱)
        float currentTargetTilt = targetTilt;
        if (isPressed && pauseTiltOnPress) currentTargetTilt = 0f;
        float tiltStep = tiltTransitionSpeed * Time.deltaTime;
        currentTilt = Mathf.MoveTowards(currentTilt, currentTargetTilt, tiltStep);

        ApplyColors();
        ApplyScale();
        ApplyPressPos();
        ApplyTilt();
        UpdateRipple();
    }

    // 光圈扩散动画: 计时器递减, scale 从 1 到 rippleMaxScale, alpha 从 1 到 0
    // UGUI 模式优先用 CanvasGroup 控制 alpha(避免改 Image.color 触发 Canvas Stencil 重绘闪烁)
    void UpdateRipple()
    {
        bool hasImage = rippleImage != null;
        bool hasRenderer = rippleRenderer != null;
        if (!hasImage && !hasRenderer) return;
        if (rippleTimer <= 0f) return;

        rippleTimer -= Time.deltaTime;
        if (rippleTimer < 0f) rippleTimer = 0f;

        // 进度 0(刚点击) → 1(扩散结束)
        float progress = 1f - (rippleTimer / rippleDuration);
        // 用 smoothstep 让扩散先快后慢(更自然)
        progress = progress * progress * (3f - 2f * progress);

        // 缩放因子: 从 1 到 rippleMaxScale
        float currentScaleFactor = Mathf.Lerp(1f, rippleMaxScale, progress);
        // 当前 alpha: 从 1 淡出到 0
        float currentAlpha = Mathf.Lerp(1f, 0f, progress);

        // 应用到 UGUI Image 模式
        if (hasImage)
        {
            rippleImage.transform.localScale = rippleInitialScale * currentScaleFactor;
            // 优先用 CanvasGroup 控制 alpha(不每帧改 Image.color, 避免触发 Canvas Stencil 重绘闪烁)
            if (rippleCanvasGroup != null)
            {
                rippleCanvasGroup.alpha = currentAlpha;
            }
            else
            {
                Color c = rippleColor;
                c.a = currentAlpha;
                rippleImage.color = c;
            }
        }
        // 应用到 SpriteRenderer 模式(优先, 不依赖 UGUI Mask)
        if (hasRenderer)
        {
            Color c = rippleColor;
            c.a = currentAlpha;
            rippleRenderer.transform.localScale = rippleRendererInitialScale * currentScaleFactor;
            rippleRenderer.color = c;
        }

        // 扩散结束, alpha 归零隐藏
        if (rippleTimer <= 0f)
        {
            if (hasImage)
            {
                if (rippleCanvasGroup != null) rippleCanvasGroup.alpha = 0f;
                else { Color endColor = rippleColor; endColor.a = 0f; rippleImage.color = endColor; }
            }
            if (hasRenderer) { Color endColor = rippleColor; endColor.a = 0f; rippleRenderer.color = endColor; }
        }
    }

    // 逐通道(RGBA)用 Mathf.MoveTowards 过渡颜色
    Color MoveColorTowards(Color current, Color target, float maxDelta)
    {
        current.r = Mathf.MoveTowards(current.r, target.r, maxDelta);
        current.g = Mathf.MoveTowards(current.g, target.g, maxDelta);
        current.b = Mathf.MoveTowards(current.b, target.b, maxDelta);
        current.a = Mathf.MoveTowards(current.a, target.a, maxDelta);
        return current;
    }

    // Vector2 用 Mathf.MoveTowards 逐分量过渡
    Vector2 MoveVector2Towards(Vector2 current, Vector2 target, float maxDelta)
    {
        current.x = Mathf.MoveTowards(current.x, target.x, maxDelta);
        current.y = Mathf.MoveTowards(current.y, target.y, maxDelta);
        return current;
    }

    // Vector3 用 Mathf.MoveTowards 逐分量过渡(用于位置下沉)
    Vector3 MoveVector3Towards(Vector3 current, Vector3 target, float maxDelta)
    {
        current.x = Mathf.MoveTowards(current.x, target.x, maxDelta);
        current.y = Mathf.MoveTowards(current.y, target.y, maxDelta);
        current.z = Mathf.MoveTowards(current.z, target.z, maxDelta);
        return current;
    }

    // 把当前颜色应用到 Image 和文字
    void ApplyColors()
    {
        if (frameImage != null) frameImage.color = currentFrameColor;
        if (titleText != null) titleText.color = currentTextColor;
    }

    // 把当前缩放应用到 transform
    // 注意: 在 baseScale(用户设置的大小) 上叠加倍率, 不覆写用户的 Scale
    void ApplyScale()
    {
        transform.localScale = new Vector3(
            baseScale.x * currentScale.x,
            baseScale.y * currentScale.y,
            baseScale.z);
    }

    // 把当前位置应用到边框和背板(下沉效果)
    void ApplyPressPos()
    {
        if (frameImage != null) frameImage.transform.localPosition = currentFramePos;
        if (backplateImage != null) backplateImage.transform.localPosition = currentBackplatePos;
    }

    // 把当前倾斜角度应用到 transform(绕 Z 轴旋转, 2D 视觉倾斜)
    void ApplyTilt()
    {
        transform.localEulerAngles = new Vector3(0f, 0f, currentTilt);
    }

    // === 鼠标事件 ===
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        // 进入时随机生成倾斜角度(±randomTiltAngle 度)
        targetTilt = Random.Range(-randomTiltAngle, randomTiltAngle);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        // 鼠标离开时也算松开(防止按住后拖出按钮卡在按下状态)
        isPressed = false;
        // 离开时倾斜归零
        targetTilt = 0f;
    }

    // 鼠标按下瞬间触发
    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
        // 触发点击冲击缩放: 启动 punchDuration 计时器, 期间强制缩小制造立体感
        punchTimer = punchDuration;
        // 触发光圈扩散: 显示光圈, 放到点击位置, 启动计时器
        TriggerRipple(eventData);
    }

    // 触发光圈扩散(从点击位置开始)
    // 同时支持 UGUI Image 和 SpriteRenderer
    void TriggerRipple(PointerEventData eventData)
    {
        // UGUI Image 模式: 把光圈放到点击位置(按钮内 localPosition)
        if (rippleImage != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera, out Vector2 localPos);
            rippleImage.transform.localPosition = localPos;
            rippleImage.transform.localScale = rippleInitialScale;
            // 优先用 CanvasGroup 控制 alpha(不改 Image.color, 避免闪烁)
            if (rippleCanvasGroup != null)
            {
                rippleCanvasGroup.alpha = 1f;
            }
            else
            {
                rippleImage.color = rippleColor;
            }
        }
        // SpriteRenderer 模式: 把光圈放到点击位置(世界坐标)
        if (rippleRenderer != null)
        {
            // 把屏幕坐标转换成世界坐标(z 用 SpriteRenderer 的 z)
            Camera cam = eventData.pressEventCamera;
            if (cam != null)
            {
                Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(eventData.position.x, eventData.position.y, cam.transform.position.z - rippleRenderer.transform.position.z));
                rippleRenderer.transform.position = new Vector3(worldPos.x, worldPos.y, rippleRenderer.transform.position.z);
            }
            rippleRenderer.transform.localScale = rippleRendererInitialScale;
            rippleRenderer.color = rippleColor;
        }
        rippleTimer = rippleDuration;
    }

    // 鼠标松开瞬间触发
    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        // 松开时也重置冲击计时器(让缩放回弹)
        punchTimer = 0f;
    }
}
