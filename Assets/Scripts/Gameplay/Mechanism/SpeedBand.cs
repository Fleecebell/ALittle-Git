using UnityEngine;

public class SpeedBand : MonoBehaviour
{
    public enum SpeedDirectionMode
    {
        RightAccelerate,   // 向右加速，向左减速（原始效果）
        LeftAccelerate,   // 向左加速，向右减速（反向）
        // AccelerateBoth,    // 无论左右都加速
        // DecelerateBoth     // 无论左右都减速
    }

    [Header("移动距离阈值设置")]
    public float unitLength = 1f;      // 每 unitLength 米触发一次速度变化
    public float speedPerUnit = 1f;    // 每次变化的速度增量（绝对值）

    [Header("加减速方向模式")]
    public SpeedDirectionMode mode = SpeedDirectionMode.RightAccelerate;

    [Header("查看速度")]
    public bool debugShowSpeed = false;

    void Update()
    {
        if (debugShowSpeed)
            Debug.Log($"P1:{MoveFirst.moveSpeed}  P2:{MoveLate.moveSpeed}");
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            MoveFirst.traveledDistance = 0;
        }
        else if (collision.CompareTag("Player2"))
        {
            MoveLate.traveledDistance = 0;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        // 可选：离开区域时是否重置距离（避免下次进入时残留）
        // 如果你希望离开后恢复速度，可以另外实现，这里只重置距离
        if (collision.CompareTag("Player"))
        {
            MoveFirst.traveledDistance = 0;
        }
        else if (collision.CompareTag("Player2"))
        {
            MoveLate.traveledDistance = 0;
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && MoveFirst.isMoving)
        {
            AdjustSpeedForPlayer(ref MoveFirst.moveSpeed, ref MoveFirst.traveledDistance, MoveFirst.moveDir);
        }
        else if (collision.CompareTag("Player2") && MoveLate.isMoving)
        {
            AdjustSpeedForPlayer(ref MoveLate.moveSpeed, ref MoveLate.traveledDistance, MoveLate.moveDir);
        }
    }

    private void AdjustSpeedForPlayer(ref float moveSpeed, ref float traveledDistance, float moveDir)
    {
        // 移动方向为 0 时不处理（虽然 isMoving 已过滤，但保险）
        if (Mathf.Approximately(moveDir, 0)) return;

        // 累计距离达到阈值时触发速度变化
        if (Mathf.Abs(traveledDistance) >= unitLength)
        {
            // 根据模式计算速度变化量（带符号）
            float delta = GetDeltaSpeedByMode(moveDir);
            moveSpeed += delta;

            // 减去已处理的单位距离（保留剩余小数）
            traveledDistance -= Mathf.Sign(traveledDistance) * unitLength;
        }
    }

    /// <summary>
    /// 根据配置的模式和当前移动方向，返回本次速度变化量（可为正或负）
    /// </summary>
    private float GetDeltaSpeedByMode(float moveDir)
    {
        // moveDir: -1 向左, 1 向右
        switch (mode)
        {
            case SpeedDirectionMode.RightAccelerate:
                // 向右移动 (moveDir>0) 加速，向左移动 (moveDir<0) 减速
                return moveDir > 0 ? speedPerUnit : -speedPerUnit;

            case SpeedDirectionMode.LeftAccelerate:
                // 向左加速，向右减速
                return moveDir < 0 ? speedPerUnit : -speedPerUnit;

            // case SpeedDirectionMode.AccelerateBoth:
            //     // 无论左右都加速
            //     return speedPerUnit;

            // case SpeedDirectionMode.DecelerateBoth:
            //     // 无论左右都减速
            //     return -speedPerUnit;

            default:
                return 0;
        }
    }
}