using UnityEngine;
using System.Collections;

public class MoveFirst : MonoBehaviour
{
    #region 参数
    public Animator jumpAnimator;

    [Header("基础移动速度")]
    [Tooltip("角色初始移动速度（检查器可调）。运行时 moveSpeed 会以该值初始化")]
    public float baseMoveSpeed = 10f;
    // 运行时实际移动速度（static，被 SpeedBand 等脚本动态修改，P1/P2 共享）
    public static float moveSpeed = 10f;
    public float minMoveSpeed = 0f;

    public float maxMoveSpeed = 50f;
    public static float jumpForce = 20f;
    public float dashForce = 20f;

    public static float lastPositionX;
    public static float traveledDistance;

    [Header("移动加速度")]
    [Tooltip("开关：勾选后移动有加速度（速度渐变到目标值）；不勾选则速度瞬间到位")]
    public bool useAcceleration = false;
    [Tooltip("加速度大小（越大加速越快）。仅在 useAcceleration 勾选时生效")]
    public float acceleration = 30f;
    // 当前实际水平速度（用于加速度平滑，静止时为 0）
    private float currentHorizontalSpeed;

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

    // 离梯宽限计时：爬梯时短暂离开梯子(爬过头)给予回到梯子的缓冲时间, 防止直接掉落
    private const float ladderGraceTime = 0.3f;
    private float ladderGraceTimer;

    // 记录上一次 P2 同步冲刺方向
    public static float lastDashDirection = 0f;
    public static float moveDir; // 方向：-1=左  0=不动  1=右
    public static bool isMoving; // 是否在移动 

    // 外部风力(水平) —— 由 WindArea 每帧写入. 移动时叠加到水平速度上.
    // 玩家自己输入 = moveInput*moveSpeed, 叠加风 = windVx, 两者共存不冲突.
    public float externalWindVx = 0f;
    // 外部风力(垂直) —— 由 WindArea(上下风) 每帧写入. 叠加到垂直速度上.
    public float externalWindVy = 0f;
    #endregion

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        // 关闭 Rigidbody2D 插值，避免在 Update 里写 velocity 时产生平滑/渐变观感
        rb.interpolation = RigidbodyInterpolation2D.None;
        // 用检查器可调的 baseMoveSpeed 初始化实际移动速度
        moveSpeed = baseMoveSpeed;
    }

    void Update()
    {
        // 死亡动画播放期间, 禁用 P1 (主玩家) 的所有输入
        // 否则死亡瞬间还能按方向键/跳跃, 会导致动画结束后状态错乱
        if (DeathRespawnVFX.isDead)
        {
            // 死亡时停止移动 (防止惯性)
            if (rb != null) rb.velocity = Vector2.zero;
            return;
        }

        MoveInput();
        LadderInput();
        DashInput();
        Animation();

        // 离梯宽限计时递减；超时仍未回到梯子则彻底放弃攀爬
        if (ladderGraceTimer > 0)
        {
            ladderGraceTimer -= Time.deltaTime;
            if (ladderGraceTimer <= 0)
            {
                ladderGraceTimer = 0;
                isClimbing = false;
                isOnLadder = false;
            }
        }
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

        // 用 GetAxisRaw 获得瞬间的 -1/0/1，避免 Horizontal 轴的平滑导致无加速度时仍有渐变
        float moveInput = Input.GetAxisRaw("Horizontal");

        moveDir = moveInput;
        isMoving = Mathf.Abs(moveInput) > 0.1f;

        // 读取 WindArea 写入的外部风力, 用完归零 (等 WindArea 下一帧再写)
        float windVx = externalWindVx;
        externalWindVx = 0f;
        float windVy = externalWindVy;
        externalWindVy = 0f;

        float horizontalVelocity;
        if (useAcceleration)
        {
            // 有加速度：实际水平速度渐变逼近目标速度（加速、减速都平滑）
            currentHorizontalSpeed = Mathf.MoveTowards(
                currentHorizontalSpeed, moveInput * moveSpeed, acceleration * Time.deltaTime);
            horizontalVelocity = currentHorizontalSpeed + windVx;
        }
        else
        {
            // 无加速度：速度瞬间到位
            currentHorizontalSpeed = moveInput * moveSpeed;
            horizontalVelocity = currentHorizontalSpeed + windVx;
        }

        rb.velocity = new Vector2(horizontalVelocity, rb.velocity.y + windVy);

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
        // 只有在梯子上且正在攀爬时才能产生爬梯位移(防止凭空上爬)
        if (isClimbing && isOnLadder)
        {
            rb.gravityScale = 0f;
            float v = 0;
            // 向上爬：空格 或 W 都可以
            if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W)) v = climbSpeed;
            if (Input.GetKey(KeyCode.S)) v = -climbSpeed;
            rb.velocity = new Vector2(rb.velocity.x, v);
        }
        // 宽限期内离开梯子(爬到顶短暂脱离): 仍保留 isClimbing 防掉落,
        // 但走重力分支, 让玩家落回梯子顶部. 回到梯子后 OnTriggerStay2D 会恢复攀爬.
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
        {
            isOnLadder = true;
            // 宽限期内回到梯子则保留攀爬状态
            if (ladderGraceTimer > 0)
            {
                ladderGraceTimer = 0;
                isClimbing = true;
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Ladder"))
        {
            isOnLadder = false;
            // 正在攀爬时离开梯子(如爬过头), 启动宽限计时, 允许在短时间内回到梯子
            if (isClimbing)
                ladderGraceTimer = ladderGraceTime;
            else
                isClimbing = false;
        }
    }
    #endregion
}
