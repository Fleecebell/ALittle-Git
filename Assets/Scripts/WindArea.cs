using UnityEngine;

// ======================================================================
// WindArea —— 风区 (用 Tilemap 涂出风的影响范围)
// --------------------------------------------------------------------------
// 工作方式:
//   挂在带 TilemapCollider2D 的 Tilemap 物体上, 玩家走进涂了瓦片的区域时受风.
//   左右风: 直接移动 Transform (适合平台跳跃横向推力).
//   上下风: 改 Rigidbody2D.velocity (向上托起/向下压).
//
// 与 SandstormController 联动:
//   风力受 SandstormController.CurrentIntensity 控制.
//   没沙尘暴时(intensity=0)风完全不生效, 主角正常行走;
//   沙尘暴时风力随强度平滑增大.
//   如果场景没挂 SandstormController(旧场景), 默认强度=1, 保持原有行为.
// ======================================================================
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

    [Header("=== 风滚草风力 ===")]
    [Tooltip("施加给风滚草(Tumbleweed)的力. 风滚草很轻(Mass=0.1), 同样的力效果比玩家明显得多, 通常设玩家速度的 1.5~2 倍")]
    [SerializeField] private float tumbleweedForce = 4f;

    private void OnTriggerStay2D(Collider2D other)
    {
        // 查询当前沙尘暴强度: 0=没沙尘暴(风不生效), 1=强沙尘暴(风力全开).
        // 如果场景里没挂 SandstormController(旧场景), 默认强度=1, 保持原有行为兼容.
        float intensity = (SandstormController.Instance != null)
            ? SandstormController.Instance.CurrentIntensity
            : 1f;
        // 没沙尘暴时风完全不生效, 主角行走不受影响
        if (intensity < 0.01f) return;

        // ── 左右风 ──
        if (direction == WindDirection.Left || direction == WindDirection.Right)
        {
            float dir = direction == WindDirection.Right ? 1f : -1f;

            // 风滚草: 用 AddForce (它是 Dynamic Rigidbody, 直接改 transform 会和物理冲突)
            // 风力 = tumbleweedForce × 沙尘暴强度, 平滑过渡, 风暴时被吹飞
            // 变量名用 weedRb 避免和下面上下风分支的 rb 重名 (CS0136)
            if (other.CompareTag("Tumbleweed"))
            {
                Rigidbody2D weedRb = other.attachedRigidbody;
                if (weedRb != null)
                    weedRb.AddForce(Vector2.right * dir * tumbleweedForce * intensity, ForceMode2D.Force);
                return;
            }

            // 玩家: 通过 Rigidbody2D 施加水平速度
            // 注意: 不能直接改 transform.position!
            // 直接改 position 会绕过物理碰撞检测, 两个角色被吹到一起时互相穿透,
            // 物理引擎把它们强行挤开 → 下一帧风又拉回去 → 重叠并交替闪烁.
            // 用 rb.velocity 让物理引擎统一处理碰撞, 角色会自然被推开并保持在碰撞体之外.
            Rigidbody2D prb = other.attachedRigidbody;
            if (prb == null) return;

            float speed;
            if (other.CompareTag("Player"))
                speed = speedP1;
            else if (other.CompareTag("Player2"))
                speed = speedP2;
            else
                return;

            // 风力乘以沙尘暴强度, 实现平滑过渡(淡入时风逐渐变大, 淡出时逐渐变小)
            float windVx = dir * speed * intensity;
            // 保留原有竖直速度(如重力/跳跃), 只覆盖水平速度. 风强的直接把水平速度推满.
            prb.velocity = new Vector2(windVx, prb.velocity.y);
            return;
        }

        // ── 上下风：修改竖直速度 ──
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null) return;

        float vDir = direction == WindDirection.Up ? 1f : -1f;

        // 风滚草: 用 AddForce (轻物, 直接改 velocity 会很突兀, 力更自然)
        if (other.CompareTag("Tumbleweed"))
        {
            rb.AddForce(Vector2.up * vDir * tumbleweedForce * intensity, ForceMode2D.Force);
            return;
        }

        float force;
        if (other.CompareTag("Player"))
            force = hoverForceP1;
        else if (other.CompareTag("Player2"))
            force = hoverForceP2;
        else
            return;

        // 风力乘以沙尘暴强度, 平滑过渡
        rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y + vDir * force * intensity * Time.fixedDeltaTime);
    }
}
