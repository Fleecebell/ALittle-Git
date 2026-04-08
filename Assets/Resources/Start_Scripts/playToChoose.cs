using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class playToChoose : MonoBehaviour
{
    public GameObject c0;
    public Transform c1_pos;
    private Vector3 c0_pos;
    public float speed = 10f;
    public bool open = false;

    public void StartGame()
    {
        if (open)
        {
            open = false;
        }
        else
        {
            open = true;
        }
    }

    void Start()
    {
        c0_pos = c0.transform.position;
    }

    void Update()
    {

        if (open)
        {
            c0.transform.position = Vector3.Lerp(c0.transform.position, c1_pos.position, Mathf.Min(speed * Time.deltaTime, 1f));
        }
        else
        {
            c0.transform.position = Vector3.Lerp(c0.transform.position, c0_pos, Mathf.Min(speed * Time.deltaTime, 1f));
        }
    }
}

