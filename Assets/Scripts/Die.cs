using UnityEngine;

public class Die : MonoBehaviour
{
    public GameObject p1;
    public GameObject p2;

    Vector3 p1Pos;
    Vector3 p2Pos;

    public static bool touch_lava;

    void Start()
    {
        p1 = GameObject.Find("Player");
        p2 = GameObject.Find("Player2");
        p1Pos = p1.transform.position;
        p2Pos = p2.transform.position;
    }
    void Update()
    {
        Debug.Log(touch_lava);
    }

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
