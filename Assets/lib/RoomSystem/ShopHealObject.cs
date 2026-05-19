using UnityEngine;

/// <summary>
/// Shop item — hồi full HP cho player tương tác.
/// Cache Stat khi trigger enter → hồi HP trực tiếp khi interact.
/// Fire OnShopItemUsed → ShopBuffObject tự SetActive(false).
/// </summary>
public class ShopHealObject : InteractiveObject
{
    private Stat _playerStat;

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
        var stat = other.GetComponent<Stat>();
        if (stat == null) return;

        _playerStat = stat;

        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(true, nameObject));
    }

    protected override void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<Stat>() == null) return;

        _playerStat = null;

        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(false, nameObject));
    }

    // ── Interact ──────────────────────────────────────────────────
    private void OnInteract(Component sender, object data)
    {
        if (_playerStat == null) return;

        // Ẩn interact button
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(false, nameObject));

        // Hồi full HP trực tiếp
        _playerStat.CurHealth = _playerStat.MaxHealth;

        // Báo item còn lại biến mất
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