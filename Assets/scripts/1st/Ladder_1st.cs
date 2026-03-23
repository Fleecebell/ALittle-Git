using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ladder_1st : MonoBehaviour
{
    public GameObject Ladder0;
    public Transform Ladder1_pos;
    private Vector3 Ladder0_pos;
    public float Ladder_speed = 10f;

    public bool Ladder_isopen_1st = false;

    void Start()
    {
        Ladder0_pos = Ladder0.transform.position;
        Ladder_isopen_1st = false;
    }

    void Update()
    {
        if (Ladder_trigger_1st.ladder_1st)
        {
            Ladder0.transform.position = Vector3.Lerp(Ladder0.transform.position, Ladder1_pos.position, Mathf.Min(Ladder_speed * Time.deltaTime, 1f));
        }
        else
        {
            Ladder0.transform.position = Vector3.Lerp(Ladder0.transform.position, Ladder0_pos, Mathf.Min(Ladder_speed * Time.deltaTime, 1f));
        }
    }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            Ladder_isopen_1st = true;
        }
        else
        {
            Ladder_isopen_1st = false;
        }
    }
}
