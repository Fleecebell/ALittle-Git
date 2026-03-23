using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class button0_2nd : MonoBehaviour
{
    public GameObject a;

    public GameObject footrest0;
    public Transform footrest1_pos;
    private Vector3 footrest0_pos;
    public float speed = 10f;

    public GameObject footrest0_;
    public Transform footrest1_pos_;
    private Vector3 footrest0_pos_;

    public bool footrest_trigger2_isopen_2st = false;

    void Start()
    {
        footrest0_pos = footrest0.transform.position;
        footrest0_pos_ = footrest0_.transform.position;
        footrest_trigger2_isopen_2st = false;

        a.SetActive(true);
    }

    void Update()
    {
        if (footrest_trigger2_isopen_2st)
        {
            footrest0.transform.position = Vector3.Lerp(footrest0.transform.position, footrest1_pos.position, Mathf.Min(speed * Time.deltaTime, 1f));
            footrest0_.transform.position = Vector3.Lerp(footrest0_.transform.position, footrest1_pos_.position, Mathf.Min(speed * Time.deltaTime, 1f));
            a.SetActive(false);
        }
        else
        {
            footrest0.transform.position = Vector3.Lerp(footrest0.transform.position, footrest0_pos, Mathf.Min(speed * Time.deltaTime, 1f));
            footrest0_.transform.position = Vector3.Lerp(footrest0_.transform.position, footrest0_pos_, Mathf.Min(speed * Time.deltaTime, 1f));
            a.SetActive(true);
        }
    }
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            footrest_trigger2_isopen_2st = true;
        }
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            footrest_trigger2_isopen_2st = false;
        }
    }
}