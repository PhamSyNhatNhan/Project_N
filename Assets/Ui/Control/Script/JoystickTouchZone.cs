using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;

public class JoystickTouchZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform joystickBackground;
    [SerializeField] private OnScreenStick onScreenStick;

    private Canvas _canvas;

    void Start()
    {
        _canvas = GetComponentInParent<Canvas>();
    }

    public void OnPointerDown(PointerEventData e)
    {
        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            joystickBackground.parent as RectTransform,
            e.position, cam, out Vector2 local);
        joystickBackground.anchoredPosition = local;

        onScreenStick.OnPointerDown(e);
    }

    public void OnDrag(PointerEventData e)
    {
        onScreenStick.OnDrag(e);
    }

    public void OnPointerUp(PointerEventData e)
    {
        onScreenStick.OnPointerUp(e);
    }
}