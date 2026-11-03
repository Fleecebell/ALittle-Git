using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro; // TextMeshPro 命名空间

// ======================================================================
// BouncyButton —— Q弹按钮: 多层结构 + 悬停颜色变化 + Q弹压缩 + 按下下沉
// --------------------------------------------------------------------------
// 已实现:
//   第1步: 三层结构(边框/背板/阴影) + 悬停颜色变化(边框+文字)
//   第2步: Q弹压缩(进入先快速膨胀一下, 再压扁成悬停形状)
//   第3步: 按下下沉(鼠标按下时边框+背板+文字向下沉, 松开回弹)
//   第4步: 点击冲击(瞬间缩小再Q弹回弹)
//   第5步: 点击光圈(从点击点扩散淡出; 放在 Back 的 Mask 下, 被裁成背板形状)
//   第6步: 悬停随机倾斜(绕Z轴小角度倾斜, 松开回正)
//
// 光圈裁剪原理(简单可靠):
//   Back(背板) 挂一个 Mask 组件(勾选 Show Mask Graphic, 背板本身正常显示),
//   光圈 Ripple 放到 Back 下面 —— Mask 只裁"Back 下面的子物体",
//   所以只有光圈被裁剪成背板形状(含圆角, 倾斜/下沉/压缩全天然跟随),
//   而阴影/边框/文字是 Back 的同级, 完全不受影响。
//
// 结构要求(Unity Hierarchy):
//   Button(挂本脚本)
//     ├─ Shadow    (Image, 阴影层, 按下时不沉)
//     ├─ Back      (Image, 背板层, 按下时下沉; 挂 Mask 组件)
//     │   └─ Ripple  (Image 圆圈, 点击光圈, 被裁剪在背板内)
//     ├─ Frame     (Image, 外边框层, 按下时下沉)
//     └─ Title     (TMP_Text, 按钮文字)
//
// 下沉原理: 按下时 frameImage/backplateImage/titleText 的 localPosition.y 减小(向下移动)
//          松开时回到初始位置, 阴影不动(制造"按钮被按下"的立体感)
// ======================================================================
public class BouncyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                                                IPointerDownHandler, IPointerUpHandler
{
    [Header("=== 三层结构引用 ===")]
    [Tooltip("外边框 Image(最上层, 显示边框颜色). 拖拽 Frame 子对象到这")]
    public Image frameImage;
    [Tooltip("背板 Image(中间层, 按钮主体背景, 挂 Mask 组件当光圈蒙版). 拖拽 Backplate 子对象到这")]
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

    [Header("=== 第2步: Q弹压缩(进入先膨胀, 再压扁成悬停形状) ===")]
    [Tooltip("正常缩放(默认 1,1 即原大小). 通常不用改")]
    public Vector2 normalScale = new Vector2(1f, 1f);
    [Tooltip("悬停时缩放: Y 减小(压扁), X 增大(变宽). 例如 (1.1, 0.92) = 宽10% 扁8%")]
    public Vector2 hoverScale = new Vector2(1.1f, 0.92f);
    [Tooltip("进入按钮时先快速膨胀到这个峰值(制造Q弹的duang感), 之后过渡回 hoverScale 悬停形状. 设得比 hoverScale 各分量都大, 膨胀感才明显")]
    public Vector2 hoverPopScale = new Vector2(1.15f, 1.08f);
    [Tooltip("膨胀时间: 进入后用多少秒从当前大小膨胀到 hoverPopScale 峰值. 0.08 = 快速duang一下")]
    public float hoverPopInDuration = 0.08f;
    [Tooltip("回缩时间: 到达峰值后用多少秒缩回 hoverScale 悬停形状. 0.15 = 稍慢一点回落, 更Q弹")]
    public float hoverPopOutDuration = 0.15f;
    [Tooltip("缩放过渡速度(越大越快). 15 = 快速Q弹, 5 = 慢速柔软")]
    public float scaleTransitionSpeed = 15f;

    [Header("=== 第3步: 按下下沉(边框+背板+文字下沉) ===")]
    [Tooltip("按下时下沉距离(正值=向下沉), 边框/背板/文字一起沉同样的高度. 8 = 下沉8单位, 制造按钮被按下去的感觉")]
    public float pressSinkDistance = 8f;
    [Tooltip("下沉/回弹过渡速度(越大越快). 30 = 快速按下回弹, 10 = 慢速松软")]
    public float pressTransitionSpeed = 30f;
    [Tooltip("按下时是否暂停Q弹压缩(避免下沉+压扁叠加看起来乱). 勾选=按下时只下沉不压扁")]
    public bool pauseSquishOnPress = true;

