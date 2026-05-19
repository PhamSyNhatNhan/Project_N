using UnityEngine;

/// <summary>
/// Shop item — mở RogueBuffSelectUI cho player tương tác.
/// Fire OnRogueBuffSelectOpen → RogueBuffSelectUI hiện lên.
/// Fire OnShopItemUsed → ShopHealObject tự SetActive(false).
/// </summary>
public class ShopBuffObject : InteractiveObject
{
    // ── Subscribe ─────────────────────────────────────────────────
    protected override void OnEnable()
    {
        EventManager.Enviroment.TriggerInteractiveEvent.Get(nameObject)
            .AddListener(OnInteract);
        EventManager.Gm.OnShopItemUsed.Get()
            .AddListener(OnShopItemUsed);
    }

    protected override void OnDisable()
    {
        EventManager.Enviroment.TriggerInteractiveEvent.Get(nameObject)
            .RemoveListener(OnInteract);
        EventManager.Gm.OnShopItemUsed.Get()
            .RemoveListener(OnShopItemUsed);
    }

    // ── Trigger ───────────────────────────────────────────────────
    protected override void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<Stat>() == null) return;

        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(true, nameObject));
    }

    protected override void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<Stat>() == null) return;

        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(false, nameObject));
    }

    // ── Interact ──────────────────────────────────────────────────
    private void OnInteract(Component sender, object data)
    {
        // Ẩn interact button
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(false, nameObject));

        // Mở buff select
        EventManager.Gm.OnRogueBuffSelectOpen.Get()
            .Invoke(this, BuffSelectSource.Shop);

        // Báo cho shop item còn lại biến mất
        EventManager.Gm.OnShopItemUsed.Get()
            .Invoke(this, null);

        gameObject.SetActive(false);
    }

    // ── Khi item kia được dùng → ẩn cái này ─────────────────────
    private void OnShopItemUsed(Component sender, object data)
    {
        if (sender == this) return;

        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(false, nameObject));

        gameObject.SetActive(false);
    }
}