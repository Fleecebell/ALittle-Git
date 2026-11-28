using UnityEngine;
using System.Collections.Generic;

// ======================================================================
// WaterPool —— 水池: 检测角色进出水, 改变角色物理状态(沉浮/阻力/游泳)
// --------------------------------------------------------------------------
// 机制(平缓阻尼模型):
//   1. 水池对象挂 BoxCollider2D + Is Trigger, 角色进入触发区算"在水里"
//   2. 浮力/下沉用"目标速度 + 阻尼平滑"模型(不是加速度叠加, 避免抖动)
//      - 变大(轻): 目标速度向上(浮) → 角色平滑趋近上浮速度, 到水面自然停
//      - 正常(重): 目标速度向下(沉) → 角色平滑趋近下沉速度
//   3. 浸没比例用 smoothstep 让中间过渡区间占大部分(0.2~0.8 浸没时效果最明显)
//   4. 按 空格/W 在水里 = 向上游(检查角色顶部是否已出水)
//   5. 水位变化已注释(有问题, 暂时关闭)
//   6. 美术: 单一水体 Sprite, 整体做轻微 sin 波浪(上下浮动幅度小), 贴图滚动模拟水流
// ======================================================================

// 角色实现此接口以响应水池进出(设置 currentWaterPool 字段)
public interface IWaterResponder
{
    void SetWaterPool(WaterPool pool);
}

[RequireComponent(typeof(BoxCollider2D))]
public class WaterPool : MonoBehaviour
{
    [Header("=== 水池设置 ===")]
    [Tooltip("水池碰撞体(必须 Is Trigger, 矩形). 留空自动取 GetComponent")]
    public BoxCollider2D waterCollider;
    [Tooltip("水体视觉 SpriteRenderer(矩形, 覆盖水池区域). 用于显示水和波浪")]
    public SpriteRenderer waterBody;

    [Header("=== 变大角色(轻)水中物理 ===")]
    [Tooltip("上浮目标速度(正值=向上). 大人在水里会平滑趋近这个速度上浮. 5 = 慢慢上浮")]
    public float bigBuoyancySpeed = 5f;
    [Tooltip("上浮阻尼系数(0~1, 越小越平滑越慢). 控制趋近上浮速度的快慢. 0.02 = 很平缓")]
    [Range(0.001f, 0.5f)] public float bigBuoyancyDamp = 0.02f;
    [Tooltip("水平阻力(0~1, 越小阻力越大). 大人体积大阻力大. 0.95 = 轻微阻力")]
    [Range(0.5f, 1f)] public float bigDragFactor = 0.95f;
    [Tooltip("大人游泳向上速度")]
    public float bigSwimUpSpeed = 5f;

    [Header("=== 正常角色(重)水中物理 ===")]
    [Tooltip("下沉目标速度(负值=向下). 小人在水里会平滑趋近这个速度下沉. -3 = 缓慢下沉")]
    public float normalSinkSpeed = -3f;
    [Tooltip("下沉阻尼系数(0~1, 越小越平滑越慢). 0.02 = 很平缓")]
    [Range(0.001f, 0.5f)] public float normalSinkDamp = 0.02f;
    [Tooltip("水平阻力. 小人阻力小, 相对灵活")]
    [Range(0.5f, 1f)] public float normalDragFactor = 0.98f;
    [Tooltip("小人游泳向上速度")]
    public float normalSwimUpSpeed = 6f;

    [Header("=== 浸没区间(让中间过渡占大部分) ===")]
    [Tooltip("浸没比例下限(0~1). 低于此值算几乎出水, 浮力/下沉很弱. 0.2 = 浸没20%以下效果很弱")]
    [Range(0f, 1f)] public float submersionMin = 0.2f;
    [Tooltip("浸没比例上限(0~1). 高于此值算完全浸没, 效果满. 0.8 = 浸没80%以上效果最大")]
    [Range(0f, 1f)] public float submersionMax = 0.8f;

    [Header("=== 水位(已注释, 暂时关闭) ===")]
    public bool enableWaterLevel = false;
    public float baseWaterLevel = 1f;
    public float displacementFactor = 0.5f;
    public float waterLevelSmoothSpeed = 3f;

    [Header("=== 波浪(美术) ===")]
    public bool enableWaves = true;
    [Tooltip("波浪幅度(整个水体上下浮动距离). 0.05 = 很轻微, 像水面波动")]
    public float waveAmplitude = 0.05f;
    [Tooltip("波浪速度")]
    public float waveSpeed = 1.5f;
    [Tooltip("贴图滚动速度(模拟水流方向). 0=不滚动")]
    public float textureScrollSpeed = 0.05f;

