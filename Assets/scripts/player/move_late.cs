using UnityEngine;
using System.Collections.Generic;

public class move_late : MonoBehaviour
{
    #region 变量
    public Animator JumpAnimator;
    public move_first player;
    public float delayTime = 0.5f;

    [Header("冲刺")]
    public float dashCooldown = 1f;

    [Header("缓降")]
    public float fallSpeedLimit = -3f;

    [Header("攀爬")]
    public float climbSpeed = 5f;

    private Rigidbody2D rb;
    public bool isGrounded;
    public float groundAngleThreshold = 45f;

    public GameObject p1;
    public GameObject p2;
    private Vector3 p1Pos, p2Pos;

    public static bool isR = false;
    private float lastDashTime;
    private bool isSlowFalling;

    private bool isOnLadder;
    private bool isClimbing;
    #endregion

    #region 队列、结构体
    // 输入队列
    private Queue<MovementRecord> movementHistory = new Queue<MovementRecord>();
    private Queue<ShiftRecord> shiftHistory = new Queue<ShiftRecord>();
    private Queue<KeyRecord> inputHistory = new Queue<KeyRecord>();

    private struct MovementRecord
    {
        public float h;
        public bool jump;
        public bool dashL;
        public bool dashR;
        public float time;
    }

    private struct ShiftRecord
    {
        public bool isShiftDown;
        public float timeRecorded;
    }

    private struct KeyRecord
    {
        public bool space;
        public bool s;
        public float time;
    }
    #endregion

    // 初始化、更新
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        p1Pos = p1.transform.position;
        p2Pos = p2.transform.position;
    }

    void Update()
    {
        if (player == null) return;

        RecordAllInput();
        SlowFallDelay();
        MoveDelay();
        LadderDelay();
        Animation();
        CheckReset();
    }

    #region 记录所有输入
    void RecordAllInput()
    {
        float h = Input.GetAxis("Horizontal");
        bool jump = Input.GetKey(KeyCode.Space);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool dashL = Input.GetKey(KeyCode.A) && shift;
        bool dashR = Input.GetKey(KeyCode.D) && shift;

        movementHistory.Enqueue(new MovementRecord { h = h, jump = jump, dashL = dashL, dashR = dashR, time = Time.time });

        if (shiftHistory.Count == 0 || shiftHistory.Peek().isShiftDown != shift)
            shiftHistory.Enqueue(new ShiftRecord { isShiftDown = shift, timeRecorded = Time.time });

        inputHistory.Enqueue(new KeyRecord { space = jump, s = Input.GetKey(KeyCode.S), time = Time.time });
    }
    #endregion

    #region 延迟部分：移动、跳跃、冲刺缓降；爬梯
    void MoveDelay()
    {
        while (movementHistory.Count > 0 && Time.time - movementHistory.Peek().time >= delayTime)
        {
            var r = movementHistory.Dequeue();
            bool canDash = Time.time >= lastDashTime + dashCooldown;

            if (canDash && r.dashL)
            {
                rb.velocity = Vector2.left * player.dashForce * 0.3f;
                lastDashTime = Time.time;
            }
            else if (canDash && r.dashR)
            {
                rb.velocity = Vector2.right * player.dashForce * 0.3f;
                lastDashTime = Time.time;
            }
            else
            {
                rb.velocity = new Vector2(r.h * move_first.moveSpeed, rb.velocity.y);
            }

            if (r.jump && isGrounded && !isClimbing)
                rb.velocity = Vector2.up * move_first.jumpForce;
        }
    }
    void SlowFallDelay()
    {
        while (shiftHistory.Count > 0 && Time.time - shiftHistory.Peek().timeRecorded >= delayTime)
            isSlowFalling = shiftHistory.Dequeue().isShiftDown;

        if (!isGrounded && rb.velocity.y < 0 && isSlowFalling && !isClimbing)
            rb.velocity = new Vector2(rb.velocity.x, Mathf.Max(rb.velocity.y, fallSpeedLimit));
    }

    void LadderDelay()
    {
        while (inputHistory.Count > 0 && Time.time - inputHistory.Peek().time >= delayTime)
        {
            var r = inputHistory.Dequeue();

            if (isOnLadder && r.space) isClimbing = true;
            if (!isOnLadder) isClimbing = false;

            if (isClimbing)
            {
                rb.gravityScale = 0;
                float v = 0;
                if (r.space) v = climbSpeed;
                if (r.s) v = -climbSpeed;
                rb.velocity = new Vector2(rb.velocity.x, v);
            }
            else
            {
                rb.gravityScale = 9.8f;
            }
        }
    }
    #endregion

    #region 动画
    void Animation()
    {
        JumpAnimator.SetBool("p2j", !isGrounded);
    }
    #endregion

    #region R重置
    void CheckReset()
    {
        if (Input.GetKeyDown(KeyCode.R) || isR)
        {
            // 核心复位
            p1.transform.position = p1Pos;
            p2.transform.position = p2Pos;
            isR = false;

            // 必须清（P2延迟队列）
            movementHistory.Clear();
            shiftHistory.Clear();
            inputHistory.Clear();

            // 建议清（防止浮空/卡状态）
            isSlowFalling = false;
            isClimbing = false;
            isOnLadder = false;
        }
    }
    #endregion

    #region 检测
    private void OnCollisionStay2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground") || col.gameObject.CompareTag("Player") || col.gameObject.CompareTag("Player2"))
        {
            foreach (ContactPoint2D c in col.contacts)
                if (Vector2.Angle(c.normal, Vector2.up) < groundAngleThreshold) { isGrounded = true; return; }
            isGrounded = false;
        }
    }

    private void OnCollisionExit2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground") || col.gameObject.CompareTag("Player") || col.gameObject.CompareTag("Player2"))
            isGrounded = false;
    }

    private void OnTriggerStay2D(Collider2D col)
    {
        if (col.CompareTag("Ladder")) isOnLadder = true;
    }

    private void OnTriggerExit2D(Collider2D col)
    {
        if (col.CompareTag("Ladder"))
        {
            isOnLadder = false;
            isClimbing = false;
        }
    }
    #endregion
}