using UnityEngine;
using UnityEngine.SceneManagement;

public class Next_Level : MonoBehaviour
{
    private int currentScene;

    void Start()
    {
        // 初始化当前场景索引
        currentScene = SceneManager.GetActiveScene().buildIndex;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            int nextScene = currentScene + 1;

            // 只加这一句判断
            if (nextScene < SceneManager.sceneCountInBuildSettings)
            {
                SceneManager.LoadScene(nextScene);
            }
            else
            {
                Debug.Log("已是最后一关");
            }
        }
    }
}
