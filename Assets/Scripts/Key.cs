using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

/// <summary>
/// 钥匙: 碰到后播放收集动画 + 激活场景里的灵光球飞向主角P1 + 标记已获得钥匙
///
/// 设计要点:
/// 1. P1 或 P2 谁碰到钥匙都行, 但灵光球永远飞向 P1
/// 2. 收集动画: 放大 → 缩小到0 → 禁用(不销毁, 重生时可重置)
/// 3. 灵光球: 引用场景里已有的 KeyOrb 物体(不是克隆!), 碰钥匙时激活它飞向 P1
///    这样你在场景里调 KeyOrb 物体的大小和参数就直接生效
/// 4. 不立即解锁门! 只设置静态标志 hasKey=true
/// 5. 重生时由 DeathRespawnVFX 调用 ResetAllKeys() 重置所有钥匙到原位
/// </summary>
public class Key : MonoBehaviour
{
    [Header("待机动画(被拿到前)")]
    [Tooltip("上下浮动幅度(米). 0.15=上下各飘0.15米")]
    [SerializeField] private float idleFloatHeight = 0.15f;
    [Tooltip("上下浮动速度. 2=每秒上下一个来回左右")]
    [SerializeField] private float idleFloatSpeed = 2f;
    [Tooltip("左右摇摆角度(度). 10=左右各倾斜10度")]
    [SerializeField] private float idleRotateAngle = 10f;
    [Tooltip("左右摇摆速度. 1.5=每秒左右一个来回左右")]
    [SerializeField] private float idleRotateSpeed = 1.5f;

    [Header("收集动画")]
    [Tooltip("收集时钥匙放大的目标倍数")]
    [SerializeField] private float collectScaleUp = 1.3f;
    [Tooltip("放大阶段时长(秒)")]
    [SerializeField] private float scaleUpDuration = 0.1f;
    [Tooltip("缩小到消失阶段时长(秒)")]
    [SerializeField] private float scaleDownDuration = 0.2f;

    [Header("灵光球")]
    [Tooltip("拖入场景里已有的 KeyOrb 物体(不是Prefab!). 碰钥匙时会激活它飞向主角. 初始应禁用")]
    [SerializeField] private KeyOrb keyOrb;

    /// <summary>
    /// 全局静态标志: 是否已获得钥匙. 由本脚本设为 true, 由 NextLevel 检查.
    /// </summary>
    public static bool hasKey = false;

    // 静态列表: 场景里所有钥匙 (重生时全部重置)
    private static List<Key> allKeys = new List<Key>();

    // 防止重复触发
    private bool isCollected = false;
    // 记录初始位置和缩放 (重生时恢复)
    private Vector3 originalPosition;
    private Vector3 originalScale;

    private void Awake()
    {
        // 记录初始位置和缩放
        originalPosition = transform.position;
        originalScale = transform.localScale;

        // 只在 Awake 添加一次, 不在 OnDisable 移除.
        // 这样即使钥匙被 SetActive(false), 它仍在 allKeys 里, 重生时能重新激活.
        if (!allKeys.Contains(this))
            allKeys.Add(this);
    }

    private void Update()
    {
        // 收集后/禁用前不再播放待机动画
        if (isCollected) return;

        // 上下浮动: 围绕 originalPosition 做 sin 波偏移, 不会越漂越远
        float yOffset = Mathf.Sin(Time.time * idleFloatSpeed) * idleFloatHeight;
        transform.position = originalPosition + new Vector3(0f, yOffset, 0f);

        // 左右摇摆: z轴旋转做 sin 波
        float zRot = Mathf.Sin(Time.time * idleRotateSpeed) * idleRotateAngle;
        transform.rotation = Quaternion.Euler(0f, 0f, zRot);
    }

    /// <summary>
    /// 重置所有钥匙 (重生时由 DeathRespawnVFX 调用):
    /// 放回原位 + 恢复缩放 + 重新激活 + 清除 hasKey
    /// </summary>
    public static void ResetAllKeys()
    {
        foreach (var key in allKeys.ToArray())
        {
            if (key != null)
            {
                key.transform.DOKill();
                key.transform.position = key.originalPosition;
                key.transform.rotation = Quaternion.identity;
                key.transform.localScale = key.originalScale;
                key.isCollected = false;
                key.gameObject.SetActive(true);
            }
        }
        hasKey = false;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isCollected) return;

        // P1 或 P2 触碰钥匙
        if (!collision.gameObject.CompareTag("Player") && !collision.gameObject.CompareTag("Player2"))
            return;

        isCollected = true;

        // 标记已获得钥匙
        hasKey = true;

        // 激活灵光球, 飞向主角 P1 (到达后变成钥匙跟随主角)
        ActivateKeyOrb();

        // 播放收集动画: 放大 → 缩小 → 禁用
        PlayCollectAnimation();
    }

    /// <summary>
    /// 激活场景里已有的灵光球, 重置状态后飞向主角 P1
    /// (不再用 Instantiate 克隆, 直接用场景里已有的 KeyOrb 物体)
    /// </summary>
    private void ActivateKeyOrb()
    {
        if (keyOrb == null)
        {
            Debug.LogWarning("Key 未指定灵光球 (keyOrb)! 请在Inspector拖入场景里的KeyOrb物体");
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("找不到 Tag 为 Player 的主角!");
            return;
        }

        // 激活灵光球物体
        keyOrb.gameObject.SetActive(true);
        // 移到钥匙当前位置
        keyOrb.transform.position = transform.position;
        // 重置灵光球状态 (隐藏钥匙视觉/显示球视觉/清除跟随)
        keyOrb.ResetState();
        // 飞向主角
        keyOrb.FlyToPlayer(player);
    }

    /// <summary>
    /// 收集动画: 放大 → 缩小到0 → 禁用(不销毁, 重生时可重置)
    /// </summary>
    private void PlayCollectAnimation()
    {
        transform.DOKill();
        Sequence seq = DOTween.Sequence();
        seq.Append(transform.DOScale(collectScaleUp, scaleUpDuration).SetEase(Ease.OutQuad));
        seq.Append(transform.DOScale(Vector3.zero, scaleDownDuration).SetEase(Ease.InQuad));
        // 完成后禁用 (不销毁, 重生时 ResetAllKeys 会重新激活)
        seq.OnComplete(() => gameObject.SetActive(false));
    }
}
