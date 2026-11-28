using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class playToChoose : MonoBehaviour
{
    [Header("要移动的对象")]
    public GameObject c0;

    [Header("移进来的目标点（检查器里拖入一个空物体，别拖 c0 的子物体）")]
    public Transform c1_pos;

    [Header("退出按钮（检查器里拖入，自动帮它绑定移出去）")]
    public Button exitButton;

    [Header("进入按钮（检查器里拖入，移进来后自动禁用，防止重复点击）")]
    public Button enterButton;

    [Header("移动手感（弹簧）")]
    [Tooltip("来回摆动的快慢，越大越快，建议 0.8~1.5")]
    public float frequency = 1.1f;

    [Tooltip("阻尼比：0.2 冲得夸张，0.4~0.5 冲过头一点点，0.8 以上基本不冲")]
    [Range(0.1f, 1.5f)]
    public float dampingRatio = 0.42f;

    // 运行时状态
    [Tooltip("当前是否处于“进来”的状态")]
    public bool open = false;

    private Vector3 homePos;      // 初始（出去后的）位置
    private Vector3 currentPos;   // 脚本自己记录的位置，避免被其他脚本一帧内改乱
    private Vector3 targetPos;    // 目标位置
    private Vector3 velocity;     // 弹簧速度
    private bool ready;

    /// <summary>按钮调用：移进来</summary>
    public void MoveIn()
    {
        if (c0 == null)
        {
            Debug.LogWarning("playToChoose: c0 未赋值");
            return;
        }
        if (c1_pos == null)
        {
            Debug.LogWarning("playToChoose: c1_pos 未赋值，无法移进来");
            return;
        }
        if (c1_pos.IsChildOf(c0.transform))
        {
            Debug.LogWarning("playToChoose: c1_pos 是 c0 的子物体，会导致永远到不了，请换成场景里的独立空物体");
            return;
        }

        // 已经在里面且已经停稳，就忽略这次点击
        if (open && (currentPos - c1_pos.position).sqrMagnitude < 1e-4f && velocity.sqrMagnitude < 1e-2f)
        {
            Debug.Log("[playToChoose] 已经在里面了，忽略这次 MoveIn");
            return;
        }

        targetPos = c1_pos.position;
        open = true;
        SetEnterButtonInteractable(false);   // 已经进来了，进入按钮不能再点
        Debug.Log("[playToChoose] MoveIn -> " + targetPos);
    }

    /// <summary>按钮调用：移出去（回到初始位置）</summary>
    public void MoveOut()
    {
        if (c0 == null)
        {
            Debug.LogWarning("playToChoose: c0 未赋值");
            return;
        }
        targetPos = homePos;
        open = false;
        SetEnterButtonInteractable(true);    // 出去了，进入按钮恢复可点
        Debug.Log("[playToChoose] MoveOut -> " + homePos);
    }

    /// <summary>保留原来的切换逻辑，按钮绑这个也行</summary>
    public void StartGame()
    {
        if (open)
            MoveOut();
        else
            MoveIn();
    }

    // 进入按钮的可点状态
    void SetEnterButtonInteractable(bool canClick)
    {
        if (enterButton == null)
        {
            Debug.LogWarning("playToChoose: enterButton 未赋值，无法禁用进入按钮");
            return;
        }

        enterButton.interactable = canClick;

        // 项目里的 BouncyButton 不检查 interactable，禁用后还会播悬停/按下/光圈动画，
        // 看起来像“还能点”，所以这里一并关掉
        BouncyButton bouncy = enterButton.GetComponent<BouncyButton>();
        if (bouncy != null)
            bouncy.enabled = canClick;
    }

    // 没手动拖 enterButton 时，自动找出绑了 MoveIn / StartGame 的按钮
    void ResolveEnterButtonIfMissing()
    {
        if (enterButton != null)
            return;

        Button[] allButtons = FindObjectsOfType<Button>(true);
        foreach (Button b in allButtons)
        {
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            {
                if (b.onClick.GetPersistentTarget(i) != this)
                    continue;

                string method = b.onClick.GetPersistentMethodName(i);
                if (method == nameof(MoveIn) || method == nameof(StartGame))
                {
                    enterButton = b;
                    Debug.Log("[playToChoose] 自动找到进入按钮: " + b.name);
                    return;
                }
            }
        }

        Debug.LogWarning("playToChoose: 没找到进入按钮，请手动把 Enter Button 拖进来");
    }

    void Awake()
    {
        ResolveEnterButtonIfMissing();

        // 和退出按钮一样的做法：脚本自己绑定点击，不用在检查器里再加 OnClick
        if (enterButton != null)
        {
            enterButton.onClick.RemoveListener(MoveIn);
            enterButton.onClick.AddListener(MoveIn);
        }

        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(MoveOut);
            exitButton.onClick.AddListener(MoveOut);
        }
    }

    void OnDestroy()
    {
        if (enterButton != null)
            enterButton.onClick.RemoveListener(MoveIn);

        if (exitButton != null)
            exitButton.onClick.RemoveListener(MoveOut);
    }

    void Start()
    {
        if (c0 == null)
        {
            Debug.LogWarning("playToChoose: c0 未赋值");
            return;
        }

        homePos = c0.transform.position;
        currentPos = homePos;
        targetPos = homePos;
        ready = true;

        SetEnterButtonInteractable(!open);   // 初始状态同步一下
    }

    // 用 LateUpdate：比 menu（鼠标视差）等脚本更晚执行，位置由本脚本说了算
    void LateUpdate()
    {
        if (!ready || c0 == null)
            return;

        // 低帧率时分步积分，避免弹簧爆掉
        float remaining = Time.deltaTime;
        const float maxStep = 1f / 120f;
        while (remaining > 0f)
        {
            float dt = Mathf.Min(remaining, maxStep);
            remaining -= dt;
            SpringStep(dt);
        }

        c0.transform.position = currentPos;
    }

    // 欠阻尼弹簧：起步慢、越走越快、冲过头、再被拉回来一点点
    void SpringStep(float dt)
    {
        float omega = 2f * Mathf.PI * Mathf.Max(0.01f, frequency);          // 角频率
        float damping = 2f * dampingRatio * omega;                          // 阻尼系数

        Vector3 offset = targetPos - currentPos;
        velocity += (offset * (omega * omega) - velocity * damping) * dt;
        currentPos += velocity * dt;

        // 停下来的时候精确贴合目标，避免长期抖动
        offset = targetPos - currentPos;
        if (offset.sqrMagnitude < 1e-6f && velocity.sqrMagnitude < 1e-4f)
        {
            currentPos = targetPos;
            velocity = Vector3.zero;
        }
    }
}
