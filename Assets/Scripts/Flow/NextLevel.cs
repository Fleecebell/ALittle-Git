using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 通关门: 玩家带钥匙走到门 → 触发锁动画 → 动画播完后通关
///
/// 判断逻辑 (简化版):
///   - 有锁(lockAnimator != null) → 需要钥匙才能通关
///   - 没锁 → 默认已解锁, 直接通关
///
/// 碰门时:
///   1. 已解锁 → 直接通关
///   2. 未解锁 + 有钥匙 → 触发锁动画 → 动画播完 → 通关
///   3. 未解锁 + 没钥匙 → 提示, 不通关
/// </summary>
public class NextLevel : MonoBehaviour
{
    [Header("锁动画(可选)")]
    [Tooltip("拖入锁的 GameObject（带 LockAnimator 脚本）. 有锁就需要钥匙才能通关; 留空则可直接通关")]
    [SerializeField] private LockAnimator lockAnimator;

    [Header("调试")]
    [Tooltip("勾选后会在Console打印钥匙/锁的状态信息, 方便排查")]
    [SerializeField] private bool showDebug = true;

    /// <summary>是否已解锁</summary>
    [HideInInspector] public bool isUnlocked = false;

    // ===================== 静态列表 (重生时重置所有门) =====================
    private static List<NextLevel> allDoors = new List<NextLevel>();

    private int currentScene;

    // 触发区内的玩家数量. P1 / P2 都算, 用计数可以避免"一人离开就当没人"的误判
    private int playersInTrigger = 0;
    private bool playerInTrigger => playersInTrigger > 0;

    // 锁动画是否正在播放
    private bool isUnlocking = false;
    // 是否已经触发过关流程 (防止重复加载下一关)
    private bool hasCleared = false;

    private void Awake()
    {
        // 只在 Awake 添加一次, 不在 OnDisable 移除.
        // 这样重生时能重置所有门的状态.
        if (!allDoors.Contains(this))
            allDoors.Add(this);
    }

    void Start()
    {
        currentScene = SceneManager.GetActiveScene().buildIndex;

        // 注意: 这里不再写 Key.hasKey = false.
        // 钥匙标志改由 Key 在"每次场景加载完成"时统一清零(见 Key.OnAnySceneLoaded).
        // 否则场景里任何一扇后激活/后实例化的门, 都会把玩家已经拿到的钥匙清掉.

        // 没有锁 → 默认已解锁, 可直接通关
        // 有锁 → 需要钥匙
        if (lockAnimator == null)
        {
            isUnlocked = true;
            if (showDebug) Debug.Log($"[NextLevel] {gameObject.name}: 无锁, 默认已解锁, 可直接通关");
        }
        else
        {
            isUnlocked = false;
            if (showDebug) Debug.Log($"[NextLevel] {gameObject.name}: 有锁, 需要钥匙才能通关");
        }
    }

    /// <summary>
    /// 重置所有门 (重生时由 DeathRespawnVFX 调用):
    /// 重置 isUnlocked + isUnlocking + 触发区状态
    /// 有锁的门重置回锁定状态, 无锁的门保持解锁
    /// </summary>
    public static void ResetAllDoors()
    {
        foreach (var door in allDoors.ToArray())
        {
            if (door != null)
            {
                door.isUnlocking = false;
                door.hasCleared = false;
                door.playersInTrigger = 0;
                // 有锁 → 锁定; 无锁 → 解锁
                door.isUnlocked = (door.lockAnimator == null);
            }
        }
    }

    // P1(Player) 和 P2(Player2) 都能触发:
    // Key 允许 P1/P2 任意一方捡钥匙, 通关门也必须允许任意一方通过,
    // 否则 P2 拿到钥匙后走到门前不会有任何反应(连日志都不会打印).
    private static bool IsPlayer(Collider2D collision)
    {
        return collision.CompareTag("Player") || collision.CompareTag("Player2");
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsPlayer(collision))
            return;

        playersInTrigger++;

        if (showDebug)
            Debug.Log($"[NextLevel] {collision.gameObject.name} 碰到门. isUnlocked={isUnlocked}, hasKey={Key.hasKey}, isUnlocking={isUnlocking}");

