using System.Collections.Generic;
using UnityEngine;

public class TimeScale : MonoBehaviour
{
    [Header("Immunity")]
    [SerializeField] private bool immuneToSlow = false;
    [SerializeField] private bool immuneToStop = false;

    [Header("Affected Components")]
    [SerializeField] private bool affectAnimator = true;
    [SerializeField] private bool affectMovement = true;

    [Header("Debug — Info")]
    [SerializeField] private float currentMovementScale  = 1f;
    [SerializeField] private float currentAnimationScale = 1f;
    [SerializeField] private List<string> activeMovementModifiers  = new();
    [SerializeField] private List<string> activeAnimationModifiers = new();

    private readonly Dictionary<string, float> movementModifiers  = new();
    private readonly Dictionary<string, float> animationModifiers = new();

    private Animator animator;
    private float lastTimeScale = 1f;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        EventManager.Time.OnSlowApply .Get().AddListener(HandleSlowApply);
        EventManager.Time.OnSlowRemove.Get().AddListener(HandleSlowRemove);
        EventManager.Time.OnGamePause .Get().AddListener(HandleGamePause);
    }

    private void OnDisable()
    {
        EventManager.Time.OnSlowApply .Get().RemoveListener(HandleSlowApply);
        EventManager.Time.OnSlowRemove.Get().RemoveListener(HandleSlowRemove);
        EventManager.Time.OnGamePause .Get().RemoveListener(HandleGamePause);
    }

    // ── Update — detect Unity timeScale change ────────────────────
    private void Update()
    {
        if (Mathf.Approximately(UnityEngine.Time.timeScale, lastTimeScale)) return;
        lastTimeScale = UnityEngine.Time.timeScale;
        ApplyToComponents();
        NotifyChanged();
    }

    // ── Handlers ──────────────────────────────────────────────────
    private void HandleSlowApply(Component sender, object data)
    {
        if (data is not SlowData slowData) return;
        AddModifier(slowData.Key, slowData.Scale, slowData.AffectType);
    }

    private void HandleSlowRemove(Component sender, object data)
    {
        if (data is not SlowData slowData) return;
        RemoveModifier(slowData.Key, slowData.AffectType);
    }

    private void HandleGamePause(Component sender, object data)
    {
        if (data is not bool isPaused) return;
        if (animator != null)
            animator.speed = isPaused ? 0f : AnimationScale * UnityEngine.Time.timeScale;
    }

    // ── Scale ─────────────────────────────────────────────────────
    private float CalcScale(Dictionary<string, float> mods)
    {
        float result = 1f;
        foreach (var mod in mods.Values)
            result *= mod;
        return result;
    }

    public float MovementScale  => affectMovement ? CalcScale(movementModifiers)  : 1f;
    public float AnimationScale => affectAnimator ? CalcScale(animationModifiers) : 1f;

    // ── DeltaTime ─────────────────────────────────────────────────
    public float DeltaTime    => UnityEngine.Time.deltaTime      * MovementScale;
    public float FixDeltaTime => UnityEngine.Time.fixedDeltaTime * MovementScale;

    // ── Modifier ──────────────────────────────────────────────────
    public void AddModifier(string key, float scale, SlowAffectType type = SlowAffectType.Both)
    {
        if (immuneToSlow) return;
        if (immuneToStop && scale <= 0f) return;

        scale = Mathf.Max(0f, scale);

        if (type == SlowAffectType.Movement || type == SlowAffectType.Both)
            movementModifiers[key] = scale;
        if (type == SlowAffectType.Animation || type == SlowAffectType.Both)
            animationModifiers[key] = scale;

        ApplyToComponents();
        NotifyChanged();
    }

    public void RemoveModifier(string key, SlowAffectType type = SlowAffectType.Both)
    {
        if (type == SlowAffectType.Movement || type == SlowAffectType.Both)
            movementModifiers.Remove(key);
        if (type == SlowAffectType.Animation || type == SlowAffectType.Both)
            animationModifiers.Remove(key);

        ApplyToComponents();
        NotifyChanged();
    }

    public void RemoveAllModifiers()
    {
        movementModifiers.Clear();
        animationModifiers.Clear();
        ApplyToComponents();
        NotifyChanged();
    }

    public bool HasModifier(string key)
        => movementModifiers.ContainsKey(key) || animationModifiers.ContainsKey(key);

    // ── Apply & Notify ────────────────────────────────────────────
    private void ApplyToComponents()
    {
        if (animator != null)
            animator.speed = AnimationScale * UnityEngine.Time.timeScale;

        RefreshDebugInfo();
    }

    private void RefreshDebugInfo()
    {
        currentMovementScale  = MovementScale;
        currentAnimationScale = AnimationScale;

        activeMovementModifiers.Clear();
        foreach (var (key, value) in movementModifiers)
            activeMovementModifiers.Add($"{key}: {value:F2}");

        activeAnimationModifiers.Clear();
        foreach (var (key, value) in animationModifiers)
            activeAnimationModifiers.Add($"{key}: {value:F2}");
    }

    private void NotifyChanged()
    {
        EventManager.Time.OnEntityScaleChanged
            .Get(gameObject.name)
            .Invoke(this, new TimeScaleChangedData
            {
                MovementScale  = MovementScale,
                AnimationScale = AnimationScale
            });
    }

    // ── Properties ────────────────────────────────────────────────
    public bool ImmuneToSlow
    {
        get => immuneToSlow;
        set => immuneToSlow = value;
    }

    public bool ImmuneToStop
    {
        get => immuneToStop;
        set => immuneToStop = value;
    }
}