using UnityEngine;
using UnityEngine.EventSystems;

public sealed class TouchKeyButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("按下时发送一次对应键。空格同时保留按住状态，直到松手。")]
    public KeyCode key;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (key == KeyCode.Space) Input.BeginJump(eventData.pointerId);
        else Input.Press(key);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (key == KeyCode.Space && eventData.button == PointerEventData.InputButton.Left)
            Input.EndJump(eventData.pointerId);
    }

    private void OnDisable()
    {
        if (key == KeyCode.Space) Input.CancelJump();
    }
}

