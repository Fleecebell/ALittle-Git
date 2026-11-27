using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Image（UI）的“水里漂浮”效果：
/// 1. 平时自己在整个 Canvas 范围内慢慢飘（漫游），碰到边缘会反弹；
/// 2. 被鼠标点击、或者每隔 N 秒（可调，0 = 关闭），会被推一下，并向上跳一下；
/// 3. 位置和旋转都只有阻尼减速，不会回到原位/原角度。
/// 所有速度都按画面大小自动换算，换分辨率/Canvas 缩放后手感一致。
/// </summary>
public class ObjectFloat : MonoBehaviour
{
    [Header("漂浮的物体（拖入 Image，可多个）")]
    public List<Image> objects = new List<Image>();

    [Header("阻尼（只减速，不回原位）")]
    [Tooltip("越大停得越快，越小飘得越久。1~2 比较像水里")]
    public float damping = 1.2f;

    [Tooltip("最大速度（相对值），防止被推得太猛飞出屏幕")]
    public float maxSpeed = 3f;

    [Header("全屏漫游")]
    [Tooltip("平时的漫游速度（相对值），0 = 不动，只被动推动")]
    public float wanderSpeed = 0.6f;

    [Tooltip("隔多久换一次漫游方向（秒，区间内随机）")]
    public Vector2 wanderChangeTime = new Vector2(2f, 5f);

    [Tooltip("限制在 Canvas 范围内，碰到边缘反弹")]
    public bool keepInScreen = true;

    [Tooltip("离边缘的留白，按画面宽/高的百分比算。0.02 = 2%")]
    public float edgePadding = 0.02f;

    [Header("被推动")]
    [Tooltip("点击 / 自动推动时给的速度大小（相对值）")]
    public float pushForce = 2f;

    [Tooltip("点击时向上跳的速度（相对值），0 = 只推不跳")]
    public float jumpForce = 1.8f;

    [Tooltip("每隔多少秒自动推一下，0 = 不自动推（只靠点击）")]
    public float autoPushInterval = 3f;

    [Tooltip("点击物体时推它一下")]
    public bool clickToPush = true;

    [Header("速度微调")]
    [Tooltip("1 = 按画面大小自动换算；觉得整体太快/太慢就改这个倍数")]
    public float speedScale = 1f;

    [Header("Q弹挤压")]
    [Tooltip("Q弹程度：0 = 完全不形变，1 = 最弹。控制形变强弱的总开关")]
    public float jellyAmount = 0.7f;

    [Tooltip("点击瞬间的挤压强度（再乘以 Q弹程度）")]
    public float squashKick = 6f;

    [Tooltip("回弹刚度，越大弹得越快")]
    public float squashStiffness = 220f;

    [Tooltip("回弹阻尼，越大越快停止晃动")]
    public float squashDamping = 14f;

    [Tooltip("形变幅度上限（会再乘上 Q弹程度）")]
    public float squashAmount = 0.22f;

    [Tooltip("横向跟着变形的比例，1 = 纵向压多少横向就鼓多少")]
    public float squashAxisShare = 0.6f;

    [Header("旋转（水里的转动）")]
    [Tooltip("平时自转的速度（度/秒），每个物体方向随机，0 = 平时不转")]
    public float spinIdleSpeed = 20f;

    [Tooltip("旋转阻尼：只减速，不会把角度转回初始值。越大转得越快停")]
    public float spinDamping = 1.2f;

    [Tooltip("点击时额外加的旋转速度（度/秒），方向由点在物体哪一侧决定，0 = 点击不转")]
    public float clickSpin = 200f;

    [Tooltip("最大旋转速度（度/秒），防止转成陀螺")]
    public float maxSpinSpeed = 240f;

    [Header("圆环涟漪（每次被弹时冒一圈）")]
    [Tooltip("被点击 / 被推动时冒一圈涟漪")]
    public bool enableRipple = true;

    [Tooltip("一圈涟漪从出现到消失的时长（秒）")]
    public float rippleLife = 1.0f;

    [Tooltip("涟漪最大半径，占画面宽度的比例（0.15 = 15%）")]
    public float rippleMaxRadius = 0.12f;

    [Tooltip("起始半径占最大半径的比例（0.05 = 从小点开始长大）")]
    public float rippleStartRatio = 0.05f;

    [Tooltip("圆环线宽（UI 坐标/像素），不随圆变大而变粗")]
    public float rippleThickness = 6f;

