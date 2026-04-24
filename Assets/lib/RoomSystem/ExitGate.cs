using UnityEngine;

/// <summary>
/// Cổng exit — spawn tại (0,0) sau khi clear room.
/// Player interact → check floor cuối hay không:
///   - Không phải cuối → hiện RogueBuffSelectUI
///   - Floor cuối      → load scene "Start"
/// </summary>
public class ExitGate : InteractiveObject
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
        if (DungeonFlowManager.Instance == null)
        {
            Debug.LogError("[ExitGate] DungeonFlowManager chưa tồn tại.");
            return;
        }

        // Ẩn interact button
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(false, nameObject));

        if (IsFinalFloor())
        {
            // Tầng cuối — save rồi về Start, không chọn buff
            DungeonFlowManager.Instance.HandleFinalFloorCleared();
        }
        else
        {
            // Hiện màn chọn buff
            EventManager.Gm.OnRogueBuffSelectOpen.Get().Invoke(this, null);
        }

        Destroy(gameObject);
    }

    private bool IsFinalFloor()
    {
        var config = DungeonFlowManager.Instance.CurrentFloor;
        if (config == null) return false;

        var dungeonConfig = DungeonFlowManager.Instance.DungeonConfig;
        if (dungeonConfig?.floors == null) return false;

        return config.floorId == dungeonConfig.floors[dungeonConfig.floors.Length - 1].floorId;
    }
}