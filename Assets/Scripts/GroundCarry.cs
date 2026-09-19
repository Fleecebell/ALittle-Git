using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GroundCarry —— 让角色被"踩着的移动地面"带走(站在移动平台上会跟着平台一起动)。
///
/// 挂载: P1 / P2 (挂有 MoveFirst / MoveLate 的物体, 物体上要有 Collider2D)。
///       由 MoveFirst / MoveLate 在 Start 里调用 EnsureOn() 自动挂载。
///
/// 原理(为什么用"脚底探测"而不是"碰撞回调"):
///   工程里的移动平台都是靠 transform.position 瞬移的(它们自己的 Rigidbody2D 是冻结位置的),
///   平台速度快时物理是"离散"的, 平台会直接穿过角色, 角色被吞进平台内部 ——
///   这时碰撞回调里的接触法线已经不再"朝上", 靠接触法线判定会直接失效(表现就是"完全没作用")。
///   所以这里改成每帧用一个贴在脚底的探测盒去找 Ground 碰撞体:
///     · 没被吞的时候和踩在平台上等价;
///     · 被吞进平台内部时照样能找到, 于是能继续把角色跟着平台一起往上带, 不会卡死;
///   然后算出这些地面本帧的真实位移, 取最大的那个(也就是真正在动的平台)叠加到角色位置上。
///
/// 为什么用"位移叠加"而不是"速度叠加":
///   MoveFirst / MoveLate 每帧都在 Update 里重写 rb.velocity(还夹着重力/跳跃/风力/加速度/爬梯/下穿),
///   直接改速度既会被它们覆盖, 又要区分"叠加"和"赋值"以免速度累积; 位置叠加不碰这套逻辑。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class GroundCarry : MonoBehaviour
{
    [Header("=== 地面识别 ===")]
    [Tooltip("只跟随该 tag 的地面物体移动")]
    public string groundTag = "Ground";

    [Tooltip("脚底探测盒高度(世界单位). 越大越容易提前跟住, 越小越严格")]
    public float feetProbeHeight = 0.2f;

    [Tooltip("脚底探测盒宽度占角色宽度的比例")]
    [Range(0.1f, 1f)] public float feetProbeWidthRatio = 0.9f;

    [Header("=== 跟随设置 ===")]
    [Tooltip("跟随强度. 0=不跟随, 1=完全跟随平台位移")]
    [Range(0f, 1f)] public float carryFactor = 1f;

    [Tooltip("单帧位移小于该值视为平台没动(避免浮点误差导致角色抖动)")]
    public float minDelta = 0.0001f;

    [Tooltip("平台速度上限(单位/秒). 超过视为平台瞬移(死亡复原/场景切换), 只更新基准不跟随, 防止角色被甩飞")]
    public float maxCarrySpeed = 30f;

    [Header("=== 调试 ===")]
    public bool showDebug = false;

    private Rigidbody2D rb;
    private Collider2D myCollider;

    // 每个地面物体上一帧的位置(用于算本帧位移)
    private readonly Dictionary<Transform, Vector3> lastPositions = new Dictionary<Transform, Vector3>();
    private readonly List<Transform> seenThisFrame = new List<Transform>();
    private readonly List<Transform> staleKeys = new List<Transform>();

    // ===================== 自动挂载 =====================
    /// <summary>
    /// 保证该物体上有 GroundCarry(没有就自动加一个)。
    /// 由 MoveFirst / MoveLate 在 Start 里调用: 无论角色是场景里摆好的、
    /// 后期被 SetActive(true) 激活的、还是运行时 Instantiate 出来的, 都能自动获得本功能。
    /// 已经手动挂过组件的话会跳过。
    /// </summary>
    public static void EnsureOn(GameObject target)
    {
        if (target == null) return;
        if (target.GetComponent<GroundCarry>() == null)
            target.AddComponent<GroundCarry>();
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        myCollider = GetComponent<Collider2D>();
    }

    // ---- 在所有平台移动的 Update / FixedUpdate 之后结算位移 ----
    void LateUpdate()
    {
        // 死亡/重生期间角色在缩放 + 传送, 清空记录并跳过, 防止吃到异常位移
        if (DeathRespawnVFX.isDead)
        {
            lastPositions.Clear();
            return;
        }

        if (myCollider == null) return;

        // 脚底探测盒: 中心在脚底, 上下各覆盖 feetProbeHeight/2
        Bounds b = myCollider.bounds;
        Vector2 probeCenter = new Vector2(b.center.x, b.min.y);
        Vector2 probeSize = new Vector2(b.size.x * feetProbeWidthRatio, feetProbeHeight);
        Collider2D[] hits = Physics2D.OverlapBoxAll(probeCenter, probeSize, 0f);

        // 找本帧位移最大的地面: 静止地面位移为 0, 只会取到真正在动的平台
        seenThisFrame.Clear();
        Vector3 bestDelta = Vector3.zero;
        string bestName = null;

        foreach (Collider2D hit in hits)
        {
            if (hit == null || hit == myCollider) continue;
            if (!string.IsNullOrEmpty(groundTag) && !hit.CompareTag(groundTag)) continue;

            Transform ground = hit.transform;
            if (seenThisFrame.Contains(ground)) continue;
            seenThisFrame.Add(ground);

            Vector3 current = ground.position;
            Vector3 delta;
            if (lastPositions.TryGetValue(ground, out Vector3 last))
                delta = current - last;
            else
                delta = Vector3.zero;      // 本帧刚踩上: 以当前为基准, 不吃到踩上之前的位移
            lastPositions[ground] = current;

            if (delta.sqrMagnitude > bestDelta.sqrMagnitude)
            {
                bestDelta = delta;
                bestName = ground.name;
            }
        }

        // 清理已经不在脚下的记录(下次踩上去重新记基准)
        if (seenThisFrame.Count == 0)
        {
            lastPositions.Clear();
        }
        else if (lastPositions.Count > seenThisFrame.Count)
        {
            staleKeys.Clear();
            foreach (var kv in lastPositions)
            {
                if (!seenThisFrame.Contains(kv.Key)) staleKeys.Add(kv.Key);
            }
            foreach (Transform key in staleKeys) lastPositions.Remove(key);
        }

        if (bestDelta.sqrMagnitude < minDelta * minDelta) return;    // 平台没动

        // 速度守卫: 单帧位移超过上限 → 视为平台瞬移, 只更新基准, 不跟随
        float maxDelta = Mathf.Max(0.001f, maxCarrySpeed * Time.deltaTime);
        if (bestDelta.magnitude > maxDelta) return;

        Vector3 offset = bestDelta * carryFactor;

        // 关键: 必须写 Transform(不能只写 rb.position)。
        // 本工程 Physics2D.autoSyncTransforms = false(默认关闭), 而 MoveFirst/MoveLate 每帧都会写
        // transform.localScale(变大/变小), 这会把 Transform 标记成"脏"的 ——
        // 物理步开始时 Unity 会用 Transform 的位置同步覆盖 Rigidbody2D 的位置,
        // 只写 rb.position 的话, 跟随位移在下一步就被旧 Transform 位置冲掉了(表现: 完全没跟随)。
        transform.position += offset;
        if (rb != null && rb.bodyType != RigidbodyType2D.Static)
            rb.position = transform.position;   // 双写, 保证物理体与显示一致

        if (showDebug)
            Debug.Log($"[GroundCarry] {name} 站在 [{bestName}] 上, 本帧跟随位移 {offset}", this);
    }
}
