using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;

/// <summary>
/// 死亡与重生视觉特效 (蔚蓝风格: 极速缩放消失 + 粒子爆发 + 重生点缩放出现)
///
/// 设计要点:
/// 1. 极快: 死亡 0.12s + 重生 0.18s ≈ 0.3s 总时长 (适配"短时间多次死亡"的场景, 不冗余)
/// 2. 不用 Animator 帧动画, 用 DOTween 控制 transform.localScale (零美术成本, 适合方块画风)
/// 3. 静态 isDead 标志位, 死亡期间禁用 MoveFirst/MoveLate 的输入 (防止死亡瞬间还能操作)
/// 4. 挂在 P1 和 P2 两个角色上, 静态 TriggerDeath() 统一触发所有实例
/// 5. 视觉与逻辑分离: 本脚本只管"缩小/放大/粒子", 实际位置重置仍由 MoveLate.DoReset() 完成
/// </summary>
public class DeathRespawnVFX : MonoBehaviour
{
    // ===================== 静态状态 (供 MoveFirst/MoveLate 检查) =====================
    /// <summary>
    /// 全局死亡状态. true=正在播放死亡/重生动画, 主角脚本应在 Update 开头检查并跳过输入处理
    /// </summary>
    public static bool isDead = false;

    // 防止重复触发 (死亡动画进行中再触发会被忽略)
    private static bool isProcessing = false;

    // 所有活跃的实例 (P1 和 P2 各挂一个), 用于统一触发
    private static List<DeathRespawnVFX> activeInstances = new List<DeathRespawnVFX>();

    // ===================== Inspector 参数 =====================
    [Header("=== 时长 (秒) ===")]
    [Tooltip("死亡阶段时长: sprite 从 1 缩到 0 的时间. 越小越快. 0.12 = 极快消失")]
    [SerializeField] private float deathDuration = 0.12f;

    [Tooltip("重生阶段时长: sprite 从 0 放回 1 的时间. 越小越快. 0.18 = 快速弹出")]
    [SerializeField] private float respawnDuration = 0.18f;

    [Header("=== 缓动曲线 ===")]
    [Tooltip("死亡缩小曲线. 推荐保持 InQuad (开始慢结束快, 像被吸走)")]
    [SerializeField] private Ease deathEase = Ease.InQuad;

    [Tooltip("重生放大曲线. 推荐 OutBack (有轻微回弹, 像弹出来) 或 OutQuad (平滑)")]
    [SerializeField] private Ease respawnEase = Ease.OutBack;

    [Header("=== 粒子 (可选, 留空则只有缩放) ===")]
    [Tooltip("死亡时爆发的粒子系统 (向四周飞散). 留空则不播, 只有缩放效果")]
    [SerializeField] private ParticleSystem deathParticles;

    [Tooltip("重生时爆发的粒子系统 (轻微上飘). 留空则不播")]
    [SerializeField] private ParticleSystem respawnParticles;

    [Header("=== 调试 ===")]
    [SerializeField] private bool showDebug = false;

    // ===================== 内部状态 =====================
    private Vector3 originalScale;   // 角色 sprite 的原始缩放 (脚本启动时记录, 重生时恢复)

    // ===================== 生命周期 =====================
    private void Awake()
    {
        // 记录原始缩放, 重生动画结束时要恢复成这个值
        originalScale = transform.localScale;
    }

    private void OnEnable()
    {
        // 注册到静态列表 (供 TriggerDeath 统一调用)
        if (!activeInstances.Contains(this))
            activeInstances.Add(this);
    }

    private void OnDisable()
    {
        activeInstances.Remove(this);
    }

    // ===================== 静态触发入口 =====================
    /// <summary>
    /// 触发死亡+重生流程. 供 Die.cs (碰熔岩) 和 MoveLate.CheckReset (按R) 调用.
    /// 内部有防重复: 正在播放中再调用会被忽略.
    /// </summary>
    public static void TriggerDeath()
    {
        if (isProcessing)
        {
            if (activeInstances.Count > 0 && activeInstances[0].showDebug)
                Debug.Log("[DeathVFX] 已在死亡流程中, 忽略重复触发");
            return;
        }

        // 找一个实例启动协程 (协程里统一驱动所有实例)
        if (activeInstances.Count == 0)
        {
            Debug.LogWarning("[DeathVFX] 没有活跃的 DeathRespawnVFX 实例, 直接重置");
            // 没有VFX实例也要保证重置逻辑能跑, 直接调 MoveLate.DoReset
            var ml = Object.FindObjectOfType<MoveLate>();
            if (ml != null) ml.DoReset();
            return;
        }

        // 用第一个实例启动总流程协程
        activeInstances[0].StartCoroutine(RunDeathRespawnSequence());
    }

