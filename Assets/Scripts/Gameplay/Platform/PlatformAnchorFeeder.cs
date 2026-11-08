using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把"平台当前的世界位置"喂给按位置采样贴图的 shader：
///   PlatformDapple.shader  → _DappleAnchorPos   （斑驳层）
///   ContolPlatform.shader  → _PlatformAnchorPos （木纹/石纹平台）
///
/// 为什么需要：
///   TilemapRenderer 送进 shader 的顶点已经是世界坐标，而对象矩阵的平移列又是 0，
///   也就是 shader 内部拿不到"平台自己在哪"。移动平台上，按位置采样的贴图就会被
///   世界坐标锁死，平台一动纹路从表面扫过去（看起来像在"拉噪声"）。
///   由移动脚本每帧调用 Feed(被移动的物体)，贴图就钉在平台表面上了。
///
/// 用法（移动脚本里）：
///     private readonly PlatformAnchorFeeder _anchorFeeder = new PlatformAnchorFeeder();
///     ...
///     transform.position = 新位置;
///     _anchorFeeder.Feed(transform);          // 被移动的那个物体
///
/// 说明：
///   - 只处理平移。若平台会自转，需要额外传平台轴向。
///   - 不修改材质资源：通过 MaterialPropertyBlock 按 Renderer 单独写，
///     所以同一个材质被多个平台共用、各自锚各自的。
///   - 只在被锚物体的位置变化时才真正写属性，平台不动时开销接近 0。
/// </summary>
public sealed class PlatformAnchorFeeder
{
    private static readonly int AnchorIdDapple = Shader.PropertyToID("_DappleAnchorPos");
    private static readonly int AnchorIdPattern = Shader.PropertyToID("_PlatformAnchorPos");

    private readonly List<Renderer> _renderers = new List<Renderer>();
    private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
    private readonly List<Vector4> _lastValues = new List<Vector4>();

    private Transform _root;
    private bool _built;
    private Vector2 _phaseOffset;

    /// <summary>
    /// 用"平台初始位置"生成一个稳定的伪随机相位（同一个平台每次运行都一样，
    /// 但不同平台之间互相错开），用来避免同尺寸平台的斑驳/木纹长得一模一样。
    /// 返回值单位是世界单位。
    /// </summary>
    public static Vector2 StablePhase(Vector3 initialWorldPos)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)Mathf.RoundToInt(initialWorldPos.x * 100f)) * 16777619u;
            h = (h ^ (uint)Mathf.RoundToInt(initialWorldPos.y * 100f)) * 16777619u;
            h = (h ^ (uint)Mathf.RoundToInt(initialWorldPos.z * 100f)) * 16777619u;

            float a = (h & 0xFFFFu) / 65535f;
            float b = ((h >> 16) & 0xFFFFu) / 65535f;
            // 0..256 世界单位：对 _DappleScale(约 0.3) 来说足够跳到完全不同的噪声区域
            return new Vector2(a * 256f, b * 256f);
        }
    }

    public void Feed(Transform root)
    {
        if (root == null) return;

        if (!_built || _root != root) Build(root);
        if (_renderers.Count == 0) return;

        Vector3 p = root.position;
        // 减掉固定相位：shader 里 (顶点 - 锚点) = 平台局部坐标 + 相位，每块平台的花纹就错开了
        var value = new Vector4(p.x - _phaseOffset.x, p.y - _phaseOffset.y, 0f, 0f);

        for (int i = 0; i < _renderers.Count; i++)
        {
            Renderer r = _renderers[i];
            if (r == null)
            {
                // 有对象被销毁 / 层级重建 → 下一帧重新收集
                _built = false;
                return;
            }

            if (_lastValues[i] == value) continue;

            MaterialPropertyBlock mpb = _blocks[i];
            r.GetPropertyBlock(mpb);
            mpb.SetVector(AnchorIdDapple, value);
            mpb.SetVector(AnchorIdPattern, value);
            r.SetPropertyBlock(mpb);

            _lastValues[i] = value;
        }
    }

    private void Build(Transform root)
    {
        _root = root;
        _built = true;
        // 相位只在第一次收集时按初始位置算一次，之后平台怎么移动它都不变（否则花纹会滑）
        _phaseOffset = StablePhase(root.position);
        _renderers.Clear();
        _blocks.Clear();
        _lastValues.Clear();

        Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null) continue;

            Material[] mats = r.sharedMaterials;
            bool usesAnchor = false;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null) continue;
                if (mat.HasProperty(AnchorIdDapple) || mat.HasProperty(AnchorIdPattern))
                {
                    usesAnchor = true;
                    break;
                }
            }
            if (!usesAnchor) continue;

            _renderers.Add(r);
            _blocks.Add(new MaterialPropertyBlock());
            // NaN 和任何值都不相等，保证第一帧一定写一次
            _lastValues.Add(new Vector4(float.NaN, float.NaN, 0f, 0f));
        }
    }
}
