using System.Collections.Generic;
using UnityEngine;

public class HeartSpiritSkill : EnemySkill
{
    // ── Config ────────────────────────────────────────────────────
    private float       chaseSpeed      = 150f;
    private List<float> bombDamage      = new List<float> { 200f };
    private DamageType  bombDamageType  = DamageType.Magic;
    private string      bombPrefabPath  = "Prefabs/HeartSpiritBomb";

    // ── Runtime ───────────────────────────────────────────────────
    private Transform playerTransform;
    private Stat      enemyStat;
    private Move      enemyMove;
    private string    entityKey;
    private bool      facingRight = true;

    [Header("Setup")]
    [SerializeField] private LayerMask  playerLayer;
    [SerializeField] private GameObject bombPrefab;

    private Hitbox hitbox;

    private float DeltaTime => timeScale.DeltaTime;

    // ── Components ────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        enemyStat = GetComponent<Stat>();
        enemyMove = GetComponent<Move>();
        hitbox    = GetComponent<Hitbox>();
        entityKey = $"{enemyStat.NameCharacter}_{enemyStat.GetInstanceID()}";
    }

    protected override void Start()
    {
        base.Start();

        if (bombPrefab == null && !string.IsNullOrEmpty(bombPrefabPath))
            bombPrefab = Resources.Load<GameObject>(bombPrefabPath);
    }

    private void OnEnable()
    {
        if (enemyMove == null || enemyMove.Rb == null) return;
        enemyMove.ResetAll();
        enemyMove.CanMove = true;
        facingRight = true;
        transform.rotation = Quaternion.identity;
    }

    private void OnDisable()
    {
        if (enemyStat != null) enemyStat.CanDamge = true;
        if (enemyMove != null) enemyMove.ResetAll();
    }

    // ── ILoadable ─────────────────────────────────────────────────
    public override void ApplyData()
    {
        base.ApplyData();

        if (skillData.TryGetValue("HeartSpiritChase", out var data))
        {
            chaseSpeed     = data.Get<float>("chaseSpeed",     150f);
            bombPrefabPath = data.Get<string>("bombPrefabPath", bombPrefabPath);

            if (data.damage != null && data.damage.Count > 0)
                bombDamage = data.damage;

            if (!string.IsNullOrEmpty(bombPrefabPath))
                bombPrefab = Resources.Load<GameObject>(bombPrefabPath);
        }
    }

    // ── Update ────────────────────────────────────────────────────
    protected override void Update()
    {
        base.Update();
        FindPlayer();
        if (playerTransform == null) return;

        FlipToPlayer();

        enemyMove.TargetPosition = playerTransform.position;
        enemyMove.MoveToXYLimitDistance(chaseSpeed, 0.1f, 0f);

        DetectContact();
    }

    // ── Contact Detection ─────────────────────────────────────────
    private void DetectContact()
    {
        if (hitbox == null) return;
        var hits = hitbox.detectObject(playerLayer);
        if (hits == null) return;

        foreach (var hit in hits)
        {
            var hitStat = hit.GetComponent<Stat>();
            if (hitStat == null || hitStat == enemyStat) continue;
            SpawnBomb();
            return;
        }
    }

    // ── Spawn Bomb & tự huỷ ───────────────────────────────────────
    private void SpawnBomb()
    {
        if (bombPrefab != null)
        {
            GameObject bomb      = Instantiate(bombPrefab, transform.position, Quaternion.identity);
            var        bombScript = bomb.GetComponent<HeatSpiritBomb>();
            if (bombScript != null)
                bombScript.SetUp(bombDamageType, bombDamage, enemyStat,
                                 enemyStat.CurCritRate, enemyStat.CurCritDamage);
        }

        enemyStat.CurHealth = 0f;
        EventManager.Entity.OnEntityHealthChanged.Get(entityKey).Invoke(this, 0f);
        EventManager.Entity.OnEntityDead.Get(entityKey).Invoke(this, null);
        gameObject.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void FindPlayer()
    {
        if (playerTransform != null) return;
        var player = GameObject.FindWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    private void FlipToPlayer()
    {
        if (playerTransform == null) return;
        bool playerIsRight = playerTransform.position.x >= transform.position.x;
        if (playerIsRight == facingRight) return;
        facingRight = playerIsRight;
        transform.Rotate(0f, 180f, 0f);
    }
}