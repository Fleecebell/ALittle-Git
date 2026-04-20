using UnityEngine;

public class SpeedBand : MonoBehaviour
{
    [Header("改变速度的单位长度")]
    public float unitLength = 0.5f;

    [Header("每移动1单位速度变化")]
    public float speedPerUnit = 0.1f;

    void Update()
    {
        Debug.Log("P1: " + MoveFirst.moveSpeed + "  P2: " + MoveLate.moveSpeed);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            MoveFirst.traveledDistance = 0;
        }
        if (collision.CompareTag("Player2"))
        {
            MoveLate.traveledDistance = 0;
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            if (MoveFirst.isMoving)
            {
                // 每累计走了 1 单位，就加 0.1 速度
                while (Mathf.Abs(MoveFirst.traveledDistance) >= unitLength)
                {
                    float sign = Mathf.Sign(MoveFirst.traveledDistance);
                    
                    if (sign > 0) MoveFirst.moveSpeed += speedPerUnit;
                    else if (sign < 0) MoveFirst.moveSpeed -= speedPerUnit;

                    // 减掉已经计算过的 1 单位
                    MoveFirst.traveledDistance -= sign;
                }
            }
        }

        if (collision.CompareTag("Player2"))
        {
            if (MoveLate.isMoving)
            {
                while (Mathf.Abs(MoveLate.traveledDistance) >= unitLength)
                {
                    float sign = Mathf.Sign(MoveLate.traveledDistance);
                    
                    if (sign > 0) MoveLate.moveSpeed += speedPerUnit;
                    else if (sign < 0) MoveLate.moveSpeed -= speedPerUnit;

                    MoveLate.traveledDistance -= sign;
                }
            }
        }
    }
}