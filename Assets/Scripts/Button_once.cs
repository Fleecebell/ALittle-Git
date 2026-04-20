using UnityEngine;

public class Button_once : MonoBehaviour
{
    GameObject big_button;

    [Header("移动物体")]
    public GameObject wall0;
    private Vector3 wall0_pos;
    [Header("目标位置")]
    public Transform wall1_pos;
    [Header("移动速度")]
    public float speed = 10f;

    bool wall_isgone = false;

    void Start()
    {
        big_button = transform.Find("big").gameObject;
        if (wall0 != null && wall0_pos != null)
        {
            wall0_pos = wall0.transform.position;
        }
        else Debug.Log("未正确设置移动物体");
        wall_isgone = false;
        
    }

    void Update()
    {
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

        if(Input.GetKeyDown(KeyCode.R) || Die.touch_lava)
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
}