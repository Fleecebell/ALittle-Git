using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 选关面板上的单个关卡按钮：显示关卡数字，点击后加载对应关卡。
/// 由编辑器工具 Tools/关卡工具/生成选关面板 自动创建，一般不用手动挂。
///
/// 对场景只读：不会覆盖 Unity 里已经填好的文本，也不会改写自己的序列化引用。
/// </summary>
[RequireComponent(typeof(Button))]
public class LevelSelectButton : MonoBehaviour
{
    [Tooltip("对应的关卡索引 = Build Settings 里的顺序（SceneManager.LoadScene 用这个值）")]
    public int levelIndex;

    [Tooltip("显示数字的文本，留空则自动取子物体上的 TMP 文本")]
    public TMP_Text label;

    [Tooltip("所属的选关面板，留空则自动往上找；再找不到就直接 LoadScene")]
    public LevelSelectPanel panel;

    private Button _button;

    private void Awake()
    {
        // 只在文本为空时补上数字；Unity 里已经填好的字不会被改
        TMP_Text text = label != null ? label : GetComponentInChildren<TMP_Text>(true);
        if (text != null && string.IsNullOrEmpty(text.text))
            text.text = levelIndex.ToString();

        _button = GetComponent<Button>();
        _button.onClick.RemoveListener(OnClick);
        _button.onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        // 只在需要时临时找一下，不回写 panel 字段（保持 Unity 里的配置不变）
        LevelSelectPanel owner = panel != null ? panel : GetComponentInParent<LevelSelectPanel>(true);

        if (owner != null)
            owner.LoadLevel(levelIndex);
        else
            SceneManager.LoadScene(levelIndex);
    }
}
