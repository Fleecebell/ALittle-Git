using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TouchControlsPrefab
{
    private const string AssetPath = "Assets/Resources/TouchControls.prefab";

    [MenuItem("Tools/移动端/生成或定位触摸 UI 预制体")]
    public static void Generate()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath);
        if (existing != null)
        {
            EditorGUIUtility.PingObject(existing);
            return;
        }

        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("TouchControls", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, preview);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.25f;
            var controls = root.AddComponent<TouchControls>();
            controls.overlay = Stretch("Controls", root.transform);
            controls.safeArea = Stretch("Safe Area", controls.overlay);

            var circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            controls.pad = Disc("Stick", controls.overlay, circle, 148, new Color(.10f, .16f, .19f, .48f));
            controls.knob = Disc("Thumb", controls.pad, circle, 56, new Color(.86f, .96f, .89f, .84f));
            controls.pad.gameObject.SetActive(false);

            var anchor = controls.safeArea;
            Key(anchor, circle, "Jump", KeyCode.Space, -82, 86, 112);
            Key(anchor, circle, "Down", KeyCode.S, -204, 68, 86);
            Key(anchor, circle, "Reset", KeyCode.R, -54, 252, 68);
            controls.dashButton = Key(anchor, circle, "Dash", KeyCode.LeftShift, -174, 177, 76);
            controls.sizeButton = Key(anchor, circle, "Size", KeyCode.E, -270, 156, 76);
            controls.overlay.gameObject.SetActive(false);
            if (PrefabUtility.SaveAsPrefabAsset(root, AssetPath) == null)
                throw new System.InvalidOperationException("生成触摸 UI 预制体失败。");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static RectTransform Child(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static RectTransform Stretch(string name, Transform parent)
    {
        var rect = Child(name, parent);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static RectTransform Disc(string name, Transform parent, Sprite circle, float diameter, Color color)
    {
        var rect = Child(name, parent);
        rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
        rect.sizeDelta = Vector2.one * diameter;
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = circle;
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private static GameObject Key(Transform parent, Sprite circle, string label, KeyCode code, float x, float y, float size)
    {
        var rect = Disc(label, parent, circle, size, new Color(.10f, .16f, .19f, .69f));
        rect.anchorMin = rect.anchorMax = Vector2.right;
        rect.anchoredPosition = new Vector2(x, y);
        rect.GetComponent<Image>().raycastTarget = true;
        rect.gameObject.AddComponent<TouchKeyButton>().key = code;

        var text = Stretch("Label", rect).gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = label;
        text.fontSize = label == "Jump" ? 25 : 19;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(.96f, 1f, .94f);
        text.raycastTarget = false;
        return rect.gameObject;
    }
}