        if (isUnlocked || Key.hasKey)
        {
            TryClear();
        }
        else if (showDebug)
        {
            Debug.Log("[NextLevel] 门是锁着的，需要先收集钥匙！");
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (IsPlayer(collision))
            playersInTrigger = Mathf.Max(0, playersInTrigger - 1);
    }

    private void Update()
    {
        // 兜底轮询: 玩家已经在触发区里时才检查
        // (比如先站进门里、再去拿钥匙; 或者物理触发事件被漏掉的情况)
        if (hasCleared || !playerInTrigger || isUnlocking)
            return;

        if (isUnlocked || Key.hasKey)
            TryClear();
    }

    /// <summary>
    /// 尝试通关/解锁. 已解锁 → 通关; 有钥匙 → 播解锁动画; 没钥匙 → 不动.
    /// </summary>
    private void TryClear()
    {
        if (hasCleared || isUnlocking)
            return;

        // 已解锁 → 直接通关
        if (isUnlocked)
        {
            GoToNextLevel();
            return;
        }

        // 有钥匙 → 触发解锁动画
        if (Key.hasKey)
        {
            if (showDebug) Debug.Log("[NextLevel] 玩家有钥匙, 开始解锁动画");
            StartCoroutine(UnlockSequence());
        }
    }

    /// <summary>
    /// 解锁流程协程: 播放锁动画 → 等动画播完 → 通关(如果玩家还在门内)
    /// </summary>
    private IEnumerator UnlockSequence()
    {
        isUnlocking = true;
        isUnlocked = true;

        // 播放锁的解锁动画 (跳起 → 下掉 → 透明 → 销毁, 黑暗覆盖层淡出)
        if (lockAnimator != null)
        {
            if (showDebug) Debug.Log("[NextLevel] 调用 lockAnimator.PlayUnlockAnimation()");
            lockAnimator.PlayUnlockAnimation();
            // 等锁动画播完 (跳起时长 + 下掉时长)
            float waitTime = lockAnimator.TotalDuration;
            if (showDebug) Debug.Log($"[NextLevel] 等待锁动画播完, 时长={waitTime}秒");
            yield return new WaitForSeconds(waitTime);
        }
        else
        {
            yield return null;
        }

        isUnlocking = false;

        // 动画期间玩家死亡重生 → 门被重置回锁定状态, 本次解锁作废
        if (!isUnlocked)
        {
            if (showDebug) Debug.Log("[NextLevel] 解锁过程被重置(死亡重生), 取消本次通关");
            yield break;
        }

        Debug.Log("[NextLevel] 通关门已解锁！");

        // 如果玩家还在门内, 通关
        if (playerInTrigger)
        {
            if (showDebug) Debug.Log("[NextLevel] 玩家还在门内, 通关");
            GoToNextLevel();
        }
        else
        {
            if (showDebug) Debug.Log("[NextLevel] 玩家已离开门, 不自动通关, 走回来再碰门通关");
        }
    }

    private void GoToNextLevel()
    {
        // 防止同一帧/同一扇门重复触发加载
        if (hasCleared) return;
        hasCleared = true;

        // [Telemetry] 通关埋点：记录本关时长/死亡数/跳跃数，并把格子统计一次性发完。
        // 放在加载下一关之前，此时统计还是本关的。
        Telemetry.OnLevelClear();

        int nextScene = currentScene + 1;

        if (nextScene < SceneManager.sceneCountInBuildSettings)
        {
            LevelLoader loader = FindObjectOfType<LevelLoader>();
            if (loader != null)
            {
                loader.LoadNextLevel();
            }
            else
            {
                Debug.LogWarning("[NextLevel] 未找到 LevelLoader，直接加载场景 " + nextScene);
                SceneManager.LoadScene(nextScene);
            }
        }
        else
        {
            Debug.Log("[NextLevel] 没有更多的关卡了！当前场景序号=" + currentScene + ", Build Settings里总场景数=" + SceneManager.sceneCountInBuildSettings);
        }
    }
}
