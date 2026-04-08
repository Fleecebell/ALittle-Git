using UnityEngine;
using System.Collections;

public class Move_first : MonoBehaviour
{
    #region 变量
    public Animator JumpAnimator;
    public static float moveSpeed = 10f;
    public static float jumpForce = 20f;
    public float dashForce = 20f;

    [Header("冲刺")]
    public float dashDuration = 0.1f;
    public float dashCooldown = 1f;

    [Header("攀爬")]
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

    // 用于 P2 同步冲刺方向
    public static float lastDashDirection = 0f;
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
        LadderPhysics();
    }

    #region 移动
    void MoveInput()
    {
        if (isDashing) return;

        float moveInput = Input.GetAxis("Horizontal");
        rb.velocity = new Vector2(moveInput * moveSpeed, rb.velocity.y);

        if (Input.GetKey(KeyCode.Space) && isGrounded && !isClimbing)
        {
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);
        }
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
        lastDashDirection = horizontal;   // 记录方向供 P2 使用

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
        if (isOnLadder && Input.GetKey(KeyCode.Space))
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
            if (Input.GetKey(KeyCode.Space)) v = climbSpeed;
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
        JumpAnimator.SetBool("p1j", !isGrounded);
    }
    #endregion

    #region 检测
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
                    canDash = true;
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