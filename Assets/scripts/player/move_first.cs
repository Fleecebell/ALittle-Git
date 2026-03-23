using UnityEngine;

public class move_first : MonoBehaviour
{
    public Animator p1_jump;

    public static float moveSpeed = 10f;
    public static float jumpForce = 20f;
    public static float dashForce = 20f;//
    public float dashTime = 0.1f;//
    public bool canDash = true;
    private Rigidbody2D rb;
    public static bool isGrounded;

    // 用于判断地面的角度阈值，小于这个角度认为是地面
    public float groundAngleThreshold = 45f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        float moveInput = Input.GetAxis("Horizontal");
        rb.velocity = new Vector2(moveInput * moveSpeed, rb.velocity.y);

        if(isGrounded)
        {
            p1_jump.SetBool("p1j", false);
        }
        else
        {
            p1_jump.SetBool("p1j", true);
        }

        if (Input.GetKey(KeyCode.Space) && isGrounded)
        {
            rb.velocity = Vector2.up * jumpForce;
            isGrounded = false; // 跳跃后立即设为未落地
        }

        if(Input.GetKey(KeyCode.A) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && canDash)
        {
            dashTime -= Time.deltaTime;
            if (dashTime <= 0)
            {
                canDash = false;
            }
            else
            {
                rb.velocity = Vector2.left * dashForce;
            }
        }

        if (Input.GetKey(KeyCode.D) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && canDash)
        {
            dashTime -= Time.deltaTime;
            if (dashTime <= 0)
            {
                canDash = false;
            }
            else
            {
                rb.velocity = Vector2.right * dashForce;
            }
        }

        if(isGrounded)
        {
            dashForce = 10f;
        }
        else
        {
            dashForce = 20f;
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
                    canDash = true;
                    dashTime = 0.1f;
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
            move_late.isR = true;
        }
    }
    //private void OnTriggerStay2D(Collider2D collision)
    //{
    //    if (collision.gameObject.CompareTag("Finish"))
    //    {
    //        isGrounded = true;
    //    }
    //}
}