using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ladder_trigger_1st : MonoBehaviour
{
    public GameObject b;

    public static bool ladder_1st = false;
    void Start()
    {
        ladder_1st = false;
    }
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            ladder_1st = true;
            b.SetActive(false);
        }
    }
}
