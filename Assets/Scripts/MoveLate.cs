using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

public class MoveLate : MonoBehaviour, IWaterResponder
{
    #region 变量
    public Animator jumpAnimator;
    public MoveFirst player;

    [Header("基础移动速度")]
    [Tooltip("角色初始移动速度（检查器可调）。运行时 moveSpeed 会以该值初始化")]
    public float baseMoveSpeed = 10f;
    // 运行时实际移动速度（static，被 SpeedBand 等脚本动态修改，P1/P2 共享）
    public static float moveSpeed = 10f;
    public float minMoveSpeed = 0f;

    public float maxMoveSpeed = 50f;
    public float jumpForce = 20f;

    [Header("跳跃缓冲")]
    [Tooltip("跳跃缓冲时间(秒). 影子收到跳跃记录后, 落地前这么短时间内落地会自动起跳")]
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
    // 是否处于变大状态(影子独立维护, 由重放记录切换)
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

    public static float moveDir;
    public static bool isMoving;
    
    public float delayTime = 0.5f;

    [Header("移动加速度")]
    [Tooltip("开关：勾选后移动有加速度（速度渐变到目标值）；不勾选则速度瞬间到位")]
    public bool useAcceleration = false;
    [Tooltip("加速度大小（越大加速越快）。仅在 useAcceleration 勾选时生效")]
    public float acceleration = 30f;
    // 当前实际水平速度（用于加速度平滑，静止时为 0）
    private float currentHorizontalSpeed;

    public static float lastPositionX;
    public static float traveledDistance;

    [Header("冲刺")]
    public float dashCooldown = 1f;

    [Header("攀爬")]
    public float climbSpeed = 5f;

    private Rigidbody2D rb;
    public bool isGrounded;
    public float groundAngleThreshold = 45f;
    [Tooltip("单向平台着地容差. 角色脚底比平台顶面低多少以内才算踩到(防止从下方跳到平台中间就误判着地)")]
    public float oneWayPlatformSurfaceTolerance = 0.1f;

    public GameObject p1, p2;
    private Vector3 p1Pos, p2Pos;

    public static bool isR = false;

    private bool canDash = true;
    private float dashCooldownTimer;
    private bool isDashing;

    [Header("P1 踩住时固定 P2")]
    public bool preventWhenSteppedOn = false;
    private bool isSteppedOn;

    private bool isOnLadder;
    private bool isClimbing;

    // 离梯宽限计时：爬梯时短暂离开梯子(爬过头)给予回到梯子的缓冲时间, 防止直接掉落
    private const float ladderGraceTime = 0.3f;
    private float ladderGraceTimer;

    // 外部风力(水平) —— 由 WindArea 每帧写入. 移动时叠加到水平速度上.
    // 玩家自己输入 = r.h*moveSpeed, 叠加风 = windVx, 两者共存不冲突.
    public float externalWindVx = 0f;
    // 外部风力(垂直) —— 由 WindArea(上下风) 每帧写入. 叠加到垂直速度上.
    public float externalWindVy = 0f;
    // 本帧是否处理过移动记录 (用于静止时是否单独应用风力)
    private bool processedMoveThisFrame = false;

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
    // 上一帧的 climbDown 状态, 用于边沿检测(模拟主角按下 S 的瞬间)
    private bool lastClimbDown = false;
    #endregion

    #region 队列
    public Queue<MovementRecord> movementHistory = new Queue<MovementRecord>();

