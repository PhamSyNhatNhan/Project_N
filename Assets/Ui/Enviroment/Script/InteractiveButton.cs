using UnityEngine;

public class InteractiveButton : MonoBehaviour
{
    [SerializeField] private GameObject bt;

    private string _eventName;
    private object _payload;

    private void Awake()
    {
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .AddListener(HandleInteractiveUiEvent);
    }

    private void OnDestroy()
    {
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .RemoveListener(HandleInteractiveUiEvent);
    }

    private void Start()
    {
        bt.SetActive(false);
    }

    private void HandleInteractiveUiEvent(Component sender, object data)
    {
        if (data is not InteractiveUiData uiData) return;

        bt.SetActive(uiData.show);
        _eventName = uiData.eventName;
        _payload   = uiData.payload;
    }

    public void Click()
    {
        if (string.IsNullOrEmpty(_eventName)) return;
        EventManager.Enviroment.TriggerInteractiveEvent.Get(_eventName).Invoke(this, _payload);
    }
}