using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    public GameObject targetPanel;
    private bool isPanelActive;
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

    [Header("=== 音量控制 ===")]
    [Tooltip("控制全局音乐的 AudioSource（音乐走这条）。可拖入多个")]
    public List<AudioSource> musicSources;

    [Tooltip("控制音效的 AudioSource（除音乐外的所有声音走这条）。可拖入多个")]
    public List<AudioSource> sfxSources;

    [Tooltip("音乐滑条：把 Slider 的 On Value Changed 事件接到这里")]
    public void SetMusicVolume(float value)
    {
        ApplyVolume(musicSources, value);
    }

    [Tooltip("音效滑条：把 Slider 的 On Value Changed 事件接到这里")]
    public void SetSFXVolume(float value)
    {
        ApplyVolume(sfxSources, value);
    }

    private void ApplyVolume(List<AudioSource> sources, float value)
    {
        if (sources == null) return;
        foreach (var src in sources)
        {
            if (src != null)
                src.volume = value;
        }
    }
}
