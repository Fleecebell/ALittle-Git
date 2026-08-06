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
    // 玩家是否在门触发器内
    private bool playerInTrigger = false;
    // 锁动画是否正在播放
    private bool isUnlocking = false;

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

        // 新场景开始时重置钥匙标志
        Key.hasKey = false;

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
    /// 重置 isUnlocked + isUnlocking + playerInTrigger
    /// 有锁的门重置回锁定状态, 无锁的门保持解锁
    /// </summary>
    public static void ResetAllDoors()
    {
        foreach (var door in allDoors.ToArray())
        {
            if (door != null)
            {
                door.isUnlocking = false;
                door.playerInTrigger = false;
                // 有锁 → 锁定; 无锁 → 解锁
                door.isUnlocked = (door.lockAnimator == null);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.gameObject.CompareTag("Player"))
            return;

        playerInTrigger = true;

        if (showDebug)
            Debug.Log($"[NextLevel] 玩家碰到门. isUnlocked={isUnlocked}, hasKey={Key.hasKey}, isUnlocking={isUnlocking}");

        // 已解锁 → 直接通关
        if (isUnlocked)
        {
            GoToNextLevel();
            return;
        }

        // 未解锁 (有锁)
        if (!isUnlocking)
        {
            if (Key.hasKey)
            {
                // 有钥匙 → 触发解锁动画
                if (showDebug) Debug.Log("[NextLevel] 玩家有钥匙, 开始解锁动画");
                StartCoroutine(UnlockSequence());
            }
            else
            {
                // 没钥匙 → 提示
                Debug.Log("[NextLevel] 门是锁着的，需要先收集钥匙！");
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
            playerInTrigger = false;
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
