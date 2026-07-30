using UnityEngine;

public class Key : MonoBehaviour
{
    [Header("目标通关门")]
    [Tooltip("拖入场景中需要解锁的通关门（挂有 NextLevel 组件的 GameObject）")]
    [SerializeField] private NextLevel targetDoor;

    [Header("动画（可选）")]
    [Tooltip("拖入钥匙上的 Animator 组件，并在 Animator Controller 中准备好 Collect 触发器")]
    [SerializeField] private Animator animator;



    private static readonly int CollectTrigger = Animator.StringToHash("Collect");

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // P1 或 P2 触碰钥匙
        if (!collision.gameObject.CompareTag("Player") && !collision.gameObject.CompareTag("Player2"))
            return;

        // 解锁目标通关门
        if (targetDoor != null)
        {
            targetDoor.Unlock();
        }
        else
        {
            Debug.LogWarning("Key 未指定目标通关门 (targetDoor)！");
        }

        // 播放收集动画
        if (animator != null)
        {
            animator.SetTrigger(CollectTrigger);
        }

        // 钥匙消失
        gameObject.SetActive(false);
    }
}
