using UnityEngine;
using UnityEngine.SceneManagement;

public class NextLevel : MonoBehaviour
{
    [Header("钥匙设置")]
    [Tooltip("拖入通关所需的钥匙 GameObject（留空则无需钥匙可直接通关）")]
    [SerializeField] private GameObject requiredKey;

    /// <summary>
    /// 是否已解锁（由 Key 脚本设置）
    /// </summary>
    [HideInInspector] public bool isUnlocked = false;

    private int currentScene;

    void Start()
    {
        currentScene = SceneManager.GetActiveScene().buildIndex;

        // 如果没有设置钥匙，则默认已解锁
        if (requiredKey == null)
        {
            isUnlocked = true;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.gameObject.CompareTag("Player"))
            return;

        // 需要钥匙但未解锁 → 无反应
        if (requiredKey != null && !isUnlocked)
        {
            Debug.Log("门是锁着的，需要先收集钥匙！");
            return;
        }

        // 无需钥匙 / 已解锁 → 通关
        GoToNextLevel();
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
                Debug.LogWarning("未找到 LevelLoader，直接加载场景");
                SceneManager.LoadScene(nextScene);
            }
        }
        else
        {
            Debug.Log("没有更多的关卡了！");
        }
    }

    /// <summary>
    /// 由 Key 脚本调用，解锁此门
    /// </summary>
    public void Unlock()
    {
        isUnlocked = true;
        Debug.Log("通关门已解锁！");
    }
}