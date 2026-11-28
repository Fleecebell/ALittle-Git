using System.Collections;
using UnityEngine;

// ======================================================================
// TumbleweedSpawner —— 风滚草生成器
// --------------------------------------------------------------------------
// 设计思路:
//   1. 挂在屏幕右侧外的空 GameObject 上, 定时生成风滚草往左滚.
//   2. 数量上限: 场景里同时存在的风滚草超过 maxCount 就停止生成,
//      等有风滚草销毁(出屏幕/超时)后才继续.
//   3. 随机大小: 每个风滚草 scale 在 [minScale, maxScale] 间随机,
//      质量按比例缩放 (大的重小的轻), CircleCollider2D 自动跟着缩放.
//   4. 随机位置: 生成点 Y 轴有小范围抖动, 避免排成一条直线.
//   5. 启用/禁用: 勾选 Enable Spawning 开关, 可在 Inspector 里运行时控制.
//   6. 订阅 Tumbleweed.OnDestroyed 事件, 风滚草销毁时自动减计数.
// ======================================================================
public class TumbleweedSpawner : MonoBehaviour
{
    [Header("=== 预制体 ===")]
    [Tooltip("风滚草预制体 (Tumbleweed.cs 挂的那个)")]
    [SerializeField] private Tumbleweed prefab;

    [Header("=== 生成参数 ===")]
    [Tooltip("是否启用生成. 运行时可在 Inspector 实时开关")]
    [SerializeField] private bool enableSpawning = true;

    [Tooltip("生成间隔(秒). 每隔这么久生成一个")]
    [SerializeField] private float spawnInterval = 3f;

    [Tooltip("场景里同时存在的风滚草数量上限. 达到此数停止生成, 有销毁后才继续")]
    [SerializeField] private int maxCount = 8;

    [Header("=== 随机大小 ===")]
    [Tooltip("最小缩放. 0.6 = 比原 sprite 小 40%")]
    [SerializeField] private float minScale = 0.6f;

    [Tooltip("最大缩放. 1.4 = 比原 sprite 大 40%")]
    [SerializeField] private float maxScale = 1.4f;

    [Tooltip("基础质量. 实际质量 = baseMass × scale (大的重小的轻)")]
    [SerializeField] private float baseMass = 0.1f;

    [Header("=== 生成位置 ===")]
    [Tooltip("Y 轴随机抖动范围. 生成时 Y = 生成器位置.y + Random(-yJitter, yJitter)")]
    [SerializeField] private float yJitter = 1f;

    [Header("=== 调试 ===")]
    [Tooltip("运行时显示当前存活数量, 方便调参")]
    [SerializeField] private bool showDebug = false;

    private int activeCount = 0;       // 当前存活数量
    private Coroutine spawnRoutine;    // 协程引用, 用于停止

    void OnEnable()
    {
        if (enableSpawning)
        {
            spawnRoutine = StartCoroutine(SpawnLoop());
        }
    }

    void OnDisable()
    {
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }
    }

    /// <summary>
    /// 定时生成协程. 每隔 spawnInterval 秒尝试生成一个.
    /// 数量满了就跳过, 等下次.
    /// </summary>
    private IEnumerator SpawnLoop()
    {
        // 首次等一个间隔再开始, 避免场景一加载就刷一堆
        yield return new WaitForSeconds(spawnInterval);

        while (true)
        {
            if (activeCount < maxCount)
            {
                SpawnOne();
            }
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    /// <summary>
    /// 生成一个风滚草. 随机大小、随机 Y、订阅销毁事件.
    /// </summary>
    private void SpawnOne()
    {
        if (prefab == null)
        {
            Debug.LogWarning("[TumbleweedSpawner] 预制体没拖! Inspector 里把 Tumbleweed 预制体拖进 Prefab 字段");
            return;
        }

        // 随机大小
        float scale = Random.Range(minScale, maxScale);
        // 生成位置 = 生成器自身的位置 + Y 轴随机抖动
        // 把 TumbleweedSpawner 物体摆到屏幕右外想要生成的地方即可
        Vector3 pos = transform.position + new Vector3(0f, Random.Range(-yJitter, yJitter), 0f);

        // 实例化
        Tumbleweed t = Instantiate(prefab, pos, Quaternion.identity);

        // 应用随机大小: 基于预制体原始 scale 乘以随机系数 (不替换, 保留预制体大小设定)
        // 比如预制体 scale=0.3, scale 系数=1.4 → 最终 0.42 (比预制体大 40%)
        t.transform.localScale = t.transform.localScale * scale;

        // 质量按比例缩放 (大的重小的轻, 物理表现更真实)
        Rigidbody2D rb = t.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.mass = baseMass * scale;
        }

        // 订阅销毁事件, 风滚草销毁时减计数
        t.OnDestroyed += HandleTumbleweedDestroyed;

        activeCount++;

        if (showDebug)
        {
            Debug.Log($"[TumbleweedSpawner] 生成一个 (scale={scale:F2}), 当前存活={activeCount}/{maxCount}");
        }
    }

    /// <summary>
    /// 风滚草销毁时的回调. 减计数, 允许生成器继续生成.
    /// </summary>
    private void HandleTumbleweedDestroyed(Tumbleweed t)
    {
        if (activeCount > 0) activeCount--;

        if (showDebug)
        {
            Debug.Log($"[TumbleweedSpawner] 销毁一个, 当前存活={activeCount}/{maxCount}");
        }
    }

    /// <summary>
    /// 运行时可调: 立即清空所有风滚草 (比如场景切换时)
    /// </summary>
    public void ClearAll()
    {
        Tumbleweed[] all = FindObjectsOfType<Tumbleweed>();
        foreach (Tumbleweed t in all)
        {
            Destroy(t.gameObject);
        }
        activeCount = 0;
    }
}
