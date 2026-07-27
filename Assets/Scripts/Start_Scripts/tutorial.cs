using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class tutorial : MonoBehaviour
{
    public GameObject xx;
    public GameObject jj;
    public GameObject p1;
    public GameObject p11;
    public GameObject p12;
    public GameObject p13;
    public GameObject p14;
    public GameObject main_camera;
    public GameObject main_1;
    public GameObject main_2;
    public GameObject main_3;
    public GameObject main_4;
    public GameObject main_5;

    public GameObject tu0;
    public Transform tu_pos;
    private Vector3 tu0_pos;
    public float speed = 10f;

    public static bool isTutorial = false;

    void Start()
    {
        tu0_pos = tu0.transform.position;
        isTutorial = false;

    }

    void Update()
    {
        if (isTutorial)
        {
            tu0.transform.position = Vector3.Lerp(tu0.transform.position, tu_pos.position, Mathf.Min(speed * Time.deltaTime, 1f));
        }
        else
        {
            tu0.transform.position = Vector3.Lerp(tu0.transform.position, tu0_pos, Mathf.Min(speed * Time.deltaTime, 1f));
        }

        if(Input.GetKeyDown(KeyCode.R))
        {
            p1.SetActive(true);
            p11.SetActive(false);
            p12.SetActive(false);
            p13.SetActive(false);
            p14.SetActive(false);
            MoveLate.isR = true;
            main_1.SetActive(true);
            main_2.SetActive(false);
            main_3.SetActive(false);
            main_4.SetActive(false);
            main_5.SetActive(false);
        }
    }

    public void OnClickOpen()
    {
        if (!isTutorial)
        {
            isTutorial = true;
            MoveLate.isR = false;
            xx.SetActive(true);
            jj.SetActive(false);
            //main_camera.SetActive(false);
            main_1.SetActive(true);
        }
        else
        {
            isTutorial = false;
            MoveLate.isR = true;
            xx.SetActive(false);
            jj.SetActive(true);
            main_camera.SetActive(true);
            main_1.SetActive(false);
            main_2.SetActive(false);
            main_3.SetActive(false);
            main_4.SetActive(false);
            main_5.SetActive(false);
        }
    }
    //public void OnClickClose()
    //{
    //    if (isTutorial)
    //    {
    //        isTutorial = false;
    //    }
    //}
}
