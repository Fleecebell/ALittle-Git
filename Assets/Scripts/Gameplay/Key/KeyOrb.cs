using UnityEngine;
using UnityEngine.Rendering.Universal; // Light2D (URP 14+) 在这个命名空间
using DG.Tweening;
using System.Collections.Generic;

/// <summary>
/// 灵光球: 从钥匙位置飞向主角P1, 到达后变成钥匙(从小放大) + 跟随主角(延迟跟随)
/// 挂在灵光球 Prefab 上
///
/// Prefab 结构 (推荐):
///   KeyOrb (空物体, 挂本脚本)
///   ├── OrbVisual (SpriteRenderer, 发光球, 飞行时显示)
///   └── KeyVisual (SpriteRenderer, 钥匙, 初始隐藏, 到达后显示)
///
/// 飞行: DOTween.To + 正弦弧线
/// 到达后: 隐藏球 → 显示钥匙从小放大 → 主角缩放反馈 → 灵光粒子 → 进入跟随模式
/// </summary>
public class KeyOrb : MonoBehaviour
{
    [Header("=== 飞行参数 ===")]
    [Tooltip("基础飞行时长(秒). 距离越远会自动延长")]
    [SerializeField] private float flyDuration = 0.6f;

    [Tooltip("距离因子: 距离每增加1米, 飞行时间增加多少秒")]
    [SerializeField] private float distanceFactor = 0.05f;

    [Tooltip("最大飞行时长(秒)")]
    [SerializeField] private float maxFlyDuration = 1.2f;

    [Tooltip("飞行加减速曲线: InOutSine=先慢后快再慢")]
    [SerializeField] private Ease flyEase = Ease.InOutSine;

    [Tooltip("弧线高度: 飞行路径中点向上凸起多少. 0=直线飞")]
    [SerializeField] private float arcHeight = 1.5f;

    [Header("=== 发光球视觉 (飞行时显示) ===")]
    [Tooltip("发光球的视觉 GameObject (飞行时显示, 到达后隐藏). 留空则无球视觉")]
    [SerializeField] private GameObject orbVisual;

    [Tooltip("飞行时脉动放大的倍数 (1=不脉动)")]
    [SerializeField] private float pulseScale = 1.2f;

    [Tooltip("脉动一次的时长(秒)")]
    [SerializeField] private float pulseSpeed = 0.15f;

    [Header("=== 光晕 (Light2D) ===")]
    [Tooltip("拖入 KeyOrb 下的 Light2D 组件(不是3D Light!). 飞行和跟随时发光, 到达后可淡出. 留空则无光晕")]
    [SerializeField] private Light2D orbLight;

    [Tooltip("光晕基础强度. 1=正常亮度")]
    [SerializeField, Range(0.1f, 5f)] private float lightIntensity = 1.5f;

    [Tooltip("光晕外半径(米). 2=照亮半径2米范围")]
    [SerializeField, Range(0.5f, 10f)] private float lightOuterRadius = 2f;

    [Tooltip("到达主角变钥匙后, 光晕是否淡出. 勾选=淡出(只飞行时发光), 不勾选=跟随钥匙一直发光")]
    [SerializeField] private bool fadeOutLightOnArrive = false;

    [Tooltip("光晕淡出时长(秒), 仅 fadeOutLightOnArrive 勾选时生效")]
    [SerializeField] private float lightFadeOutDuration = 0.5f;

    [Header("=== 到达后变成钥匙 ===")]
    [Tooltip("钥匙的视觉 GameObject (到达后显示, 初始应隐藏). 留空则到达后不切换视觉")]
    [SerializeField] private GameObject keyVisual;

    [Tooltip("钥匙放大动画时长(秒)")]
    [SerializeField] private float keyScaleDuration = 0.3f;

    [Tooltip("钥匙放大曲线: OutBack=带过冲(弹一下), OutElastic=Q弹")]
    [SerializeField] private Ease keyScaleEase = Ease.OutBack;

    [Header("=== 到达主角后的效果 ===")]
    [Tooltip("主角缩放的目标倍数 (1.15=轻微放大)")]
    [SerializeField] private float playerScaleTo = 1.15f;

    [Tooltip("主角缩放动画总时长(秒)")]
    [SerializeField] private float playerScaleDuration = 0.3f;

