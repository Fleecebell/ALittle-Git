using UnityEngine;

public class Fruit_Delay : MonoBehaviour
{
    [Header("效果设置")]
    [Tooltip("勾选：增加延迟时间；不勾选：减少延迟时间")]
    public bool increase = true;

    [Header("数值")]
    public float changeAmount = 1f;
    public float minDelay = 0f;

    [Header("是否一次性道具")]
    public bool destroyOnTrigger = true;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateColor();
    }

    private void OnValidate()
    {
        // 编辑器下修改 increase 时实时更新颜色
        if (spriteRenderer == null && TryGetComponent(out SpriteRenderer sr))
            spriteRenderer = sr;
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (spriteRenderer == null) return;
        spriteRenderer.color = increase ? Color.blue : Color.red;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        MoveLate moveLate = FindObjectOfType<MoveLate>();
        if (moveLate == null)
        {
            Debug.LogWarning("未找到 MoveLate 组件！");
            return;
        }

        float newDelay = moveLate.delayTime + (increase ? changeAmount : -changeAmount);
        newDelay = Mathf.Max(minDelay, newDelay);
        moveLate.delayTime = newDelay;

        if (destroyOnTrigger)
            Destroy(gameObject);
    }
}