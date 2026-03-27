using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Button_once : MonoBehaviour
{
    GameObject big_button;

    [Header("要移动的物体")]
    public GameObject wall0;
    [Header("移动到的位置")]
    public Transform wall1_pos;
    private Vector3 wall0_pos;
    [Header("移动速度")]
    public float speed = 10f;

    bool wall_isgone = false;

    void Start()
    {
        if (wall0 != null && wall0_pos != null)
        {
            wall0_pos = wall0.transform.position;
        }
        else Debug.Log("未正确挂载移动物体");
        big_button = transform.Find("big").gameObject;
        //wall0_pos = wall0.transform.position;
        wall_isgone = false;
    }

    void Update()
    {
        if (wall_isgone)
        {
            big_button.SetActive(false);
            wall0.transform.position = Vector3.Lerp(wall0.transform.position, wall1_pos.position, Mathf.Min(speed * Time.deltaTime, 1f));

        }
    }
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            wall_isgone = true;
        }
    }
}