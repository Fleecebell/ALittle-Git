using UnityEngine;
using System.Collections.Generic;

public class move_late : MonoBehaviour
{
    public Animator p2_jump;

    public move_first player;
    public float delayTime = 0.5f;

    private Rigidbody2D rb;
    private bool isGrounded;

    private Queue<MovementRecord> movementHistory = new Queue<MovementRecord>();
    public float groundAngleThreshold = 45f;

    public GameObject p1;
    public GameObject p2;

    Vector3 p1Pos;
    Vector3 p2Pos;
    public static bool isR = false;

    // 冲刺冷却
    public float dashCooldown = 1f;
    private float lastDashTime;

    [Header("缓降")]
    public float fallSpeedLimit = -3f;

    // 用来记录缓降时间段
    private Queue<ShiftRecord> shiftHistory = new Queue<ShiftRecord>();
    private struct ShiftRecord
    {
        public bool isShiftDown;
        public float timeRecorded;
    }

    // 当前P2是否应该缓降
    private bool isSlowFalling = false;

    private struct MovementRecord
    {
        public float horizontalInput;
        public bool jumpInput;
        public bool isDashingLeft;
        public bool isDashingRight;
        public float timeRecorded;
    }

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        p1Pos = p1.transform.position;
        p2Pos = p2.transform.position;
    }

    void Update()
    {
        if (player == null) return;

        RecordPlayerInput();
        UpdateSlowFallState(); // 更新缓降状态（延迟）
        ExecuteDelayedActions();

        // 应用缓降
        if (!isGrounded && rb.velocity.y < 0 && isSlowFalling)
        {
            rb.velocity = new Vector2(rb.velocity.x, Mathf.Max(rb.velocity.y, fallSpeedLimit));
        }

        if (isGrounded)
        {
            p2_jump.SetBool("p2j", false);
        }
        else
        {
            p2_jump.SetBool("p2j", true);
        }

        if (Input.GetKeyDown(KeyCode.R) || isR)
        {
            p1.transform.position = p1Pos;
            p2.transform.position = p2Pos;
            isR = false;
            lastDashTime = -10f;
            shiftHistory.Clear();
            isSlowFalling = false;
        }
    }

    // 记录Shift按下/松开
    private void RecordPlayerInput()
    {
        float horizontal = Input.GetAxis("Horizontal");
        bool jump = Input.GetKey(KeyCode.Space);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        bool dashLeft = Input.GetKey(KeyCode.A) && shift;
        bool dashRight = Input.GetKey(KeyCode.D) && shift;

        movementHistory.Enqueue(new MovementRecord
        {
            horizontalInput = horizontal,
            jumpInput = jump,
            isDashingLeft = dashLeft,
            isDashingRight = dashRight,
            timeRecorded = Time.time
        });

        // 记录shift状态变化
        if (shiftHistory.Count == 0 || shiftHistory.Peek().isShiftDown != shift)
        {
            shiftHistory.Enqueue(new ShiftRecord
            {
                isShiftDown = shift,
                timeRecorded = Time.time
            });
        }
    }

    // 更新P2的缓降状态（延迟执行）
    private void UpdateSlowFallState()
    {
        while (shiftHistory.Count > 0 && Time.time - shiftHistory.Peek().timeRecorded >= delayTime)
        {
            var rec = shiftHistory.Dequeue();
            isSlowFalling = rec.isShiftDown;
        }
    }

    private void ExecuteDelayedActions()
    {
        while (movementHistory.Count > 0 && Time.time - movementHistory.Peek().timeRecorded >= delayTime)
        {
            var record = movementHistory.Dequeue();

            bool canDash = Time.time >= lastDashTime + dashCooldown;

            if (canDash && record.isDashingLeft)
            {
                rb.velocity = Vector2.left * player.dashForce * 0.3f;
                lastDashTime = Time.time;
            }
            else if (canDash && record.isDashingRight)
            {
                rb.velocity = Vector2.right * player.dashForce * 0.3f;
                lastDashTime = Time.time;
            }
            else
            {
                rb.velocity = new Vector2(record.horizontalInput * move_first.moveSpeed, rb.velocity.y);
            }

            if (record.jumpInput && isGrounded)
            {
                rb.velocity = Vector2.up * move_first.jumpForce;
            }
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground") || collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            foreach (ContactPoint2D contact in collision.contacts)
            {
                float angle = Vector2.Angle(contact.normal, Vector2.up);
                if (angle < groundAngleThreshold)
                {
                    isGrounded = true;
                    return;
                }
            }
            isGrounded = false;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground") || collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            isGrounded = false;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("red"))
        {
            isR = true;
        }
    }
}