using UnityEngine;

[System.Serializable]
public class PedalInfo
{
    [Tooltip("踏板对象")]
    public GameObject pedal;
    [Tooltip("是否一次性")]
    public bool isOneTime;
}

public class Button_Multi : MonoBehaviour
{
    [Header("踏板数组")]
    public PedalInfo[] pedals;

    [Header("目标位置")]
    public Transform targetPosition;

    [Header("移动速度")]
    public float speed = 10f;

    private Vector3 originPosition;
    private bool[] pedalPressed;
    private bool allPressed;

    void Start()
    {
        originPosition = transform.position;
        pedalPressed = new bool[pedals.Length];

        for (int i = 0; i < pedals.Length; i++)
        {
            if (pedals[i].pedal == null) continue;

            // 移除旧的 PedalTrigger 避免重复添加
            var oldTriggers = pedals[i].pedal.GetComponents<PedalTrigger>();
            foreach (var t in oldTriggers) Destroy(t);

            // 添加触发器监听组件
            var trigger = pedals[i].pedal.AddComponent<PedalTrigger>();
            trigger.Init(this, i, pedals[i].isOneTime);

            // 初始状态：big 显示（踏板弹起）
            var big = pedals[i].pedal.transform.Find("big");
            if (big != null) big.gameObject.SetActive(true);
        }
    }

    void Update()
    {
        // 检查是否所有踏板都被按下
        allPressed = true;
        for (int i = 0; i < pedals.Length; i++)
        {
            if (!pedalPressed[i])
            {
                allPressed = false;
                break;
            }
        }

        // 更新 big 显示状态
        UpdateBigs();

        // 移动物体
        if (allPressed)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition.position, Mathf.Min(speed * Time.deltaTime, 1f));
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, originPosition, Mathf.Min(speed * Time.deltaTime, 1f));
        }

        // 按 R 或碰到岩浆 → 全部重置
        if (Input.GetKeyDown(KeyCode.R) || Die.touch_lava)
        {
            ResetAll();
        }
    }

    void UpdateBigs()
    {
        for (int i = 0; i < pedals.Length; i++)
        {
            if (pedals[i].pedal == null) continue;
            var big = pedals[i].pedal.transform.Find("big");
            if (big == null) continue;

            if (allPressed)
            {
                // 全部按下 → 所有 big 隐藏
                big.gameObject.SetActive(false);
            }
            else
            {
                // 未全部按下 → 根据踏板类型决定 big 显示
                // 一次性：被踩过后 big 永久隐藏；非一次性：按当前状态显示
                big.gameObject.SetActive(!pedalPressed[i]);
            }
        }
    }

    /// <summary>
    /// 由 PedalTrigger 调用来更新某个踏板的按下状态
    /// </summary>
    public void SetPedalPressed(int index, bool pressed)
    {
        if (index < 0 || index >= pedalPressed.Length) return;

        if (pedals[index].isOneTime)
        {
            // 一次性踏板：只能从 false → true，不会弹起
            if (pressed) pedalPressed[index] = true;
        }
        else
        {
            pedalPressed[index] = pressed;
        }
    }

    void ResetAll()
    {
        for (int i = 0; i < pedalPressed.Length; i++)
        {
            pedalPressed[i] = false;
            if (pedals[i].pedal != null)
            {
                var big = pedals[i].pedal.transform.Find("big");
                if (big != null) big.gameObject.SetActive(true);
            }
        }
    }
}

/// <summary>
/// 自动附着在每个踏板上的触发器监听组件
/// </summary>
public class PedalTrigger : MonoBehaviour
{
    private Button_Multi controller;
    private int index;
    private bool isOneTime;

    public void Init(Button_Multi ctrl, int idx, bool oneTime)
    {
        controller = ctrl;
        index = idx;
        isOneTime = oneTime;
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            controller.SetPedalPressed(index, true);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            // 一次性踏板不受离开影响
            if (!isOneTime)
            {
                controller.SetPedalPressed(index, false);
            }
        }
    }
}
