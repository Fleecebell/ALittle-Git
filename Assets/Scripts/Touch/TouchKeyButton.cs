using UnityEngine;
using UnityEngine.EventSystems;

public sealed class TouchKeyButton : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("按下时发送一次对应键，长按不重复。")]
    public KeyCode key;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            Input.Press(key);
    }
}

