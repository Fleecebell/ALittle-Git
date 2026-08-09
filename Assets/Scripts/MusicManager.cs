using UnityEngine;

/// <summary>
/// MusicManager —— 全局音乐/音效管理器（单例，DontDestroyOnLoad）
///
/// 职责：
///   1. 全局控制音乐音量：每关有一个叫 "music" 的物体，其 AudioSource 上挂好本关 BGM。
///      本脚本在场景加载时找到它并应用保存的音量，保证跨关卡音量不重置。
///   2. 全局控制音效音量，并提供播放接口（素材在检查器拖入）。
///   3. 提供 UI 按钮音效播放接口，按钮监听由挂在各场景的 UIButtonAudio 负责。
///
/// 滑条绑定：
///   SetMusicVolume(float)  —— 音乐滑条 On Value Changed
///   SetSFXVolume(float)     —— 音效滑条 On Value Changed
/// </summary>
public class MusicManager : MonoBehaviour
{
    // 单例
    public static MusicManager Instance { get; private set; }

    [Header("=== 音量存储键 ===")]
    [Tooltip("音乐音量的 PlayerPrefs 键名")]
    public string musicPrefsKey = "MusicVolume";
    [Tooltip("音效音量的 PlayerPrefs 键名")]
    public string sfxPrefsKey = "SFXVolume";

    [Header("=== UI 音效素材（在检查器拖入）===")]
    [Tooltip("按钮移入音效")]
    public AudioClip uiHoverClip;
    [Tooltip("按钮移出音效")]
    public AudioClip uiExitClip;
    [Tooltip("按钮点击音效")]
    public AudioClip uiClickClip;

    // 音效播放器（单例音效源，播放非循环音效）
    private AudioSource sfxSource;

    // 当前音乐音量 / 音效音量（0~1）
    public float MusicVolume { get; private set; }
    public float SFXVolume { get; private set; }

    void Awake()
    {
        // 单例 + DontDestroyOnLoad
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 创建音效播放器（放在当前物体上）
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;

        // 从 PlayerPrefs 读取音量（若没有则默认 1）
        MusicVolume = PlayerPrefs.GetFloat(musicPrefsKey, 1f);
        SFXVolume = PlayerPrefs.GetFloat(sfxPrefsKey, 1f);
    }

    void Start()
    {
        // 应用一次音乐音量到当前场景的音乐物体
        ApplyMusicVolumeToCurrentScene();
    }

    void OnEnable()
    {
        // 场景切换后重新应用音量（Start 只调用一次，这里兜底）
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        ApplyMusicVolumeToCurrentScene();
    }

    // ===================== 音量控制 =====================

    /// <summary>
    /// 设置音乐音量（供音乐滑条 On Value Changed 调用）
    /// </summary>
    public void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(musicPrefsKey, MusicVolume);
        PlayerPrefs.Save();
        ApplyMusicVolumeToCurrentScene();
    }

    /// <summary>
    /// 设置音效音量（供音效滑条 On Value Changed 调用）
    /// </summary>
    public void SetSFXVolume(float value)
    {
        SFXVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(sfxPrefsKey, SFXVolume);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 找到当前场景中名为 "music" 的物体，应用音乐音量
    /// </summary>
    private void ApplyMusicVolumeToCurrentScene()
    {
        var musicObj = GameObject.Find("music");
        if (musicObj == null)
        {
            // 有些场景可能没有 music 物体，忽略
            return;
        }
        var src = musicObj.GetComponent<AudioSource>();
        if (src != null)
            src.volume = MusicVolume;
    }

    // ===================== 播放音效 =====================

    /// <summary>
    /// 播放一个音效（单次、非循环，受全局音效音量控制）
    /// </summary>
    public void PlaySFX(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.volume = SFXVolume;
        sfxSource.PlayOneShot(clip);
    }

    // ===================== UI 按钮音效 =====================

    /// <summary>
    /// 播放按钮移入音效
    /// </summary>
    public void PlayUIHover()
    {
        PlaySFX(uiHoverClip);
    }

    /// <summary>
    /// 播放按钮移出音效
    /// </summary>
    public void PlayUIExit()
    {
        PlaySFX(uiExitClip);
    }

    /// <summary>
    /// 播放按钮点击音效
    /// </summary>
    public void PlayUIClick()
    {
        PlaySFX(uiClickClip);
    }
}
