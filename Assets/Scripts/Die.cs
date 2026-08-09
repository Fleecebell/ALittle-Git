using UnityEngine;

public class Die : MonoBehaviour
{
    // 玩家是否处于死亡状态（碰岩浆等致死危险时置 true，离开后置 false）。
    // 由按钮脚本在 Update 中作为"玩家死亡、需立即复原按钮"的信号。
    public static bool playerDying;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            MoveLate.isR = true;
            playerDying = true;
        }
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            playerDying = false;
        }
    }
}
