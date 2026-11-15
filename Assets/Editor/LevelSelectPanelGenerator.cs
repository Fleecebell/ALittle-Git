using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 选关面板生成器（编辑器工具）。
///
/// 菜单：Tools / 关卡工具 / 生成选关面板
///
/// 点「生成到当前场景」后，会在当前场景里直接建出：
///   LevelSelectCanvas            ← Canvas + CanvasScaler + LevelSelectPanel
///   └ Panel                      ← 半透明背景、标题、关闭按钮
///      └ List (ScrollRect)
///         └ Viewport
///            └ Content (GridLayoutGroup)
///               └ Level_0 / Level_1 / ...   ← 每个按钮一个数字，点击 = LoadScene(索引)
///
/// 数字就是 Build Settings 里的顺序索引（0 起），和 SceneManager.LoadScene(index) 完全一致。
/// 生成完直接点 Play，游戏里点数字就能跳关；面板也可保存成预制体拖到别的场景。
/// </summary>
public class LevelSelectPanelGenerator : EditorWindow
{
    private const string MenuPath = "Tools/关卡工具/生成选关面板";
    private const string CanvasName = "LevelSelectCanvas";
    private const string UndoName = "生成选关面板";

    private const float PanelPadding = 18f;
    private const float TitleHeight = 52f;
    private const float MaxListHeight = 620f;
    private const float GridPadding = 6f;

    // ---- 生成参数 ----
    private int _columns = 5;
    private float _cellWidth = 110f;
    private float _cellHeight = 76f;
    private float _spacing = 10f;
    private bool _showSceneName = true;
    private int _fromIndex;
    private int _toIndex;
    private bool _startVisible = true;
    private KeyCode _toggleKey = KeyCode.Tab;
    private bool _addCloseButton = true;

    private Vector2 _previewScroll;

    private class LevelInfo
    {
        public int index;
        public string name;
        public string path;
    }

    [MenuItem(MenuPath, false, 2)]
    private static void OpenWindow()
    {
        LevelSelectPanelGenerator window = GetWindow<LevelSelectPanelGenerator>(false, "生成选关面板", true);
        window.minSize = new Vector2(380f, 460f);
        window.ResetRange();
        window.Show();
    }

    private void OnEnable()
    {
        ResetRange();
    }

    private void ResetRange()
    {
        _fromIndex = 0;
        _toIndex = Mathf.Max(0, CountBuildScenes() - 1);
    }

    // ==================================================================
    //  界面
    // ==================================================================

    private void OnGUI()
    {
        EditorGUILayout.Space(4);

        GUILayout.Label("生成选关面板", EditorStyles.boldLabel);
        GUILayout.Label("在当前场景里生成一个带数字按钮的面板，运行游戏时点数字即可跳到对应关卡。\n" +
                        "数字 = Build Settings 里的顺序索引（0 起），即 SceneManager.LoadScene(index)。",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space(6);

        _columns = EditorGUILayout.IntSlider("每行按钮数", _columns, 1, 12);
        _cellWidth = EditorGUILayout.Slider("按钮宽度", _cellWidth, 60f, 200f);
        _cellHeight = EditorGUILayout.Slider("按钮高度", _cellHeight, 50f, 160f);
        _spacing = EditorGUILayout.Slider("按钮间距", _spacing, 0f, 30f);
        _showSceneName = EditorGUILayout.ToggleLeft("按钮上同时显示场景名（小字）", _showSceneName);

        EditorGUILayout.Space(6);

        int total = CountBuildScenes();
        _fromIndex = Mathf.Clamp(EditorGUILayout.IntField("起始索引", _fromIndex), 0, Mathf.Max(0, total - 1));
        _toIndex = Mathf.Clamp(EditorGUILayout.IntField("结束索引", _toIndex), _fromIndex, Mathf.Max(0, total - 1));
        EditorGUILayout.LabelField("Build Settings 里共 " + total + " 个启用场景", EditorStyles.miniLabel);

        EditorGUILayout.Space(6);

        _startVisible = EditorGUILayout.ToggleLeft("进入游戏时就显示（不勾则按快捷键呼出）", _startVisible);
        _toggleKey = (KeyCode)EditorGUILayout.EnumPopup("显示/隐藏快捷键", _toggleKey);
        _addCloseButton = EditorGUILayout.ToggleLeft("右上角加关闭按钮（×）", _addCloseButton);

        EditorGUILayout.Space(8);

        // ---- 预览 ----
        List<LevelInfo> levels = CollectLevels();
        GUILayout.Label("将生成 " + levels.Count + " 个按钮", EditorStyles.boldLabel);

        _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, EditorStyles.helpBox, GUILayout.Height(120f));
        for (int i = 0; i < levels.Count; i++)
            EditorGUILayout.LabelField(levels[i].index + "   " + levels[i].name, EditorStyles.miniLabel);
        if (levels.Count == 0)
            EditorGUILayout.LabelField("（没有可用的关卡）", EditorStyles.miniLabel);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8);

