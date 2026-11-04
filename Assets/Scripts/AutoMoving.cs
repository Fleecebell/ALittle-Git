using UnityEngine;

public class AutoMoving : MonoBehaviour
{
    [Header("移动物体")]
    public Transform pointA;
    [Header("目标位置")]
    public Transform pointB;
    [Header("移动速度")]
    public float moveSpeed = 2f;

    [Header("跟随父物体")]
    [Tooltip("勾选后按“父物体本地坐标系”移动：父物体整体平移时，往返轨迹会随之整体平移（AutoFather 这类分组必须保持勾选）。" +
             "根节点平台（没有父物体）行为完全不变。")]
    public bool useParentSpace = true;

    private Vector2 targetPos, p1Pos, p2Pos;
    // 用于移动的坐标系：父物体(勾选 useParentSpace 时) 或 世界坐标
    private Transform parentT;

    void Start()
    {
        parentT = useParentSpace ? transform.parent : null;

        if (pointB == null)
        {
            Debug.LogWarning($"[AutoMoving] {name} 没有设置 pointB，已停用", this);
            enabled = false;
            return;
        }

        // 端点统一换算到“移动所使用的坐标系”，这样父物体平移时轨迹会跟着一起平移
        p1Pos = ToSpace(pointA != null ? pointA.position : transform.position);
        p2Pos = ToSpace(pointB.position);
        targetPos = p2Pos;
    }

    // 世界坐标 -> 移动坐标系(父物体本地空间；没有父物体时就是世界坐标)
    private Vector2 ToSpace(Vector3 worldPos)
    {
        return parentT != null
            ? (Vector2)parentT.InverseTransformPoint(worldPos)
            : (Vector2)worldPos;
    }

    private Vector2 GetCurrent()
    {
        return parentT != null ? (Vector2)transform.localPosition : (Vector2)transform.position;
    }

    private void SetCurrent(Vector2 pos)
    {
        if (parentT != null) transform.localPosition = pos;
        else transform.position = pos;
    }

    void FixedUpdate()
    {
        Vector2 current = Vector2.MoveTowards(
            GetCurrent(),
            targetPos,
            moveSpeed * Time.fixedDeltaTime
        );
        SetCurrent(current);

        if (Vector2.Distance(current, p2Pos) < 0.1f)
        {
            targetPos = p1Pos;
        }
        else if (Vector2.Distance(current, p1Pos) < 0.1f)
        {
            targetPos = p2Pos;
        }
    }
}
