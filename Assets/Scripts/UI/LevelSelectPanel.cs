using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 选关面板（运行时逻辑）。
///
/// 本脚本对场景是「只读」的（除了按字段开关做 SetActive）：
///   · 不改 Canvas 的渲染模式 / Sorting Order / Override Sorting
///   · 不增删任何组件（不会自动补 GraphicRaycaster）
///   · 不动 EventSystem（不会自动启用或新建）
///   · 不改任何 Transform / 坐标
///   · 不销毁任何物体
/// 也就是：Unity 里配置成什么样，运行时就是什么样。
///
/// 只读诊断（只往 Console 打警告，不做任何修复）：
///   · Canvas 上没有 GraphicRaycaster
///   · 场景里没有可用的 EventSystem
///   · 点击被别的 UI 挡住 / 被另一个选关面板接住
///   · 场景里有多个选关面板
/// </summary>
public class LevelSelectPanel : MonoBehaviour
{
    [Header("=== 面板 ===")]
    [Tooltip("要显示/隐藏的面板根物体")]
    public GameObject panelRoot;

    [Tooltip("勾选：进入游戏时就显示；不勾：进入游戏时隐藏，按快捷键呼出")]
    public bool startVisible = true;

    [Header("=== 快捷键 ===")]
    [Tooltip("按这个键显示/隐藏面板；None 表示不响应按键")]
    public KeyCode toggleKey = KeyCode.Tab;

    [Header("=== 关闭按钮 ===")]
    [Tooltip("点一下隐藏面板（可以不填）")]
    public Button closeButton;

    [Header("=== 诊断（只打日志，不改任何东西）===")]
    [Tooltip("勾选：面板显示时，如果点击被别的 UI 挡住，会在 Console 打一条警告（排查点不动的问题用）")]
    public bool logClickBlockers = true;

    private Canvas _canvas;
    private float _nextBlockLogTime;

    private void Reset()
    {
        panelRoot = gameObject;
    }

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();

