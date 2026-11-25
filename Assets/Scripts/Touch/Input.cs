using System.Collections.Generic;
using UnityEngine;
using NativeInput = UnityEngine.Input;

// 游戏脚本在全局命名空间里直接使用 Input。只在本程序集接管这三个静态入口，
// 鼠标、其他轴和其余旧 Input API 继续使用 Unity 原实现。
internal sealed class Input : NativeInput
{
    private Input() { }

    private static readonly HashSet<KeyCode> pending = new HashSet<KeyCode>();
    private static readonly HashSet<KeyCode> pressed = new HashSet<KeyCode>();
    private static readonly HashSet<int> jumpPointers = new HashSet<int>();
    private static bool jumpHeld;
    private static Vector2 stick;
    private const float VerticalThreshold = 0.65f;

    // 上推只提供爬梯和游泳意图，避免落地或离梯时误触跳跃缓冲。
    internal static bool AscendHeld => stick.y >= VerticalThreshold || GetKey(KeyCode.Space) || GetKey(KeyCode.W);
    internal static bool StickUpPressed { get; private set; }

    internal static void Press(KeyCode key) => pending.Add(key);

    internal static void BeginJump(int pointerId)
    {
        if (jumpPointers.Add(pointerId) && jumpPointers.Count == 1) Press(KeyCode.Space);
    }

    internal static void EndJump(int pointerId) => jumpPointers.Remove(pointerId);

    internal static void CancelJump()
    {
        jumpPointers.Clear();
        jumpHeld = false;
        pending.Remove(KeyCode.Space);
        pressed.Remove(KeyCode.Space);
    }

    // 在角色 Update 之前锁存。P1、P2 录制、机关和计时器都能读到同一个边沿。
    internal static void Sample(Vector2 direction)
    {
        pressed.Clear();
        pressed.UnionWith(pending);
        pending.Clear();
        jumpHeld = jumpPointers.Count != 0;
        StickUpPressed = direction.y >= VerticalThreshold && stick.y < VerticalThreshold;
        if (direction.y <= -VerticalThreshold && stick.y > -VerticalThreshold) pressed.Add(KeyCode.S);
        stick = direction;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ClearTouch()
    {
        CancelJump();
        pending.Clear();
        pressed.Clear();
        stick = Vector2.zero;
        StickUpPressed = false;
    }

    public new static float GetAxisRaw(string axis)
    {
        float native = NativeInput.GetAxisRaw(axis);
        return axis == "Horizontal" && Mathf.Abs(stick.x) > Mathf.Abs(native) ? stick.x : native;
    }

    public new static bool GetKeyDown(KeyCode key) => NativeInput.GetKeyDown(key) || pressed.Contains(key);

    public new static bool GetKey(KeyCode key)
    {
        if (NativeInput.GetKey(key) || pressed.Contains(key)) return true;
        if (key == KeyCode.Space) return jumpHeld;
        if (key == KeyCode.S) return stick.y <= -VerticalThreshold;
        // P2 录制冲刺用 A/D 判断方向，需要和 Horizontal 的胜出方向一致。
        if (key != KeyCode.A && key != KeyCode.D) return false;
        if (Mathf.Abs(stick.x) <= Mathf.Abs(NativeInput.GetAxisRaw("Horizontal"))) return false;
        return key == KeyCode.A ? stick.x < 0f : stick.x > 0f;
    }
}

