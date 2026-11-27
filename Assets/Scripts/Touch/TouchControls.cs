using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using NativeInput = UnityEngine.Input;

[DefaultExecutionOrder(-1000)]
public sealed class TouchControls : MonoBehaviour
{
    public enum DisplayMode { Auto, Always, Never }
    [Tooltip("Auto 跟随当前构建平台：Android/iOS 显示，Windows/macOS/Linux 隐藏。Always 强制显示（可用鼠标调试），Never 强制隐藏。")]
    public DisplayMode mode;
    public RectTransform overlay, safeArea, pad, knob;
    public GameObject dashButton, sizeButton;

    private const int NoPointer = -1, Mouse = -2;
    private const float StickDeadZone = 0.15f;
    private int owner = NoPointer;
    private Vector2 origin;
    private MoveFirst player;
    private UIManager hud;
    private bool acceptingInput = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Application.isMobilePlatform && !Application.isEditor)
        {
            NativeInput.multiTouchEnabled = true;
            Application.targetFrameRate = 60;
            DebugManager.instance.enableRuntimeUI = false;
        }
        var prefab = Resources.Load<TouchControls>("TouchControls");
        if (prefab != null)
            DontDestroyOnLoad(Instantiate(prefab).gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindScene();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode) => BindScene();

    private void BindScene()
    {
        Hide();
        player = FindObjectOfType<MoveFirst>();
        hud = FindObjectOfType<UIManager>();
        if (player == null) return;
        var feel = player.feelSettings;
        dashButton.SetActive(feel == null || feel.enableDash);
        sizeButton.SetActive(feel == null || feel.enableSizeToggle);
    }

    private bool ShouldShow()
    {
        if (mode == DisplayMode.Never) return false;
        if (mode == DisplayMode.Always) return true;
        // 编辑器和正式包共用当前构建目标的平台宏。
#if UNITY_ANDROID || UNITY_IOS
        return true;
#elif UNITY_STANDALONE
        return false;
#else
        return Application.isMobilePlatform || NativeInput.touchSupported;
#endif
    }

    private void Update()
    {
        if (player == null || !ShouldShow() ||
            (hud != null && hud.targetPanel != null && hud.targetPanel.activeInHierarchy))
        {
            if (overlay.gameObject.activeSelf) Hide();
            return;
        }
        if (!overlay.gameObject.activeSelf) overlay.gameObject.SetActive(true);
        Rect area = GameplayHudLayout.NormalizedPlayArea();
        if (safeArea.anchorMin != area.min) safeArea.anchorMin = area.min;
        if (safeArea.anchorMax != area.max) safeArea.anchorMax = area.max;
        if (acceptingInput) Input.Sample(ReadStick());
        else Input.ClearTouch();
    }

    private Vector2 ReadStick()
    {
        int touches = NativeInput.touchCount;
        if (touches > 0)
        {
            if (owner == Mouse) Release();
            for (int i = 0; i < touches; ++i)
            {
                Touch t = NativeInput.GetTouch(i);
                if (owner == NoPointer && t.phase == TouchPhase.Began && t.position.x < Screen.width * 0.5f)
                    Capture(t.fingerId, t.position);
                if (t.fingerId != owner) continue;
                if (t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled)
                    return Drag(t.position);
                Release();
            }
        }
        else if (!Application.isMobilePlatform || Application.isEditor)
        {
            if (owner >= 0) Release();
            Vector2 pos = NativeInput.mousePosition;
            if (owner == NoPointer && NativeInput.GetMouseButtonDown(0) && pos.x < Screen.width * 0.5f)
                Capture(Mouse, pos);
            if (owner == Mouse && NativeInput.GetMouseButton(0)) return Drag(pos);
        }
        Release();
        return Vector2.zero;
    }

    private Vector2 Local(Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, screenPoint, null, out var point);
        return point;
    }

    private void Capture(int pointer, Vector2 pos)
    {
        owner = pointer;
        origin = Local(pos);
        pad.anchoredPosition = origin;
        pad.gameObject.SetActive(true);
    }

    private Vector2 Drag(Vector2 pos)
    {
        float radius = (pad.rect.width - knob.rect.width) * 0.5f;
        Vector2 direction = Vector2.ClampMagnitude((Local(pos) - origin) / radius, 1f);
        knob.anchoredPosition = direction * radius;
        float distance = direction.magnitude;
        if (distance <= StickDeadZone) return Vector2.zero;

        // 横移从死区边缘平滑起步，推满时恢复原速。纵向仍用原始位移判断爬梯和游泳。
        float travel = Mathf.InverseLerp(StickDeadZone, 1f, distance);
        float strength = travel * Mathf.Lerp(0.7f, 1f, travel);
        direction.x *= strength / distance;
        return direction;
    }

    private void Release()
    {
        owner = NoPointer;
        if (pad.gameObject.activeSelf) pad.gameObject.SetActive(false);
    }

    private void Hide()
    {
        Release();
        overlay.gameObject.SetActive(false);
        Input.ClearTouch();
    }

    private void OnApplicationFocus(bool focused)
    {
        acceptingInput = focused;
        if (!focused)
        {
            Release();
            Input.ClearTouch();
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) Hide();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Hide();
    }
}

