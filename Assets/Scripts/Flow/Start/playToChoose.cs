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
        if (c0 != null)
            c0_pos = c0.transform.position;
        else
            Debug.LogWarning("playToChoose: c0 未赋值");
    }

    void Update()
    {
        if (c0 == null)
            return;

        if (open)
        {
            if (c1_pos == null)
            {
                Debug.LogWarning("playToChoose: c1_pos 未赋值");
                return;
            }
            c0.transform.position = Vector3.Lerp(c0.transform.position, c1_pos.position, Mathf.Min(speed * Time.deltaTime, 1f));
        }
        else
        {
            c0.transform.position = Vector3.Lerp(c0.transform.position, c0_pos, Mathf.Min(speed * Time.deltaTime, 1f));
        }
    }
}