    // 在水里的角色 → 他们的 Collider2D
    private Dictionary<Rigidbody2D, Collider2D> swimmers = new Dictionary<Rigidbody2D, Collider2D>();
    // 角色原始重力(出水时恢复)
    private Dictionary<Rigidbody2D, float> originalGravity = new Dictionary<Rigidbody2D, float>();

    // 本帧已调用 SwimUp 的角色 — ApplyWaterPhysics 不覆盖其垂直速度
    private HashSet<Rigidbody2D> swimmingThisFrame = new HashSet<Rigidbody2D>();

    // 水体初始 position(波浪基于此浮动)
    private Vector3 bodyBaseLocalPos;

    void Start()
    {
        if (waterCollider == null) waterCollider = GetComponent<BoxCollider2D>();
        waterCollider.isTrigger = true;
        if (waterBody != null)
        {
            bodyBaseLocalPos = waterBody.transform.localPosition;
        }
    }

    void Update()
    {
        UpdateWaterVisual();
    }

    void LateUpdate()
    {
        // 在 LateUpdate 中执行, 确保在所有 MoveFirst/MoveLate.Update() 之后运行
        // 这样 SwimUp 已经设置好游泳标记和速度, ApplyWaterPhysics 不会覆盖它们
        ApplyPhysicsToSwimmers();
    }

    // 对所有在水里的角色施加物理
    void ApplyPhysicsToSwimmers()
    {
        List<Rigidbody2D> toRemove = new List<Rigidbody2D>();
        foreach (var kvp in swimmers)
        {
            Rigidbody2D rb = kvp.Key;
            if (rb == null) { toRemove.Add(rb); continue; }
            ApplyWaterPhysics(rb);
        }
        foreach (var rb in toRemove)
        {
            swimmers.Remove(rb);
            if (originalGravity.ContainsKey(rb))
            {
                rb.gravityScale = originalGravity[rb];
                originalGravity.Remove(rb);
            }
        }
        swimmingThisFrame.Clear();
    }

    // 施加浮力/下沉力(平缓阻尼模型, 不抖动)
    void ApplyWaterPhysics(Rigidbody2D rb)
    {
        Collider2D col = swimmers.ContainsKey(rb) ? swimmers[rb] : rb.GetComponent<Collider2D>();
        if (col == null) return;

        Bounds charBounds = col.bounds;
        Bounds waterBounds = waterCollider.bounds;

        // 计算浸没比例 (0=完全出水 1=完全在水里)
        float submergedTop = Mathf.Min(charBounds.max.y, waterBounds.max.y);
        float submergedBottom = Mathf.Max(charBounds.min.y, waterBounds.min.y);
        float submergedHeight = Mathf.Max(0, submergedTop - submergedBottom);
        float charHeight = charBounds.max.y - charBounds.min.y;
        float submersionRatio = charHeight > 0 ? submergedHeight / charHeight : 0;

        if (submersionRatio <= 0.001f)
        {
            // 完全出水, 恢复重力
            if (originalGravity.ContainsKey(rb)) rb.gravityScale = originalGravity[rb];
            return;
        }

        // 在水里, 关闭重力(用目标速度模型, 避免重力+浮力叠加抖动)
        if (!originalGravity.ContainsKey(rb))
            originalGravity[rb] = rb.gravityScale;
        rb.gravityScale = 0f;

        float scale = rb.transform.localScale.x;
        bool isBig = scale > 1.5f;
        float dragFactor = isBig ? bigDragFactor : normalDragFactor;

        // 本帧已游泳(SwimUp设置了向上速度): 只施加水平阻力, 不覆盖垂直速度
        if (swimmingThisFrame.Contains(rb))
        {
            rb.velocity = new Vector2(rb.velocity.x * dragFactor, rb.velocity.y);
            return;
        }

        // === 用 smoothstep 把浸没比例映射到 [0,1], 让中间区间(submersionMin~submersionMax)占大部分 ===
        // 这样浸没 20%~80% 时效果从弱到强平滑过渡, 不会只在很窄的区间有效
        float t = Mathf.InverseLerp(submersionMin, submersionMax, submersionRatio);
        float effectStrength = t * t * (3f - 2f * t); // smoothstep

        if (isBig)
        {
            // 上浮: 目标速度 = bigBuoyancySpeed, 用阻尼平滑趋近(不是加速度叠加)
            // 阻尼模型: velocity.y = MoveTowards(velocity.y, target, damp)
            // 这样浮到水面时速度自然减小, 不会冲过去再抖回来
            float targetVy = bigBuoyancySpeed * effectStrength;
            float newVy = Mathf.MoveTowards(rb.velocity.y, targetVy, bigBuoyancyDamp * 100f * Time.deltaTime);
            rb.velocity = new Vector2(rb.velocity.x * dragFactor, newVy);
        }
        else
        {
            // 下沉: 目标速度 = normalSinkSpeed(负值), 阻尼平滑趋近
            float targetVy = normalSinkSpeed * effectStrength;
            float newVy = Mathf.MoveTowards(rb.velocity.y, targetVy, normalSinkDamp * 100f * Time.deltaTime);
            rb.velocity = new Vector2(rb.velocity.x * dragFactor, newVy);
        }
    }

