using UnityEngine;
using System;

public class Button_twice : MonoBehaviour
{
    GameObject big_button;

    [Header("移动物体")]
    public GameObject wall0;
    [Header("目标位置")]
    public Transform wall1_pos;
    private Vector3 wall0_pos;
    [Header("移动速度")]
    public float speed = 10f;

    bool wall_isgone = false;

    // 平台移动增量
    private Vector3 previousWall0Pos;
    public Vector3 PlatformDelta { get; private set; }

    void Start()
    {
        big_button = transform.Find("big").gameObject;
        if (wall0 != null)
        {
            wall0_pos = wall0.transform.position;
            previousWall0Pos = wall0.transform.position;
            Button_once.PlatformDeltas[wall0] = () => PlatformDelta;
        }
        else Debug.Log("未正确设置移动物体");
        wall_isgone = false;
        PlatformDelta = Vector3.zero;
    }

    void Update()
    {
        if (wall0 == null) return;

        previousWall0Pos = wall0.transform.position;

        if (wall_isgone)
        {
            big_button.SetActive(false);
            wall0.transform.position = Vector3.Lerp(wall0.transform.position, wall1_pos.position, Mathf.Min(speed * Time.deltaTime, 1f));
        }
        else
        {
            big_button.SetActive(true);
            wall0.transform.position = Vector3.Lerp(wall0.transform.position, wall0_pos, Mathf.Min(speed * Time.deltaTime, 1f));
        }

        PlatformDelta = wall0.transform.position - previousWall0Pos;
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            wall_isgone = true;
        }
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            wall_isgone = false;
        }
    }

    private void OnDestroy()
    {
        if (wall0 != null && Button_once.PlatformDeltas.ContainsKey(wall0))
            Button_once.PlatformDeltas.Remove(wall0);
    }
}