    [Tooltip("圆环颜色，A 通道就是透明度")]
    public Color rippleColor = new Color(1f, 1f, 1f, 0.6f);

    [Header("涟漪层级")]
    [Tooltip("把背景 Image 拖进来：涟漪会显示在它之上、漂浮物体之下")]
    public Image rippleBackdrop;

    [Tooltip("没拖背景时的兜底：勾选 = 涟漪画在最上层；取消 = 画在最底层")]
    public bool rippleOnTop = true;

    [Header("原地漂浮（不飘走）")]
    [Tooltip("拖入 Image：这些物体只在原位附近朝随机方向漂浮，不会被推走")]
    public List<Image> anchorObjects = new List<Image>();

    [Tooltip("漂浮幅度，占画面宽度的比例（0.02 = 2%）")]
    public float anchorAmplitude = 0.02f;

    [Tooltip("漂浮速度（每秒摆动的弧度，越大越快）")]
    public float anchorSpeed = 1.5f;

    [Tooltip("每个物体的速度随机浮动比例（0.25 = 各自 ±25%）")]
    public float anchorSpeedVariation = 0.25f;

    // ================= 运行时数据 =================
    class Item
    {
        public Image img;
        public RectTransform rect;
        public Canvas canvas;
        public Vector3 pos;            // 脚本自己维护的位置（世界/Canvas 坐标）
        public Vector3 vel;            // 当前速度
        public Vector2 wanderDir;      // 当前漫游方向
        public float wanderTimer;      // 下次换方向的时间
        public float nextAutoPush;     // 下次自动推动的时间
        public Quaternion homeRot;     // 初始旋转
        public Vector3 baseScale;      // 初始缩放
        public float squash;           // 挤压量（>0 拉长，<0 压扁）
        public float squashVel;        // 挤压速度
        public float angle;            // 当前绕 Z 的角度（度，相对初始角度）
        public float angVel;           // 当前角速度（度/秒）
        public float spinDir;          // 自转方向 +1 / -1
    }

    // 原地漂浮的物体（不回原位、只在小范围内晃）
    class AnchorItem
    {
        public Image img;
        public RectTransform rect;
        public Canvas canvas;
        public Vector3 home;        // 原位
        public Vector2 dir;         // 主方向
        public Vector2 perp;        // 副方向（垂直）
        public float phase;         // 相位，让每个物体不同步
        public float speedMul;      // 速度随机倍数
        public float startTime;     // 开始时间（用于缓入，避免开局跳一下）
    }

    private List<Item> items = new List<Item>();
    private List<AnchorItem> anchors = new List<AnchorItem>();

    void Start()
    {
        InitItems();
    }

    // 运行时改了列表可以调这个（组件右键菜单也能点）
    [ContextMenu("重新记录初始状态")]
    public void Refresh()
    {
        InitItems();
    }

    void InitItems()
    {
        items.Clear();

        foreach (Image img in objects)
        {
            if (img == null)
                continue;

            Item it = new Item();
            it.img = img;
            it.rect = img.rectTransform;
            it.canvas = img.GetComponentInParent<Canvas>();
            it.pos = it.rect.position;
            it.homeRot = it.rect.rotation;
            it.baseScale = it.rect.localScale;
            it.spinDir = Random.value < 0.5f ? -1f : 1f;
            it.wanderDir = Random.insideUnitCircle.normalized;
            it.wanderTimer = Random.Range(wanderChangeTime.x, wanderChangeTime.y);
            it.nextAutoPush = autoPushInterval > 0f
                ? Time.time + Random.Range(0.2f, 1f) * autoPushInterval
                : float.MaxValue;

            items.Add(it);
        }

        // 原地漂浮列表
        anchors.Clear();
        foreach (Image img in anchorObjects)
        {
            if (img == null)
                continue;

            if (objects.Contains(img))
            {
                Debug.LogWarning("ObjectFloat: " + img.name + " 同时在“漂浮的物体”和“原地漂浮”里，已忽略原地漂浮");
                continue;
            }

            AnchorItem a = new AnchorItem();
            a.img = img;
            a.rect = img.rectTransform;
            a.canvas = img.GetComponentInParent<Canvas>();
            a.home = a.rect.position;
            a.dir = Random.insideUnitCircle.normalized;
            a.perp = new Vector2(-a.dir.y, a.dir.x);
            a.phase = Random.Range(0f, Mathf.PI * 2f);
            a.speedMul = 1f + Random.Range(-anchorSpeedVariation, anchorSpeedVariation);
            a.startTime = Time.time;

            anchors.Add(a);
        }
    }

