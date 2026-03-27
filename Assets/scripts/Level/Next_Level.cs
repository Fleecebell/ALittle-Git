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

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            int nextScene = currentScene + 1;
            SceneManager.LoadScene(nextScene);
        }
    }
}