    [Header("=== 第4步: 点击缩放立体感(瞬间变小再回弹) ===")]
    [Tooltip("点击瞬间整体缩小到这个比例(0.9 = 缩小10%). 制造'被压进去'的立体感")]
    [Range(0.5f, 1f)] public float clickPunchScale = 0.9f;
    [Tooltip("快速缩放过渡速度(点击冲击/悬停膨胀阶段用, 越大越快). 20 = 快速Q弹感, 8 = 慢速松软")]
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

    [Header("=== 第5步: 点击光圈(Back 挂 Mask, 光圈放 Back 下面) ===")]
    [Tooltip("光圈 Image(圆形 Sprite). 必须放在挂了 Mask 的 Back(背板)下面才会被裁剪在按钮内")]
    public Image rippleImage;
    [Tooltip("光圈 CanvasGroup(可选). 配了就用 CanvasGroup 控制透明度, 不配则改用 Image.color, 都能正常工作")]
    public CanvasGroup rippleCanvasGroup;
    [Tooltip("光圈初始直径 = 按钮高度 × 这个值. 1 = 初始光圈和按钮一样高(触摸点大小), 想起点更大就调大")]
    public float rippleStartRatio = 1f;
    [Tooltip("光圈最终直径 = 按钮高度 × 这个值. 扩散的终点; 想让光圈铺满整个按钮, 把它设成 按钮宽度÷高度(比如宽按钮设 7)")]
    public float rippleMaxRatio = 7f;
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
    // 悬停膨胀阶段: 0=无, 1=膨胀中(当前→峰值), 2=回缩中(峰值→悬停形状)
    private int hoverPopPhase = 0;
    // 当前膨胀/回缩段已经过的时间(秒)
    private float hoverPopTimer = 0f;
    // 当前膨胀/回缩段的起点缩放(进入瞬间的 currentScale / 刚到达峰值时的 currentScale)
    private Vector2 hoverPopFromScale;
    // 当前倾斜角度(度, 绕 Z 轴, 用于平滑过渡)
    private float currentTilt = 0f;
    // 目标倾斜角度(进入时随机生成, 离开时归零)
    private float targetTilt = 0f;
    // 光圈扩散计时器(>0 表示光圈正在扩散, 到期淡出)
    private float rippleTimer = 0f;
    // 光圈初始 localScale(让初始直径=按钮高×rippleStartRatio, 扩散时在这基础上乘倍率)
    private Vector3 rippleInitialScale;
    // 光圈初始直径是否已适配成功(画布布局未驱动完成时会失败, 下次点击自动重试)
    private bool rippleFitted = false;
    // 按钮初始 localScale(用户在 Inspector 设置的, 脚本不覆写, 在这基础上叠加倍率)
    private Vector3 baseScale;
    // 当前实际边框颜色(用于平滑过渡)
    private Color currentFrameColor;
    // 当前实际文字颜色
    private Color currentTextColor;
    // 当前实际缩放(用于平滑过渡)
    private Vector2 currentScale;

    // 边框/背板/文字的初始 localPosition(用于下沉时基于此偏移)
    private Vector3 frameInitialLocalPos;
    private Vector3 backplateInitialLocalPos;
    private Vector3 titleInitialLocalPos;
    // 当前边框/背板/文字的 localPosition(按下=下沉, 松开=回原位)
    private Vector3 currentFramePos;
    private Vector3 currentBackplatePos;
    private Vector3 currentTitlePos;