        if (panelRoot == null)
            panelRoot = gameObject;

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Hide);
            closeButton.onClick.AddListener(Hide);
        }

        SetVisible(startVisible);
    }

    private void Start()
    {
        // 等所有 Awake 跑完再检查，避免误报（比如别的脚本在 Awake 里建 EventSystem）
        WarnAboutClickEnvironment();
        WarnDuplicatePanels();
    }

    private void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            Toggle();

        if (logClickBlockers)
            DetectClickBlock();
    }

    // ==================================================================
    //  显示 / 隐藏（只改 SetActive，不动任何坐标）
    // ==================================================================

    public void SetVisible(bool visible)
    {
        if (panelRoot != null)
            panelRoot.SetActive(visible);
    }

    public void Show()
    {
        SetVisible(true);
    }

    public void Hide()
    {
        SetVisible(false);
    }

    public void Toggle()
    {
        if (panelRoot == null)
            return;

        SetVisible(!panelRoot.activeSelf);
    }

    // ==================================================================
    //  加载关卡
    // ==================================================================

    /// <summary>
    /// 加载指定索引的关卡。index 就是 Build Settings 里的顺序索引（SceneManager.LoadScene 用的那个）。
    /// </summary>
    public void LoadLevel(int levelIndex)
    {
        int count = SceneManager.sceneCountInBuildSettings;
        if (levelIndex < 0 || levelIndex >= count)
        {
            Debug.LogWarning($"[选关] 关卡索引 {levelIndex} 超出范围（Build Settings 共 {count} 个场景）", this);
            return;
        }

        Debug.Log($"[选关] 加载关卡 {levelIndex}");
        SceneManager.LoadScene(levelIndex);
    }

    // ==================================================================
    //  只读诊断（只打日志，什么都不改）
    // ==================================================================

    /// <summary>点击环境有问题时只报告，不去修（修不修由你在 Unity 里决定）。</summary>
    private void WarnAboutClickEnvironment()
    {
        if (_canvas == null)
        {
            Debug.LogWarning("[选关] 本组件没挂在 Canvas 上，面板可能收不到点击。", this);
        }
        else
        {
            if (_canvas.GetComponent<GraphicRaycaster>() == null)
            {
                Debug.LogWarning("[选关] " + GetHierarchyPath(_canvas.transform) +
                                 " 上没有 GraphicRaycaster，这个 Canvas 上的 UI 收不到点击。" +
                                 "（脚本不会自动加，请在 Unity 里手动添加）", this);
            }

        }

        if (EventSystem.current == null)
        {
            Debug.LogWarning("[选关] 场景里没有可用的 EventSystem，整个 UI 都收不到点击。" +
                             "（脚本不会自动创建或启用，请检查场景里的 EventSystem 物体是否激活/启用）", this);
        }

        if (_canvas != null && _canvas.transform.parent != null &&
            _canvas.transform.parent.GetComponentInParent<Canvas>() != null &&
            !_canvas.overrideSorting)
        {
            Debug.Log("[选关] 本 Canvas 嵌套在别的 Canvas 下且没有勾 Override Sorting，" +
                      "它自身的 Sorting Order 不会生效。", this);
        }
    }

    /// <summary>
    /// 场景里有多个选关面板时，它们会互相挡住点击（点到的那个会把其余的当「挡路物」）。
    /// 只报告，不删除。
    /// </summary>
    private void WarnDuplicatePanels()
    {
        LevelSelectPanel[] all = FindObjectsOfType<LevelSelectPanel>(true);
        if (all.Length <= 1)
            return;

        string list = "";
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null)
                continue;
            list += "\n  · " + GetHierarchyPath(all[i].transform) +
                    (all[i].gameObject.activeInHierarchy ? "" : "（未激活）");
        }

        Debug.LogWarning("[选关] 场景里有 " + all.Length + " 个选关面板，它们会互相挡住点击，" +
                         "请只保留一个（脚本不会自动删）：" + list, this);
    }

    /// <summary>
    /// 面板显示时点击鼠标：如果最上层命中的不是本面板，就把挡路的物体打出来。
    /// 只在点不动的时候偶尔打一条，不会刷屏。
    /// </summary>
    private void DetectClickBlock()
    {
        if (!Input.GetMouseButtonDown(0))
            return;
        if (panelRoot == null || !panelRoot.activeSelf)
            return;
        if (Time.unscaledTime < _nextBlockLogTime)
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            _nextBlockLogTime = Time.unscaledTime + 3f;
            Debug.LogWarning("[选关] 场景里没有可用的 EventSystem，UI 收不到点击。", this);
            return;
        }

        PointerEventData pointer = new PointerEventData(eventSystem) { position = Input.mousePosition };
        List<RaycastResult> hits = new List<RaycastResult>();
        eventSystem.RaycastAll(pointer, hits);

        if (hits.Count == 0)
            return;

        for (int i = 0; i < hits.Count; i++)
        {
            if (hits[i].gameObject != null && hits[i].gameObject.GetComponentInParent<LevelSelectPanel>() == this)
                return; // 点到的是自己，正常
        }

        _nextBlockLogTime = Time.unscaledTime + 3f;

        GameObject blocker = hits[0].gameObject;

        // 点被另一个选关面板接住了（场景里有多份）
        LevelSelectPanel blockerPanel = blocker.GetComponentInParent<LevelSelectPanel>();
        if (blockerPanel != null)
        {
            Debug.LogWarning("[选关] 点击被另一个选关面板接住了：" + GetHierarchyPath(blockerPanel.transform) +
                             "。场景里有多个选关面板，请只保留一个（脚本不会自动删）。", blocker);
            return;
        }

        Canvas blockerCanvas = blocker.GetComponentInParent<Canvas>();
        Debug.LogWarning(
            "[选关] 点击被挡住了：最上层是 " + GetHierarchyPath(blocker.transform) +
            (blockerCanvas != null
                ? "（Canvas 渲染模式=" + blockerCanvas.renderMode +
                  "，sortingOrder=" + blockerCanvas.sortingOrder + "）"
                : "（不在任何 Canvas 下）") +
            "，本面板 Canvas 渲染模式=" + (_canvas != null ? _canvas.renderMode.ToString() : "无 Canvas") +
            "，sortingOrder=" + (_canvas != null ? _canvas.sortingOrder : 0) +
            "。脚本不会改这些设置：请在 Unity 里把挡路的那张图关掉 Raycast Target，" +
            "或把它所在 Canvas 的 Sorting Order 调到本面板之下。",
            blocker);
    }

    private static string GetHierarchyPath(Transform t)
    {
        if (t == null)
            return "(null)";

        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
