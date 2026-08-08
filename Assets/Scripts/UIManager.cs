using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public GameObject targetPanel;
    private bool isPanelActive;

    [Header("=== 音量滑条（自动绑定，一般无需手动设置）===")]
    [Tooltip("音乐滑条路径（相对本物体）。留空则尝试按默认路径查找")]
    public string musicSliderPath = "SetPanel/Music/Slider";
    [Tooltip("音效滑条路径（相对本物体）。留空则尝试按默认路径查找")]
    public string sfxSliderPath = "SetPanel/SFX/Slider";

    void Start()
    {
        AutoBindSliders();
    }

    public void TogglePanel()
    {
        isPanelActive = !isPanelActive;
        targetPanel.SetActive(isPanelActive);
    }
    public void OpenPanel()
    {
        isPanelActive = true;
        targetPanel.SetActive(true);
    }
    public void ClosePanel()
    {
        isPanelActive = false;
        targetPanel.SetActive(false);
    }
    public void BackToMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
    }

    /// <summary>
    /// 自动把 Music / SFX 两个滑条的 On Value Changed 绑定到 MusicManager 的音量方法。
    /// 这样无需在每个场景手动拖滑条事件。
    /// </summary>
    private void AutoBindSliders()
    {
        if (MusicManager.Instance == null)
        {
            Debug.LogWarning("UIManager: 找不到 MusicManager 实例，滑条音量控制未绑定");
            return;
        }

        BindSlider(musicSliderPath, MusicManager.Instance.SetMusicVolume, "音乐");
        BindSlider(sfxSliderPath, MusicManager.Instance.SetSFXVolume, "音效");
    }

    private void BindSlider(string path, UnityEngine.Events.UnityAction<float> method, string name)
    {
        if (string.IsNullOrEmpty(path)) return;

        Transform sliderT = transform.Find(path);
        if (sliderT == null)
        {
            Debug.LogWarning($"UIManager: 找不到滑条路径 {path}（{name}音量滑条未绑定）");
            return;
        }

        var slider = sliderT.GetComponent<Slider>();
        if (slider == null)
        {
            Debug.LogWarning($"UIManager: {path} 上没有 Slider 组件（{name}音量滑条未绑定）");
            return;
        }

        // 移除旧监听（防止重复绑定），再添加
        slider.onValueChanged.RemoveListener(method);
        slider.onValueChanged.AddListener(method);
    }
}
