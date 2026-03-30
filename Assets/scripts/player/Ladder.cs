using UnityEngine;

public class Ladder : MonoBehaviour
{
    [Header("攀爬速度")]
    public float climbSpeed = 5f;

    private bool isOnLadder;
    private bool isClimbing; // 是否处于攀爬状态

    [SerializeField] private Rigidbody2D rb;

    void Update()
    {
        // 只要在梯子上按住空格，就进入攀爬状态
        if (isOnLadder && Input.GetKey(KeyCode.Space))
        {
            isClimbing = true;
        }
    }

    private void FixedUpdate()
    {
        if (isClimbing)
        {
            rb.gravityScale = 0f;

            float verticalVel = 0f;
            if (Input.GetKey(KeyCode.Space)) verticalVel = climbSpeed;
            if (Input.GetKey(KeyCode.S)) verticalVel = -climbSpeed;

            rb.velocity = new Vector2(rb.velocity.x, verticalVel);
        }
        else
        {
            rb.gravityScale = 9.8f;
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Ladder"))
        {
            isOnLadder = true;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Ladder"))
        {
            isOnLadder = false;
            isClimbing = false; // 离开梯子才取消攀爬悬浮
        }
    }
}