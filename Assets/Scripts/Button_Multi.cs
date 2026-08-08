using UnityEngine;
using System;
using System.Collections.Generic;

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
    public float speed = 2f;

    [Header("一次性移动")]
    [Tooltip("勾选后：物体触发后移动到目标位置不再返回，按钮保持被按下（不回弹）。不勾选：往返移动，松开按钮即返回，按钮回弹。")]
    public bool oneTimeMove = false;

    private Vector3 originPosition;
    private bool[] pedalPressed;
    private bool allPressed;
    private bool triggered;   // 是否已触发过一次性移动
    // 返回原位中：死亡/按R复原后平台平滑移回起点，此期间忽略踏板触发
    private bool returning = false;

    // 平台移动增量（Button_Multi 自身就是移动平台）
    private Vector3 previousPos;
    public Vector3 PlatformDelta { get; private set; }

    // SmoothStep 进度（0=起点，1=目标），direction 表示方向（+1前进，-1返回），用于慢快慢曲线
    private float moveProgress = 0f;
    private int direction = 1;

    // 所有活跃实例，用于死亡时统一复原所有按钮
    private static List<Button_Multi> activeInstances = new List<Button_Multi>();

    // 死亡/按R时统一复原所有按钮（由 DeathRespawnVFX.TriggerDeath 调用）
    public static void ResetAllButtons()
    {
        for (int i = activeInstances.Count - 1; i >= 0; i--)
        {
            if (activeInstances[i] != null)
                activeInstances[i].ResetButton();
        }
    }

    void Start()
    {
        if (!activeInstances.Contains(this))
            activeInstances.Add(this);
        originPosition = transform.position;
        previousPos = transform.position;
        PlatformDelta = Vector3.zero;
        pedalPressed = new bool[pedals.Length];

        // 将自身注册为移动平台
        Button_once.PlatformDeltas[this.gameObject] = () => PlatformDelta;

        for (int i = 0; i < pedals.Length; i++)
        {
            if (pedals[i].pedal == null) continue;

            var oldTriggers = pedals[i].pedal.GetComponents<PedalTrigger>();
            foreach (var t in oldTriggers) Destroy(t);

            var trigger = pedals[i].pedal.AddComponent<PedalTrigger>();
            trigger.Init(this, i, pedals[i].isOneTime, () => returning);

            var big = pedals[i].pedal.transform.Find("big");
            if (big != null) big.gameObject.SetActive(true);
        }
    }

    void Update()
    {
        allPressed = true;
        for (int i = 0; i < pedals.Length; i++)
        {
            if (!pedalPressed[i])
            {
                allPressed = false;
                break;
            }
        }

        UpdateBigs();

        // 记录移动前位置
        previousPos = transform.position;

        if (returning && moveProgress <= 0f)
        {
            // 已平滑回到起点，结束返回状态
            returning = false;
        }

        if (returning)
        {
            // 返回原位：忽略踏板状态，强制平滑移回起点
            direction = -1;
        }
        else if (allPressed)
        {
            if (oneTimeMove) triggered = true;
            direction = 1;
        }
        else
        {
            // 一次性移动：已触发则继续移动到目标位置，即使玩家中途走开也不停在半路；
            // 到达目标后保持不回弹；未触发则返回起点
            if (oneTimeMove && triggered)
            {
                direction = moveProgress >= 1f ? 0 : 1;
            }
            else
            {
                direction = -1;
            }
        }

        // 基于单个进度变量移动，保证方向切换时不会瞬移
        moveProgress = Mathf.Clamp01(moveProgress + direction * speed * Time.deltaTime);
        float t = Mathf.SmoothStep(0f, 1f, moveProgress);
        transform.position = Vector3.Lerp(originPosition, targetPosition.position, t);

        PlatformDelta = transform.position - previousPos;

        if (Input.GetKeyDown(KeyCode.R) || Die.playerDying)
        {
            ResetButton();
            previousPos = transform.position;
            PlatformDelta = Vector3.zero;
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
                big.gameObject.SetActive(false);
            }
            else
            {
                // 一次性移动已触发：按钮保持按下（不回弹）
                if (oneTimeMove && triggered)
                    big.gameObject.SetActive(false);
                else
                    big.gameObject.SetActive(!pedalPressed[i]);
            }
        }
    }

    public void SetPedalPressed(int index, bool pressed)
    {
        if (index < 0 || index >= pedalPressed.Length) return;

        if (pedals[index].isOneTime)
        {
            if (pressed) pedalPressed[index] = true;
        }
        else
        {
            pedalPressed[index] = pressed;
        }
    }

    // 复原按钮：平滑返回起点（不清零进度，靠 direction=-1 平滑移回），期间忽略踏板触发
    public void ResetButton()
    {
        triggered = false;
        returning = true;
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

    private void OnDestroy()
    {
        activeInstances.Remove(this);
        if (Button_once.PlatformDeltas.ContainsKey(this.gameObject))
            Button_once.PlatformDeltas.Remove(this.gameObject);
    }
}

public class PedalTrigger : MonoBehaviour
{
    private Button_Multi controller;
    private int index;
    private bool isOneTime;
    private Func<bool> isReturning;

    public void Init(Button_Multi ctrl, int idx, bool oneTime, Func<bool> returningCheck)
    {
        controller = ctrl;
        index = idx;
        isOneTime = oneTime;
        isReturning = returningCheck;
    }

    // 一次性踏板：用 Enter 触发，只在玩家进入时触发一次。
    // 复原后玩家重新踩上会重新触发 Enter，无需依赖离开事件解锁。
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            // 返回原位期间忽略触发，防止把平台又推回去
            if (isReturning != null && isReturning()) return;
            if (isOneTime)
                controller.SetPedalPressed(index, true);
        }
    }

    // 非一次性踏板：用 Stay 触发（持续保持按下，离开则弹回）
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            // 返回原位期间忽略触发
            if (isReturning != null && isReturning()) return;
            if (!isOneTime)
                controller.SetPedalPressed(index, true);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            // 非一次性踏板离开时复位（一次性踏板在 SetPedalPressed 内部会忽略 false）
            controller.SetPedalPressed(index, false);
        }
    }
}
