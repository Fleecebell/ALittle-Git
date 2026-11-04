using UnityEngine;

// ======================================================================
// DeadGrassSway —— 戈壁枯草摆动 (沙尘暴联动 + 主角互动)
// --------------------------------------------------------------------------
// 设计思路 (复用 PlantSway 的双段骨架 + spring 逻辑, 改成联动沙尘暴):
//   1. 双段弯曲骨架: Root(挂脚本) → 下半段 sprite + 上关节(空物体) → 上半段 sprite
//      脚本同时控制 Root 和 UpperJoint 的旋转, 上关节角度 = 根角度 × upperBendScale
//      (越往尖端弯得越多), 形成自然弧线, 不是直棍转.
//   2. Spring 缓冲: 刚度(stiffness)+阻尼(damping)公式. 暴风停/主角走开后,
//      枯草会前后回弹震荡 2-3 次再停, 像真实草被吹过.
//   3. 两种摆动来源 (同时生效, 叠加):
//      a) 沙尘暴联动: 每帧查 SandstormController.Instance.CurrentIntensity (0~1),
//         暴风强度越大, 枯草朝 windDirection 方向倾倒越多 (持续倾向, 不是抖动).
//         没沙尘暴时强度=0, 枯草回正.
//      b) 主角互动: 主角进入 Trigger → 根据主角相对位置额外倾斜 (主角在左→草往右倒).
//         主角走开 → spring 自动回弹.
//   4. 复用: 戈壁多关卡换枯草 sprite + Color 即可. 枯草/小花/芦苇等都用同一脚本.
//   5. 受光: 配合 Sprite-Lit 材质, 响应 Light2D.
//   6. 平台跟随: 枯草作为移动平台子物体, 平台移动时自动跟随 (靠 Unity 父子关系).
//
// 骨架结构 (Unity 里搭, 和 PlantSway 完全一样):
//   DeadGrass_Root (空物体, 挂本脚本, Rigidbody2D Kinematic, BoxCollider2D Trigger)
//   ├── LowerSprite (下半段 sprite, SpriteRenderer, Pivot Bottom Center)
//   └── UpperJoint (空物体, 位置 0,半高,0)
//       └── UpperSprite (上半段 sprite, SpriteRenderer, 位置 0,半高,0 衔接)
// ======================================================================
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class DeadGrassSway : MonoBehaviour
{
    [Header("=== 骨架引用 ===")]
    [Tooltip("上半段关节 (空物体). 脚本同时旋转 Root 和这个关节, 实现双段弯曲. 如果不填则只转 Root (单段模式, 适合矮草)")]
    [SerializeField] private Transform upperJoint;

    [Header("=== 摆动幅度 ===")]
    [Tooltip("主角在旁边时, 根段最大倾斜角度(度). 15=轻微, 30=明显")]
    [SerializeField] private float maxTiltAngle = 25f;

    [Tooltip("暴风时根段最大倾倒角度(度). 沙尘暴强度=1 时枯草朝风向倒这么多. 40=被吹弯, 60=几乎贴地")]
    [SerializeField] private float stormTiltAngle = 45f;

    [Tooltip("上关节弯曲倍率. 1.5 = 上半段比根段多弯 50%, 形成弧线. 1=直棍, 2=上半段弯两倍")]
    [SerializeField] private float upperBendScale = 1.5f;

    [Tooltip("风向 (只看正负). 1=风往右吹(枯草往右倒), -1=风往左吹(枯草往左倒). 跟你 WindArea 的风向保持一致")]
    [SerializeField] private float windDirection = 1f;

    [Header("=== 暴风来回摆动 ===")]
    [Tooltip("暴风时来回摆动速度(角频率). 越大摆得越快. 推荐值 3~8")]
    [SerializeField] private float swingSpeed = 5f;

    [Tooltip("暴风时来回摆动幅度(度). 在主倾倒方向上叠加的左右摇晃. 10=轻微摇, 25=明显摇晃")]
    [SerializeField] private float swingAmplitude = 15f;

    [Header("=== Spring 缓冲 (回弹震荡) ===")]
    [Tooltip("刚度. 越大回正越快, 太大会震荡不止. 推荐值 30~80")]
    [SerializeField] private float stiffness = 50f;

    [Tooltip("阻尼. 越大震荡衰减越快, 0=永远震荡, 1=不震荡. 推荐值 3~8")]
    [SerializeField] private float damping = 5f;

    [Header("=== 触发范围 ===")]
    [Tooltip("主角进入此距离内开始摆动. 通常和 Trigger Collider 大小一致")]
    [SerializeField] private float triggerRadius = 1.5f;

    [Header("=== 调试 ===")]
    [Tooltip("运行时显示当前角度和状态, 方便调参")]
    [SerializeField] private bool showDebug = false;

    // ===================== 内部状态 =====================
    private float rootAngle = 0f;       // 根段当前角度
    private float rootVelocity = 0f;    // 根段角速度 (spring 用)
    private float upperAngle = 0f;      // 上关节当前角度
    private float upperVelocity = 0f;   // 上关节角速度

    private Transform playerNear;       // 当前在范围内的主角 (null=没人)

    void Update()
    {
        // ---- 1. 查询沙尘暴强度 (0=无暴风, 1=强暴风) ----
        // 没挂 SandstormController 的场景 (如旧场景) 默认强度=0, 枯草静止
        float stormIntensity = 0f;
        if (SandstormController.Instance != null)
            stormIntensity = SandstormController.Instance.CurrentIntensity;

        // ---- 2. 计算目标角度 ----
        // 暴风时: 主倾向(朝风向倒) + sin 波来回摆动(被风吹得摇晃, 不是死板地倒向一边)
        float baseLean = windDirection * stormTiltAngle * stormIntensity;
        float swing = Mathf.Sin(Time.time * swingSpeed) * swingAmplitude * stormIntensity;
        float targetRoot = baseLean + swing;

        // 叠加: 主角互动 (主角在旁边时额外倾斜, 走开自动恢复)
        if (playerNear != null)
        {
            // 主角在左 (offset.x<0) → 草往右倒(正); 主角在右 → 草往左倒(负)
            // 距离越近倾斜越大, 距离=triggerRadius 时不倾斜
            Vector2 offset = playerNear.position - transform.position;
            float distanceRatio = Mathf.Clamp01(1f - Mathf.Abs(offset.x) / triggerRadius);
            targetRoot += -Mathf.Sign(offset.x) * maxTiltAngle * distanceRatio;
        }

        float targetUpper = targetRoot * upperBendScale;

        // ---- 3. Spring 公式: 弹力 + 阻尼 ----
        // F = -k*x - c*v  (k=刚度, c=阻尼, x=位移, v=速度)
        rootVelocity += (targetRoot - rootAngle) * stiffness * Time.deltaTime;
        rootVelocity *= Mathf.Clamp01(1f - damping * Time.deltaTime);
        rootAngle += rootVelocity * Time.deltaTime;

        upperVelocity += (targetUpper - upperAngle) * stiffness * Time.deltaTime;
        upperVelocity *= Mathf.Clamp01(1f - damping * Time.deltaTime);
        upperAngle += upperVelocity * Time.deltaTime;

        // ---- 4. 应用旋转 ----
        transform.rotation = Quaternion.Euler(0, 0, rootAngle);
        if (upperJoint != null)
            upperJoint.rotation = Quaternion.Euler(0, 0, upperAngle);

        if (showDebug)
        {
            Debug.Log($"[DeadGrassSway] root={rootAngle:F1}° upper={upperAngle:F1}° " +
                      $"storm={stormIntensity:F2} {(playerNear != null ? "互动" : "")}");
        }
    }

    // ===================== Trigger 检测主角 =====================
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") || other.CompareTag("Player2"))
        {
            playerNear = other.transform;
            if (showDebug) Debug.Log($"[DeadGrassSway] {other.tag} 进入");
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if ((other.CompareTag("Player") || other.CompareTag("Player2")) && playerNear == other.transform)
        {
            playerNear = null;
            if (showDebug) Debug.Log($"[DeadGrassSway] {other.tag} 离开");
        }
    }
}
