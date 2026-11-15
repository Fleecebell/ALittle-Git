using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 选关面板（运行时逻辑），由编辑器工具 Tools/关卡工具/生成选关面板 自动生成并挂到 Canvas 上。
///
/// 只负责：面板的显示/隐藏、快捷键、关闭按钮、按索引加载关卡。
/// 每个数字按钮上的事由 LevelSelectButton 自己处理。
///
/// 注意：本组件挂在外层 Canvas 上（Canvas 一直激活），
///      要隐藏时只隐藏 panelRoot，否则按键监听也会跟着停掉。
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

    [Header("=== 正式版保护 ===")]
    [Tooltip("勾选：只在编辑器和 Development Build 里出现，正式版打包时自动销毁（避免调试面板漏进正式游戏）")]
    public bool devOnly = true;

    private void Reset()
    {
        panelRoot = gameObject;
    }

    private void Awake()
    {
        if (devOnly && !Application.isEditor && !Debug.isDebugBuild)
        {
            Destroy(gameObject);
            return;
        }

        if (panelRoot == null)
            panelRoot = gameObject;

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Hide);
            closeButton.onClick.AddListener(Hide);
        }

        SetVisible(startVisible);
    }

    private void Update()
    {
        if (toggleKey == KeyCode.None)
            return;

        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

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
        bool visible = panelRoot != null && panelRoot.activeSelf;
        SetVisible(!visible);
    }

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
}
