using UnityEngine;

/// <summary>
/// Để sẵn trong scene combat/boss.
/// Player tương tác → fire OnStartCombat → RoomSpawner bắt đầu spawn wave.
/// Tự destroy sau khi kích hoạt.
/// </summary>
public class StartGate : InteractiveObject
{
    protected override void OnEnable()
    {
        EventManager.Enviroment.TriggerInteractiveEvent.Get(nameObject)
            .AddListener(OnInteract);
    }

    protected override void OnDisable()
    {
        EventManager.Enviroment.TriggerInteractiveEvent.Get(nameObject)
            .RemoveListener(OnInteract);
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

    private void OnInteract(Component sender, object data)
    {
        // Ẩn interact button
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(false, nameObject));

        // Fire event bắt đầu combat
        EventManager.Gm.OnStartCombat.Get().Invoke(this, null);

        Destroy(gameObject);
    }
}