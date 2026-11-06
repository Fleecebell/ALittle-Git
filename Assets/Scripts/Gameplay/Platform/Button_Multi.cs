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

    [Header("单按钮触发")]
    [Tooltip("勾选后：只要踩到任意一个踏板就触发（逻辑或）。不勾选：必须踩下全部踏板才触发（逻辑与）。")]
    public bool triggerOnSinglePedal = false;

    private Vector3 originPosition;
    private bool[] pedalPressed;
    // 所有踏板是否都被踩下（用于踏板回弹视觉）
    private bool allPressed;
    // 本帧是否满足触发条件（由 triggerOnSinglePedal 决定：全部踩下 或 任意一个踩下）
    private bool triggerConditionMet;
    private bool triggered;   // 是否已触发过一次性移动
    // 返回原位中：死亡/按R复原后平台平滑移回起点，此期间忽略踏板触发
    private bool returning = false;

    // 平台移动增量（Button_Multi 自身就是移动平台）
    private Vector3 previousPos;
    public Vector3 PlatformDelta { get; private set; }

    // SmoothStep 进度（0=起点，1=目标），direction 表示方向（+1前进，-1返回），用于慢快慢曲线
    private float moveProgress = 0f;
    private int direction = 1;

    // 已绑定的踏板触发器（销毁时解绑，避免残留监听者）
    private readonly List<PedalTrigger> boundTriggers = new List<PedalTrigger>();

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

            // 同一个踏板可能被多个平台(多个 Button_Multi)共用：
            // 已经有 PedalTrigger 就复用它并追加一个监听者，不能 Destroy 掉别人注册的那份，
            // 否则后执行的 Start 会把先注册的平台踢掉(表现就是只有一个平台会动)。
            var trigger = pedals[i].pedal.GetComponent<PedalTrigger>();
            if (trigger == null) trigger = pedals[i].pedal.AddComponent<PedalTrigger>();
            trigger.Bind(this, i, pedals[i].isOneTime, () => returning);

            if (!boundTriggers.Contains(trigger))
                boundTriggers.Add(trigger);

            var big = pedals[i].pedal.transform.Find("big");
            if (big != null) big.gameObject.SetActive(true);
        }
    }

    void Update()
    {
        // 统计已踩下的踏板数量
        int pressedCount = 0;
        for (int i = 0; i < pedals.Length; i++)
        {
            if (pedalPressed[i]) pressedCount++;
        }

        // 所有踏板是否都被踩下（仅用于踏板回弹视觉，与触发条件解耦）
        allPressed = pressedCount == pedals.Length;
        // 触发条件：不勾选时需全部踩下；勾选 triggerOnSinglePedal 后踩到任意一个即可
        triggerConditionMet = triggerOnSinglePedal ? pressedCount > 0 : allPressed;

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
        else if (triggerConditionMet)
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

        // 把自己从踏板触发器上解绑，避免留下空监听者
        for (int i = 0; i < boundTriggers.Count; i++)
        {
            if (boundTriggers[i] != null)
                boundTriggers[i].Unbind(this);
        }
        boundTriggers.Clear();
    }
}

// ======================================================================
// PedalTrigger —— 踏板触发器
// ----------------------------------------------------------------------
// 一个踏板可以被多个 Button_Multi（多个平台）同时引用，所以这里维护的是
// “监听者列表”，而不是单个 controller：
//   · Button_Multi.Start 里用 Bind 追加监听者（同一控制器重复 Bind 会覆盖旧记录）
//   · Button_Multi.OnDestroy 里用 Unbind 移除
//   · 触发时把 Enter / Stay / Exit 广播给所有监听者，各自按自己的
//     index / isOneTime / returning 状态处理
// ======================================================================
public class PedalTrigger : MonoBehaviour
{
    private class Listener
    {
        public Button_Multi controller;
        public int index;
        public bool isOneTime;
        public Func<bool> isReturning;
    }

    private readonly List<Listener> listeners = new List<Listener>();

    // 绑定一个控制器（同一控制器重复绑定会覆盖旧记录）
    public void Bind(Button_Multi ctrl, int idx, bool oneTime, Func<bool> returningCheck)
    {
        if (ctrl == null) return;

        Unbind(ctrl);
        listeners.Add(new Listener
        {
            controller = ctrl,
            index = idx,
            isOneTime = oneTime,
            isReturning = returningCheck,
        });
    }

    // 解绑一个控制器
    public void Unbind(Button_Multi ctrl)
    {
        for (int i = listeners.Count - 1; i >= 0; i--)
        {
            if (listeners[i].controller == ctrl)
                listeners.RemoveAt(i);
        }
    }

    private static bool IsPlayer(Collider2D collision)
    {
        return collision.CompareTag("Player") || collision.CompareTag("Player2");
    }

    // 一次性踏板：用 Enter 触发，只在玩家进入时触发一次。
    // 复原后玩家重新踩上会重新触发 Enter，无需依赖离开事件解锁。
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsPlayer(collision)) return;

        for (int i = listeners.Count - 1; i >= 0; i--)
        {
            Listener listener = listeners[i];
            if (listener.controller == null)
            {
                listeners.RemoveAt(i);
                continue;
            }

            // 返回原位期间忽略触发，防止把平台又推回去
            if (listener.isReturning != null && listener.isReturning()) continue;
            if (listener.isOneTime)
                listener.controller.SetPedalPressed(listener.index, true);
        }
    }

    // 非一次性踏板：用 Stay 触发（持续保持按下，离开则弹回）
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (!IsPlayer(collision)) return;

        for (int i = listeners.Count - 1; i >= 0; i--)
        {
            Listener listener = listeners[i];
            if (listener.controller == null)
            {
                listeners.RemoveAt(i);
                continue;
            }

            // 返回原位期间忽略触发
            if (listener.isReturning != null && listener.isReturning()) continue;
            if (!listener.isOneTime)
                listener.controller.SetPedalPressed(listener.index, true);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!IsPlayer(collision)) return;

        for (int i = listeners.Count - 1; i >= 0; i--)
        {
            Listener listener = listeners[i];
            if (listener.controller == null)
            {
                listeners.RemoveAt(i);
                continue;
            }

            // 非一次性踏板离开时复位（一次性踏板在 SetPedalPressed 内部会忽略 false）
            listener.controller.SetPedalPressed(listener.index, false);
        }
    }
}