    [Tooltip("主角回弹曲线: OutElastic=Q弹回弹")]
    [SerializeField] private Ease playerScaleEase = Ease.OutElastic;

    [Tooltip("到达主角后在主角位置生成的灵光粒子 Prefab (带 ParticleSystem)")]
    [SerializeField] private GameObject sparklePrefab;

    [Header("=== 跟随主角 (到达后) ===")]
    [Tooltip("跟随主角的偏移量. X=0.6表示在主角右侧0.6米")]
    [SerializeField] private Vector3 followOffset = new Vector3(0.6f, 0.4f, 0f);

    [Tooltip("延迟跟随平滑时间(秒). 越大跟随越慢越拖沓, 0.1=几乎贴身, 0.3=明显延迟拖尾感")]
    [SerializeField] private float followSmoothTime = 0.2f;

    // ===================== 静态列表 (重生时销毁所有灵光球) =====================
    private static List<KeyOrb> allOrbs = new List<KeyOrb>();

    // ===================== 内部状态 =====================
    private Vector3 orbOriginalScale;   // 球视觉的原始缩放 (脉动基于这个值)
    private Vector3 keyOriginalScale;   // 钥匙视觉的原始缩放 (你在Inspector调的大小, 到达后放大到这个值)
    private bool isFollowing = false;   // 是否已到达主角, 进入跟随模式
    private GameObject followTarget;    // 跟随的目标(主角)
    private Vector3 followVelocity = Vector3.zero; // SmoothDamp 的当前速度

    private void Awake()
    {
        // 只在 Awake 添加一次, 不在 OnDisable 移除.
        // 这样即使 SetActive(false), 灵光球仍在列表里, 重生时能重新激活.
        if (!allOrbs.Contains(this))
            allOrbs.Add(this);

        // 记录球视觉和钥匙视觉的原始缩放 (你在 Inspector 里调的大小)
        // 这样飞行脉动和到达后放大都以这个原始缩放为准, 不会覆盖你调的值
        if (orbVisual != null)
            orbOriginalScale = orbVisual.transform.localScale;
        if (keyVisual != null)
        {
            keyOriginalScale = keyVisual.transform.localScale;
            // 初始隐藏钥匙视觉
            keyVisual.SetActive(false);
        }

        // 初始化光晕参数 (代码控制, 不用你在 Inspector 调 Light2D 组件本身)
        if (orbLight != null)
        {
            orbLight.intensity = lightIntensity;
            orbLight.pointLightOuterRadius = lightOuterRadius;
            orbLight.enabled = true;
        }
    }

