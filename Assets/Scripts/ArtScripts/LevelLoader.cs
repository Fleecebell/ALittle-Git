using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelLoader : MonoBehaviour
{
    public Animator transition;
    public float fadeInDuration = 0.88f;
    public float fadeOutDuration = 0.889f;

    private static bool isTransitioning = false;

    void Start()
    {
        if (isTransitioning)
        {
            transition.enabled = true;
            transition.Play("CircleWipe_02");
            StartCoroutine(DisableAfterFadeOut());
        }
        else
        {
            transition.enabled = false;
        }
    }

    IEnumerator DisableAfterFadeOut()
    {
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            if (transition.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f)
                break;
            yield return null;
        }
        transition.enabled = false;
        isTransitioning = false;
    }

    public void LoadNextLevel()
    {
        StopLevelTimer();
        StartCoroutine(LoadLevel(SceneManager.GetActiveScene().buildIndex + 1));
    }

    // 通关瞬间冻结计时，把当前耗时存为成绩（供后续显示）
    private void StopLevelTimer()
    {
        UIManager ui = FindObjectOfType<UIManager>();
        if (ui != null)
            ui.StopTimer();
    }

    IEnumerator LoadLevel(int levelIndex)
    {
        isTransitioning = true;
        transition.enabled = true;
        transition.speed = 1f;

        // ★★★ 重置动画机，解决覆盖动画不播放的问题 ★★★
        transition.Rebind();

        transition.SetTrigger("Start");

        yield return new WaitForSeconds(fadeInDuration);
        SceneManager.LoadScene(levelIndex);
    }
}