    public struct MovementRecord
    {
        public float h;
        public bool jump;      // 按下瞬间(GetKeyDown)
        public bool jumpHeld;  // 持续按住(GetKey), 用于影子长按连跳
        public bool dash;
        public float dashDir;
        public bool climbDown;
        public bool sizeToggle; // 主角按 E 切换大小的瞬间(影子延迟重放)
        public float time;
    }
    #endregion

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        myCollider = GetComponent<Collider2D>();
        // 关闭 Rigidbody2D 插值，避免在 Update 里写 velocity 时产生平滑/渐变观感
        rb.interpolation = RigidbodyInterpolation2D.None;
        // 用检查器可调的 baseMoveSpeed 初始化实际移动速度
        moveSpeed = baseMoveSpeed;
        p1Pos = p1.transform.position;
        p2Pos = p2.transform.position;
    }

    void Update()
    {
        if (player == null) return;

        // 死亡动画播放期间, 禁用 P2 的所有输入和移动 (但仍要重置着地状态)
        // 注意: 不能直接 return, 否则 FixedUpdate 里 isGrounded 重置逻辑会受影响
        if (!DeathRespawnVFX.isDead)
        {
            RecordAllInput();

            if (!canDash)
            {
                dashCooldownTimer -= Time.deltaTime;
                if (dashCooldownTimer <= 0) canDash = true;
            }

            MoveDelay();
            Animation();
        }

        CheckReset();

        UpdateCurrentActionClear(Time.deltaTime);

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

        // 变大/变小过渡(始终执行, 让影子持续趋近目标缩放)
        UpdateScale();
    }

    // 变大/变小过渡: 每帧让当前缩放趋近目标缩放(和主角 MoveFirst 机制一致)
    void UpdateScale()
    {
        float currentScale = transform.localScale.x;
        currentScale = Mathf.MoveTowards(currentScale, targetScale, growShrinkSpeed * Time.deltaTime);
        transform.localScale = new Vector3(currentScale, currentScale, 1f);
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

    private void FixedUpdate()
    {
        // 每帧物理更新开始时先把着地状态重置为 false，
        // 然后由 OnCollisionStay2D 在真正踩到地面（法线朝上）时重新设为 true。
        isGrounded = false;
        isSteppedOn = false;
    }

    #region 记录输入
    void RecordAllInput()
    {
        // 用 GetAxisRaw 获得瞬间的 -1/0/1，避免 Horizontal 轴的平滑导致无加速度时仍有渐变
        float h = Input.GetAxisRaw("Horizontal");
        // 跳跃输入：空格 或 W
        // jump = 按下瞬间(GetKeyDown), 用于触发跳跃缓冲
        // jumpHeld = 持续按住(GetKey), 用于影子长按连跳(落地就跳)
        bool jump = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W);
        bool jumpHeld = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W);
        bool shiftDown = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
        bool dash = false;
        float dashDir = 0f;
        if (shiftDown)
        {
            if (Input.GetKey(KeyCode.A) && !Input.GetKey(KeyCode.D))
            {
                dash = true;
                dashDir = -1f;
            }
            else if (Input.GetKey(KeyCode.D) && !Input.GetKey(KeyCode.A))
            {
                dash = true;
                dashDir = 1f;
            }
        }

        movementHistory.Enqueue(new MovementRecord
        {
            h = h,
            jump = jump,
            jumpHeld = jumpHeld,
            dash = dash,
            dashDir = dashDir,
            climbDown = Input.GetKey(KeyCode.S),
            sizeToggle = Input.GetKeyDown(KeyCode.E),
            time = Time.time
        });
        if (movementHistory.Count > 1200) movementHistory.Dequeue();
    }
    #endregion

    #region 延迟行为
    void MoveDelay()
    {
        // 读取 WindArea 写入的外部风力并归零 (等 WindArea 下一帧再写).
        // 注意: P2 的 velocity 只在有到期移动记录时才被设置.
        // 若本帧没有到期记录(静止), 循环不执行, 这里单独把风力应用到当前速度,
        // 保证 P2 静止时也能被风吹动, 而不是一卡一卡.
        float windVx = externalWindVx;
        externalWindVx = 0f;
        float windVy = externalWindVy;
        externalWindVy = 0f;

        while (movementHistory.Count > 0 && Time.time - movementHistory.Peek().time >= delayTime)
        {
            var r = movementHistory.Dequeue();

            if (isDashing) continue;

            // P1 踩着 P2：跳过移动，只清队列
            if (preventWhenSteppedOn && isSteppedOn)
            {
                SetCurrentActionFromRecord(r);
                continue;
            }

            SetCurrentActionFromRecord(r);

            if (canDash && r.dash)
            {
                StartCoroutine(DashCoroutine(r.dashDir, player.dashDuration, player.dashForce));
                canDash = false;
                continue;
            }

            moveSpeed = Mathf.Clamp(moveSpeed, minMoveSpeed, maxMoveSpeed);

            moveDir = r.h;
            isMoving = Mathf.Abs(moveDir) > 0.1f;

            float horizontalVelocity;
            if (useAcceleration)
            {
                // 有加速度：实际水平速度渐变逼近目标速度（加速、减速都平滑）
                currentHorizontalSpeed = Mathf.MoveTowards(
                    currentHorizontalSpeed, r.h * moveSpeed, acceleration * Time.deltaTime);
                horizontalVelocity = currentHorizontalSpeed + windVx;
            }
            else
            {
                // 无加速度：速度瞬间到位
                currentHorizontalSpeed = r.h * moveSpeed;
                horizontalVelocity = currentHorizontalSpeed + windVx;
            }

            float vx = horizontalVelocity;
            float vy = rb.velocity.y + windVy;

            // 梯子状态
            if (isOnLadder && r.jump) isClimbing = true;
            // 未在梯子上: 若宽限计时已结束则彻底放弃攀爬, 否则保留(等待回到梯子)
            if (!isOnLadder && ladderGraceTimer <= 0) isClimbing = false;

            // 梯子移动 / 重力
            // 只有确实在梯子上且正在攀爬时才产生爬梯位移(防止凭空上爬);
            // 宽限期内离开梯子(爬到顶短暂脱离)则走重力分支, 让玩家落回梯子顶部.
            if (isClimbing && isOnLadder && !isDashing)
            {
                rb.gravityScale = 0;
                vy = 0;
                if (r.jump) vy = climbSpeed;
                if (r.climbDown) vy = -climbSpeed;
            }
            else if (!isDashing)
            {
                // 在水里时不恢复重力 — WaterPool 已设 gravityScale=0 并用目标速度模型管理沉浮
                if (currentWaterPool == null)
                    rb.gravityScale = 9.8f;
            }

            rb.velocity = new Vector2(vx, vy);

            // 收到跳跃记录 → 记入跳跃缓冲(无论此刻是否在地面)
            // jump = 按下瞬间(短按也能跳), jumpHeld = 长按(持续刷新缓冲 → 落地就跳)
            // 缓冲机制: 即使影子此刻不在地面, 落地后只要在缓冲时间内也会自动起跳
            if (r.jump || r.jumpHeld)
            {
                jumpBufferTimer = jumpBufferDuration;
            }

            // 水里游泳(影子): 收到主角按住跳跃的记录 → 影子也向上游
            // WaterPool.SwimUp 内部检查角色顶部是否出水, 出水不施加(防止游出水面)
            if (currentWaterPool != null && r.jumpHeld)
            {
                currentWaterPool.SwimUp(rb);
            }

            // 变大/变小: 收到主角按 E 的记录 → 延迟切换影子大小状态
            // UpdateScale() 会平滑过渡到目标缩放
            if (r.sizeToggle)
            {
                isBig = !isBig;
                targetScale = isBig ? bigScale : 1f;
            }

            // 单向平台下穿: climbDown 从 false 变 true 时触发(模拟主角按下 S 的瞬间)
            // 必须在地面 + 没在爬梯 + 没在冲刺 + 没在已下穿状态
            if (r.climbDown && !lastClimbDown && isGrounded && !isClimbing && !isDashing && dropThroughTimer <= 0f)
            {
                // 立即对当前接触的、有 PlatformEffector2D 的碰撞体(即单向平台)忽略碰撞
                bool foundPlatform = false;
                foreach (var c in currentContacts)
                {
                    if (c == null) continue;
                    if (c.GetComponent<PlatformEffector2D>() != null)
                    {
                        Physics2D.IgnoreCollision(myCollider, c, true);
                        if (!ignoredPlatforms.Contains(c)) ignoredPlatforms.Add(c);
                        foundPlatform = true;
                    }
                }
                if (foundPlatform)
                {
                    dropThroughTimer = dropThroughDuration;
                }
            }
            lastClimbDown = r.climbDown;

            // 标记本帧处理过移动 (循环内已把 windVx 叠加到水平速度)
            processedMoveThisFrame = true;
        }

        // 静止时 (本帧没有到期移动记录 → 循环没执行):
        // P2 的 velocity 不被更新, 若还有风力, 单独把风力应用到当前速度,
        // 否则风推不动静止的 P2. 水平风改 x, 垂直风改 y.
        if (!processedMoveThisFrame && (Mathf.Abs(windVx) > 0.001f || Mathf.Abs(windVy) > 0.001f))
        {
            Vector2 cv = rb.velocity;
            rb.velocity = new Vector2(cv.x + windVx, cv.y + windVy);
        }
        processedMoveThisFrame = false;

        // 跳跃缓冲 + 长按连跳: 落地自动起跳 (和主角 MoveFirst 机制一致)
        // jumpLockTimer 冷却防止还没离地又跳(起飞问题)
        if (jumpLockTimer > 0f) jumpLockTimer -= Time.deltaTime;
        if (jumpBufferTimer > 0f)
        {
            jumpBufferTimer -= Time.deltaTime;
            if (isGrounded && !isClimbing && dropThroughTimer <= 0f && jumpLockTimer <= 0f)
            {
                rb.velocity = new Vector2(rb.velocity.x, jumpForce);
                jumpBufferTimer = 0f;   // 跳了就清零缓冲
                jumpLockTimer = jumpLockDuration; // 启动冷却
            }
        }

        float deltaX = transform.position.x - lastPositionX;
        traveledDistance += deltaX;
        lastPositionX = transform.position.x;
    }

    private IEnumerator DashCoroutine(float direction, float duration, float force)
    {
        isDashing = true;
        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0;
        rb.velocity = new Vector2(direction * force, 0);
        yield return new WaitForSeconds(duration);
        rb.gravityScale = originalGravity;
        isDashing = false;
        dashCooldownTimer = dashCooldown;
    }
    #endregion

    #region 动画
    void Animation()
    {
        jumpAnimator.SetBool("p2j", !isGrounded);
    }
    #endregion

    #region R重置
    void CheckReset()
    {
        // 按 R 或 被 Die.cs 设了 isR 标志, 都触发死亡重生动画流程
        // 不再直接传送, 而是交给 DeathRespawnVFX 播放动画, 动画结束后由 DoReset() 做实际传送
        if (Input.GetKeyDown(KeyCode.R) || isR)
        {
            isR = false; // 清掉标志, 防止下一帧重复触发
            DeathRespawnVFX.TriggerDeath();
        }
    }

    /// <summary>
    /// 执行实际的位置重置 (传送 P1/P2 到初始点, 清空状态).
    /// 由 DeathRespawnVFX 在死亡动画播放完毕后调用.
    /// 原来这段逻辑在 CheckReset 里, 现在抽出来供动画流程调用.
    /// </summary>
    public void DoReset()
    {
        p1.transform.position = p1Pos;
        p2.transform.position = p2Pos;
        movementHistory.Clear();
        rb.velocity = Vector2.zero;
        isDashing = false;
        isClimbing = false;
        isOnLadder = false;
        canDash = true;
        dashCooldownTimer = 0f;
        // 重置下穿状态: 恢复所有被忽略的碰撞
        if (ignoredPlatforms.Count > 0 && myCollider != null)
        {
            foreach (var c in ignoredPlatforms)
            {
                if (c != null) Physics2D.IgnoreCollision(myCollider, c, false);
            }
            ignoredPlatforms.Clear();
        }
        dropThroughTimer = 0f;
        lastClimbDown = false;
    }
    #endregion

    #region 检测
    private void OnCollisionStay2D(Collision2D col)
    {
        // 收集当前接触的碰撞体(用于 S 下穿时找到单向平台)
        Collider2D collider = col.collider;
        if (collider != null && !currentContacts.Contains(collider))
        {
            currentContacts.Add(collider);
        }

        if (col.gameObject.CompareTag("Ground") || col.gameObject.CompareTag("Player") || col.gameObject.CompareTag("Player2"))
        {
            foreach (ContactPoint2D c in col.contacts)
            {
                if (Vector2.Angle(c.normal, Vector2.up) < groundAngleThreshold)
                {
                    // 单向平台额外检查: 角色脚底要接近平台顶面才算踩到
                    // 防止从下方跳上来时身体碰到平台中间就误判着地(中途借力再跳)
                    PlatformEffector2D effector = collider.GetComponent<PlatformEffector2D>();
                    if (effector != null && myCollider != null)
                    {
                        float feetY = myCollider.bounds.min.y;       // 角色脚底
                        float platformTopY = collider.bounds.max.y;   // 平台顶面
                        if (feetY < platformTopY - oneWayPlatformSurfaceTolerance)
                        {
                            continue; // 脚还在平台顶面下方 → 不算着地
                        }
                    }
                    isGrounded = true;
                    canDash = true;
                }
            }

            // P1 从上方踩住 P2：接触点在 P2 的上半身
            if (preventWhenSteppedOn && col.gameObject.CompareTag("Player"))
            {
                foreach (ContactPoint2D c in col.contacts)
                {
                    if (c.point.y > transform.position.y)
                    {
                        isSteppedOn = true;
                        break;
                    }
                }
            }
        }
    }

    private void OnCollisionExit2D(Collision2D col)
    {
        // 从接触列表移除
        if (col.collider != null)
        {
            currentContacts.Remove(col.collider);
        }
        // 不在这里设 isGrounded = false，交给 FixedUpdate 重置
        // 否则离开与另一个角色的接触时（即使还站在地上）会把 isGrounded 错误设为 false
    }

    private void OnTriggerStay2D(Collider2D col)
    {
        if (col.CompareTag("Ladder"))
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

    private void OnTriggerExit2D(Collider2D col)
    {
        if (col.CompareTag("Ladder"))
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

    #region 行为显示UI
    public enum ActionType
    {
        None,
        MoveLeft,
        MoveRight,
        Jump,
        DashLeft,
        DashRight,
        ClimbUp,
        ClimbDown
    }

    private ActionType currentReplayingAction = ActionType.None;
    private float currentActionClearTimer = 0f;

    public ActionType GetCurrentAction() => currentReplayingAction;

    public List<ActionType> GetPendingActions()
    {
        List<ActionType> rawList = new List<ActionType>();
        foreach (var record in movementHistory)
        {
            ActionType act = GetActionTypeFromRecord(record);
            if (act != ActionType.None)
                rawList.Add(act);
        }
        List<ActionType> compressed = new List<ActionType>();
        ActionType last = ActionType.None;
        foreach (var act in rawList)
        {
            if (act != last)
            {
                compressed.Add(act);
                last = act;
            }
        }
        return compressed.Take(6).ToList();
    }

    public ActionType GetActionTypeFromRecord(MovementRecord r)
    {
        if (r.dash)
            return r.dashDir > 0 ? ActionType.DashRight : ActionType.DashLeft;
        if (r.jump)
            return ActionType.Jump;
        if (r.climbDown)                     // 新增
            return ActionType.ClimbDown;
        if (r.h < 0)
            return ActionType.MoveLeft;
        if (r.h > 0)
            return ActionType.MoveRight;
        return ActionType.None;
    }

    public void SetCurrentActionFromRecord(MovementRecord r)
    {
        currentReplayingAction = GetActionTypeFromRecord(r);
        if (currentReplayingAction == ActionType.Jump ||
            currentReplayingAction == ActionType.DashLeft ||
            currentReplayingAction == ActionType.DashRight)
        {
            currentActionClearTimer = 0.2f;
        }
        else
        {
            currentActionClearTimer = -1f;
        }
    }

    public void UpdateCurrentActionClear(float deltaTime)
    {
        if (currentActionClearTimer > 0)
        {
            currentActionClearTimer -= deltaTime;
            if (currentActionClearTimer <= 0)
                currentReplayingAction = ActionType.None;
        }
    }
    #endregion
}
