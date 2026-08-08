using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// UIButtonAudio —— 监听本场景中指定按钮的 移入 / 移出 / 点击 音效
///
/// 挂载位置：放到每个场景中任意一个激活的物体上（如 Canvas）。
/// 配置：在检查器把本场景所有需要播放音效的按钮拖入 uiButtons 数组。
///
/// 原理：
///   给每个按钮添加 EventTrigger 监听 PointerEnter/PointerExit，
///   并用 onClick.AddListener 监听点击，最终调用 MusicManager 播放对应音效。
///   该组件随场景销毁，不会残留，也不会有跨场景的重复绑定问题。
/// </summary>
public class UIButtonAudio : MonoBehaviour
{
    [Header("=== 需要监听的按钮（在检查器拖入）===")]
    [Tooltip("本场景所有需要播放 移入/移出/点击 音效的按钮")]
    public Button[] uiButtons;

    void Start()
    {
        BindButtons();
    }

    /// <summary>
    /// 给按钮数组中的每个按钮添加移入/移出/点击监听
    /// </summary>
    private void BindButtons()
    {
        if (uiButtons == null) return;

        foreach (var btn in uiButtons)
        {
            if (btn == null) continue;

            // 添加 EventTrigger 组件（若已存在则复用）
            var trigger = btn.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = btn.gameObject.AddComponent<EventTrigger>();

            // 移入
            AddTrigger(trigger, EventTriggerType.PointerEnter, OnPointerEnter);
            // 移出
            AddTrigger(trigger, EventTriggerType.PointerExit, OnPointerExit);
            // 点击
            btn.onClick.AddListener(OnClick);
        }
    }

    private void OnPointerEnter()
    {
        if (MusicManager.Instance != null)
            MusicManager.Instance.PlayUIHover();
    }

    private void OnPointerExit()
    {
        if (MusicManager.Instance != null)
            MusicManager.Instance.PlayUIExit();
    }

    private void OnClick()
    {
        if (MusicManager.Instance != null)
            MusicManager.Instance.PlayUIClick();
    }

    /// <summary>
    /// 向 EventTrigger 添加一种事件监听
    /// </summary>
    private void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction action)
    {
        var entry = new EventTrigger.Entry();
        entry.eventID = type;
        entry.callback.AddListener((data) => action());
        trigger.triggers.Add(entry);
    }
}
