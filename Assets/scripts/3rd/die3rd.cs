using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class die3rd : MonoBehaviour
{
    public GameObject p1;
    public GameObject p2;

    Vector3 p1Pos;
    Vector3 p2Pos;

    private void Start()
    {
        //记录两个物体的世界位置
        p1Pos = p1.transform.position;
        p2Pos = p2.transform.position;
    }
    private void Update()
    {

    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            //两个物体回到p1Pos，p2Pos的位置
            p1.transform.position = p1Pos;
            p2.transform.position = p2Pos;

        }
    }
}
