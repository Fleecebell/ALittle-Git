using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class button1_3rd_1 : MonoBehaviour
{
    public GameObject a;

    public GameObject wall0;
    public Transform wall1_pos;
    public float speed = 10f;

    public bool wall2_isopen_2st = false;

    void Start()
    {
        wall2_isopen_2st = false;
    }

    void Update()
    {
        if (wall2_isopen_2st)
        {
            wall0.transform.position = Vector3.Lerp(wall0.transform.position, wall1_pos.position, Mathf.Min(speed * Time.deltaTime, 1f));
            a.SetActive(false);
        }
    }
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            wall2_isopen_2st = true;
        }
    }
}