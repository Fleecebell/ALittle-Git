using UnityEngine;

public class Die : MonoBehaviour
{
    public static bool touch_lava;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            Move_late.isR = true;
            touch_lava = true;
        }
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            touch_lava = false;
        }
    }
}