    // 游泳: 检查角色顶部是否还在水下, 是才施加向上速度(防止游出水面)
    public void SwimUp(Rigidbody2D rb)
    {
        if (!swimmers.ContainsKey(rb)) return;
        Collider2D col = swimmers[rb];
        if (col == null) return;

        Bounds charBounds = col.bounds;
        Bounds waterBounds = waterCollider.bounds;

        // 角色顶部到水面的距离
        float distToSurface = waterBounds.max.y - charBounds.max.y;
        // 已出水(或非常接近水面) → 不再施加向上速度
        if (distToSurface <= 0.05f) return;

        // 标记本帧正在游泳 → ApplyWaterPhysics 不覆盖垂直速度
        swimmingThisFrame.Add(rb);

        // 接近水面时游泳速度减小(自然停在水面, 不会冲出去)
        float speedMul = Mathf.Clamp01(distToSurface / 0.5f);

        float scale = rb.transform.localScale.x;
        float swimSpeed = (scale > 1.5f ? bigSwimUpSpeed : normalSwimUpSpeed) * speedMul;
        rb.velocity = new Vector2(rb.velocity.x, swimSpeed);
    }

    // 波浪视觉更新(只做轻微整体浮动 + 贴图滚动, 不做水位)
    void UpdateWaterVisual()
    {
        // === 水位(已注释, 暂时关闭) ===
        /*
        if (enableWaterLevel && waterBody != null)
        {
            float totalDisplacement = 0f;
            Bounds waterBounds = waterCollider.bounds;
            foreach (var kvp in swimmers)
            {
                Rigidbody2D rb = kvp.Key;
                if (rb == null) continue;
                Collider2D col = kvp.Value;
                if (col == null) continue;
                Bounds charBounds = col.bounds;
                if (charBounds.max.y <= waterBounds.max.y)
                {
                    float scale = rb.transform.localScale.x;
                    totalDisplacement += scale * scale * displacementFactor;
                }
            }
            // ... 水位变化逻辑(有问题, 暂时关闭)
        }
        */

        if (waterBody == null) return;

        // === 波浪: 整个水体做轻微上下浮动(模拟水面波动) ===
        if (enableWaves)
        {
            // 两个 sin 波叠加, 频率不同, 让波浪更自然
            float waveOffset = Mathf.Sin(Time.time * waveSpeed) * waveAmplitude
                             + Mathf.Sin(Time.time * waveSpeed * 1.7f + 1.5f) * waveAmplitude * 0.5f;
            Vector3 pos = bodyBaseLocalPos;
            pos.y += waveOffset;
            waterBody.transform.localPosition = pos;
        }

        // === 贴图滚动模拟水流 ===
        if (enableWaves && textureScrollSpeed != 0f)
        {
            waterBody.material.mainTextureOffset = new Vector2(Time.time * textureScrollSpeed, 0f);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        Rigidbody2D rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;
        if (!swimmers.ContainsKey(rb))
        {
            swimmers[rb] = other;
            originalGravity[rb] = rb.gravityScale;
        }
        IWaterResponder responder = other.GetComponent<IWaterResponder>();
        if (responder != null) responder.SetWaterPool(this);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        Rigidbody2D rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;
        swimmers.Remove(rb);
        if (originalGravity.ContainsKey(rb))
        {
            rb.gravityScale = originalGravity[rb];
            originalGravity.Remove(rb);
        }
        IWaterResponder responder = other.GetComponent<IWaterResponder>();
        if (responder != null) responder.SetWaterPool(null);
    }
}