    void Update()
    {
        if (!clickToPush || !Input.GetMouseButtonDown(0) || items.Count == 0)
            return;

        Vector2 screenPos = Input.mousePosition;
        Item it = PickItem(screenPos);
        if (it != null)
            PushFromPoint(it, screenPos);
    }

    // LateUpdate：比其它改位置/旋转的 UI 脚本更晚执行
    void LateUpdate()
    {
        if (items.Count == 0 && anchors.Count == 0)
            return;

        float remaining = Mathf.Min(Time.deltaTime, 0.1f);
        const float maxStep = 1f / 120f;

        while (remaining > 0f)
        {
            float dt = Mathf.Min(remaining, maxStep);
            remaining -= dt;
            Step(dt);
        }

        foreach (Item it in items)
        {
            if (it.rect == null)
                continue;

            it.rect.position = it.pos;

            // 挤压拉伸（Q弹跳动感），强度由 Q弹程度 控制
            float maxSquash = squashAmount * jellyAmount;
            float s = Mathf.Clamp(it.squash, -maxSquash, maxSquash);
            it.rect.localScale = new Vector3(
                it.baseScale.x * (1f - s * squashAxisShare),
                it.baseScale.y * (1f + s),
                it.baseScale.z);

            // 旋转（绕 Z 轴自由转，不回正）
            it.rect.rotation = it.homeRot * Quaternion.Euler(0f, 0f, it.angle);
        }

        UpdateRipples(Time.deltaTime);
        UpdateAnchors();
    }

    // 原地漂浮：在 home 附近按正弦来回晃，幅度/速度可调，永远飘不走
    void UpdateAnchors()
    {
        if (anchors.Count == 0)
            return;

        float now = Time.time;

        for (int i = 0; i < anchors.Count; i++)
        {
            AnchorItem a = anchors[i];
            if (a.rect == null)
                continue;

            if (!GetArea(a.canvas, out Rect area))
                continue;

            float amp = area.width * anchorAmplitude;
            float ramp = Mathf.Clamp01(now - a.startTime);   // 一秒钟内缓入，开局不跳

            a.rect.position = a.home + AnchorOffset(a, amp, now) * ramp;
        }
    }

    Vector3 AnchorOffset(AnchorItem a, float amplitude, float time)
    {
        float t = time * anchorSpeed * a.speedMul + a.phase;

        Vector3 main = (Vector3)a.dir * (Mathf.Sin(t) * amplitude);
        Vector3 side = (Vector3)a.perp * (Mathf.Sin(t * 0.7f + a.phase * 1.7f) * amplitude * 0.35f);
        return main + side;
    }

    void Step(float dt)
    {
        foreach (Item it in items)
        {
            if (it.rect == null)
                continue;

            // 画面范围 + 速度换算（1920 宽的画面约等于“像素/秒”）
            bool hasArea = GetArea(it, out Rect area);
            float unit = (hasArea ? area.width : 1920f) * 0.05f * Mathf.Max(0.0001f, speedScale);

            float wanderSpeedEff = wanderSpeed * unit;
            float maxSpeedEff = maxSpeed * unit;

            // ---- 定时自动推 ----
            if (it.nextAutoPush != float.MaxValue && Time.time >= it.nextAutoPush)
            {
                Kick(it, false);
                it.nextAutoPush = Time.time + autoPushInterval * Random.Range(0.7f, 1.3f);
            }

            // ---- 漫游：维持一个缓慢漂移的速度 ----
            it.wanderTimer -= dt;
            if (it.wanderTimer <= 0f)
            {
                it.wanderDir = Random.insideUnitCircle.normalized;
                it.wanderTimer = Random.Range(wanderChangeTime.x, wanderChangeTime.y);
            }

            if (wanderSpeedEff > 0f)
            {
                float speed = it.vel.magnitude;
                if (speed < wanderSpeedEff)
                    it.vel += (Vector3)it.wanderDir * (wanderSpeedEff * damping * dt);
            }

            // ---- 阻尼：只减速，不回原位 ----
            it.vel /= (1f + damping * dt);

            // ---- 限速 ----
            if (it.vel.sqrMagnitude > maxSpeedEff * maxSpeedEff)
                it.vel = it.vel.normalized * maxSpeedEff;

            it.pos += it.vel * dt;

            // ---- 出界就反弹 ----
            if (keepInScreen && hasArea)
                ClampInside(it, area);

            // ---- 挤压弹簧（Q弹） ----
            it.squashVel += (-it.squash * squashStiffness - it.squashVel * squashDamping) * dt;
            it.squash += it.squashVel * dt;

            // ---- 旋转：维持缓慢自转 + 阻尼减速（不转回初始角度）----
            if (spinIdleSpeed > 0f)
            {
                float targetSpin = spinIdleSpeed * it.spinDir;
                if (Mathf.Abs(it.angVel) < Mathf.Abs(targetSpin))
                    it.angVel = Mathf.MoveTowards(it.angVel, targetSpin, spinIdleSpeed * spinDamping * dt);
            }

            it.angVel /= (1f + spinDamping * dt);

            if (Mathf.Abs(it.angVel) > maxSpinSpeed)
                it.angVel = Mathf.Sign(it.angVel) * maxSpinSpeed;

            it.angle += it.angVel * dt;
            if (Mathf.Abs(it.angle) > 360f)
                it.angle %= 360f;   // 防止长时间累计后精度漂移
        }
    }

