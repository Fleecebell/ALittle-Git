using UnityEngine;
using System.Collections;

public class MoveFirst : MonoBehaviour
{
    #region 参数
    public Animator jumpAnimator;
    public static float moveSpeed = 10f;
    public float minMoveSpeed = 0f;

    public float maxMoveSpeed = 50f;
    public static float jumpForce = 20f;
    public float dashForce = 20f;

    public static float lastPositionX;
    public static float traveledDistance;

    [Header("冲刺")]
    public float dashDuration = 0.1f;
    public float dashCooldown = 1f;

    [Header("爬梯")]
    public float climbSpeed = 5f;

    private Rigidbody2D rb;
    public bool isGrounded;
    public float groundAngleThreshold = 45f;

    // 状态
    private bool canDash = true;
    private float dashCooldownTimer;
    private bool isDashing;

    private bool isOnLadder;
    private bool isClimbing;

    // 记录上一次 P2 同步冲刺方向
    public static float lastDashDirection = 0f;
    public static float moveDir; // 方向：-1=左  0=不动  1=右
    public static bool isMoving; // 是否在移动 
    #endregion

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        MoveInput();
        LadderInput();
        DashInput();
        Animation();
    }

    private void FixedUpdate()
    {
        // 每帧物理更新开始时先把着地状态重置为 false，
        // 然后由 OnCollisionStay2D 在真正踩到地面（法线朝上）时重新设为 true。
        // 注意：不能在 OnCollisionStay2D / OnCollisionExit2D 里设 false，
        // 否则两个角色贴在一起时，水平碰撞的法线不满足地面条件，会把 isGrounded 错误覆盖为 false，导致跳不起来。
        isGrounded = false;
        LadderPhysics();
    }

    #region 移动
    void MoveInput()
    {
        if (isDashing) return;

        float moveInput = Input.GetAxis("Horizontal");

        moveDir = moveInput;
        isMoving = Mathf.Abs(moveInput) > 0.1f;

        rb.velocity = new Vector2(moveInput * moveSpeed, rb.velocity.y);

        // 跳跃：空格 或 W 都可以触发
        // 必须同时满足：在地面(isGrounded) + 没在爬梯(!isClimbing)
        if ((Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W)) && isGrounded && !isClimbing)
        {
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);
        }

        moveSpeed = Mathf.Clamp(moveSpeed, minMoveSpeed, maxMoveSpeed);

        float deltaX = transform.position.x - lastPositionX;
        traveledDistance += deltaX;
        lastPositionX = transform.position.x;
    }
    #endregion

    #region 冲刺
    void DashInput()
    {
        if (!canDash)
        {
            dashCooldownTimer -= Time.deltaTime;
            if (dashCooldownTimer <= 0) canDash = true;
        }

        if (Input.GetKeyDown(KeyCode.LeftShift) && canDash)
        {
            StartCoroutine(Dash()); 
        }
    }

    private IEnumerator Dash()
    {
        canDash = false;
        isDashing = true;
        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0;

        float horizontal = Input.GetAxisRaw("Horizontal");
        lastDashDirection = horizontal;   // 记录冲刺方向 P2 使用

        if (horizontal != 0)
        {
            rb.velocity = new Vector2(horizontal * dashForce, 0);
        }

        yield return new WaitForSeconds(dashDuration);

        rb.gravityScale = originalGravity;
        isDashing = false;
        dashCooldownTimer = dashCooldown;
    }
    #endregion

    #region 爬梯
    void LadderInput()
    {
        // 爬梯触发：空格 或 W 都可以
        if (isOnLadder && (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W)))
        {
            isClimbing = true;
        }
    }

    void LadderPhysics()
    {
        if (isClimbing)
        {
            rb.gravityScale = 0f;
            float v = 0;
            // 向上爬：空格 或 W 都可以
            if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W)) v = climbSpeed;
            if (Input.GetKey(KeyCode.S)) v = -climbSpeed;
            rb.velocity = new Vector2(rb.velocity.x, v);
        }
        else if (!isDashing)
        {
            rb.gravityScale = 9.8f;
        }
    }
    #endregion

    #region 动画
    void Animation()
    {
        jumpAnimator.SetBool("p1j", !isGrounded);
    }
    #endregion

    #region 碰撞
    private void OnCollisionStay2D(Collision2D collision)
    {
        // 检测是否站在移动平台上，自动跟随（无需配置）
        if (Button_once.PlatformDeltas.TryGetValue(collision.gameObject, out var getDelta))
        {
            Vector3 d = getDelta();
            if (d != Vector3.zero)
                transform.position += d;
        }

        if (collision.gameObject.CompareTag("Ground") || collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            foreach (ContactPoint2D contact in collision.contacts)
            {
                float angle = Vector2.Angle(contact.normal, Vector2.up);
                if (angle < groundAngleThreshold)
                {
                    isGrounded = true;
                    canDash = true;
                    return;
                }
            }
            // 不在这里设 isGrounded = false，交给 FixedUpdate 重置
            // 否则两个角色贴在一起时，水平碰撞法线不满足地面条件，会把 isGrounded 错误覆盖为 false
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        // 不在这里设 isGrounded = false，交给 FixedUpdate 重置
        // 否则离开与另一个角色的接触时（即使还站在地上）会把 isGrounded 错误设为 false
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Ladder"))
            isOnLadder = true;
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Ladder"))
        {
            isOnLadder = false;
            isClimbing = false;
        }
    }
    #endregion
}
