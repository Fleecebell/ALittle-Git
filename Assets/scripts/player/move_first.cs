using UnityEngine;

public class move_first : MonoBehaviour
{
    public Animator p1_jump;

    public static float moveSpeed = 10f;
    public static float jumpForce = 20f;
    public float dashForce = 20f;

    [Header("冲刺设置")]
    public float dashDuration = 0.1f;
    public float dashCooldown = 1f;
    private bool canDash = true;
    private float dashCooldownTimer;
    private bool isDashing;

    private Rigidbody2D rb;
    public bool isGrounded;

    public float groundAngleThreshold = 45f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        // 冷却计时
        if (!canDash)
        {
            dashCooldownTimer -= Time.deltaTime;
            if (dashCooldownTimer <= 0)
            {
                canDash = true;
            }
        }

        float moveInput = Input.GetAxis("Horizontal");

        // 正常移动（冲刺时不覆盖速度）
        if (!isDashing)
        {
            rb.velocity = new Vector2(moveInput * moveSpeed, rb.velocity.y);
        }

        // ========== 长按空格连续跳（只有在地面才跳） ==========
        if (Input.GetKey(KeyCode.Space) && isGrounded)
        {
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);
        }

        // 动画
        if (isGrounded)
        {
            p1_jump.SetBool("p1j", false);
        }
        else
        {
            p1_jump.SetBool("p1j", true);
        }

        // 冲刺（支持空中）
        if (Input.GetKeyDown(KeyCode.LeftShift) && canDash)
        {
            StartCoroutine(Dash());
        }
    }

    // 冲刺协程
    private System.Collections.IEnumerator Dash()
    {
        canDash = false;
        isDashing = true;
        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0;

        float horizontal = Input.GetAxisRaw("Horizontal");
        if (horizontal != 0)
        {
            rb.velocity = new Vector2(horizontal * dashForce, 0);
        }

        yield return new WaitForSeconds(dashDuration);

        rb.gravityScale = originalGravity;
        isDashing = false;
        dashCooldownTimer = dashCooldown;
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

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("red"))
        {
            move_late.isR = true;
        }
    }
}