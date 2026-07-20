using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

public class MoveLate : MonoBehaviour
{
    #region 变量
    public Animator jumpAnimator;
    public MoveFirst player;

    public static float moveSpeed = 10f;
    public float minMoveSpeed = 0f;

    public float maxMoveSpeed = 50f;
    public static float jumpForce = 20f;
    public static float moveDir;
    public static bool isMoving;
    
    public float delayTime = 0.5f;

    public static float lastPositionX;
    public static float traveledDistance;

    [Header("冲刺")]
    public float dashCooldown = 1f;

    [Header("攀爬")]
    public float climbSpeed = 5f;

    private Rigidbody2D rb;
    public bool isGrounded;
    public float groundAngleThreshold = 45f;

    public GameObject p1, p2;
    private Vector3 p1Pos, p2Pos;

    public static bool isR = false;

    private bool canDash = true;
    private float dashCooldownTimer;
    private bool isDashing;

    private bool isOnLadder;
    private bool isClimbing;
    #endregion

    #region 队列
    public Queue<MovementRecord> movementHistory = new Queue<MovementRecord>();
    public Queue<KeyRecord> inputHistory = new Queue<KeyRecord>();

    public struct MovementRecord
    {
        public float h;
        public bool jump;
        public bool dash;
        public float dashDir;
        public bool climbDown;   // 新增：向下爬梯
        public float time;
    }

    public struct KeyRecord
    {
        public bool space;
        public bool s;
        public float time;
    }
    #endregion

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        p1Pos = p1.transform.position;
        p2Pos = p2.transform.position;
    }

    void Update()
    {
        if (player == null) return;

        RecordAllInput();

        if (!canDash)
        {
            dashCooldownTimer -= Time.deltaTime;
            if (dashCooldownTimer <= 0) canDash = true;
        }

        MoveDelay();
        LadderDelay();
        Animation();
        CheckReset();

        UpdateCurrentActionClear(Time.deltaTime);
    }

    #region 记录输入
    void RecordAllInput()
    {
        float h = Input.GetAxis("Horizontal");
        bool jump = Input.GetKey(KeyCode.Space);
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
            dash = dash,
            dashDir = dashDir,
            climbDown = Input.GetKey(KeyCode.S),
            time = Time.time
        });

        inputHistory.Enqueue(new KeyRecord
        {
            space = Input.GetKey(KeyCode.Space),
            s = Input.GetKey(KeyCode.S),
            time = Time.time
        });
    }
    #endregion

    #region 延迟行为
    void MoveDelay()
    {
        while (movementHistory.Count > 0 && Time.time - movementHistory.Peek().time >= delayTime)
        {
            var r = movementHistory.Dequeue();

            if (isDashing) continue;

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
            rb.velocity = new Vector2(r.h * moveSpeed, rb.velocity.y);

            if (r.jump && isGrounded && !isClimbing)
            {
                rb.velocity = new Vector2(rb.velocity.x, jumpForce);
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

    void LadderDelay()
    {
        while (inputHistory.Count > 0 && Time.time - inputHistory.Peek().time >= delayTime)
        {
            var r = inputHistory.Dequeue();

            if (isOnLadder && r.space) isClimbing = true;
            if (!isOnLadder) isClimbing = false;

            if (isClimbing && !isDashing)
            {
                rb.gravityScale = 0;
                float v = 0;
                if (r.space) v = climbSpeed;
                if (r.s) v = -climbSpeed;
                rb.velocity = new Vector2(rb.velocity.x, v);
            }
            else if (!isDashing)
            {
                rb.gravityScale = 9.8f;
            }
        }
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
        if (Input.GetKeyDown(KeyCode.R) || isR)
        {
            p1.transform.position = p1Pos;
            p2.transform.position = p2Pos;
            isR = false;
            movementHistory.Clear();
            inputHistory.Clear();
            isDashing = false;
            isClimbing = false;
            isOnLadder = false;
            canDash = true;
            dashCooldownTimer = 0f;
        }
    }
    #endregion

    #region 检测
    private void OnCollisionStay2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground") || col.gameObject.CompareTag("Player") || col.gameObject.CompareTag("Player2"))
        {
            foreach (ContactPoint2D c in col.contacts)
                if (Vector2.Angle(c.normal, Vector2.up) < groundAngleThreshold)
                {
                    isGrounded = true;
                    canDash = true;
                    return;
                }
            isGrounded = false;
        }
    }

    private void OnCollisionExit2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground") || col.gameObject.CompareTag("Player") || col.gameObject.CompareTag("Player2"))
            isGrounded = false;
    }

    private void OnTriggerStay2D(Collider2D col)
    {
        if (col.CompareTag("Ladder")) isOnLadder = true;
    }

    private void OnTriggerExit2D(Collider2D col)
    {
        if (col.CompareTag("Ladder"))
        {
            isOnLadder = false;
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