using UnityEngine;

public class RougeGate : InteractiveObject
{
    protected override void OnEnable()
    {
        EventManager.Enviroment.TriggerInteractiveEvent.Get(nameObject)
            .AddListener((component, data) => OpenCharacterSelect());
    }

    protected override void OnDisable()
    {
        EventManager.Enviroment.TriggerInteractiveEvent.Get(nameObject)
            .RemoveListener((component, data) => OpenCharacterSelect());
    }

    protected override void Start() { }
    protected override void Update() { }

    private void OpenCharacterSelect()
    {
        Debug.Log("[RougeGate] OpenCharacterSelect called");

        EventManager.Gm.OnOpenCharacterSelect.Get().Invoke(this, null);
    }

    protected override void OnTriggerEnter2D(Collider2D other)
    {
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(true, nameObject));
    }

    protected override void OnTriggerExit2D(Collider2D other)
    {
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(false, nameObject));
    }
}