using UnityEngine;
using UnityEngine.UI;

public class PlayerAvatarUI : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Image avatarImage;

    private string _entityKey = "";

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        EventManager.Gm.OnPlayerSpawned.Get().AddListener(OnPlayerSpawned);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnPlayerSpawned.Get().RemoveListener(OnPlayerSpawned);
        Unsubscribe();
    }

    private void OnDisable() => Unsubscribe();

    // ── Handler ───────────────────────────────────────────────────
    private void OnPlayerSpawned(Component sender, object data)
    {
        if (data is not Stat stat) return;
        SetEntityKey($"{stat.NameCharacter}_{stat.GetInstanceID()}");
    }

    private void OnAvatarLoaded(Component sender, object data)
    {
        if (data is Sprite sprite && avatarImage != null)
            avatarImage.sprite = sprite;
    }

    // ── Subscribe ─────────────────────────────────────────────────
    private void Subscribe()
    {
        if (string.IsNullOrEmpty(_entityKey)) return;
        EventManager.Entity.OnEntityAvatarLoaded
            .Get(_entityKey)
            .AddListener(OnAvatarLoaded);
    }

    private void Unsubscribe()
    {
        if (string.IsNullOrEmpty(_entityKey)) return;
        EventManager.Entity.OnEntityAvatarLoaded
            .Get(_entityKey)
            .RemoveListener(OnAvatarLoaded);
    }

    // ── Public ────────────────────────────────────────────────────
    public void SetEntityKey(string key)
    {
        Unsubscribe();
        _entityKey = key;
        Subscribe();
    }

    public string EntityKey => _entityKey;
}