    void Start()
    {
        // 初始化颜色和缩放为正常状态
        currentFrameColor = normalFrameColor;
        currentTextColor = normalTextColor;
        currentScale = normalScale;

        // 记录用户在 Inspector 设置的初始 scale 作为基础大小
        // 之后所有悬停/按下/点击的缩放都是在这基础上叠加倍率, 不覆写用户设的大小
        baseScale = transform.localScale;

        // 记录边框/背板/文字的初始位置(下沉基于这个位置偏移)
        if (frameImage != null) frameInitialLocalPos = frameImage.transform.localPosition;
        if (backplateImage != null) backplateInitialLocalPos = backplateImage.transform.localPosition;
        if (titleText != null) titleInitialLocalPos = titleText.transform.localPosition;
        currentFramePos = frameInitialLocalPos;
        currentBackplatePos = backplateInitialLocalPos;
        currentTitlePos = titleInitialLocalPos;

        // 初始化光圈: 隐藏 + 不拦截鼠标 + 把初始直径适配成 按钮高×rippleStartRatio
        if (rippleImage != null)
        {
            rippleInitialScale = rippleImage.transform.localScale;
            // 光圈只是视觉特效, 不拦截鼠标(否则扩散时会挡住按钮的点击)
            rippleImage.raycastTarget = false;
            // 设置光圈颜色(只设一次, 后续淡出优先用 CanvasGroup 控制)
            rippleImage.color = rippleColor;
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
        // 适配初始大小(若画布布局尚未驱动完成则失败, 下次点击时会自动重试)
        rippleFitted = FitRippleToButton();

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

        // === 点击冲击计时器递减 ===
        if (punchTimer > 0f)
        {
            punchTimer -= Time.deltaTime;
            if (punchTimer < 0f) punchTimer = 0f;
        }

        // === 悬停膨胀/回缩(两段精确时长插值: 当前→峰值用 hoverPopInDuration, 峰值→悬停形状用 hoverPopOutDuration) ===
        bool hoverPopDriving = false;
        if (hoverPopPhase > 0 && isHovered && punchTimer <= 0f)
        {
            hoverPopDriving = true;
            hoverPopTimer += Time.deltaTime;
            if (hoverPopPhase == 1)
            {
                // 第1段: 从进入瞬间的缩放膨胀到 hoverPopScale 峰值
                float t = Mathf.Clamp01(hoverPopTimer / Mathf.Max(hoverPopInDuration, 0.0001f));
                currentScale = Vector2.Lerp(hoverPopFromScale, hoverPopScale, SmoothEase(t));
                if (t >= 1f)
                {
                    hoverPopPhase = 2;
                    hoverPopTimer = 0f;
                    hoverPopFromScale = currentScale;
                }
            }
            else
            {
                // 第2段: 从峰值缩回 hoverScale 悬停形状
                float t = Mathf.Clamp01(hoverPopTimer / Mathf.Max(hoverPopOutDuration, 0.0001f));
                currentScale = Vector2.Lerp(hoverPopFromScale, hoverScale, SmoothEase(t));
                if (t >= 1f) hoverPopPhase = 0;
            }
        }

        // === 平滑过渡缩放(Q弹压缩 + 点击冲击; 膨胀/回缩期间由上面的插值接管, 跳过) ===
        if (!hoverPopDriving)
        {
            // 优先级: 点击冲击 > 按下 > 悬停形状 > 正常
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
        }

        // === 平滑过渡边框/背板/文字位置(按下下沉) ===
        // 目标位置 = 初始位置 + 下沉偏移(按下时 Y 减小下沉, 松开时回原位)
        Vector3 targetFramePos = frameInitialLocalPos;
        Vector3 targetBackplatePos = backplateInitialLocalPos;
        Vector3 targetTitlePos = titleInitialLocalPos;
        if (isPressed)
        {
            // 按下: 边框/背板/文字一起向下沉(pressSinkDistance)
            targetFramePos.y = frameInitialLocalPos.y - pressSinkDistance;
            targetBackplatePos.y = backplateInitialLocalPos.y - pressSinkDistance;
            targetTitlePos.y = titleInitialLocalPos.y - pressSinkDistance;
        }
        float posStep = pressTransitionSpeed * Time.deltaTime;
        currentFramePos = MoveVector3Towards(currentFramePos, targetFramePos, posStep);
        currentBackplatePos = MoveVector3Towards(currentBackplatePos, targetBackplatePos, posStep);
        currentTitlePos = MoveVector3Towards(currentTitlePos, targetTitlePos, posStep);

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

    // 把光圈的初始 localScale 换算成 "初始直径 = 按钮高度 × rippleStartRatio"
    // 按光圈当前实际世界尺寸等比缩放, 所以无论 Rect/Sprite/Scale 在 Inspector 里设成什么样都正确
    // 返回是否成功(画布布局尚未驱动完成时按钮高度为 0, 返回 false 等下次点击重试)
    bool FitRippleToButton()
    {
        if (rippleImage == null) return false;

        // 按钮当前的世界高度(Overlay 画布即 Canvas 像素空间)
        Vector3[] corners = new Vector3[4];
        ((RectTransform)transform).GetWorldCorners(corners);
        float buttonHeight = Mathf.Abs(corners[2].y - corners[0].y);
        if (buttonHeight <= 0.001f) return false; // 画布布局还没就绪

        // 光圈当前的世界尺寸 → 缩放到目标直径(按当前实际大小换算, 重复调用也正确)
        rippleImage.rectTransform.GetWorldCorners(corners);
        float currentSize = Mathf.Max(Mathf.Abs(corners[2].x - corners[0].x),
                                      Mathf.Abs(corners[2].y - corners[0].y));
        if (currentSize <= 0.001f) return false;

        rippleInitialScale = rippleImage.transform.localScale * ((buttonHeight * rippleStartRatio) / currentSize);
        return true;
    }

    // 光圈扩散动画: 计时器递减, 直径从 按钮高×startRatio 平滑扩到 按钮高×maxRatio, alpha 从 1 到 0
    // 裁剪由 Back 上的 Mask 组件完成, 脚本只管大小/位置/淡出
    void UpdateRipple()
    {
        if (rippleImage == null) return;
        if (rippleTimer <= 0f) return;

        rippleTimer -= Time.deltaTime;
        if (rippleTimer < 0f) rippleTimer = 0f;

        // 进度 0(刚点击) → 1(扩散结束)
        float progress = 1f - (rippleTimer / rippleDuration);
        // 用 smoothstep 让扩散先快后慢(更自然)
        progress = progress * progress * (3f - 2f * progress);

        // 直径倍率: 从 rippleStartRatio 平滑过渡到 rippleMaxRatio
        float diameter = Mathf.Lerp(rippleStartRatio, rippleMaxRatio, progress);
        rippleImage.transform.localScale = rippleInitialScale * (diameter / rippleStartRatio);
        // 当前 alpha: 从 1 淡出到 0(再乘用户设置的光圈颜色透明度)
        float currentAlpha = rippleColor.a * Mathf.Lerp(1f, 0f, progress);

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

        // 扩散结束, 隐藏光圈
        if (rippleTimer <= 0f)
        {
            if (rippleCanvasGroup != null) rippleCanvasGroup.alpha = 0f;
            else { Color endColor = rippleColor; endColor.a = 0f; rippleImage.color = endColor; }
        }
    }

    // smoothstep 缓动(先快后慢), t ∈ [0,1]
    float SmoothEase(float t)
    {
        return t * t * (3f - 2f * t);
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

    // 把当前位置应用到边框/背板/文字(下沉效果)
    void ApplyPressPos()
    {
        if (frameImage != null) frameImage.transform.localPosition = currentFramePos;
        if (backplateImage != null) backplateImage.transform.localPosition = currentBackplatePos;
        if (titleText != null) titleText.transform.localPosition = currentTitlePos;
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
        // 启动"膨胀过渡"第1段: 从当前缩放膨胀到峰值(第2段自动缩回悬停形状)
        hoverPopPhase = 1;
        hoverPopTimer = 0f;
        hoverPopFromScale = currentScale;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        // 鼠标离开时也算松开(防止按住后拖出按钮卡在按下状态)
        isPressed = false;
        // 离开时倾斜归零, 并取消还没播完的膨胀/回缩(直接平滑回原大小)
        targetTilt = 0f;
        hoverPopPhase = 0;
    }

    // 鼠标按下瞬间触发
    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
        // 点击冲击优先: 打断还没播完的膨胀/回缩
        hoverPopPhase = 0;
        // 触发点击冲击缩放: 启动 punchDuration 计时器, 期间强制缩小制造立体感
        punchTimer = punchDuration;
        // 触发光圈扩散: 显示光圈, 放到点击位置, 启动计时器
        TriggerRipple(eventData);
    }

    // 触发光圈扩散(从点击位置开始)
    // 光圈会被 Back 上的 Mask 裁剪在背板范围内, 这里只负责定位/大小/显示
    void TriggerRipple(PointerEventData eventData)
    {
        if (rippleImage == null) return;

        // 初始直径还没适配成功(比如 Start 时画布布局未就绪), 点击时再试一次
        if (!rippleFitted) rippleFitted = FitRippleToButton();

        // 把光圈放到点击位置: 先在按钮矩形空间算出局部点, 再换算到光圈父级(Back)的局部空间
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera, out Vector2 localPos))
        {
            Vector3 worldPoint = transform.TransformPoint(new Vector3(localPos.x, localPos.y, 0f));
            Transform rippleParent = rippleImage.transform.parent;
            rippleImage.transform.localPosition = rippleParent != null
                ? rippleParent.InverseTransformPoint(worldPoint)
                : worldPoint;
        }
        rippleImage.transform.localScale = rippleInitialScale;

        // 显示光圈(alpha 从这帧开始 1 → 0 淡出)
        if (rippleCanvasGroup != null)
        {
            rippleCanvasGroup.alpha = 1f;
        }
        else
        {
            rippleImage.color = rippleColor;
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
