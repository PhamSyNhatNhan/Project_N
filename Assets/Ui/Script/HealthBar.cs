using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Image hpBar;

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

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    // ── Handler ───────────────────────────────────────────────────
    private void OnPlayerSpawned(Component sender, object data)
    {
        if (data is not Stat stat) return;
        SetEntityKey($"{stat.NameCharacter}_{stat.GetInstanceID()}");
    }

    private void OnHealthChanged(Component sender, object data)
    {
        if (data is float percent)
            UpdateBar(percent);
    }

    private void UpdateBar(float percent)
    {
        if (hpBar != null)
            hpBar.fillAmount = Mathf.Clamp01(percent);
    }

    // ── Subscribe ─────────────────────────────────────────────────
    private void Subscribe()
    {
        if (string.IsNullOrEmpty(_entityKey)) return;
        EventManager.Entity.OnEntityHealthChanged
            .Get(_entityKey)
            .AddListener(OnHealthChanged);
    }

    private void Unsubscribe()
    {
        if (string.IsNullOrEmpty(_entityKey)) return;
        EventManager.Entity.OnEntityHealthChanged
            .Get(_entityKey)
            .RemoveListener(OnHealthChanged);
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