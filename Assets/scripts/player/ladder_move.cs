using UnityEngine;

public class ladder_move : MonoBehaviour
{
    private float vertical; //垂直输入
    private float speed = 5f; //爬梯子的速度
    private bool isLadder; //是否在梯子上
    private bool isClimbing; //是否在爬梯子

    [SerializeField] private Rigidbody2D rb; //刚体组件 

    void Start()
    {

    }

    void Update()
    {
        vertical = Input.GetAxis("Vertical"); //获取垂直输入

        if (isLadder && Mathf.Abs(vertical) > 0f) //在梯子上且有垂直输入
        {
            isClimbing = true; //开始攀爬
        }
    }

    private void FixedUpdate()
    {
        if (isClimbing)
        {
            rb.gravityScale = 0f; //取消重力
            rb.velocity = new Vector2(rb.velocity.x, vertical * speed); //设置速度
        }
        else
        {
            rb.gravityScale = 9.8f; //恢复重力
        }
    }
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Ladder"))
        {
            if(Mathf.Abs(vertical) > 0f)
            {
                isLadder = true;
                isClimbing = true;
            }

        }
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Ladder"))
        {
            isLadder = false;
            isClimbing = false; //在更新退出梯子的时候触发攀爬为false
        }
    }
}