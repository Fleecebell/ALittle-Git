using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class two_to_play : MonoBehaviour
{
    public GameObject camera0;
    public GameObject camera1;
    public GameObject p0;
    public GameObject p1;
    Vector3 p1Pos;

    void Start()
    {
        p1Pos = p0.transform.position;
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            camera0.SetActive(false);
            camera1.SetActive(true);
            p0.SetActive(false);
            p1.SetActive(true);
            tutorial.isTutorial = false;
            move_late.isR = true;
        }
        if(other.gameObject.CompareTag("Player2"))
        {
            p0.transform.position = p1Pos;
        }
    }
}
