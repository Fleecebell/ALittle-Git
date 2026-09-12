using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    public GameObject targetPanel;
    private bool isPanelActive;

    [Header("=== 音量滑条（自动绑定，一般无需手动设置）===")]
    [Tooltip("音乐滑条路径（相对本物体）。留空则尝试按默认路径查找")]
    public string musicSliderPath = "SetPanel/Music/Slider";
    [Tooltip("音效滑条路径（相对本物体）。留空则尝试按默认路径查找")]
    public string sfxSliderPath = "SetPanel/SFX/Slider";

    [Header("=== 计时器（自动绑定，一般无需手动设置）===")]
    [Tooltip("计时器根物体路径（相对本物体）。由勾选框控制显隐")]
    public string timerPath = "Timer";
    [Tooltip("显示时间的 TMP 文本路径（相对本物体）")]
    public string timerTextPath = "Timer/Text (TMP)";
    [Tooltip("控制计时器显隐的勾选框路径（相对本物体）。找不到时会自动在子树里找 Toggle，可留空")]
    public string timerTogglePath = "SetPanel/TimerSwitch/Toggle";

    [Tooltip("勾选：玩家第一次按键/移动时才开始计时（避免关卡刚加载读秒就先跑了）；" +
             "取消勾选：关卡一加载就从 0 开始计时")]
    public bool startOnFirstInput = true;

    /// <summary>本关成绩（秒）。通关时冻结，之后可以拿这个值去别处显示</summary>
    [HideInInspector] public float score;

    /// <summary>是否已开始计时（startOnFirstInput 勾选时，玩家第一次操作后才为 true）</summary>
    [HideInInspector] public bool isTiming;

    // 已累计的时间（秒）
    private float elapsedTime;
    // 通关后冻结，不再累加
    private bool isStopped;

    // 计时器相关引用
    private GameObject timerRoot;
    private TextMeshProUGUI timerText;
    private Toggle timerToggle;

    void Start()
    {
        AutoBindSliders();
        AutoBindTimer();

        // 不要求"第一次操作"时，关卡一加载就直接开始计时
        if (!startOnFirstInput) isTiming = true;
    }

    void Update()
    {
        // 未开始计时：等玩家第一次操作（startOnFirstInput 关闭时，Start 里已置 true，走不到这里）
        if (!isTiming)
        {
            if (!AnyGameplayInputDown()) return;
            isTiming = true;
        }

        // 通关后冻结成绩，不再累加
        if (isStopped) return;

        elapsedTime += Time.deltaTime;
        score = elapsedTime;
        RefreshTimerText();
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

    // ===================== 计时器 =====================

    /// <summary>
    /// 冻结计时（通关时由 LevelLoader 调用），把当前时间存为最终成绩
    /// </summary>
    public void StopTimer()
    {
        isTiming = true;
        isStopped = true;
        score = elapsedTime;
        RefreshTimerText();
    }

    /// <summary>
    /// 自动查找 Timer 物体 / TMP 文本 / 勾选框，并绑定勾选框的显隐事件
    /// </summary>
    private void AutoBindTimer()
    {
        Transform timerT = string.IsNullOrEmpty(timerPath) ? null : transform.Find(timerPath);
        if (timerT != null) timerRoot = timerT.gameObject;

        // 文本：先按路径找；找不到再从 Timer 子级兜底找（Timer 是空物体，Text (TMP) 在其下）
        Transform textT = string.IsNullOrEmpty(timerTextPath) ? null : transform.Find(timerTextPath);
        if (textT != null) timerText = textT.GetComponent<TextMeshProUGUI>();
        if (timerText == null && timerRoot != null)
            timerText = timerRoot.GetComponentInChildren<TextMeshProUGUI>(true);

        if (timerRoot == null)
            Debug.LogWarning($"UIManager: 找不到计时器物体路径 {timerPath}");

        if (timerText == null)
            Debug.LogWarning($"UIManager: 找不到计时器文本（路径 {timerTextPath} 无效，Timer 子级下也没找到 TMP 文本）");

        // 初始显示 00:00.00
        RefreshTimerText();

        // 绑定勾选框：勾选显示 Timer 物体，取消勾选隐藏
        // 查找顺序：① 按路径 → ② 路径末段的父物体下找 → ③ 整棵子树里找唯一的 Toggle
        // ③ 不依赖物体名字，所以物体被改名/移位后依然能绑定上
        if (!string.IsNullOrEmpty(timerTogglePath))
        {
            Transform toggleT = transform.Find(timerTogglePath);
            if (toggleT != null) timerToggle = toggleT.GetComponent<Toggle>();

            // 兜底②：路径末段物体上没挂 Toggle 组件时，到其父物体下找
            if (timerToggle == null)
            {
                int slash = timerTogglePath.LastIndexOf('/');
                string parentPath = slash > 0 ? timerTogglePath.Substring(0, slash) : timerTogglePath;
                Transform parentT = transform.Find(parentPath);
                if (parentT != null) timerToggle = parentT.GetComponentInChildren<Toggle>(true);
            }
        }

        // 兜底③：整棵子树里找 Toggle（忽略名字，只认组件，优先取激活状态的那个）
        if (timerToggle == null)
            timerToggle = FindSingleToggle();

        if (timerToggle != null)
        {
            // 移除旧监听（防止重复绑定），再添加
            timerToggle.onValueChanged.RemoveListener(OnTimerToggleChanged);
            timerToggle.onValueChanged.AddListener(OnTimerToggleChanged);

            // 按勾选框当前状态同步一次显隐
            ApplyTimerVisibility(timerToggle.isOn);
        }
    }

    /// <summary>
    /// 兜底查找勾选框：不看名字，只认 Toggle 组件。
    /// 优先在"层级上处于激活状态"的里面找唯一的那个（避免匹配到隐藏的旧勾选框），
    /// 激活的没有/有多个时，再看包含隐藏的整体是否唯一。找不到唯一结果时返回 null。
    /// </summary>
    private Toggle FindSingleToggle()
    {
        Toggle[] all = GetComponentsInChildren<Toggle>(true);

        Toggle active = null;
        int activeCount = 0;
        foreach (var t in all)
        {
            if (t.isActiveAndEnabled)
            {
                active = t;
                activeCount++;
            }
        }

        if (activeCount == 1)
        {
            Debug.Log($"UIManager: 未按路径 {timerTogglePath} 找到勾选框, 已自动使用激活的 " +
                      $"{GetHierarchyPath(active.transform)}", this);
            return active;
        }

        if (all.Length == 1)
        {
            Debug.Log($"UIManager: 未按路径 {timerTogglePath} 找到勾选框, 已自动使用(隐藏的) " +
                      $"{GetHierarchyPath(all[0].transform)}", this);
            return all[0];
        }

        if (all.Length == 0)
        {
            Debug.LogWarning($"UIManager: 找不到勾选框（路径 {timerTogglePath} 无效, 子树下也没有 Toggle）, " +
                             $"计时器开关未绑定", this);
        }
        else
        {
            Debug.LogWarning($"UIManager: 子树下有 {all.Length} 个 Toggle（其中激活 {activeCount} 个）, " +
                             $"无法自动判断用哪个, 请在检查器填写 timerTogglePath", this);
        }
        return null;
    }

    private void OnTimerToggleChanged(bool isOn)
    {
        ApplyTimerVisibility(isOn);
    }

    /// <summary>
    /// 拼出某个 Transform 相对本物体(SetCanvas)的路径, 仅用于日志提示
    /// </summary>
    private string GetHierarchyPath(Transform t)
    {
        if (t == null) return "(null)";

        string path = t.name;
        while (t.parent != null && t.parent != transform)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    /// <summary>
    /// 控制 Timer 物体的显隐（隐藏时计时仍在后台继续）
    /// </summary>
    private void ApplyTimerVisibility(bool visible)
    {
        if (timerRoot != null)
            timerRoot.SetActive(visible);
    }

    /// <summary>
    /// 把 score（秒）按 mm:ss.xx 格式刷新到文本上
    /// </summary>
    private void RefreshTimerText()
    {
        if (timerText == null) return;

        // 先转成"百分秒"整数再拆分，避免 seconds 单独四舍五入时显示出 :60.00
        int totalHundredths = Mathf.FloorToInt(score * 100f);
        int minutes = totalHundredths / 6000;
        int seconds = (totalHundredths % 6000) / 100;
        int hundredths = totalHundredths % 100;

        timerText.text = $"{minutes:00}:{seconds:00}.{hundredths:00}";
    }

    /// <summary>
    /// 是否检测到玩家的"第一次操作"（对应角色操作键：左右/跳跃/S/E/冲刺）
    /// 不含鼠标和 Esc 等非游戏操作键
    /// </summary>
    private bool AnyGameplayInputDown()
    {
        if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f) return true;
        if (Input.GetKeyDown(KeyCode.Space)) return true;
        if (Input.GetKeyDown(KeyCode.W)) return true;
        if (Input.GetKeyDown(KeyCode.S)) return true;
        if (Input.GetKeyDown(KeyCode.E)) return true;
        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) return true;
        return false;
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

        // 绑定事件，并把滑条初始位置设置成当前保存的音量，保证跨场景后滑条显示一致
        BindSlider(musicSliderPath, MusicManager.Instance.SetMusicVolume,
                   MusicManager.Instance.MusicVolume, "音乐");
        BindSlider(sfxSliderPath, MusicManager.Instance.SetSFXVolume,
                   MusicManager.Instance.SFXVolume, "音效");
    }

    private void BindSlider(string path, UnityEngine.Events.UnityAction<float> method,
                            float currentValue, string name)
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

        // 先把滑条位置设置成当前音量（此时尚未绑定事件，不会反向触发）
        slider.value = Mathf.Clamp01(currentValue);

        // 移除旧监听（防止重复绑定），再添加
        slider.onValueChanged.RemoveListener(method);
        slider.onValueChanged.AddListener(method);
    }
}
