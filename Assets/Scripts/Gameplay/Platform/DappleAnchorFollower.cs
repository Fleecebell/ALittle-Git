using UnityEngine;

/// <summary>
/// 把平台的世界位置每帧写进斑驳材质的 _DappleAnchorPos。
///
/// 为什么需要它：
///   实测 TilemapRenderer 送进 shader 的顶点 v.vertex 已经是"世界坐标"（平台一动它跟着变），
///   而对象矩阵的平移列是 0 —— 也就是 shader 内部**拿不到平台自己在哪**。
///   结果就是移动平台上斑驳的噪声场会随平台平移而从表面扫过去（看起来像在"拉噪声"）。
///   由这个脚本把平台位置喂进去，shader 用 (顶点 - 平台位置) 就得到平台相对坐标，
///   噪声场就钉在平台表面上了。
///
/// 用法：
///   1. 挂在"移动平台的斑驳层"上（用了 PlatformDapple 材质的那个 Tilemap 物体）。
///   2. 该材质的 Inspector 里把 噪声锚定(_DappleAnchorMode) 设成 5。
///   静态平台可以不挂：它不动，锚点为 0 时 (顶点 - 0) 就是固定值，结果一样稳。
///
/// 说明：
///   - 只处理平移。如果以后有会自转的平台，需要把平台轴向量一起传进来（找我要）。
///   - 脚本不会改动材质资源本身：每个 Renderer 的锚点通过 MaterialPropertyBlock 单独写，
///     所以同一个材质可以被多个平台共用，各自锚各自的。
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class DappleAnchorFollower : MonoBehaviour
{
    [Tooltip("作为锚点的 Transform。留空 = 用本物体的 Transform（斑驳层通常是平台的子级，位置等价）")]
    [SerializeField] private Transform anchorSource;

    [Tooltip("要写入属性的 Renderer。留空 = 自动取本物体上的 Renderer（TilemapRenderer 也算）")]
    [SerializeField] private Renderer targetRenderer;

    private static readonly int AnchorPosId = Shader.PropertyToID("_DappleAnchorPos");
    private static readonly int PlatformAnchorPosId = Shader.PropertyToID("_PlatformAnchorPos");

    private MaterialPropertyBlock _mpb;
    private Vector4 _lastValue;
    private bool _hasValue;

    private void OnEnable()
    {
        // 重新启用 / 进入 Edit 模式时立刻刷一次，保证编辑器里预览也是锚定的
        _hasValue = false;
        Apply();
    }

    // AutoMoving 在 FixedUpdate 里移动平台，LateUpdate 时本帧位置已经确定
    private void LateUpdate()
    {
        Apply();
    }

    private void Apply()
    {
        if (ReferenceEquals(targetRenderer, null))
        {
            targetRenderer = GetComponent<Renderer>();
            if (ReferenceEquals(targetRenderer, null)) return;
        }

        var src = anchorSource != null ? anchorSource : transform;
        var p = src.position;
        var value = new Vector4(p.x, p.y, 0f, 0f);

        // 没动就不重复写：静态平台第一帧之后零开销
        if (_hasValue && value == _lastValue) return;

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(_mpb);
        // 同时写两个名字，供不同 shader 使用（没声明的属性会被忽略，无副作用）：
        //   _DappleAnchorPos    → PlatformDapple.shader  (斑驳层)
        //   _PlatformAnchorPos  → ContolPlatform.shader  (木纹/石纹平台)
        _mpb.SetVector(AnchorPosId, value);
        _mpb.SetVector(PlatformAnchorPosId, value);
        targetRenderer.SetPropertyBlock(_mpb);

        _lastValue = value;
        _hasValue = true;
    }
}