    /// <summary>
    /// 禁用所有活跃的灵光球 (重生时由 DeathRespawnVFX 调用).
    /// 不销毁, 改为禁用, 这样场景里原有的 KeyOrb 物体能被重新激活.
    /// </summary>
    public static void DisableAllOrbs()
    {
        foreach (var orb in allOrbs.ToArray())
        {
            if (orb != null)
            {
                orb.transform.DOKill();
                orb.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 重置灵光球状态 (供 Key 脚本在激活前调用):
    /// 停止所有动画 + 隐藏钥匙视觉 + 显示球视觉 + 重置缩放 + 清除跟随状态
    /// </summary>
    public void ResetState()
    {
        // 停止所有 DOTween 动画
        transform.DOKill();
        if (orbVisual != null) orbVisual.transform.DOKill();
        if (keyVisual != null) keyVisual.transform.DOKill();

        // 清除跟随状态
        isFollowing = false;
        followTarget = null;
        followVelocity = Vector3.zero;

        // 显示球视觉, 恢复球原始缩放
        if (orbVisual != null)
        {
            orbVisual.SetActive(true);
            orbVisual.transform.localScale = orbOriginalScale;
        }

        // 隐藏钥匙视觉, 恢复钥匙原始缩放 (你在Inspector调的大小)
        if (keyVisual != null)
        {
            keyVisual.SetActive(false);
            keyVisual.transform.localScale = keyOriginalScale;
        }

        // 恢复光晕: 停止淡出动画 + 重新激活 + 恢复强度
        if (orbLight != null)
        {
            orbLight.DOKill();
            orbLight.intensity = lightIntensity;
            orbLight.enabled = true;
        }
    }

    private void Update()
    {
        // 跟随模式: 用 SmoothDamp 延迟跟随主角 (有拖尾感)
        if (isFollowing && followTarget != null)
        {
            Vector3 targetPos = followTarget.transform.position + followOffset;
            transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref followVelocity, followSmoothTime);
        }
    }

    /// <summary>
    /// 飞向主角. 由 Key 脚本调用.
    /// </summary>
    public void FlyToPlayer(GameObject player)
    {
        Vector3 start = transform.position;
        Vector3 end = player.transform.position;

        float distance = Vector3.Distance(start, end);
        float duration = Mathf.Min(flyDuration + distance * distanceFactor, maxFlyDuration);

        // 飞行时球脉动 (缩放 orbVisual, 不是根物体)
        if (pulseScale > 1f && orbVisual != null)
        {
            orbVisual.transform.DOScale(orbOriginalScale * pulseScale, pulseSpeed).SetLoops(-1, LoopType.Yoyo);
        }

        // 正弦弧线飞行 (移动根物体)
        float t = 0f;
        DOTween.To(() => t, x => t = x, 1f, duration)
            .SetEase(flyEase)
            .OnUpdate(() =>
            {
                float x = Mathf.Lerp(start.x, end.x, t);
                float y = Mathf.Lerp(start.y, end.y, t);
                y += Mathf.Sin(t * Mathf.PI) * arcHeight;
                transform.position = new Vector3(x, y, transform.position.z);
            })
            .OnComplete(() => OnReachedPlayer(player));
    }

    /// <summary>
    /// 到达主角后: 隐藏球 → 显示钥匙从小放大 → 主角缩放反馈 → 灵光粒子 → 进入跟随模式
    /// </summary>
    private void OnReachedPlayer(GameObject player)
    {
        // 1. 隐藏发光球 (停止脉动 + 禁用)
        if (orbVisual != null)
        {
            orbVisual.transform.DOKill();
            orbVisual.SetActive(false);
        }

        // 1.5 光晕处理: 勾选淡出 → 渐变到0后禁用; 不勾选 → 保持发光(钥匙跟随时光晕也跟着)
        if (orbLight != null && fadeOutLightOnArrive)
        {
            orbLight.DOKill();
            DOTween.To(() => orbLight.intensity, x => orbLight.intensity = x, 0f, lightFadeOutDuration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() => orbLight.enabled = false);
        }

        // 2. 显示钥匙并从小放大到你在Inspector调的原始大小 (OutBack = 带过冲, 弹一下)
        if (keyVisual != null)
        {
            keyVisual.SetActive(true);
            keyVisual.transform.localScale = Vector3.zero;
            keyVisual.transform.DOScale(keyOriginalScale, keyScaleDuration).SetEase(keyScaleEase);
        }

        // 3. 主角缩放反馈
        Vector3 playerOriginalScale = player.transform.localScale;
        player.transform.DOKill();

        Sequence scaleSeq = DOTween.Sequence();
        scaleSeq.Append(player.transform.DOScale(playerOriginalScale * playerScaleTo, playerScaleDuration * 0.4f).SetEase(Ease.OutQuad));
        scaleSeq.Append(player.transform.DOScale(playerOriginalScale, playerScaleDuration * 0.6f).SetEase(playerScaleEase));

        // 4. 在主角位置生成灵光粒子并播放
        if (sparklePrefab != null)
        {
            GameObject sparkle = Instantiate(sparklePrefab, player.transform.position, Quaternion.identity);
            ParticleSystem ps = sparkle.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Play();
                if (ps.main.stopAction != ParticleSystemStopAction.Destroy)
                {
                    float life = ps.main.duration + ps.main.startLifetime.constantMax;
                    Destroy(sparkle, life);
                }
            }
            else
            {
                Destroy(sparkle, 2f);
            }
        }
        else
        {
            Debug.LogWarning("KeyOrb 未指定 sparklePrefab, 到达主角时无粒子效果");
        }

        // 5. 进入跟随模式 (钥匙跟着主角走, 不销毁)
        followTarget = player;
        isFollowing = true;
        followVelocity = Vector3.zero;
    }
}
