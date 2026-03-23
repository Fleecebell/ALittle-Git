using UnityEngine;
using System.Collections.Generic;
using System;


public class move_late : MonoBehaviour
{
    public Animator p2_jump;

    // 主角的引用，需要在Inspector中手动赋值
    public move_first player;
    // 延迟时间（秒）
    public float delayTime = 0.5f;

    private Rigidbody2D rb;
    private bool isGrounded;

    // 存储主角的动作历史（包含冲刺状态）
    private Queue<MovementRecord> movementHistory = new Queue<MovementRecord>();
    
    // 用于判断地面的角度阈值，小于这个角度认为是地面
    public float groundAngleThreshold = 45f;

    public GameObject p1;
    public GameObject p2;

    Vector3 p1Pos;
    Vector3 p2Pos;
    public static bool isR = false;

    // 扩展记录结构，增加冲刺相关信息
    private struct MovementRecord
    {
        public float horizontalInput;
        public bool jumpInput;
        public bool isDashingLeft;  // 是否向左冲刺
        public bool isDashingRight; // 是否向右冲刺
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

        // 记录主角当前的输入（包括冲刺）
        RecordPlayerInput();

        // 执行延迟后的动作
        ExecuteDelayedActions();

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
        }

    }

    // 记录主角的输入（增加冲刺状态记录）
    private void RecordPlayerInput()
    {
        float horizontal = Input.GetAxis("Horizontal");
        bool jump = Input.GetKey(KeyCode.Space);// && move_first.isGrounded;
        // 记录冲刺状态
        bool dashLeft = Input.GetKey(KeyCode.A) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        bool dashRight = Input.GetKey(KeyCode.D) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

        movementHistory.Enqueue(new MovementRecord
        {
            horizontalInput = horizontal,
            jumpInput = jump,
            isDashingLeft = dashLeft,
            isDashingRight = dashRight,
            timeRecorded = Time.time
        });
    }

    // 执行延迟后的动作（增加冲刺动作执行）
    private void ExecuteDelayedActions()
    {
        // 移除过期的记录
        while (movementHistory.Count > 0 &&
               Time.time - movementHistory.Peek().timeRecorded >= delayTime)
        {
            var record = movementHistory.Dequeue();

            // 优先处理冲刺动作
            if (record.isDashingLeft)
            {
                rb.velocity = Vector2.left * move_first.dashForce * 0.3f;
            }
            else if (record.isDashingRight)
            {
                rb.velocity = -Vector2.left * move_first.dashForce * 0.3f;
            }
            // 常规移动
            else
            {
                rb.velocity = new Vector2(record.horizontalInput * move_first.moveSpeed, rb.velocity.y);
            }

            // 跳跃动作
            if (record.jumpInput && isGrounded)
            {
                rb.velocity = Vector2.up * move_first.jumpForce;
                Debug.Log("jump");
            }
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground") || collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            // 检查是否有碰撞点的法线接近垂直（地面）
            foreach (ContactPoint2D contact in collision.contacts)
            {
                // 计算法线与竖直方向的夹角
                float angle = Vector2.Angle(contact.normal, Vector2.up);

                // 如果角度小于阈值，认为是在地面上
                if (angle < groundAngleThreshold)
                {
                    isGrounded = true;
                    return; // 找到一个有效地面接触点就可以返回了
                }
            }
            // 如果所有接触点都不满足地面条件，则不是在地面上
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