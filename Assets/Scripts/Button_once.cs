using UnityEngine;
using System.Collections.Generic;
using System;

public class Button_once : MonoBehaviour
{
    GameObject big_button;

    [Header("移动物体")]
    public GameObject wall0;
    private Vector3 wall0_pos;
    [Header("目标位置")]
    public Transform wall1_pos;
    [Header("移动速度")]
    public float speed = 2f;

    bool wall_isgone = false;

    // 平台移动增量
    private Vector3 previousWall0Pos;
    public Vector3 PlatformDelta { get; private set; }

    // SmoothStep 进度（0=起点，1=目标），direction 表示方向（+1前进，-1返回），用于慢快慢曲线
    private float moveProgress = 0f;
    private int direction = 1;

    // 静态字典：平台 GameObject → 获取其移动增量的委托
    // 角色脚本通过它自动找到平台，无需手动配置。支持所有平台类型。
    public static Dictionary<GameObject, Func<Vector3>> PlatformDeltas = new Dictionary<GameObject, Func<Vector3>>();

    void Start()
    {
        big_button = transform.Find("big").gameObject;
        if (wall0 != null)
        {
            wall0_pos = wall0.transform.position;
            previousWall0Pos = wall0.transform.position;
            PlatformDeltas[wall0] = () => PlatformDelta;
        }
        else
        {
            Debug.Log("未正确设置移动物体");
        }
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
            direction = 1;
        }
        else
        {
            big_button.SetActive(true);
            direction = -1;
        }

        // 基于单个进度变量移动，保证方向切换时不会瞬移
        moveProgress = Mathf.Clamp01(moveProgress + direction * speed * Time.deltaTime);
        float t = Mathf.SmoothStep(0f, 1f, moveProgress);
        wall0.transform.position = Vector3.Lerp(wall0_pos, wall1_pos.position, t);

        PlatformDelta = wall0.transform.position - previousWall0Pos;

        if (Input.GetKeyDown(KeyCode.R) || Die.touch_lava)
        {
            wall_isgone = false;
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            wall_isgone = true;
        }
    }

    private void OnDestroy()
    {
        if (wall0 != null && PlatformDeltas.ContainsKey(wall0))
            PlatformDeltas.Remove(wall0);
    }
}
