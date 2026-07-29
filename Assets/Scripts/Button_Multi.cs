using UnityEngine;
using System;

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

    // 平台移动增量（Button_Multi 自身就是移动平台）
    private Vector3 previousPos;
    public Vector3 PlatformDelta { get; private set; }

    void Start()
    {
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
            trigger.Init(this, i, pedals[i].isOneTime);

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

        if (allPressed)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition.position, Mathf.Min(speed * Time.deltaTime, 1f));
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, originPosition, Mathf.Min(speed * Time.deltaTime, 1f));
        }

        PlatformDelta = transform.position - previousPos;

        if (Input.GetKeyDown(KeyCode.R) || Die.touch_lava)
        {
            ResetAll();
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

    private void OnDestroy()
    {
        if (Button_once.PlatformDeltas.ContainsKey(this.gameObject))
            Button_once.PlatformDeltas.Remove(this.gameObject);
    }
}

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
            if (!isOneTime)
            {
                controller.SetPedalPressed(index, false);
            }
        }
    }
}
