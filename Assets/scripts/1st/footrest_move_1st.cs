using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class footrest_move_1st : MonoBehaviour
{
    public GameObject footrest0;
    public Transform footrest1_pos;
    public float speed = 10f;


    void Start()
    {

    }

    void Update()
    {
        if (Footrest_trigger_1st.footrest_trigger1_isopen_1st)
        {
            footrest0.transform.position = Vector3.Lerp(footrest0.transform.position, footrest1_pos.position, Mathf.Min(speed * Time.deltaTime, 1f));
        }
    }
} 