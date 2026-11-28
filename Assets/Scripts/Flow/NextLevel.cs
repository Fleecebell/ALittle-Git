using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 通关门: 玩家碰到门就算通关 (有锁的门需要手里有钥匙)
///
/// 判断逻辑 (简化版):
///   - 有锁(lockAnimator != null) → 需要钥匙才能通关
///   - 没锁 → 默认已解锁, 直接通关
///
/// 碰门时:
///   1. 已解锁 → 碰到即通关
///   2. 未解锁 + 有钥匙 → 碰到即判定通关 (冻结计时器 + 上报埋点),
///      锁动画只作表现, 播完必定切关, 不需要玩家留在门里再碰一次
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

    // 记录主角仍在触发区内的碰撞体数量。
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
                // 已经判定通关的门不再重置: 通关瞬间已经冻结计时并上报,
                // 剩下的只是转场, 这时玩家死亡重生不应该把这次通关"撤回"
                if (door.hasCleared) continue;

                door.isUnlocking = false;
                door.hasCleared = false;
                door.playersInTrigger = 0;
                // 有锁 → 锁定; 无锁 → 解锁
                door.isUnlocked = (door.lockAnimator == null);
            }
        }
    }

    private static bool IsMainPlayer(Collider2D collision)
    {
        return collision.CompareTag("Player");
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsMainPlayer(collision))
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
        if (IsMainPlayer(collision))
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
    /// 尝试通关. 已解锁 → 碰到即通关; 有钥匙 → 碰到即判定通关并播解锁动画; 没钥匙 → 不动.
    /// </summary>
    private void TryClear()
    {
        if (hasCleared)
            return;

        // 已解锁 → 碰到即通关
        if (isUnlocked)
        {
            MarkCleared();
            LoadNextLevelScene();
            return;
        }

        // 有钥匙 → 碰到的这一帧就算通关, 锁动画只作表现
        if (Key.hasKey)
        {
            if (isUnlocking) return;

            if (showDebug) Debug.Log("[NextLevel] 玩家有钥匙, 碰到门即判定通关, 播放解锁动画");
            MarkCleared();
            StartCoroutine(UnlockSequence());
        }
    }

    /// <summary>
    /// 通关判定瞬间 (一关只执行一次):
    /// 1) 冻结 UI 计时器, 取"碰到终点这一帧"的时间作为本关成绩
    /// 2) 用这个时间上报通关埋点 (服务器直接读 duration_s, 不自己算时长)
    /// </summary>
    private void MarkCleared()
    {
        if (hasCleared) return;
        hasCleared = true;

        float finalSeconds = -1f;
        UIManager ui = FindObjectOfType<UIManager>();
        if (ui != null)
        {
            ui.StopTimer();
            finalSeconds = ui.score;
        }
        else
        {
            Debug.LogWarning("[NextLevel] 场景里没有 UIManager, 计时器无法冻结, 本关时长退回埋点内部累加值");
        }

        Telemetry.OnLevelClear(finalSeconds);
    }

    /// <summary>
    /// 解锁流程协程: 播放锁动画 → 动画播完必定切关 (不再要求玩家留在门里)
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

        if (showDebug) Debug.Log("[NextLevel] 通关门已解锁, 切关");

        // 通关在碰到门的那一帧就已经判定了, 这里只负责进下一关
        LoadNextLevelScene();
    }

    /// <summary>
    /// 加载下一关 (通关判定由 MarkCleared 负责, 这里只做转场)
    /// </summary>
    private void LoadNextLevelScene()
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