    /// <summary>
    /// 死亡+重生总流程协程 (统一驱动 P1 和 P2 两个实例)
    /// 流程: 禁用输入 → 死亡缩放+粒子 → 传送重置 → 重生缩放+粒子 → 恢复输入
    /// </summary>
    private static IEnumerator RunDeathRespawnSequence()
    {
        isProcessing = true;
        isDead = true;   // 禁用 MoveFirst/MoveLate 的输入

        if (activeInstances.Count > 0 && activeInstances[0].showDebug)
            Debug.Log("[DeathVFX] 死亡阶段开始");

        // ---- 阶段1: 死亡 (所有实例同时缩小 + 死亡粒子) ----
        float maxDeathDuration = 0f;
        foreach (var inst in activeInstances)
        {
            float d = inst.PlayDeath();
            if (d > maxDeathDuration) maxDeathDuration = d;
        }

        // 等死亡动画播完 (取最长那个实例的时长)
        yield return new WaitForSeconds(maxDeathDuration);

        // ---- 阶段2: 传送重置 (调 MoveLate 的原重置逻辑, 保证和原来一致) ----
        var ml = Object.FindObjectOfType<MoveLate>();
        if (ml != null)
        {
            ml.DoReset();
        }
        else
        {
            Debug.LogWarning("[DeathVFX] 找不到 MoveLate, 无法重置位置");
        }

        // 瞬移后停一帧让物理稳定 (防止刚传送就触发碰撞导致状态错乱)
        yield return null;

        // ---- 阶段3: 重生 (所有实例同时放大 + 重生粒子) ----
        if (activeInstances.Count > 0 && activeInstances[0].showDebug)
            Debug.Log("[DeathVFX] 重生阶段开始");

        float maxRespawnDuration = 0f;
        foreach (var inst in activeInstances)
        {
            float d = inst.PlayRespawn();
            if (d > maxRespawnDuration) maxRespawnDuration = d;
        }

        // 等重生动画播完
        yield return new WaitForSeconds(maxRespawnDuration);

        // ---- 结束: 恢复输入 ----
        isDead = false;
        isProcessing = false;

        if (activeInstances.Count > 0 && activeInstances[0].showDebug)
            Debug.Log("[DeathVFX] 死亡重生流程完成, 恢复控制");
    }

    // ===================== 实例方法 =====================
    /// <summary>
    /// 播放死亡动画: sprite 缩小到 0 + 死亡粒子爆发
    /// 返回动画时长
    /// </summary>
    private float PlayDeath()
    {
        // 停止物理 (防止缩小过程中还在移动)
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.velocity = Vector2.zero;

        // DOTween 缩放到 0 (用 SetEase 指定曲线)
        transform.DOKill(); // 先清掉旧动画, 防止冲突
        transform.DOScale(Vector3.zero, deathDuration).SetEase(deathEase);

        // 死亡粒子爆发 (在角色当前位置)
        if (deathParticles != null)
        {
            deathParticles.transform.position = transform.position;
            deathParticles.Play();
        }

        return deathDuration;
    }

    /// <summary>
    /// 播放重生动画: sprite 从 0 放回原始大小 + 重生粒子
    /// 返回动画时长
    /// </summary>
    private float PlayRespawn()
    {
        // 此时角色已被 DoReset 传送到重生点
        // 先确保 scale=0 (死亡动画结束时应该是0, 但保险起见再设一下)
        transform.localScale = Vector3.zero;

        transform.DOKill();
        // DOScale 到原始大小 (不是 Vector3.one, 因为角色可能有基础缩放)
        transform.DOScale(originalScale, respawnDuration).SetEase(respawnEase);

        // 重生粒子爆发 (在重生点位置 = 角色当前位置)
        if (respawnParticles != null)
        {
            respawnParticles.transform.position = transform.position;
            respawnParticles.Play();
        }

        return respawnDuration;
    }

    // ===================== 编辑器一键测试 =====================
    /// <summary>
    /// 编辑器右键菜单手动触发 (调试用)
    /// </summary>
    [ContextMenu("测试死亡重生")]
    private void TestDeathRespawn()
    {
        TriggerDeath();
    }
}
