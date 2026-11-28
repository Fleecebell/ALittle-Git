using UnityEngine;

public class Weapon : MonoBehaviour
{
    [Header("攻击设置")]
    public int currentStage = 0;           // 0=无,1=向前,2=向上,3=向下
    public LayerMask targetLayers;         // 可击中的层级（例如 Player2, Enemy）
    public Collider2D hitbox;              // 武器的碰撞体（必须为 Trigger）

    [Header("振刀判定")]
    public bool requireFacing = true;      // 是否需要面对面才触发振刀

    private Combatant owner;               // 持有此武器的角色

    private void Start()
    {
        owner = GetComponentInParent<Combatant>();
        if (hitbox == null) hitbox = GetComponent<Collider2D>();
        if (hitbox != null) hitbox.isTrigger = true;
    }

    // 由动画事件调用：设置当前攻击段数（1/2/3）
    public void SetStage(int stage)
    {
        currentStage = stage;
    }

    // 由动画事件调用：开启碰撞检测
    public void EnableHit()
    {
        if (hitbox != null) hitbox.enabled = true;
    }

    // 由动画事件调用：关闭碰撞检测
    public void DisableHit()
    {
        if (hitbox != null) hitbox.enabled = false;
        currentStage = 0;   // 攻击结束，重置段数
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!enabled || hitbox == null || !hitbox.enabled) return;
        if (currentStage == 0) return;

        // 只攻击目标层上的对象
        if (((1 << other.gameObject.layer) & targetLayers) == 0) return;

        Combatant target = other.GetComponent<Combatant>();
        if (target == null || target == owner) return;  // 不攻击自己

        // 获取目标的武器
        Weapon targetWeapon = target.GetComponentInChildren<Weapon>();

        // 判断是否振刀
        bool isParry = false;
        if (targetWeapon != null && targetWeapon.currentStage > 0)
        {
            if (requireFacing)
            {
                // 面对面判定：根据角色 localScale.x 或 moveDir
                float ownerFacing = owner.transform.localScale.x;
                float targetFacing = target.transform.localScale.x;
                if (ownerFacing * targetFacing < 0) // 一正一负 => 面对面
                    isParry = true;
            }
            else
            {
                isParry = true;
            }
        }

        if (isParry)
        {
            // 振刀：双方互相受到对方攻击段数的影响
            Vector2 dirToTarget = (target.transform.position - owner.transform.position).normalized;
            Vector2 dirToOwner = -dirToTarget;

            owner.OnHitByWeapon(targetWeapon.currentStage, dirToOwner, target.gameObject);
            target.OnHitByWeapon(currentStage, dirToTarget, owner.gameObject);
        }
        else
        {
            // 单方面攻击
            Vector2 attackDir = owner.transform.localScale.x > 0 ? Vector2.right : Vector2.left;
            target.OnHitByWeapon(currentStage, attackDir, owner.gameObject);
        }
    }
}