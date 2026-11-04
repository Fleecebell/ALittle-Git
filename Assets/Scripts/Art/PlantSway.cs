using UnityEngine;

// ======================================================================
// PlantSway —— 植物摆动 (草/花/藤蔓通用)
// --------------------------------------------------------------------------
// 设计思路:
//   1. 双段弯曲骨架: Root(挂脚本) → 下半段 sprite + 上关节(空物体) → 上半段 sprite
//      脚本同时控制 Root 和 UpperJoint 的旋转, 上关节角度 = 根角度 × upperBendScale
//      (越往尖端弯得越多), 形成自然弧线, 不是直棍转.
//   2. Spring 缓冲: 用刚度(stiffness)+阻尼(damping)公式, 不是简单 lerp.
//      主角走开后, 花草会前后回弹震荡 2-3 次再停, 像真实草被碰了一下.
//   3. Interactive 模式: 主角进入 Trigger → 根据主角相对位置算目标倾斜角,
//      主角在左→花草往右倒, 主角在右→往左倒. 主角离开→目标归零, 自然回正.
//   4. Rain 接口: 留 OnRain(intensity) 公开方法, 将来下雨系统调用它让植物摆动.
//      内部用 sin 波 + intensity 控制摆动幅度.
//   5. 复用: 草/花/藤蔓都用同一脚本, Inspector 调参即可. 换关卡换 sprite + Color.
//   6. 受光: 配合 Sprite-Lit 材质, 响应 Light2D.
//   7. 平台跟随: 花草作为移动平台的子物体, 平台移动时自动跟随 (本脚本不处理,
//      靠 Unity 父子关系自动完成).
//
// 骨架结构 (Unity 里搭):
//   Plant_Root (空物体, 挂本脚本, Rigidbody2D Kinematic, BoxCollider2D Trigger)
//   ├── LowerSprite (下半段 sprite, SpriteRenderer, Pivot 底部中心)
//   └── UpperJoint (空物体, 位置 0,半高,0)
//       └── UpperSprite (上半段 sprite, SpriteRenderer, 位置 0,半高,0 衔接)
// ======================================================================
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PlantSway : MonoBehaviour
{
    [Header("=== 骨架引用 ===")]
    [Tooltip("上半段关节 (空物体). 脚本同时旋转 Root 和这个关节, 实现双段弯曲. 如果不填则只转 Root (单段模式)")]
    [SerializeField] private Transform upperJoint;

    [Header("=== 摆动幅度 ===")]
    [Tooltip("主角在旁边时, 根段最大倾斜角度(度). 15=轻微, 30=明显, 45=夸张")]
    [SerializeField] private float maxTiltAngle = 25f;

    [Tooltip("上关节弯曲倍率. 1.5 = 上半段比根段多弯 50%, 形成弧线. 1=上下一样弯(直棍), 2=上半段弯两倍")]
    [SerializeField] private float upperBendScale = 1.5f;

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
    private bool isRaining = false;     // 是否处于下雨摆动模式
    private float rainIntensity = 0f;   // 下雨强度 0~1
    private float rainPhase = 0f;       // 下雨 sin 波相位

    void Update()
    {
        // ---- 计算目标角度 ----
        float targetRoot;

        if (isRaining)
        {
            // 下雨模式: sin 波循环摆动, 幅度随强度变化
            rainPhase += Time.deltaTime * 3f; // 摆动频率
            targetRoot = Mathf.Sin(rainPhase) * maxTiltAngle * 0.4f * rainIntensity;
        }
        else if (playerNear != null)
        {
            // 互动模式: 根据主角相对位置算倾斜角
            // 主角在左 (offset.x < 0) → 花草往右倒 (正角度)
            // 主角在右 (offset.x > 0) → 花草往左倒 (负角度)
            // 距离越近倾斜越大, 距离=triggerRadius 时不倾斜
            Vector2 offset = playerNear.position - transform.position;
            float distanceRatio = Mathf.Clamp01(1f - Mathf.Abs(offset.x) / triggerRadius);
            targetRoot = -Mathf.Sign(offset.x) * maxTiltAngle * distanceRatio;
        }
        else
        {
            // 无人无雨: 目标归零, 自然回正
            targetRoot = 0f;
        }

        float targetUpper = targetRoot * upperBendScale;

        // ---- Spring 公式: 弹力 + 阻尼 ----
        // F = -k*x - c*v  (k=刚度, c=阻尼, x=位移, v=速度)
        rootVelocity += (targetRoot - rootAngle) * stiffness * Time.deltaTime;
        rootVelocity *= Mathf.Clamp01(1f - damping * Time.deltaTime);
        rootAngle += rootVelocity * Time.deltaTime;

        upperVelocity += (targetUpper - upperAngle) * stiffness * Time.deltaTime;
        upperVelocity *= Mathf.Clamp01(1f - damping * Time.deltaTime);
        upperAngle += upperVelocity * Time.deltaTime;

        // ---- 应用旋转 ----
        transform.rotation = Quaternion.Euler(0, 0, rootAngle);
        if (upperJoint != null)
            upperJoint.rotation = Quaternion.Euler(0, 0, upperAngle);

        if (showDebug)
        {
            Debug.Log($"[PlantSway] root={rootAngle:F1}° upper={upperAngle:F1}° target={targetRoot:F1}° " +
                      $"{(isRaining ? "雨" : playerNear != null ? "互动" : "静止")}");
        }
    }

    // ===================== Trigger 检测主角 =====================
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") || other.CompareTag("Player2"))
        {
            playerNear = other.transform;
            if (showDebug) Debug.Log($"[PlantSway] {other.tag} 进入");
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if ((other.CompareTag("Player") || other.CompareTag("Player2")) && playerNear == other.transform)
        {
            playerNear = null;
            if (showDebug) Debug.Log($"[PlantSway] {other.tag} 离开");
        }
    }

    // ===================== 下雨接口 (将来下雨系统调用) =====================
    /// <summary>
    /// 开始下雨. intensity 0~1, 0=毛毛雨轻微摆, 1=暴雨剧烈摆.
    /// 调用此方法后, 植物自动 sin 波摆动, 不需要主角触发.
    /// </summary>
    public void OnRain(float intensity)
    {
        isRaining = true;
        rainIntensity = Mathf.Clamp01(intensity);
    }

    /// <summary>
    /// 停止下雨. 植物平滑回正 (spring 自动完成).
    /// </summary>
    public void OnRainStop()
    {
        isRaining = false;
        rainIntensity = 0f;
    }
}
