using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Footrest_trigger_1st : MonoBehaviour
{
    public GameObject a;

    public static bool footrest_trigger1_isopen_1st = false;

    private void Start()
    {
        footrest_trigger1_isopen_1st = false;
    }
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            footrest_trigger1_isopen_1st = true;
            a.SetActive(false);
        }
    }
    //private void OnCollisionExit2D(Collision2D collision)
    //{
    //    if (collision.gameObject.CompareTag("Player"))
    //    {
    //        footrest_trigger_isopen = false;
    //    }
    //}
}