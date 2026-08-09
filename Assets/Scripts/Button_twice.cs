using UnityEngine;
using System.Collections.Generic;

public class Button_twice : MonoBehaviour
{
    GameObject big_button;

    [Header("移动物体")]
    public GameObject wall0;
    [Header("目标位置")]
    public Transform wall1_pos;
    private Vector3 wall0_pos;
    [Header("移动速度")]
    public float speed = 2f;

    bool wall_isgone = false;
    // 返回原位中：死亡/按R复原后平台平滑移回起点，此期间忽略触发，防止玩家站在上面又把它推走
    bool returning = false;

    // 平台移动增量
    private Vector3 previousWall0Pos;
    public Vector3 PlatformDelta { get; private set; }

    // SmoothStep 进度（0=起点，1=目标），direction 表示方向（+1前进，-1返回），用于慢快慢曲线
    private float moveProgress = 0f;
    private int direction = 1;

    // 所有活跃实例，用于死亡时统一复原所有按钮
    private static List<Button_twice> activeInstances = new List<Button_twice>();

    // 死亡/按R时统一复原所有按钮（由 DeathRespawnVFX.TriggerDeath 调用）
    public static void ResetAllButtons()
    {
        for (int i = activeInstances.Count - 1; i >= 0; i--)
        {
            if (activeInstances[i] != null)
                activeInstances[i].ResetButton();
        }
    }

    void Start()
    {
        if (!activeInstances.Contains(this))
            activeInstances.Add(this);
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

        if (returning && moveProgress <= 0f)
        {
            // 已平滑回到起点，结束返回状态
            returning = false;
        }

        if (wall_isgone && !returning)
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

        if (Input.GetKeyDown(KeyCode.R) || Die.playerDying)
        {
            ResetButton();
        }
    }

    // 复原按钮：平滑返回起点（不清零进度，靠 direction=-1 平滑移回），期间忽略触发
    public void ResetButton()
    {
        wall_isgone = false;
        returning = true;
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Player2"))
        {
            // 返回原位期间忽略触发
            if (returning) return;
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
        activeInstances.Remove(this);
        if (wall0 != null && Button_once.PlatformDeltas.ContainsKey(wall0))
            Button_once.PlatformDeltas.Remove(wall0);
    }
}
