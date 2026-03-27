using UnityEngine;

public class Ladder : MonoBehaviour
{
    [Header("攀爬速度")]
    public float climbSpeed = 5f;

    private bool isOnLadder;   // 是否在梯子触发区内
    private bool canClimb;     // 是否激活攀爬悬停状态

    [SerializeField] private Rigidbody2D rb;

    void Update()
    {
        // 在梯子内 按下空格 → 开启攀爬悬停
        if (isOnLadder && Input.GetKeyDown(KeyCode.Space))
        {
            canClimb = true;
        }
    }

    private void FixedUpdate()
    {
        if (canClimb)
        {
            // 攀爬模式：无重力、可上下移动/悬停
            rb.gravityScale = 0f;

            float verticalVel = 0f;
            if (Input.GetKey(KeyCode.Space)) verticalVel = climbSpeed;
            if (Input.GetKey(KeyCode.S)) verticalVel = -climbSpeed;

            rb.velocity = new Vector2(rb.velocity.x, verticalVel);
        }
        else
        {
            // 没开启攀爬：正常重力、会掉落
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
            canClimb = false; // 离开梯子重置攀爬状态
        }
    }
}