    // 限制在画面内并反弹；留白按画面尺寸的百分比，区间被压扁时自动退化为“贴中间”
    void ClampInside(Item it, Rect area)
    {
        float padX = area.width * Mathf.Clamp01(edgePadding);
        float padY = area.height * Mathf.Clamp01(edgePadding);

        float minX = area.xMin + padX;
        float maxX = area.xMax - padX;
        if (maxX < minX)
        {
            float c = (minX + maxX) * 0.5f;
            minX = maxX = c;   // 画面比留白还小：别再来回跳，直接贴中间
        }

        float minY = area.yMin + padY;
        float maxY = area.yMax - padY;
        if (maxY < minY)
        {
            float c = (minY + maxY) * 0.5f;
            minY = maxY = c;
        }

        if (it.pos.x < minX) { it.pos.x = minX; it.vel.x = Mathf.Abs(it.vel.x); it.wanderDir.x = Mathf.Abs(it.wanderDir.x); }
        else if (it.pos.x > maxX) { it.pos.x = maxX; it.vel.x = -Mathf.Abs(it.vel.x); it.wanderDir.x = -Mathf.Abs(it.wanderDir.x); }

        if (it.pos.y < minY) { it.pos.y = minY; it.vel.y = Mathf.Abs(it.vel.y); it.wanderDir.y = Mathf.Abs(it.wanderDir.y); }
        else if (it.pos.y > maxY) { it.pos.y = maxY; it.vel.y = -Mathf.Abs(it.vel.y); it.wanderDir.y = -Mathf.Abs(it.wanderDir.y); }
    }

    bool GetArea(Item it, out Rect area)
    {
        return GetArea(it != null ? it.canvas : null, out area);
    }

