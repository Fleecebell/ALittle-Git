using UnityEngine;
using System.Collections;

public class MoveFirst : MonoBehaviour, IWaterResponder
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
    public float jumpForce = 20f;
    public float dashForce = 20f;

    [Header("跳跃缓冲")]
    [Tooltip("跳跃缓冲时间(秒). 落地前这么短时间内按跳跃, 落地瞬间自动起跳. 解决空中按跳跃落地后不跳的问题")]
    public float jumpBufferDuration = 0.15f;
    // 跳跃缓冲计时器(>0 表示有跳跃请求待执行)
    private float jumpBufferTimer = 0f;

    [Tooltip("跳跃后冷却时间(秒). 跳起后这么短时间内不能再跳, 防止还没离地又跳(起飞问题)")]
    public float jumpLockDuration = 0.25f;
    // 跳跃冷却计时器(>0 表示刚跳过, 还不能跳)
    private float jumpLockTimer = 0f;

    [Header("变大/变小")]
    [Tooltip("变大目标缩放倍数. 1=原大小, 2=两倍大")]
    public float bigScale = 2f;
    [Tooltip("变大/变小过渡速度(越大变化越快)")]
    public float growShrinkSpeed = 5f;
    // 是否处于变大状态
    private bool isBig = false;
    // 目标缩放(1=原大小 或 bigScale)
    private float targetScale = 1f;

    [Header("水池")]
    [Tooltip("当前所在的水池(进入时由 WaterPool 设置, 离开时清空). 为空表示不在水里")]
    public WaterPool currentWaterPool = null;

    // IWaterResponder 接口实现: 供水池设置 currentWaterPool 引用
    public void SetWaterPool(WaterPool pool)
    {
        currentWaterPool = pool;
    }

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
    [Tooltip("单向平台着地容差. 角色脚底比平台顶面低多少以内才算踩到(防止从下方跳到平台中间就误判着地)")]
    public float oneWayPlatformSurfaceTolerance = 0.1f;

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

    [Header("单向平台下穿")]
    [Tooltip("按 S 从单向平台下落的持续时间(秒). 期间忽略单向平台碰撞, 到期自动恢复")]
    public float dropThroughDuration = 0.3f;
    // 下穿计时器(>0 表示正在下穿, 期间忽略单向平台碰撞)
    private float dropThroughTimer = 0f;
    // 角色自身的碰撞体引用
    private Collider2D myCollider;
    // 下穿期间被忽略碰撞的单向平台列表(到期恢复)
    private System.Collections.Generic.List<Collider2D> ignoredPlatforms = new System.Collections.Generic.List<Collider2D>();
    // 当前正在接触的地面/平台碰撞体(OnCollisionStay2D 收集, 用于 S 下穿时找到要忽略的平台)
    private System.Collections.Generic.List<Collider2D> currentContacts = new System.Collections.Generic.List<Collider2D>();
    #endregion

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        myCollider = GetComponent<Collider2D>();
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
        SizeInput();
        UpdateScale();

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

        // 单向平台下穿计时更新
        UpdateDropThrough();
    }

    // 单向平台下穿: 按下 S 时已忽略碰撞, 这里只负责计时到期恢复
    void UpdateDropThrough()
    {
        if (dropThroughTimer <= 0f) return;

        dropThroughTimer -= Time.deltaTime;

        // 计时结束, 恢复所有被忽略的碰撞
        if (dropThroughTimer <= 0f)
        {
            foreach (var c in ignoredPlatforms)
            {
                if (c != null) Physics2D.IgnoreCollision(myCollider, c, false);
            }
            ignoredPlatforms.Clear();
        }
    }

    // 变大/变小输入: 按 E 切换大小状态
    void SizeInput()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            isBig = !isBig;                  // 切换状态
            targetScale = isBig ? bigScale : 1f; // 设置目标缩放
        }
    }

    // 平滑过渡缩放: 每帧让当前缩放趋近目标缩放
    void UpdateScale()
    {
        float currentScale = transform.localScale.x;
        currentScale = Mathf.MoveTowards(currentScale, targetScale, growShrinkSpeed * Time.deltaTime);
        transform.localScale = new Vector3(currentScale, currentScale, 1f);
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

        // 跳跃：空格 或 W
        // 两种触发方式：
        //   1. GetKeyDown 按下瞬间 → 记入跳跃缓冲(空中按了落地也能跳)
        //   2. GetKey 长按 → 落地就跳(连按连跳, 踩影子也能跳)
        // 用 jumpLockTimer 冷却防止起飞：跳起后 0.25 秒内不能跳, 确保已离开地面
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W))
        {
            jumpBufferTimer = jumpBufferDuration;
        }
        bool jumpHeld = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W);

        // 计时器递减
        if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;
        if (jumpLockTimer > 0f) jumpLockTimer -= Time.deltaTime;

        // 执行跳跃：缓冲有效 或 长按, 且在地面 + 没冷却 + 没在爬梯 + 不在下穿中
        if ((jumpBufferTimer > 0f || jumpHeld) && isGrounded && !isClimbing && dropThroughTimer <= 0f && jumpLockTimer <= 0f)
        {
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);
            jumpBufferTimer = 0f;   // 跳了就清零缓冲
            jumpLockTimer = jumpLockDuration; // 启动冷却, 防止还没离地又跳(起飞)
        }

        // 水里游泳: 在水里时按 空格/W = 向上游
        // WaterPool.SwimUp 内部检查角色顶部是否出水, 出水不施加(防止游出水面)
        if (currentWaterPool != null && (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W)))
        {
            currentWaterPool.SwimUp(rb);
        }

        // 按 S 从单向平台下落(架子功能). 不在梯子上才触发(梯子上 S 是下爬)
        // 必须在地面 + 没在爬梯 + 没在冲刺 + 没在已下穿状态
        if (Input.GetKeyDown(KeyCode.S) && isGrounded && !isClimbing && !isDashing && dropThroughTimer <= 0f)
        {
            // 立即对当前接触的、有 PlatformEffector2D 的碰撞体(即单向平台)忽略碰撞
            // 不依赖 Layer, 直接用 OnCollisionStay2D 收集的 currentContacts
            bool foundPlatform = false;
            foreach (var c in currentContacts)
            {
                if (c == null) continue;
                // 单向平台判定：有 PlatformEffector2D 组件
                if (c.GetComponent<PlatformEffector2D>() != null)
                {
                    Physics2D.IgnoreCollision(myCollider, c, true);
                    if (!ignoredPlatforms.Contains(c)) ignoredPlatforms.Add(c);
                    foundPlatform = true;
                }
            }
            // 只有确实站在单向平台上才启动下穿计时
            if (foundPlatform)
            {
                dropThroughTimer = dropThroughDuration;
            }
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
            // 在水里时不恢复重力 — WaterPool 已设 gravityScale=0 并用目标速度模型管理沉浮
            if (currentWaterPool == null)
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
        // 收集当前接触的碰撞体(用于 S 下穿时找到单向平台)
        Collider2D col = collision.collider;
        if (col != null && !currentContacts.Contains(col))
        {
            currentContacts.Add(col);
        }

        if (collision.gameObject.CompareTag("Ground") || collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            foreach (ContactPoint2D contact in collision.contacts)
            {
                float angle = Vector2.Angle(contact.normal, Vector2.up);
                if (angle < groundAngleThreshold)
                {
                    // 单向平台额外检查: 角色脚底要接近平台顶面才算踩到
                    // 防止从下方跳上来时身体碰到平台中间就误判着地(中途借力再跳)
                    PlatformEffector2D effector = col.GetComponent<PlatformEffector2D>();
                    if (effector != null && myCollider != null)
                    {
                        float feetY = myCollider.bounds.min.y;       // 角色脚底
                        float platformTopY = col.bounds.max.y;       // 平台顶面
                        if (feetY < platformTopY - oneWayPlatformSurfaceTolerance)
                        {
                            continue; // 脚还在平台顶面下方 → 不算着地
                        }
                    }
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
        // 从接触列表移除
        if (collision.collider != null)
        {
            currentContacts.Remove(collision.collider);
        }
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
