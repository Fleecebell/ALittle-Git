using UnityEngine;
using UnityEngine.Tilemaps;

// ======================================================================
// TilemapOneWayPlatform —— 把瓦片地图的每个瓦片自动转成独立的单向平台
// --------------------------------------------------------------------------
// 解决的问题:
//   直接给 TilemapCollider2D 加 PlatformEffector2D 会有问题:
//   - 整个 Tilemap 共享一个 Surface Arc, 多个分散平台时方向判断混乱
//   - Surface Arc=180 侧面也顶头, 调小又可能漏判
//
// 本脚本的做法:
//   1. 用户照常用瓦片地图画单向平台的位置(涂瓦片)
//   2. 运行时脚本遍历所有有瓦片的格子, 每个格子创建一个独立子物体
//   3. 每个子物体带 BoxCollider2D + PlatformEffector2D, 独立判断方向
//   4. 禁用原来的 TilemapCollider2D(避免双重碰撞)
//   5. 保留 TilemapRenderer 继续显示瓦片贴图
//
// 优点:
//   - 保持涂瓦片的便利(画关卡快)
//   - 每个平台独立, Surface Arc 中心在瓦片中心, 不会偏移
//   - 侧面不顶头(只顶部单向)
// ======================================================================
[RequireComponent(typeof(Tilemap))]
public class TilemapOneWayPlatform : MonoBehaviour
{
    [Header("=== 单向平台设置 ===")]
    [Tooltip("表面弧度. 120=只顶部±60度可踩, 侧面正常阻挡不顶头. 90=更严格只正上方. 不要用180(侧面会顶)")]
    [SerializeField, Range(60f, 170f)] private float surfaceArc = 120f;

    [Tooltip("保留瓦片渲染. 勾选=继续显示贴图(推荐), 取消=只留碰撞体不显示")]
    [SerializeField] private bool keepRendering = true;

    [Tooltip("侧面摩擦. 勾选=角色站边缘不打滑, 取消=侧面完全光滑")]
    [SerializeField] private bool useSideFriction = true;

    [Tooltip("显示调试. 运行时在 Scene 视图显示每个生成的平台位置(绿色框)")]
    [SerializeField] private bool showDebug = false;

    [Tooltip("平台 Tag. 必须和你的地面 Tag 一致(角色靠 Tag 判断能否跳跃). 默认 Ground")]
    [SerializeField] private string platformTag = "Ground";

    [Tooltip("平台 Layer. 用于按S下穿识别. 需在 Edit→Project Settings→Tags and Layers 创建 OneWayPlatform 层")]
    [SerializeField] private string platformLayer = "OneWayPlatform";

    // 瓦片地图引用
    private Tilemap tilemap;
    // 原始的 TilemapCollider2D, 运行时禁用(用生成的独立碰撞体代替)
    private TilemapCollider2D tilemapCollider;
    // 缓存的 Layer 索引(-1=不存在)
    private int platformLayerIndex = -1;

    void Start()
    {
        tilemap = GetComponent<Tilemap>();
        tilemapCollider = GetComponent<TilemapCollider2D>();

        // 解析 Layer 索引, 不存在则警告(下穿功能不可用, 但跳跃/地面检测正常)
        platformLayerIndex = LayerMask.NameToLayer(platformLayer);
        if (platformLayerIndex < 0)
        {
            Debug.LogWarning($"[TilemapOneWayPlatform] Layer '{platformLayer}' 不存在! 请在 Edit→Project Settings→Tags and Layers 添加, 否则按S下穿功能不可用(跳跃不受影响)");
        }

        // 生成独立平台
        GeneratePlatforms();

        // 禁用原始 TilemapCollider2D, 避免和生成的子物体双重碰撞
        // 不删除组件, 只禁用, 方便随时恢复
        if (tilemapCollider != null)
        {
            tilemapCollider.enabled = false;
        }

        // 如果不需要渲染, 禁用 TilemapRenderer
        if (!keepRendering)
        {
            var renderer = GetComponent<TilemapRenderer>();
            if (renderer != null) renderer.enabled = false;
        }
    }

    // 遍历所有有瓦片的格子, 每个创建独立平台
    void GeneratePlatforms()
    {
        // 获取瓦片地图的范围(所有有瓦片的区域)
        BoundsInt bounds = tilemap.cellBounds;
        int count = 0;

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                // 只处理有瓦片的格子
                if (tilemap.HasTile(cell))
                {
                    CreatePlatformAt(cell);
                    count++;
                }
            }
        }

        if (showDebug)
        {
            Debug.Log($"[TilemapOneWayPlatform] 共生成 {count} 个独立单向平台, Surface Arc={surfaceArc}");
        }
    }

    // 在指定瓦片位置创建一个独立的单向平台子物体
    void CreatePlatformAt(Vector3Int cell)
    {
        // 瓦片中心的世界坐标
        Vector3 worldPos = tilemap.GetCellCenterWorld(cell);
        // 瓦片尺寸(通常是 1x1, 但读取实际值更安全)
        Vector3 tileSize = tilemap.cellSize;

        // 创建子物体, 命名为坐标方便调试
        GameObject platform = new GameObject($"OneWay_{cell.x}_{cell.y}");
        platform.transform.SetParent(transform);
        platform.transform.position = worldPos;

        // 设置 Tag = Ground, 否则角色的 OnCollisionStay2D 不认(角色靠 Tag 判断是否着地)
        // 用 try-catch 防止 Tag 未定义时报错
        try { platform.tag = platformTag; }
        catch (System.Exception)
        {
            Debug.LogWarning($"[TilemapOneWayPlatform] Tag '{platformTag}' 不存在! 请在 Edit → Project Settings → Tags and Layers 添加, 否则角色踩单向平台无法跳跃");
        }

        // 设置 Layer = OneWayPlatform, 用于"按S下穿"识别
        // Layer 存在才设(-1=不存在)
        if (platformLayerIndex >= 0)
        {
            platform.layer = platformLayerIndex;
        }

        // 添加 BoxCollider2D, 完全匹配瓦片尺寸(瓦片自带厚度, 不用参数控制)
        BoxCollider2D col = platform.AddComponent<BoxCollider2D>();
        col.size = new Vector2(tileSize.x, tileSize.y);
        col.offset = Vector2.zero;
        // 关键! 必须勾选, PlatformEffector2D 才能接管这个碰撞体
        col.usedByEffector = true;

        // 添加 PlatformEffector2D, 配置单向
        PlatformEffector2D effector = platform.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;                 // 开启单向
        effector.surfaceArc = surfaceArc;           // 表面弧度(只顶部踩)
        effector.useSideFriction = useSideFriction; // 侧面摩擦
        effector.useSideBounce = false;             // 不要侧面反弹

        // 调试可视化
        if (showDebug)
        {
            DebugDrawPlatform(platform.transform, col.size);
        }
    }

    // 在 Scene 视图绘制绿色框显示平台位置(仅调试用)
    void DebugDrawPlatform(Transform t, Vector2 size)
    {
        // 用 MonoBehaviour 的 OnDrawGizmos 不行(子物体不是 MonoBehaviour)
        // 这里只做日志, 实际调试看 Scene 视图的 Collider 框即可
    }
}
