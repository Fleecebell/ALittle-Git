using UnityEngine;

public class AutoMoving : MonoBehaviour
{
    [Header("移动物体")]
    public Transform pointA;
    [Header("目标位置")]
    public Transform pointB;
    [Header("移动速度")]
    public float moveSpeed = 2f;

    private Vector2 targetPos, p1Pos, p2Pos;

    void Start()
    {
        targetPos = (Vector2)pointB.position;
        p1Pos = (Vector2)pointA.position;
        p2Pos = (Vector2)pointB.position;
    }

    void FixedUpdate()
    {
        transform.position = Vector2.MoveTowards(
            transform.position,
            targetPos,
            moveSpeed * Time.fixedDeltaTime
        );

        if (Vector2.Distance(transform.position, p2Pos) < 0.1f)
        {
            targetPos = p1Pos;
        }
        else if (Vector2.Distance(transform.position, p1Pos) < 0.1f)
        {
            targetPos = p2Pos;
        }
    }
}