    // 取 Canvas 的世界矩形；拿不到就用摄像机视口
    bool GetArea(Canvas canvas, out Rect area)
    {
        area = new Rect(0f, 0f, 1920f, 1080f);

        if (canvas != null)
        {
            RectTransform cr = (RectTransform)canvas.transform;
            Vector3 a = cr.TransformPoint(cr.rect.min);
            Vector3 b = cr.TransformPoint(cr.rect.max);

            float w = Mathf.Abs(b.x - a.x);
            float h = Mathf.Abs(b.y - a.y);
            if (w < 0.0001f || h < 0.0001f)
                return false;

            area = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return true;
        }

        Camera cam = Camera.main;
        if (cam == null)
            return false;

        Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
        Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));

        float ww = Mathf.Abs(tr.x - bl.x);
        float hh = Mathf.Abs(tr.y - bl.y);
        if (ww < 0.0001f || hh < 0.0001f)
            return false;

        area = Rect.MinMaxRect(Mathf.Min(bl.x, tr.x), Mathf.Min(bl.y, tr.y), Mathf.Max(bl.x, tr.x), Mathf.Max(bl.y, tr.y));
        return true;
    }

    static Camera EventCamera(Item it)
    {
        if (it.canvas == null || it.canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;
        return it.canvas.worldCamera;
    }

    /// <summary>推一下指定物体（传它自己或子物体都行）</summary>
    public void Push(Transform target)
    {
        Item it = FindItem(target);
        if (it != null)
            Kick(it, false);
    }

    /// <summary>推动列表里的所有物体</summary>
    public void PushAll()
    {
        foreach (Item it in items)
            Kick(it, false);
    }

    // 随机方向推（定时自动推 / PushAll 用）+ 挤压形变
    void Kick(Item it, bool jump)
    {
        if (it == null || it.rect == null)
            return;

        float unit = GetUnit(it);
        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector3 dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
        it.vel += dir * (pushForce * unit);

        if (jump && jumpForce > 0f)
            it.vel.y += jumpForce * unit;

        it.wanderDir = Random.insideUnitCircle.normalized;

        if (clickSpin > 0f)
            it.angVel += Random.Range(-1f, 1f) * clickSpin * 0.5f;

        if (squashKick > 0f)
            it.squashVel -= squashKick * jellyAmount;

        RippleAt(it);   // 弹一下就在原地冒一圈涟漪
    }

    // 点击推：方向 = 鼠标点击点 → 物体中心（屏幕空间算方向）
    void PushFromPoint(Item it, Vector2 clickScreen)
    {
        if (it == null || it.rect == null)
            return;

        float unit = GetUnit(it);

        Vector2 objScreen = RectTransformUtility.WorldToScreenPoint(EventCamera(it), it.rect.position);
        Vector2 dir = objScreen - clickScreen;

        if (dir.sqrMagnitude < 1e-6f)
            dir = Random.insideUnitCircle.normalized;   // 正好点在中心，随便给个方向
        dir.Normalize();

        it.vel += (Vector3)dir * (pushForce * unit);

        // 被点击时向上跳一下
        if (jumpForce > 0f)
            it.vel.y += jumpForce * unit;

        // 顺势换掉漫游方向，让它往新方向飘
        it.wanderDir = dir;

        // 点在物体哪一侧就往相反方向带一点旋转（点左右侧 → 顺时针，点右 → 逆时针）
        if (clickSpin > 0f)
        {
            float side = Mathf.Sign(clickScreen.x - objScreen.x);
            if (Mathf.Approximately(side, 0f))
                side = Random.value < 0.5f ? -1f : 1f;
            it.angVel -= side * clickSpin;
        }

        if (squashKick > 0f)
            it.squashVel -= squashKick * jellyAmount;

        RippleAt(it);   // 弹一下就在原地冒一圈涟漪
    }

    float GetUnit(Item it)
    {
        if (GetArea(it, out Rect area))
            return area.width * 0.05f * Mathf.Max(0.0001f, speedScale);
        return 1920f * 0.05f * Mathf.Max(0.0001f, speedScale);
    }

    // 在物体视觉中心冒一圈涟漪（和 pivot 无关，所以一定是从图案正中扩散）
    void RippleAt(Item it)
    {
        if (!enableRipple || it == null || it.rect == null || it.canvas == null)
            return;

        if (!GetArea(it, out Rect area))
            return;

        SpawnRipple(it.canvas, VisualCenter(it), area.width * rippleMaxRadius);
    }

    Vector3 VisualCenter(Item it)
    {
        Rect rr = it.rect.rect;
        return it.rect.TransformPoint(new Vector3(rr.center.x, rr.center.y, 0f));
    }

    Item FindItem(Transform t)
    {
        if (t == null)
            return null;

        foreach (Item it in items)
        {
            if (it.rect == null)
                continue;
            if (t == it.rect || t.IsChildOf(it.rect))
                return it;
        }
        return null;
    }

    // 鼠标下的物体：用 RectTransform 的矩形判定，重叠时取层级靠后的（更靠前显示的那个）
    Item PickItem(Vector2 screenPos)
    {
        Item best = null;
        int bestOrder = int.MinValue;

        foreach (Item it in items)
        {
            if (it.rect == null || !it.img.gameObject.activeInHierarchy)
                continue;

            if (!RectTransformUtility.RectangleContainsScreenPoint(it.rect, screenPos, EventCamera(it)))
                continue;

            int order = it.rect.GetSiblingIndex();
            if (order >= bestOrder)
            {
                bestOrder = order;
                best = it;
            }
        }

        return best;
    }

    // ================= 水波纹（涟漪）=================
    class Ripple
    {
        public RippleRing ring;
        public RectTransform rect;
        public Vector3 center;
        public float age;
        public float maxRadius;
        public bool active;
    }

    private readonly List<Ripple> ripples = new List<Ripple>();
    private readonly Dictionary<Canvas, RectTransform> rippleContainers = new Dictionary<Canvas, RectTransform>();

    // 让所有涟漪从中心扩散变大、变淡、消失（生成由“被弹”时触发）
    void UpdateRipples(float dt)
    {
        float life = Mathf.Max(0.01f, rippleLife);

        for (int i = 0; i < ripples.Count; i++)
        {
            Ripple r = ripples[i];
            if (!r.active || r.ring == null)
                continue;

            r.age += dt;
            float k = r.age / life;
            if (k >= 1f)
            {
                r.active = false;
                r.ring.enabled = false;
                continue;
            }

            // 半径从小扩散到最大（先快后慢），线宽始终不变
            float startR = r.maxRadius * rippleStartRatio;
            float radius = Mathf.Lerp(startR, r.maxRadius, 1f - (1f - k) * (1f - k));
            r.ring.SetRing(radius, rippleThickness);

            // 变大到后期逐渐变透明，最后消失
            Color c = rippleColor;
            c.a *= 1f - k * k;
            r.ring.color = c;

            r.rect.position = r.center;
        }
    }

    void SpawnRipple(Canvas canvas, Vector3 center, float maxRadius)
    {
        RectTransform container = GetRippleContainer(canvas);
        if (container == null)
            return;

        Ripple r = null;
        for (int i = 0; i < ripples.Count; i++)
        {
            if (!ripples[i].active) { r = ripples[i]; break; }
        }

        if (r == null)
        {
            GameObject go = new GameObject("FloatRipple", typeof(RectTransform), typeof(CanvasRenderer), typeof(RippleRing));
            RippleRing ring = go.GetComponent<RippleRing>();
            ring.raycastTarget = false;

            r = new Ripple();
            r.ring = ring;
            r.rect = (RectTransform)go.transform;
            r.rect.anchorMin = new Vector2(0.5f, 0.5f);
            r.rect.anchorMax = new Vector2(0.5f, 0.5f);
            r.rect.pivot = new Vector2(0.5f, 0.5f);
            ripples.Add(r);
        }

        if (r.rect.parent != container)
            r.rect.SetParent(container, false);

        r.rect.SetAsLastSibling();
        r.rect.rotation = Quaternion.identity;
        r.ring.enabled = true;
        r.ring.color = rippleColor;

        r.center = center;
        r.maxRadius = Mathf.Max(1f, maxRadius);
        r.age = 0f;
        r.active = true;

        float startR = r.maxRadius * rippleStartRatio;
        r.ring.SetRing(startR, rippleThickness);
        r.rect.position = center;
    }

    // 涟漪容器：拖了背景就排在背景之上（其它 UI 之下），没拖就按 rippleOnTop 兜底
    RectTransform GetRippleContainer(Canvas canvas)
    {
        if (canvas == null)
            return null;

        if (rippleContainers.TryGetValue(canvas, out RectTransform cached) && cached != null)
        {
            // 别的 UI 动态生成可能打乱顺序，这里顺手纠正一次
            ApplyRippleOrder(cached, canvas);
            return cached;
        }

        GameObject go = new GameObject("FloatRipples", typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        ApplyRippleOrder(rt, canvas);

        rippleContainers[canvas] = rt;
        return rt;
    }

    // 排在背景 Image 的下一个兄弟位置 → 显示在背景之上、其余 UI（含漂浮物体）之下
    void ApplyRippleOrder(RectTransform rt, Canvas canvas)
    {
        if (rt == null || canvas == null)
            return;

        if (rippleBackdrop != null)
        {
            // 找到背景在 Canvas 下的那一层父物体
            Transform top = rippleBackdrop.transform;
            while (top.parent != null && top.parent != canvas.transform)
                top = top.parent;

            if (top.parent == canvas.transform)
            {
                rt.SetSiblingIndex(top.GetSiblingIndex() + 1);
                return;
            }
        }

        if (rippleOnTop)
            rt.SetAsLastSibling();
        else
            rt.SetAsFirstSibling();
    }

    void OnDestroy()
    {
        foreach (KeyValuePair<Canvas, RectTransform> kv in rippleContainers)
        {
            if (kv.Value != null)
                Destroy(kv.Value.gameObject);
        }
        rippleContainers.Clear();
        ripples.Clear();
        anchors.Clear();
    }
}
