using UnityEngine;

// ======================================================================
// AutoDrift —— 缓慢漂移: 水平/垂直缓慢移动, 到边后循环或反弹
// --------------------------------------------------------------------------
// 教学点: 两种"到边处理"模式.
//         Loop: 直接瞬移到另一边(像跑马灯). PingPong: 反向移动(像钟摆)
//         Mathf.PingPong(t, len) 返回 0~len 之间, 每 len 秒反弹一次
//
// 用途: 云飘 / 烟雾流动 / 落叶漂移 / 背景元素缓慢移动
// 注意: 大场景背景视差请用 ParallaxLayer.cs (跟随镜头), 不是本脚本
// ======================================================================
public class AutoDrift : MonoBehaviour
{
    public enum DriftMode { Loop, PingPong }

    [Tooltip("漂移方向(XY), 向量长度会被归一化, 只看方向")]
    public Vector2 direction = new Vector2(1f, 0f);

    [Tooltip("漂移速度(世界单位/秒)")]
    public float speed = 0.5f;

    [Tooltip("漂移范围(到边后折返/循环). 5 = 偏移 ±5")]
    public float range = 5f;

    [Tooltip("到边行为: Loop=瞬移到另一边(连续), PingPong=反弹(往返)")]
    public DriftMode mode = DriftMode.PingPong;

    private Vector3 basePos;
    private float t;

    void Start()
    {
        basePos = transform.localPosition;
    }

    void Update()
    {
        t += speed * Time.deltaTime;

        float offset;
        if (mode == DriftMode.PingPong)
        {
            // PingPong(t, len*2) = 在 0~2*len 之间反弹, 减去 len 让范围居中在 -len~+len
            offset = Mathf.PingPong(t, range * 2f) - range;
        }
        else
        {
            // Loop: t % (2*range) - range, 让 offset 在 -range~+range 之间循环
            // ((t % range) + range) % range = 数学取模, 防负数
            offset = ((t % (range * 2f)) + range * 2f) % (range * 2f) - range;
        }

        // direction.normalized = 把方向向量长度归 1, 速度只受 speed 控制
        Vector2 dir = direction.normalized;
        Vector3 p = basePos;
        p.x += dir.x * offset;
        p.y += dir.y * offset;
        transform.localPosition = p;
    }
}
