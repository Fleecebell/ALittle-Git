using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class menu : MonoBehaviour
{
    [Tooltip("鼠标移动时，UI元素的偏移距离倍数")]
    public float offsetMultiplier = 1f;

    [Tooltip("移动的平滑时间，值越小反应越快")]
    public float smoothTime = .3f;

    // 初始位置
    private Vector2 startPosition;

    // 平滑移动的速度缓存
    private Vector3 velocity;

    private void Start()
    {
        // 记录UI元素的初始位置
        startPosition = transform.position;
    }

    private void Update()
    {
        // 将鼠标在屏幕上的像素坐标转换为视口坐标（0~1范围）
        // 例如：屏幕左下角是(0,0)，右上角是(1,1)
        Vector2 offset = Camera.main.ScreenToViewportPoint(Input.mousePosition);

        // 计算目标位置：初始位置 + 鼠标偏移量 * 偏移倍数
        Vector3 targetPosition = startPosition + (Vector2)(offset * offsetMultiplier);

        // 使用 SmoothDamp 实现平滑移动
        // SmoothDamp 会让物体从当前位置平滑过渡到目标位置
        transform.position = Vector3.SmoothDamp(
            transform.position,   // 当前位置
            targetPosition,       // 目标位置
            ref velocity,         // 速度缓存（必须是引用传递）
            smoothTime            // 平滑时间
        );
    }
}