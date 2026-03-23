using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class button1_2nd : MonoBehaviour
{
    public GameObject a;

    public GameObject wall0;
    public Transform wall1_pos;
    private Vector3 wall0_pos;
    public float speed = 10f;

    public bool wall1_isopen_2st = false;

    void Start()
    {
        wall0_pos = wall0.transform.position;
        wall1_isopen_2st = false;
    }

    void Update()
    {
        if (wall1_isopen_2st)
        {
            wall0.transform.position = Vector3.Lerp(wall0.transform.position, wall1_pos.position, Mathf.Min(speed * Time.deltaTime, 1f));
            a.SetActive(false);
        }
        else
        {
            wall0.transform.position = Vector3.Lerp(wall0.transform.position, wall0_pos, Mathf.Min(speed * Time.deltaTime, 1f));
            a.SetActive(true);
        }
    }
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            wall1_isopen_2st = true;
        }
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            wall1_isopen_2st = false;
        }
    }
}