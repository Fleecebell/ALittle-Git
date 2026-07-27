using UnityEngine;
using UnityEngine.SceneManagement;

public class NextLevel : MonoBehaviour
{
    private int currentScene;

    void Start()
    {
        currentScene = SceneManager.GetActiveScene().buildIndex;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            int nextScene = currentScene + 1;

            if (nextScene < SceneManager.sceneCountInBuildSettings)
            {
                // 在当前场景中查找 LevelLoader 组件
                LevelLoader loader = FindObjectOfType<LevelLoader>();
                if (loader != null)
                {
                    loader.LoadNextLevel();
                }
                else
                {
                    // 容错：如果没找到，直接加载
                    Debug.LogWarning("未找到 LevelLoader，直接加载场景");
                    SceneManager.LoadScene(nextScene);
                }
            }
            else
            {
                Debug.Log("没有更多的关卡了！");
            }
        }
    }
}