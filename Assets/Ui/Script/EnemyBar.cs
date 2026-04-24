using System;
using UnityEngine;

/// <summary>
/// Gắn lên EnemyBar (World Space Canvas)
/// Tự tách khỏi parent, giữ offset ban đầu so với parent
/// Không bao giờ tự disable — dùng Canvas.enabled để ẩn/hiện
/// để LateUpdate luôn chạy và phát hiện enemy re-enable
/// </summary>
[RequireComponent(typeof(Canvas))]
public class EnemyBar : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private HealthBar healthBar;
    [SerializeField] private BuffPanel buffPanel;

    private Transform targetTransform;
    private Vector3   localOffset;
    private string    entityKey;
    private Canvas    canvas;
    private Stat      targetStat;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
    }

    private void Start()
    {
        targetStat = GetComponentInParent<Stat>();
        if (targetStat != null)
        {
            targetTransform = targetStat.transform;
            entityKey = $"{targetStat.NameCharacter}_{targetStat.GetInstanceID()}";
            SetTarget(targetStat);
            SubscribeDead();
        }
        else
        {
            Debug.LogWarning("[EnemyBar] Không tìm thấy Stat trên parent!");
        }

        // Cache offset theo world space trước khi tách
        localOffset = transform.position - targetTransform.position;
        transform.SetParent(null);
    }

    private void OnDestroy() => UnsubscribeDead();

    private void LateUpdate()
    {
        if (targetTransform == null) return;

        bool targetActive = targetTransform.gameObject.activeInHierarchy;

        // Ẩn/hiện Canvas — không disable gameObject
        if (canvas.enabled != targetActive)
            canvas.enabled = targetActive;

        if (!targetActive) return;

        transform.position = targetTransform.position + localOffset;
        transform.rotation = Quaternion.identity;
    }

    // ── Subscribe entity dead ─────────────────────────────────────
    private void SubscribeDead()
    {
        if (string.IsNullOrEmpty(entityKey)) return;
        EventManager.Entity.OnEntityDead
            .Get(entityKey)
            .AddListener(OnEntityDead);
    }

    private void UnsubscribeDead()
    {
        if (string.IsNullOrEmpty(entityKey)) return;
        EventManager.Entity.OnEntityDead
            .Get(entityKey)
            .RemoveListener(OnEntityDead);
    }

    private void OnEntityDead(Component sender, object data)
    {
        buffPanel?.ClearAll();
        canvas.enabled = false;
    }

    // ── Public ────────────────────────────────────────────────────
    public void SetTarget(Stat stat)
    {
        string key = $"{stat.NameCharacter}_{stat.GetInstanceID()}";
        healthBar?.SetEntityKey(key);
        buffPanel?.SetEntityKey(key);
    }
}