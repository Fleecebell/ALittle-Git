using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class WindArea : MonoBehaviour
{
    public enum WindDirection { Left, Right, Up, Down }

    [Header("风向")]
    [SerializeField] private WindDirection direction = WindDirection.Right;

    [Header("=== 左右风 ===")]
    [SerializeField] private float speedP1 = 2f;
    [SerializeField] private float speedP2 = 3f;

    [Header("=== 上下风 ===")]
    [Tooltip("达到此值时玩家悬空（风力 = 重力），低于则跳更高，高于则飞起")]
    [SerializeField] private float hoverForceP1 = 10f;
    [SerializeField] private float hoverForceP2 = 12f;

    private void OnTriggerStay2D(Collider2D other)
    {
        // ── 左右风：直接移动 Transform，不动 ──
        if (direction == WindDirection.Left || direction == WindDirection.Right)
        {
            float speed;
            if (other.CompareTag("Player"))
                speed = speedP1;
            else if (other.CompareTag("Player2"))
                speed = speedP2;
            else
                return;

            float dir = direction == WindDirection.Right ? 1f : -1f;
            other.transform.position += new Vector3(dir * speed * Time.fixedDeltaTime, 0, 0);
            return;
        }

        // ── 上下风：修改竖直速度 ──
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null) return;

        float force;
        if (other.CompareTag("Player"))
            force = hoverForceP1;
        else if (other.CompareTag("Player2"))
            force = hoverForceP2;
        else
            return;

        float vDir = direction == WindDirection.Up ? 1f : -1f;
        rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y + vDir * force * Time.fixedDeltaTime);
    }
}
