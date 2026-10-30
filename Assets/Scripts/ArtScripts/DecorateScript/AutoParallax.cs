using UnityEngine;

// ======================================================================
// AutoParallax —— 视差跟随: 跟随镜头移动产生远近感
// --------------------------------------------------------------------------
// 教学点: 视差 = 远的物体移动慢, 近的物体移动快.
//         factor=1 物体不动(完全跟随镜头), factor=0 物体跟随镜头(像前景)
//         通常背景设 factor 0.8~0.95, 越远的层越接近 1
//
//         本脚本是简化版, 复杂场景(平滑/方向优先)用已有的 ParallaxLayer.cs
//
// 用途: 多层背景远近层次 / 远山 / 远景星空 / 云层
// 注意: 镜头必须移动才能看出效果. 镜头静止时视差无效
//       ParallaxLayer.cs 更完善(支持方向优先+平滑), 本脚本作为工具箱轻量替代
// ======================================================================
public class AutoParallax : MonoBehaviour
{
    [Tooltip("镜头引用. 不填=自动用 Main Camera")]
    public Transform camera;

    [Tooltip("视差系数(0=完全跟随镜头, 1=不动. 0.8=慢速跟随, 像远景)")]
    [Range(0f, 1f)] public float factor = 0.5f;

    [Tooltip("是否在 X 轴上做视差")]
    public bool xAxis = true;

    [Tooltip("是否在 Y 轴上做视差. 通常只 X, Y 关掉")]
    public bool yAxis = false;

    private Vector3 basePos;
    private Vector3 camStart;

    void Start()
    {
        if (camera == null && Camera.main != null)
            camera = Camera.main.transform;

        basePos = transform.localPosition;
        if (camera != null)
            camStart = camera.position;
    }

    void Update()
    {
        if (camera == null) return;

        // 镜头从开始到现在移动了多少
        Vector3 camDelta = camera.position - camStart;

        // (1 - factor) = 反向移动的比例. factor=1 不动, factor=0 完全跟随
        Vector3 p = basePos;
        if (xAxis) p.x -= camDelta.x * (1f - factor);
        if (yAxis) p.y -= camDelta.y * (1f - factor);
        transform.localPosition = p;
    }
}
