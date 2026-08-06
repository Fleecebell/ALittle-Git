using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

/// <summary>
/// 锁的解锁动画 + 黑暗覆盖层
/// 挂在锁的 GameObject 上 (推荐作为门 NextLevel 的子物体)
///
/// 功能:
///   1. 未解锁时: 黑暗覆盖层显示 (半透明黑色 Sprite 覆盖门区域, 表现"封印"感)
///   2. 解锁动画: 锁跳起 → 下掉 → 透明 → 禁用, 同时黑暗覆盖层淡出禁用
///   3. 重生时由 DeathRespawnVFX 调用 ResetAllLocks() 重置所有锁和罩子
///
/// 由 NextLevel.UnlockSequence() 调用 PlayUnlockAnimation()
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class LockAnimator : MonoBehaviour
{
    [Header("=== 跳起阶段 ===")]
    [Tooltip("跳起高度(向上位移多少米)")]
    [SerializeField] private float jumpHeight = 1.5f;

    [Tooltip("跳起时长(秒)")]
    [SerializeField] private float jumpDuration = 0.25f;

    [Tooltip("跳起曲线: OutBack=带过冲(像被弹起)")]
    [SerializeField] private Ease jumpEase = Ease.OutBack;

    [Header("=== 下掉阶段 ===")]
    [Tooltip("下掉距离(向下位移多少米)")]
    [SerializeField] private float fallDistance = 3f;

    [Tooltip("下掉时长(秒)")]
    [SerializeField] private float fallDuration = 0.5f;

    [Tooltip("下掉曲线: InQuad=越掉越快(模拟重力加速)")]
    [SerializeField] private Ease fallEase = Ease.InQuad;

    [Header("=== 透明阶段 ===")]
    [Tooltip("变透明时长(秒), 和下掉同时进行")]
    [SerializeField] private float fadeDuration = 0.4f;

    [Tooltip("最终透明度(0=完全透明)")]
    [SerializeField] private float targetAlpha = 0f;

    [Tooltip("透明曲线")]
    [SerializeField] private Ease fadeEase = Ease.OutQuad;

    [Header("=== 黑暗覆盖层 (未解锁时的封印效果) ===")]
    [Tooltip("拖入黑暗覆盖层的 SpriteRenderer (一个半透明黑色 Sprite 覆盖门区域). 留空则无黑暗效果")]
    [SerializeField] private SpriteRenderer darknessOverlay;

    [Tooltip("黑暗覆盖层初始 alpha (0.5=半透明黑色). 运行时会被设为这个值")]
    [SerializeField, Range(0f, 1f)] private float darknessAlpha = 0.5f;

    [Tooltip("黑暗覆盖层淡出时长(秒), 解锁时淡出")]
    [SerializeField] private float darknessFadeOutDuration = 0.6f;

    // ===================== 静态列表 (重生时重置所有锁) =====================
    private static List<LockAnimator> allLocks = new List<LockAnimator>();

    // 锁的 SpriteRenderer
    private SpriteRenderer spriteRenderer;
    // 锁的初始位置 (用 localPosition, 因为锁推荐作为门的子物体)
    private Vector3 originalLocalPos;
    // 锁的原始颜色 (重生时恢复 alpha)
    private Color originalLockColor;
    // 罩子的原始颜色 (重生时恢复 alpha)
    private Color originalDarknessColor;
    // 防止重复触发
    private bool isUnlocked = false;

    /// <summary>
    /// 解锁动画总时长 (跳起 + 下掉). 供 NextLevel 协程等待用.
    /// </summary>
    public float TotalDuration => jumpDuration + fallDuration;

    private void Awake()
    {
        // 只在 Awake 添加一次, 不在 OnDisable 移除.
        // 这样即使锁被 SetActive(false), 它仍在列表里, 重生时能重新激活.
        if (!allLocks.Contains(this))
            allLocks.Add(this);

        spriteRenderer = GetComponent<SpriteRenderer>();
        originalLocalPos = transform.localPosition;
        if (spriteRenderer != null)
            originalLockColor = spriteRenderer.color;

        // 记录罩子原始颜色 + 初始化罩子 alpha
        if (darknessOverlay != null)
        {
            originalDarknessColor = darknessOverlay.color;
            Color c = darknessOverlay.color;
            c.a = darknessAlpha;
            darknessOverlay.color = c;
        }
    }

    /// <summary>
    /// 重置所有锁 (重生时由 DeathRespawnVFX 调用):
    /// 恢复位置 + 恢复透明度 + 重新激活 + 重置 isUnlocked
    /// 同时重置黑暗覆盖层
    /// </summary>
    public static void ResetAllLocks()
    {
        foreach (var lockAnim in allLocks.ToArray())
        {
            if (lockAnim != null)
            {
                // 停止所有 DOTween 动画
                lockAnim.transform.DOKill();
                if (lockAnim.spriteRenderer != null) lockAnim.spriteRenderer.DOKill();
                if (lockAnim.darknessOverlay != null) lockAnim.darknessOverlay.DOKill();

                // 恢复锁的位置和透明度
                lockAnim.transform.localPosition = lockAnim.originalLocalPos;
                if (lockAnim.spriteRenderer != null)
                    lockAnim.spriteRenderer.color = lockAnim.originalLockColor;

                // 恢复罩子的透明度和激活状态
                if (lockAnim.darknessOverlay != null)
                {
                    Color c = lockAnim.originalDarknessColor;
                    c.a = lockAnim.darknessAlpha;
                    lockAnim.darknessOverlay.color = c;
                    lockAnim.darknessOverlay.gameObject.SetActive(true);
                }

                // 重置状态标志
                lockAnim.isUnlocked = false;

                // 重新激活锁物体
                lockAnim.gameObject.SetActive(true);
            }
        }
    }

    /// <summary>
    /// 播放解锁动画. 由 NextLevel 调用.
    /// 锁: 跳起 → 下掉 → 透明 → 禁用(不销毁, 重生时可重置)
    /// 黑暗覆盖层: 同时淡出 → 禁用
    /// </summary>
    public void PlayUnlockAnimation()
    {
        if (isUnlocked) return;
        isUnlocked = true;

        if (spriteRenderer == null)
        {
            Debug.LogWarning("LockAnimator 找不到 SpriteRenderer! 直接禁用");
            gameObject.SetActive(false);
            return;
        }

        transform.DOKill();
        spriteRenderer.DOKill();

        // ---- 锁动画: 跳起 → 下掉 → 透明 ----
        Sequence seq = DOTween.Sequence();

        // 阶段1: 跳起
        seq.Append(transform.DOLocalMoveY(originalLocalPos.y + jumpHeight, jumpDuration).SetEase(jumpEase));

        // 阶段2: 下掉 (从跳起的最高点往下掉 fallDistance)
        seq.Append(transform.DOLocalMoveY(originalLocalPos.y + jumpHeight - fallDistance, fallDuration).SetEase(fallEase));

        // 同时: 下掉阶段渐变透明
        seq.Join(spriteRenderer.DOFade(targetAlpha, fadeDuration).SetEase(fadeEase));

        // ---- 禁用跟随主角的灵光球(钥匙) ----
        // 锁解锁后, 跟随主角的钥匙灵光球可以消失了, 直接禁用最省事
        KeyOrb.DisableAllOrbs();

        // ---- 黑暗覆盖层: 淡出 (和锁动画同时进行) ----
        if (darknessOverlay != null)
        {
            darknessOverlay.DOFade(0f, darknessFadeOutDuration).SetEase(Ease.OutQuad)
                .OnComplete(() =>
                {
                    // 罩子淡出后禁用 (不销毁, 重生时 ResetAllLocks 会重新激活)
                    darknessOverlay.gameObject.SetActive(false);
                });
        }

        // 锁动画完成: 禁用锁 (不销毁, 重生时 ResetAllLocks 会重新激活)
        seq.OnComplete(() =>
        {
            gameObject.SetActive(false);
        });
    }
}
