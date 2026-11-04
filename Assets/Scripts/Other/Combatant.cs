using UnityEngine;

public class Combatant : MonoBehaviour
{
    [Header("连击设置")]
    public float attackWindow = 0.4f;       // 每段攻击的有效输入窗口（秒）
    private float nextAttackTime;           // 下次允许输入下一段的时间

    [Header("受击飞行设置")]
    public float flySpeed = 12f;            // 被击飞后的匀速移动速度
    public float maxFlyTime = 2f;           // 最长飞行时间（防止卡死）
    private bool isFlying = false;          // 是否正在飞行中
    private float flyTimer = 0f;
    private Vector2 flyDirection;

    [Header("攻击段数")]
    [SerializeField] private int currentAttackStage = 0;   // 0=无,1=前,2=上,3=下
    public int CurrentAttackStage => currentAttackStage;

    private Rigidbody2D rb;
    public Animator animator;
    private Weapon weapon;

    // 是否允许输入（受击飞行中禁止输入）
    public bool CanControl => !isFlying;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        weapon = GetComponentInChildren<Weapon>();
    }

    private void Update()
    {
        // 受击飞行中不处理攻击输入
        if (!CanControl) return;

        // 检测攻击输入（鼠标左键）
        if (Input.GetMouseButtonDown(0))
        {
            TryAttack();
        }
    }

    private void TryAttack()
    {
        // 连击逻辑：在窗口内可以进入下一段，否则重置为第一段
        if (Time.time >= nextAttackTime)
        {
            // 窗口已过期，重置为第一段
            currentAttackStage = 1;
        }
        else
        {
            // 窗口内，且不是第三段，则进入下一段
            if (currentAttackStage < 3)
                currentAttackStage++;
        }

        // 播放对应动画
        PlayAttackAnimation();

        // 更新武器段数（不依赖动画事件也能工作）
        if (weapon != null)
        {
            weapon.SetStage(currentAttackStage);
            // 默认启用碰撞（如果不用动画事件控制，这里临时开启）
            weapon.EnableHit();
            // 延迟关闭（简单做法，实际最好用动画事件）
            Invoke(nameof(DisableWeaponHit), 0.2f);
        }

        // 设置下次允许输入的时间（窗口长度）
        nextAttackTime = Time.time + attackWindow;
    }

    private void DisableWeaponHit()
    {
        if (weapon != null) weapon.DisableHit();
    }

    private void PlayAttackAnimation()
    {
        if (animator == null) return;
        // 根据当前段数触发不同的动画触发器
        switch (currentAttackStage)
        {
            case 1: animator.SetTrigger("Attack1"); break;
            case 2: animator.SetTrigger("Attack2"); break;
            case 3: animator.SetTrigger("Attack3"); break;
        }
    }

    // 供 MoveLate 调用：重放攻击段数（影子专用）
    public void SetAttackStageFromReplay(int stage)
    {
        if (stage == 0) return;
        currentAttackStage = stage;
        PlayAttackAnimation();
        if (weapon != null)
        {
            weapon.SetStage(stage);
            weapon.EnableHit();
            Invoke(nameof(DisableWeaponHit), 0.2f);
        }
        // 重放时也设置窗口，避免后续连击错误
        nextAttackTime = Time.time + attackWindow;
    }

    // 受击方法（由武器调用）
    public void OnHitByWeapon(int hitStage, Vector2 hitDirection, GameObject attacker)
    {
        if (isFlying) return; // 飞行中不可再次受击（可改为允许，根据需求）
        
        // 根据段数决定飞行方向（覆盖传入的方向，段数决定方向）
        Vector2 finalDir = hitDirection;
        if (hitStage == 2)          // 向上
            finalDir = Vector2.up;
        else if (hitStage == 3)     // 向下
            finalDir = Vector2.down;

        // 进入飞行状态
        isFlying = true;
        flyTimer = maxFlyTime;
        flyDirection = finalDir.normalized;

        // 禁用输入控制（通过 CanControl 控制）
        // 停止原有速度
        rb.velocity = Vector2.zero;
        rb.gravityScale = 0;        // 飞行时不受重力，匀速直线

        Debug.Log($"{name} 被击中！段数={hitStage} 方向={finalDir}");
    }

    private void FixedUpdate()
    {
        if (isFlying)
        {
            // 匀速移动
            rb.velocity = flyDirection * flySpeed;
            flyTimer -= Time.fixedDeltaTime;
            if (flyTimer <= 0f)
            {
                StopFlying();
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isFlying)
        {
            // 碰到任何非触发器碰撞体就停止飞行（后续可替换为墙壁逻辑）
            StopFlying();
        }
    }

    private void StopFlying()
    {
        if (!isFlying) return;
        isFlying = false;
        rb.velocity = Vector2.zero;
        rb.gravityScale = 9.8f;      // 恢复重力
        flyTimer = 0f;
        Debug.Log($"{name} 停止飞行");
    }

    // 供外部重置受击状态（例如重置关卡时）
    public void ResetCombat()
    {
        StopFlying();
        currentAttackStage = 0;
        nextAttackTime = 0;
        if (weapon != null) weapon.DisableHit();
    }
}