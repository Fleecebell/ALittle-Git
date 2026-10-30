using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelLoader : MonoBehaviour
{
    public Animator transition;
    public float fadeInDuration = 0.88f;
    public float fadeOutDuration = 0.889f;
    [Tooltip("转场动画播放速度: 1=原速, 0.5=慢一倍")]
    public float transitionSpeed = 0.6f;

    private static bool isTransitioning = false;
    private bool isLoading = false;

    void Start()
    {
        if (isTransitioning)
        {
            transition.enabled = true;
            transition.speed = transitionSpeed;
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
        // ★★★ 不能按固定时长禁用动画机 ★★★
        // CircleWipe_02 实际长约 0.917s, 而 fadeOutDuration(0.88/0.889) 比它短;
        // 且场景加载后第一帧容易卡顿, 动画机会比计时晚一帧启动。
        // 到点强禁用时动画没播完, 图片会冻结在半路(卡在屏幕中间不走),
        // 所以这里必须等动画真正播完才禁用。
        // 超时兜底要按速度缩放, 否则把速度调慢时会被误杀
        float timeout = fadeOutDuration + 2f / Mathf.Max(0.1f, transitionSpeed);
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            AnimatorStateInfo state = transition.GetCurrentAnimatorStateInfo(0);
            if (state.IsName("CircleWipe_02") && state.normalizedTime >= 1f)
                break;
            yield return null;
        }
        transition.enabled = false;
        isTransitioning = false;
    }

    public void LoadNextLevel()
    {
        // 防重复触发: 淡入期间重进通关门会再次调用, 导致 Rebind 把正在播的
        // 转场动画倒回重播, 看起来像卡住/重来一次
        if (isLoading)
            return;

        isLoading = true;
        StartCoroutine(LoadLevel(SceneManager.GetActiveScene().buildIndex + 1));
    }

    IEnumerator LoadLevel(int levelIndex)
    {
        isTransitioning = true;
        transition.enabled = true;
        transition.speed = transitionSpeed;

        // ★★★ 重置动画机，解决覆盖动画不播放的问题 ★★★
        transition.Rebind();

        transition.SetTrigger("Start");

        // 等圆圈真正盖满屏幕再切场景 (CircleWipe_01 走到约 75% 即完全覆盖)。
        // 速度放慢后等待会自动变长; fadeInDuration 只作为最低等待时间。
        // 不能等超过 100%: 动画机里的退出过渡会提前开始播 CircleWipe_02。
        float elapsed = 0f;
        float timeout = fadeInDuration + 2f / Mathf.Max(0.1f, transitionSpeed);
        while (elapsed < fadeInDuration || !IsScreenCovered())
        {
            elapsed += Time.deltaTime;
            if (elapsed >= timeout)
                break; // 动画异常没盖住也要照常切场景, 避免卡死
            yield return null;
        }
        SceneManager.LoadScene(levelIndex);
    }

    bool IsScreenCovered()
    {
        AnimatorStateInfo s = transition.GetCurrentAnimatorStateInfo(0);
        return s.IsName("CircleWipe_01") && s.normalizedTime >= 0.75f;
    }
}