        using (new EditorGUI.DisabledScope(levels.Count == 0))
        {
            if (GUILayout.Button("生成到当前场景", GUILayout.Height(36f)))
                Generate(levels);
        }

        EditorGUILayout.HelpBox(
            "生成后面板会随场景一起进包。不想出现在正式版：保持 LevelSelectPanel 上的「正式版保护」勾选即可" +
            "（正式包里自动销毁，编辑器 / Development Build 里仍可用）。",
            MessageType.None);
    }

    // ==================================================================
    //  生成
    // ==================================================================

    private void Generate(List<LevelInfo> levels)
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("生成选关面板", "正在播放中，请先退出 Play 模式再生成。", "好");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("生成选关面板",
                "当前场景还没保存过（没有文件路径）。\n请先保存场景（Ctrl+S）再生成。", "好");
            return;
        }

        if (!TmpReady())
        {
            EditorUtility.DisplayDialog("生成选关面板",
                "没找到 TextMeshPro 的默认字体资源。\n请先执行 Window > TextMeshPro > Import TMP Essential Resources。", "好");
            return;
        }

        GameObject existing = GameObject.Find(CanvasName);
        if (existing != null && !EditorUtility.DisplayDialog("生成选关面板",
                "当前场景里已经有一个 " + CanvasName + "，继续会用新面板替换它。", "替换", "取消"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        EnsureEventSystem();

        // ---- 尺寸计算 ----
        int columns = Mathf.Clamp(_columns, 1, 12);
        int rows = Mathf.CeilToInt(levels.Count / (float)columns);
        float gridWidth = columns * _cellWidth + (columns - 1) * _spacing + GridPadding * 2f;
        float gridHeight = rows * _cellHeight + (rows - 1) * _spacing + GridPadding * 2f;
        float panelWidth = gridWidth + PanelPadding * 2f;
        float listHeight = Mathf.Min(gridHeight, MaxListHeight);
        float panelHeight = TitleHeight + 8f + listHeight + PanelPadding;

        // ---- Canvas ----
        GameObject canvasGo = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGo, UndoName);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        LevelSelectPanel panelLogic = canvasGo.AddComponent<LevelSelectPanel>();

        // ---- 面板背景 ----
        GameObject panelGo = NewUIObject("Panel", canvasGo.transform);
        RectTransform panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(panelWidth, panelHeight);

        Image panelImage = panelGo.AddComponent<Image>();
        panelImage.color = new Color(0.06f, 0.07f, 0.11f, 0.94f);

        // ---- 标题 ----
        string titleText = "选关  ·  数字 = 关卡索引";
        if (_toggleKey != KeyCode.None)
            titleText += "  ·  " + _toggleKey + " 开关";

        TextMeshProUGUI title = CreateLabel(panelGo.transform, "Title", titleText, 26f, TextAlignmentOptions.Center);
        RectTransform titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -8f);
        titleRt.sizeDelta = new Vector2(-PanelPadding * 2f, TitleHeight - 8f);

        // ---- 关闭按钮 ----
        Button closeButton = null;
        if (_addCloseButton)
        {
            GameObject closeGo = NewUIObject("CloseButton", panelGo.transform);
            RectTransform closeRt = closeGo.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-10f, -10f);
            closeRt.sizeDelta = new Vector2(34f, 34f);

            Image closeImage = closeGo.AddComponent<Image>();
            closeImage.color = new Color(0.34f, 0.19f, 0.23f, 1f);

            closeButton = closeGo.AddComponent<Button>();
            closeButton.targetGraphic = closeImage;

            TextMeshProUGUI cross = CreateLabel(closeGo.transform, "Label", "×", 22f, TextAlignmentOptions.Center);
            Stretch(cross.rectTransform, 0f, 0f, 0f, 0f);
        }

        // ---- 滚动列表 ----
        GameObject listGo = NewUIObject("List", panelGo.transform);
        RectTransform listRt = listGo.GetComponent<RectTransform>();
        listRt.anchorMin = Vector2.zero;
        listRt.anchorMax = Vector2.one;
        listRt.offsetMin = new Vector2(PanelPadding, PanelPadding);
        listRt.offsetMax = new Vector2(-PanelPadding, -(TitleHeight + 8f));

        // 透明底图：让按钮以外的空白区域也能接滚轮
        Image listBg = listGo.AddComponent<Image>();
        listBg.color = new Color(1f, 1f, 1f, 0f);
        listBg.raycastTarget = true;

        ScrollRect scroll = listGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        GameObject viewport = NewUIObject("Viewport", listGo.transform);
        Stretch(viewport.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        viewport.AddComponent<RectMask2D>();

        GameObject content = NewUIObject("Content", viewport.transform);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(_cellWidth, _cellHeight);
        grid.spacing = new Vector2(_spacing, _spacing);
        grid.padding = new RectOffset((int)GridPadding, (int)GridPadding, (int)GridPadding, (int)GridPadding);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRt;

        // ---- 关卡按钮 ----
        float numberSize = Mathf.Clamp(_cellHeight * 0.42f, 16f, 46f);
        for (int i = 0; i < levels.Count; i++)
        {
            LevelInfo level = levels[i];

            GameObject buttonGo = NewUIObject("Level_" + level.index, content.transform);
            Image buttonImage = buttonGo.AddComponent<Image>();
            buttonImage.color = Color.white;

            Button button = buttonGo.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.colors = CreateColorBlock();

            LevelSelectButton select = buttonGo.AddComponent<LevelSelectButton>();
            select.levelIndex = level.index;
            select.panel = panelLogic;

            if (_showSceneName)
            {
                TextMeshProUGUI number = CreateLabel(buttonGo.transform, "Number", level.index.ToString(),
                    numberSize, TextAlignmentOptions.Center);
                Stretch(number.rectTransform, 2f, 22f, 2f, 2f);
                select.label = number;

                TextMeshProUGUI nameLabel = CreateLabel(buttonGo.transform, "Name", level.name,
                    11f, TextAlignmentOptions.Center);
                nameLabel.color = new Color(0.62f, 0.66f, 0.75f);
                nameLabel.enableWordWrapping = false;
                nameLabel.overflowMode = TextOverflowModes.Ellipsis;

                RectTransform nameRt = nameLabel.rectTransform;
                nameRt.anchorMin = new Vector2(0f, 0f);
                nameRt.anchorMax = new Vector2(1f, 0f);
                nameRt.pivot = new Vector2(0.5f, 0f);
                nameRt.offsetMin = new Vector2(4f, 3f);
                nameRt.offsetMax = new Vector2(-4f, 20f);
            }
            else
            {
                TextMeshProUGUI number = CreateLabel(buttonGo.transform, "Number", level.index.ToString(),
                    numberSize, TextAlignmentOptions.Center);
                Stretch(number.rectTransform, 2f, 2f, 2f, 2f);
                select.label = number;
            }
        }

        // ---- 面板绑定 ----
        panelLogic.panelRoot = panelGo;
        panelLogic.startVisible = _startVisible;
        panelLogic.toggleKey = _toggleKey;
        panelLogic.closeButton = closeButton;
        panelLogic.devOnly = true;

        EditorSceneManager.MarkSceneDirty(scene);
        Undo.SetCurrentGroupName(UndoName);
        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = panelGo;
        SceneView.RepaintAll();

        Debug.Log("[选关面板] 已在 " + scene.path + " 生成面板：" + levels.Count +
                  " 个按钮（索引 " + levels[0].index + " ~ " + levels[levels.Count - 1].index +
                  "）。记得保存场景（Ctrl+S）。", panelGo);
    }

    // ==================================================================
    //  工具方法
    // ==================================================================

    /// <summary>当前 Build Settings 里启用的场景数量。</summary>
    private static int CountBuildScenes()
    {
        int count = 0;
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i] != null && scenes[i].enabled && !string.IsNullOrEmpty(scenes[i].path))
                count++;
        }
        return count;
    }

    /// <summary>
    /// 收集要生成按钮的关卡。index 按「启用场景」顺序从 0 递增，
    /// 这样和打包后的 buildIndex、SceneManager.LoadScene(index) 完全一致。
    /// </summary>
    private List<LevelInfo> CollectLevels()
    {
        List<LevelInfo> result = new List<LevelInfo>();
        int buildIndex = 0;
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

        for (int i = 0; i < scenes.Length; i++)
        {
            EditorBuildSettingsScene scene = scenes[i];
            if (scene == null || string.IsNullOrEmpty(scene.path) || !scene.enabled)
                continue;

            if (buildIndex >= _fromIndex && buildIndex <= _toIndex)
            {
                result.Add(new LevelInfo
                {
                    index = buildIndex,
                    name = Path.GetFileNameWithoutExtension(scene.path),
                    path = scene.path
                });
            }
            buildIndex++;
        }

        return result;
    }

    private static bool TmpReady()
    {
        try
        {
            return TMP_Settings.defaultFontAsset != null;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(eventSystem, UndoName);
    }

    private static GameObject NewUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoName);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text,
        float fontSize, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoName);
        go.transform.SetParent(parent, false);

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enableWordWrapping = false;

        FontAssetOverride(label);
        return label;
    }

    /// <summary>脚本新建的 TMP 文本不会自动拿到默认字体，这里补上。</summary>
    private static void FontAssetOverride(TextMeshProUGUI label)
    {
        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;
    }

    private static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    private static ColorBlock CreateColorBlock()
    {
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(0.20f, 0.23f, 0.31f, 1f);
        colors.highlightedColor = new Color(0.30f, 0.37f, 0.49f, 1f);
        colors.pressedColor = new Color(0.15f, 0.52f, 0.42f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.disabledColor = new Color(0.15f, 0.15f, 0.15f, 0.6f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        return colors;
    }
}
