using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DaelSkill : SubPlayerSkill
{
    // ── Components ────────────────────────────────────────────────
    private Stat             daelStat;
    private PlayerController daelController;
    private Hitbox           hitbox;
    private Move             daelMove;

    // ── Prefabs ───────────────────────────────────────────────────
    [Header("Prefabs")]
    [SerializeField] private GameObject prefab_Swing;
    [SerializeField] private GameObject prefab_TimeStopZone;
    [SerializeField] private GameObject prefab_FallingSword;
    [SerializeField] private GameObject prefab_SlashBurst;
    [SerializeField] private GameObject prefab_FallingSwordBurst;

    // ── Perfect Dash ──────────────────────────────────────────────
    [Header("Perfect Dash")]
    [SerializeField] private Vector2   perfectDashBoxSize = new Vector2(2f, 2f);
    [SerializeField] private LayerMask projectileLayer;

    // ── Slash Rotation ────────────────────────────────────────────
    [Header("Slash Rotation")]
    [SerializeField] private float slash1RotX     =  30f;
    [SerializeField] private float slash1RotY     = -20f;
    [SerializeField] private float slash2RotX     = -30f;
    [SerializeField] private float slash2RotY     =  20f;
    [SerializeField] private float slash3Hit1RotX =  30f;
    [SerializeField] private float slash3Hit1RotY = -20f;
    [SerializeField] private float slash3Hit2RotX = -30f;
    [SerializeField] private float slash3Hit2RotY =  20f;

    // ── Pools ─────────────────────────────────────────────────────
    private EasyPoolingList poolSwing        = new EasyPoolingList();
    private EasyPoolingList poolTimeStopZone = new EasyPoolingList();
    private EasyPoolingList poolFallingSword      = new EasyPoolingList();
    private EasyPoolingList poolSlashBurst        = new EasyPoolingList();
    private EasyPoolingList poolFallingSwordBurst = new EasyPoolingList();

    // ── Combo ─────────────────────────────────────────────────────
    private int   comboIndex      = 0;
    private float comboResetTimer = 0f;
    private const float COMBO_RESET_TIME = 5f;

    // ── Flags ─────────────────────────────────────────────────────
    private bool      isNextSlash3    = false;
    private Transform lastTarget;
    private bool      isLunging       = false;
    private HashSet<GameObject> lungeHitObjects = new HashSet<GameObject>();

    // ── Ulti AtkSpeed Stack ───────────────────────────────────────
    private int   ultiAtkStackCount = 0;
    private List<float> ultiAtkStackTimers = new List<float>();

    // ── Burst State ───────────────────────────────────────────────
    private bool  isBurstActive  = false;
    private float burstTimer     = 0f;

    // ── Entity Key ────────────────────────────────────────────────
    private string entityKey => daelStat != null
        ? $"{daelStat.NameCharacter}_{daelStat.GetInstanceID()}"
        : gameObject.name;

    // ═══════════════════════════════════════════════════════════════
    //  SETUP
    // ═══════════════════════════════════════════════════════════════

    protected override void AwakeSetUp()
    {
        daelStat       = GetComponent<Stat>();
        daelController = GetComponent<PlayerController>();
        hitbox         = GetComponent<Hitbox>();
        daelMove       = GetComponent<Move>();
    }

    protected override void StartSetUp()
    {
        poolSwing.SetPrefab(prefab_Swing);
        poolTimeStopZone.SetPrefab(prefab_TimeStopZone);
        poolFallingSword.SetPrefab(prefab_FallingSword);
        poolSlashBurst.SetPrefab(prefab_SlashBurst);
        poolFallingSwordBurst.SetPrefab(prefab_FallingSwordBurst);

        EventManager.Projectile.OnSlashEnd
            .Get(entityKey)
            .AddListener(OnSlashEnd);

        EventManager.Entity.OnEntityMoveToEnd
            .Get(entityKey)
            .AddListener(OnMoveToEnd);
    }

    private void OnDisable()
    {
        EventManager.Projectile.OnSlashEnd
            .Get(entityKey)
            .RemoveListener(OnSlashEnd);

        EventManager.Entity.OnEntityMoveToEnd
            .Get(entityKey)
            .RemoveListener(OnMoveToEnd);
    }

    // ═══════════════════════════════════════════════════════════════
    //  UPDATE
    // ═══════════════════════════════════════════════════════════════

    protected override void Update()
    {
        base.Update();
        TickComboReset();
        TickLunge();
        TickUltiAtkSpeed();
        TickBurst();
    }

    private void TickLunge()
    {
        if (!isLunging) return;

        var lungeData = GetSkillData("Lunge");
        if (lungeData == null) return;

        var enemies = hitbox.detectObject(enemyLayer, 1);
        if (enemies == null) return;

        float dmg = daelStat.CaculateDamage(DamageType.Physical,
            lungeData.damage[0] * daelStat.CurAttack / 100f);

        foreach (var e in enemies)
        {
            if (lungeHitObjects.Contains(e)) continue;
            var targetStat = e.GetComponent<Stat>();
            if (targetStat == null) continue;
            lungeHitObjects.Add(e);
            targetStat.TakeDamage(DamageType.Physical, dmg,
                daelStat.CurCritRate, daelStat.CurCritDamage,
                lungeData.Get<float>("iFrameDuration", 0.05f));
        }
    }

    private void TickBurst()
    {
        if (!isBurstActive) return;
        burstTimer -= Time.deltaTime;
        if (burstTimer <= 0f)
            DoFallingSwordBurstEnd();
    }

    private void ActivateBurst()
    {
        var data = GetSkillData("Burst");
        if (data == null) return;

        isBurstActive = true;
        burstTimer    = data.Get<float>("burstDuration", 15f);
        isBurstMode   = true;

        // Apply buff
        float atkFlat    = data.Get<float>("burstAtkFlat", 30f);
        float atkMult    = data.Get<float>("burstAtkMult", 50f);
        float defBonus   = data.Get<float>("burstDefBonus", 50f);
        float speedBonus = data.Get<float>("burstSpeedBonus", 30f);
        float atkSpd     = data.Get<float>("burstAtkSpeedBonus", 30f);
        float critRate   = data.Get<float>("burstCritRateBonus", 15f);
        float critDmg    = data.Get<float>("burstCritDmgBonus", 50f);

        daelStat.BuffAttack.SetFlat("burst", atkFlat);
        daelStat.BuffAttack.SetMultiplier("burst", atkMult);
        daelStat.BuffDefense.SetFlat("burst", defBonus);
        daelMove.BuffMoveSpeed.SetMultiplier("burst", speedBonus);
        daelStat.BuffAttackSpeed.SetMultiplier("burst", atkSpd);
        daelStat.BuffCritRate.SetMultiplier("burst", critRate);
        daelStat.BuffCritDamage.SetMultiplier("burst", critDmg);
        daelStat.RecalculateStat();

        EventManager.Player.OnPlayerBurst.Get(entityKey).Invoke(this, null);
        EventManager.Ui.OnBurstActive.Get().Invoke(this, new BurstActiveData
        {
            Name     = daelStat.NameCharacter,
            IsActive = true
        });
    }

    private void EndBurst()
    {
        isBurstActive = false;
        isBurstMode   = false;
        burstTimer    = 0f;

        daelStat.BuffAttack.RemoveFlat("burst");
        daelStat.BuffAttack.RemoveMultiplier("burst");
        daelStat.BuffDefense.RemoveFlat("burst");
        daelMove.BuffMoveSpeed.RemoveMultiplier("burst");
        daelStat.BuffAttackSpeed.RemoveMultiplier("burst");
        daelStat.BuffCritRate.RemoveMultiplier("burst");
        daelStat.BuffCritDamage.RemoveMultiplier("burst");
        daelStat.RecalculateStat();

        EventManager.Ui.OnBurstActive.Get().Invoke(this, new BurstActiveData
        {
            Name     = daelStat.NameCharacter,
            IsActive = false
        });
    }

    private void TickUltiAtkSpeed()
    {
        if (ultiAtkStackTimers.Count == 0) return;

        for (int i = ultiAtkStackTimers.Count - 1; i >= 0; i--)
        {
            ultiAtkStackTimers[i] -= Time.deltaTime;
            if (ultiAtkStackTimers[i] <= 0f)
            {
                daelStat.BuffAttackSpeed.RemoveMultiplier($"ultiAtk_{i}");
                daelStat.RecalculateStat();
                ultiAtkStackTimers.RemoveAt(i);
                RebuildUltiAtkKeys();
            }
        }
    }

    private void RebuildUltiAtkKeys()
    {
        // Xóa tất cả key cũ rồi set lại theo index mới
        for (int i = 0; i <= ultiAtkStackTimers.Count; i++)
            daelStat.BuffAttackSpeed.RemoveMultiplier($"ultiAtk_{i}");

        var data    = GetSkillData("FallingSword");
        float bonus = data?.Get<float>("ultiAtkSpeedBonus", 50f) ?? 50f;

        for (int i = 0; i < ultiAtkStackTimers.Count; i++)
            daelStat.BuffAttackSpeed.SetMultiplier($"ultiAtk_{i}", bonus);

        daelStat.RecalculateStat();
    }

    private void AddUltiAtkStack()
    {
        var data    = GetSkillData("FallingSword");
        float bonus = data?.Get<float>("ultiAtkSpeedBonus", 50f) ?? 50f;
        float dur   = data?.Get<float>("ultiAtkSpeedDuration", 5f) ?? 5f;

        int idx = ultiAtkStackTimers.Count;
        ultiAtkStackTimers.Add(dur);
        daelStat.BuffAttackSpeed.SetMultiplier($"ultiAtk_{idx}", bonus);
        daelStat.RecalculateStat();
    }

    private void TickComboReset()
    {
        if (comboIndex == 0) return;
        comboResetTimer -= Time.deltaTime;
        if (comboResetTimer <= 0f)
            comboIndex = 0;
    }

    // ═══════════════════════════════════════════════════════════════
    //  ATTACK
    // ═══════════════════════════════════════════════════════════════

    protected override void TapAttack()
    {
        if (!canInput) return;
        if (isAttack || isSkill || isUlti || isDash) return;

        if (isNextSlash3) { DoSlash3(true); return; }

        if (isBurstActive)
        {
            switch (comboIndex)
            {
                case 0: DoSlash1Burst(); break;
                case 1: DoSlash2Burst(); break;
                case 2: DoSlash3Burst(); break;
            }
            return;
        }

        switch (comboIndex)
        {
            case 0: DoSlash1(); break;
            case 1: DoSlash2(); break;
            case 2: DoSlash3(); break;
        }
    }

    private void DoSlash1()
    {
        if (!IsReady("Slash1")) return;

        var data = GetSkillData("Slash1");
        if (data == null) return;

        lastTarget = GetNearestEnemy();
        EasyFlipLock(0.1f, lastTarget);

        isAttack = true;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        float dmg = daelStat.CaculateDamage(DamageType.Physical, data.damage[0] * daelStat.CurAttack / 100f);
        Use("Slash1", false);
        SpawnSwing(data, new List<float> { dmg }, GetCenterAngle(lastTarget),
                   false, slash1RotX, slash1RotY, slashId: "Slash1", swingDirection: 1);
        AdvanceCombo();
    }

    private void DoSlash2()
    {
        if (!IsReady("Slash2")) return;

        var data = GetSkillData("Slash2");
        if (data == null) return;

        lastTarget = GetNearestEnemy();
        EasyFlipLock(0.1f, lastTarget);

        isAttack = true;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        float dmg = daelStat.CaculateDamage(DamageType.Physical, data.damage[0] * daelStat.CurAttack / 100f);
        Use("Slash2", false);
        SpawnSwing(data, new List<float> { dmg }, GetCenterAngle(lastTarget),
                   false, slash2RotX, slash2RotY, slashId: "Slash2", swingDirection: -1);
        AdvanceCombo();
    }

    private void DoSlash3()
    {
        if (!IsReady("Slash3")) return;

        var data = GetSkillData("Slash3");
        if (data == null) return;

        lastTarget = GetNearestEnemy();
        EasyFlipLock(0.1f, lastTarget);

        isAttack     = true;
        isNextSlash3 = false;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        float dmg1           = daelStat.CaculateDamage(DamageType.Physical, data.damage[0] * daelStat.CurAttack / 100f);
        float atkSpeedBonus  = data.Get<float>("slash3AtkSpeedBonus", 30f);
        float scaledAtkSpeed = daelStat.CurAttackSpeed * (1f + atkSpeedBonus / 100f);

        Use("Slash3", false);
        SpawnSwing(data, new List<float> { dmg1 }, GetCenterAngle(lastTarget),
                   true, slash3Hit1RotX, slash3Hit1RotY,
                   slashId: "Slash3", isSlash3Hit1: true,
                   atkSpeed: scaledAtkSpeed, swingDirection: 1);
        AdvanceCombo();
    }

    private void DoSlash3(bool isFromSkill)
    {
        if (!IsReady("Slash3")) return;

        var data = GetSkillData("Slash3");
        if (data == null) return;

        lastTarget = GetNearestEnemy();
        EasyFlipLock(0.1f, lastTarget);

        isAttack     = true;
        isNextSlash3 = false;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        float dmg1           = daelStat.CaculateDamage(DamageType.Physical, data.damage[0] * daelStat.CurAttack / 100f);
        float atkSpeedBonus  = data.Get<float>("slash3AtkSpeedBonus", 30f);
        float scaledAtkSpeed = daelStat.CurAttackSpeed * (1f + atkSpeedBonus / 100f);

        Use("Slash3", false);
        SpawnSwing(data, new List<float> { dmg1 }, GetCenterAngle(lastTarget),
            true, slash3Hit1RotX, slash3Hit1RotY,
            slashId: "Slash3", isSlash3Hit1: true,
            atkSpeed: scaledAtkSpeed, swingDirection: 1);

        if (!isFromSkill) AdvanceCombo();
    }

    // ── Burst Slashes ─────────────────────────────────────────────
    private void DoSlash1Burst()
    {
        if (!IsReady("Slash1Burst")) return;

        var data = GetSkillData("Slash1Burst");
        if (data == null) return;

        lastTarget = GetNearestEnemy();
        EasyFlipLock(0.1f, lastTarget);

        isAttack = true;
        canInput = false;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        float atkSpeedBonus  = data.Get<float>("slash3AtkSpeedBonus", 30f);
        float scaledAtkSpeed = daelStat.CurAttackSpeed * (1f + atkSpeedBonus / 100f);
        float dmg = daelStat.CaculateDamage(DamageType.Physical, data.damage[0] * daelStat.CurAttack / 100f);

        Use("Slash1Burst", false);
        SpawnSwing(data, new List<float> { dmg }, GetCenterAngle(lastTarget),
                   false, slash1RotX, slash1RotY, slashId: "Slash1Burst",
                   swingDirection: 1, pool: poolSlashBurst, atkSpeed: scaledAtkSpeed);
        AdvanceCombo();
    }

    private void DoSlash2Burst()
    {
        if (!IsReady("Slash2Burst")) return;

        var data = GetSkillData("Slash2Burst");
        if (data == null) return;

        lastTarget = GetNearestEnemy();
        EasyFlipLock(0.1f, lastTarget);

        isAttack = true;
        canInput = false;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        float atkSpeedBonus  = data.Get<float>("slash3AtkSpeedBonus", 30f);
        float scaledAtkSpeed = daelStat.CurAttackSpeed * (1f + atkSpeedBonus / 100f);
        float dmg = daelStat.CaculateDamage(DamageType.Physical, data.damage[0] * daelStat.CurAttack / 100f);

        Use("Slash2Burst", false);
        SpawnSwing(data, new List<float> { dmg }, GetCenterAngle(lastTarget),
                   false, slash2RotX, slash2RotY, slashId: "Slash2Burst",
                   swingDirection: -1, pool: poolSlashBurst, atkSpeed: scaledAtkSpeed);
        AdvanceCombo();
    }

    private void DoSlash3Burst()
    {
        if (!IsReady("Slash3Burst")) return;

        var data = GetSkillData("Slash3Burst");
        if (data == null) return;

        lastTarget = GetNearestEnemy();
        EasyFlipLock(0.1f, lastTarget);

        isAttack     = true;
        isNextSlash3 = false;
        canInput = false;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        float atkSpeedBonus  = data.Get<float>("slash3AtkSpeedBonus", 30f);
        float scaledAtkSpeed = daelStat.CurAttackSpeed * (1f + atkSpeedBonus / 100f);
        float dmg1 = daelStat.CaculateDamage(DamageType.Physical, data.damage[0] * daelStat.CurAttack / 100f);

        Use("Slash3Burst", false);
        SpawnSwing(data, new List<float> { dmg1 }, GetCenterAngle(lastTarget),
                   true, slash3Hit1RotX, slash3Hit1RotY,
                   slashId: "Slash3Burst", isSlash3Hit1: true,
                   atkSpeed: scaledAtkSpeed, swingDirection: 1,
                   pool: poolSlashBurst, totalArcOverride: 360f);
        AdvanceCombo();
    }

    // ═══════════════════════════════════════════════════════════════
    //  SKILL
    // ═══════════════════════════════════════════════════════════════

    protected override void TapSkill()
    {
        if (!canInput) return;
        if (isAttack || isSkill || isUlti || isDash) return;
        Debug.Log("Lung");
        DoLunge();
    }

    private void DoLunge()
    {
        if (!IsReady("Lunge")) return;

        var data = GetSkillData("Lunge");
        if (data == null) return;

        var enemies = hitbox.detectObject(enemyLayer);
        if (enemies == null || enemies.Count == 0) return;

        lastTarget = GetNearestEnemy();
        if (lastTarget == null) return;

        isSkill = true;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerSkill.Get(entityKey).Invoke(this, null);

        EasyFlipLock(data.Get<float>("retreatTime", 0.15f), lastTarget);

        Vector2 lungeDir = ((Vector2)lastTarget.position - (Vector2)transform.position).normalized;
        daelController.MoveSnap = lungeDir;
        daelController.MoveTo(
            data.Get<float>("speed", 600f),
            data.Get<float>("retreatTime", 0.15f)
        );

        isLunging = true;
        lungeHitObjects.Clear();
        Use("Lunge");
        SetInputDelay(data.Get<float>("inputDelay", 0.2f));
    }

    // ═══════════════════════════════════════════════════════════════
    //  BURST
    // ═══════════════════════════════════════════════════════════════

    protected override void TapBurst()
    {
        if (!canInput) return;

        if (isBurstActive)
        {
            DoFallingSwordBurstEnd();
            return;
        }

        if (isAttack || isSkill || isUlti || isDash || isBurst) return;

        var burstGauge = GetCounter("BurstGauge");
        if (burstGauge == null) return;

        int threshold = GetSkillData("Burst")?.Get<int>("burstActivateThreshold", 150) ?? 150;
        if (burstGauge.CurCounter < threshold) return;

        burstGauge.Use(threshold);
        ActivateBurst();
    }

    private void DoFallingSwordUltiBurst()
    {
        if (!IsReady("FallingSwordUltiBurst")) return;

        var data = GetSkillData("FallingSwordUltiBurst");
        if (data == null) return;

        int bladeEdgeCost = data.Get<int>("bladeEdgeCost", 50);
        if (!IsReady("BladeEdge", bladeEdgeCost)) return;

        var enemies = hitbox.detectObject(enemyLayer);
        if (enemies == null || enemies.Count == 0) return;

        Transform target = GetNearestEnemy();
        if (target == null) return;

        isUlti = true;
        canInput = false;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerUlti.Get(entityKey).Invoke(this, null);

        GetCounter("BladeEdge")?.Use(bladeEdgeCost);

        float spawnHeight = data.Get<float>("swordSpawnHeight", 4f);
        Vector3 spawnPos  = target.position + new Vector3(0f, spawnHeight, 0f);

        var go    = poolFallingSword.GetGameObject();
        var sword = go.GetComponent<DaelFallingSword>();
        if (sword == null) return;

        go.transform.position = spawnPos;
        go.transform.parent   = null;

        float dmg = daelStat.CaculateDamage(DamageType.Physical,
            data.damage[0] * daelStat.CurAttack / 100f);

        sword.SetUp(entityKey, DamageType.Physical,
            new List<float> { dmg },
            daelStat.CurCritRate, daelStat.CurCritDamage,
            data.Get<float>("iFrameDuration", 0.05f));
        sword.EasyModeChange(BulletMoveMode.Homing, target);
        go.SetActive(true);

        AddUltiAtkStack();
        isNextSlash3 = true;
        Use("FallingSwordUltiBurst", false);
        SetInputDelay(data.Get<float>("inputDelay", 0.3f));
    }

    private void DoFallingSwordBurstEnd()
    {
        var data = GetSkillData("FallingSwordBurstEnd");
        if (data == null) return;

        int bladeEdgeCost = data.Get<int>("bladeEdgeCost", 70);
        if (!IsReady("BladeEdge", bladeEdgeCost)) return;

        var enemies = hitbox.detectObject(enemyLayer);
        if (enemies == null || enemies.Count == 0) return;

        Transform target = GetNearestEnemy();
        if (target == null) return;

        isBurst = true;
        canInput = false;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerUlti.Get(entityKey).Invoke(this, null);

        GetCounter("BladeEdge")?.Use(bladeEdgeCost);

        float spawnHeight = data.Get<float>("swordSpawnHeight", 4f);
        float spreadX     = data.Get<float>("swordSpreadX", 1.5f);
        int   swordCount  = data.Get<int>("swordCount", 3);
        float dmg         = daelStat.CaculateDamage(DamageType.Physical,
                                data.damage[0] * daelStat.CurAttack / 100f);

        float startX = -(spreadX * (swordCount - 1) / 2f);
        for (int i = 0; i < swordCount; i++)
        {
            Vector3 spawnPos = target.position + new Vector3(startX + spreadX * i, spawnHeight, 0f);

            var go    = poolFallingSwordBurst.GetGameObject();
            var sword = go.GetComponent<DaelFallingSword>();
            if (sword == null) continue;

            go.transform.position = spawnPos;
            go.transform.parent   = null;

            sword.SetUp(entityKey, DamageType.Physical,
                new List<float> { dmg },
                daelStat.CurCritRate, daelStat.CurCritDamage,
                data.Get<float>("iFrameDuration", 0.05f));
            sword.EasyModeChange(BulletMoveMode.Homing, target);
            go.SetActive(true);
        }

        EndBurst();
        Use("FallingSwordBurstEnd");
        SetInputDelay(data.Get<float>("inputDelay", 0.3f));
    }

    // ═══════════════════════════════════════════════════════════════
    //  ULTI
    // ═══════════════════════════════════════════════════════════════

    protected override void TapUlti()
    {
        if (!canInput) return;
        if (isAttack || isSkill || isUlti || isDash) return;

        if (isBurstActive)
        {
            DoFallingSwordUltiBurst();
            return;
        }

        DoFallingSword();
    }

    private void DoFallingSword()
    {
        if (!IsReady("FallingSword")) return;

        var data = GetSkillData("FallingSword");
        if (data == null) return;

        // Check BladeEdge
        int bladeEdgeCost = data.Get<int>("bladeEdgeCost", 90);
        if (!IsReady("BladeEdge", bladeEdgeCost)) return;

        // Tìm target
        var enemies = hitbox.detectObject(enemyLayer);
        if (enemies == null || enemies.Count == 0) return;

        Transform target = GetNearestEnemy();
        if (target == null) return;

        isUlti = true;
        canInput = false;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerUlti.Get(entityKey).Invoke(this, null);

        // Trừ BladeEdge
        GetCounter("BladeEdge")?.Use(bladeEdgeCost);

        // Spawn sword
        float spawnHeight = data.Get<float>("swordSpawnHeight", 4f);
        Vector3 spawnPos  = target.position + new Vector3(0f, spawnHeight, 0f);

        var go    = poolFallingSword.GetGameObject();
        var sword = go.GetComponent<DaelFallingSword>();
        if (sword == null) return;

        go.transform.position = spawnPos;
        go.transform.parent   = null;

        float dmg = daelStat.CaculateDamage(DamageType.Physical,
            data.damage[0] * daelStat.CurAttack / 100f);

        sword.SetUp(entityKey, DamageType.Physical,
            new List<float> { dmg },
            daelStat.CurCritRate, daelStat.CurCritDamage,
            data.Get<float>("iFrameDuration", 0.05f));
        sword.EasyModeChange(BulletMoveMode.Homing, target);
        go.SetActive(true);

        AddUltiAtkStack();
        Use("FallingSword");
        SetInputDelay(data.Get<float>("inputDelay", 0.3f));
    }

    // ═══════════════════════════════════════════════════════════════
    //  DASH
    // ═══════════════════════════════════════════════════════════════

    protected override void TapDash()
    {
        if (!canInput) return;
        if (isAttack || isSkill || isUlti || isDash) return;
        DoStep();
    }

    private void DoStep()
    {
        if (!IsReady("Step")) return;

        var data = GetSkillData("Step");
        if (data == null) return;

        isDash = true;
        canInput = false;
        daelController.CanInput = false;
        EventManager.Player.OnPlayerDash.Get(entityKey).Invoke(this, null);

        daelController.MoveTo(
            data.Get<float>("speed", 500f),
            data.Get<float>("retreatTime", 0.15f)
        );

        Use("Step", false);
        SetInputDelay(data.Get<float>("inputDelay", 0.15f));
        StartCoroutine(DashPerfectWindow(data));
    }

    private System.Collections.IEnumerator DashPerfectWindow(SkillData data)
    {
        bool  isPerfect  = false;
        float elapsed    = 0f;
        float windowTime = 0.1f;

        while (!isPerfect && elapsed < windowTime)
        {
            elapsed += Time.unscaledDeltaTime;
            var hits = Physics2D.OverlapBoxAll(transform.position, perfectDashBoxSize, 0f, projectileLayer);
            foreach (var hit in hits)
            {
                if (hit.CompareTag("Enemy")) { isPerfect = true; break; }
            }
            yield return null;
        }

        GetCounter("BurstGauge")?.AddCounter(data.Get<int>("bgOnDash", 5));
        GetCounter("BladeEdge")?.AddCounter(data.Get<int>("beOnDash", 10));

        if (isPerfect)
        {
            float duration = data.Get<float>("timeStopDuration", 1.5f);
            ActivateTimeStop(duration);
            isNextSlash3 = true;
            GetCounter("BurstGauge")?.AddCounter(data.Get<int>("bgOnPerfect", 20));
            GetCounter("BladeEdge")?.AddCounter(data.Get<int>("beOnPerfect", 10));
        }
    }

    private void ActivateTimeStop(float duration)
    {
        EventManager.Time.OnSlowApply.Get().Invoke(this, SlowData.Create("timestop", 0f));

        var go   = poolTimeStopZone.GetGameObject();
        var zone = go.GetComponent<TimeStopZone>();
        go.transform.position = transform.position;
        go.transform.parent   = null;
        zone?.Activate(duration);
    }

    // ═══════════════════════════════════════════════════════════════
    //  GIZMOS
    // ═══════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawCube(transform.position, perfectDashBoxSize);
        Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
        Gizmos.DrawWireCube(transform.position, perfectDashBoxSize);
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private void SpawnSwing(SkillData data, List<float> damage, float centerAngle,
                            bool isSlash3, float rotX = 0f, float rotY = 0f,
                            string slashId = "", bool isSlash3Hit1 = false,
                            float atkSpeed = -1f, int swingDirection = 1,
                            EasyPoolingList pool = null, float totalArcOverride = -1f)
    {
        var usePool = pool ?? poolSwing;
        var go      = usePool.GetGameObject();
        var swing   = go.GetComponent<DaelSwingObject>();
        if (swing == null) return;

        go.transform.position = transform.position;
        go.transform.parent   = null;

        swing.SetUp(
            entityKey, DamageType.Physical, damage,
            daelStat.CurCritRate, daelStat.CurCritDamage,
            data.Get<float>("iFrameDuration", 0.05f),
            atkSpeed > 0f ? atkSpeed : daelStat.CurAttackSpeed,
            centerAngle, daelController.FlipDirect,
            rotX, rotY, isSlash3, isSlash3Hit1, slashId, swingDirection,
            totalArcOverride
        );

        go.SetActive(true);
    }

    private Transform GetNearestEnemy()
    {
        var enemies = hitbox.detectObject(enemyLayer);
        if (enemies == null || enemies.Count == 0) return null;
        float     minDist = float.MaxValue;
        Transform nearest = null;
        foreach (var e in enemies)
        {
            float d = Vector2.Distance(transform.position, e.transform.position);
            if (d < minDist) { minDist = d; nearest = e.transform; }
        }
        return nearest;
    }

    private float GetCenterAngle(Transform target)
    {
        if (target != null)
        {
            Vector2 dir = (target.position - transform.position).normalized;
            return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }
        return daelController.FlipDirect >= 0 ? 0f : 180f;
    }

    private IEnumerator SpawnSlash3Hit2()
    {
        yield return null;
        var slash3Data = GetSkillData("Slash3");
        if (slash3Data == null) yield break;

        float dmg2           = daelStat.CaculateDamage(DamageType.Physical,
                                   slash3Data.damage[1] * daelStat.CurAttack / 100f);
        float atkSpeedBonus  = slash3Data.Get<float>("slash3AtkSpeedBonus", 30f);
        float scaledAtkSpeed = daelStat.CurAttackSpeed * (1f + atkSpeedBonus / 100f);

        SpawnSwing(slash3Data, new List<float> { dmg2 },
                   GetCenterAngle(lastTarget) + 30f * daelController.FlipDirect,
                   true, slash3Hit2RotX, slash3Hit2RotY,
                   slashId: "Slash3", isSlash3Hit1: false,
                   atkSpeed: scaledAtkSpeed, swingDirection: -1);
    }

    private IEnumerator SpawnSlash3Hit2Burst()
    {
        yield return null;
        var slash3Data = GetSkillData("Slash3Burst");
        if (slash3Data == null) yield break;

        float dmg2           = daelStat.CaculateDamage(DamageType.Physical,
                                   slash3Data.damage[1] * daelStat.CurAttack / 100f);
        float atkSpeedBonus  = slash3Data.Get<float>("slash3AtkSpeedBonus", 30f);
        float scaledAtkSpeed = daelStat.CurAttackSpeed * (1f + atkSpeedBonus / 100f);

        SpawnSwing(slash3Data, new List<float> { dmg2 },
                   GetCenterAngle(lastTarget) + 30f * daelController.FlipDirect,
                   true, slash3Hit2RotX, slash3Hit2RotY,
                   slashId: "Slash3Burst", isSlash3Hit1: false,
                   atkSpeed: scaledAtkSpeed, swingDirection: -1,
                   pool: poolSlashBurst, totalArcOverride: 360f);
    }

    private void AdvanceCombo()
    {
        comboIndex++;
        if (comboIndex >= 3 || comboIndex < 0)
        {
            comboIndex = 0;
        }
        comboResetTimer = COMBO_RESET_TIME;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MOVETO CALLBACK
    // ═══════════════════════════════════════════════════════════════

    private void OnMoveToEnd(Component sender, object data)
    {
        if (!isSkill) return;

        var lungeData = GetSkillData("Lunge");
        if (lungeData == null) return;

        isLunging = false;
        lungeHitObjects.Clear();

        var lungeData2 = GetSkillData("Lunge");
        GetCounter("BurstGauge")?.AddCounter(lungeData2?.Get<int>("bgOnEnd", 5) ?? 5);
        GetCounter("BladeEdge")?.AddCounter(lungeData2?.Get<int>("beOnEnd", 10) ?? 10);

        daelController.CanInput = true;
        isNextSlash3 = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CALLBACKS
    // ═══════════════════════════════════════════════════════════════

    private void OnSlashEnd(Component sender, object data)
    {
        if (data is not DaelSlashData slashData) return;

        // BladeEdge
        var slashSkillData = GetSkillData(slashData.SlashId);
        int be1   = slashSkillData?.Get<int>("bladeEdgeHit1", 4)    ?? 4;
        int be2   = slashSkillData?.Get<int>("bladeEdgeHit2Plus", 6) ?? 6;
        int beAdd = slashData.HitCount >= 2 ? be2 : (slashData.HitCount >= 1 ? be1 : 0);
        if (beAdd > 0)
            GetCounter("BladeEdge")?.AddCounter(beAdd);

        // Slash3 hit1 end → spawn hit2
        if (slashData.IsSlash3Hit1)
        {
            if (slashData.SlashId == "Slash3Burst")
                StartCoroutine(SpawnSlash3Hit2Burst());
            else
                StartCoroutine(SpawnSlash3Hit2());
            return;
        }

        // Slash3Burst hit2 end
        if (slashData.IsSlash3 && slashData.SlashId == "Slash3Burst")
        {
            var slash3Data = GetSkillData("Slash3Burst");
            float heal = slash3Data?.Get<float>("healOnEnd", 1200f) ?? 1200f;
            daelStat.CurHealth = Mathf.Min(daelStat.CurHealth + heal, daelStat.MaxHealth);

            int bgAdd = slash3Data?.Get<int>("bgOnHit", 10) ?? 10;
            GetCounter("BurstGauge")?.AddCounter(bgAdd);

            float atkSpeedBonus  = slash3Data?.Get<float>("slash3AtkSpeedBonus", 30f) ?? 30f;
            float scaledAtkSpeed = daelStat.CurAttackSpeed * (1f + atkSpeedBonus / 100f);
            float rawDelay       = slash3Data?.Get<float>("inputDelay", 0.2f) ?? 0.2f;
            SetInputDelay(rawDelay / (scaledAtkSpeed / 100f));
            daelController.CanInput = true;
            return;
        }

        // Slash3 hit2 end → heal + BurstGauge + inputDelay
        if (slashData.IsSlash3)
        {
            var slash3Data = GetSkillData("Slash3");
            float heal = slash3Data?.Get<float>("healOnEnd", 1200f) ?? 1200f;
            daelStat.CurHealth = Mathf.Min(daelStat.CurHealth + heal, daelStat.MaxHealth);

            int bgAdd = slash3Data?.Get<int>("bgOnHit", 10) ?? 10;
            GetCounter("BurstGauge")?.AddCounter(bgAdd);
            GetCounter("BladeEdge")?.AddCounter(slash3Data?.Get<int>("beOnEnd", 20) ?? 20);

            float atkSpeedBonus  = slash3Data?.Get<float>("slash3AtkSpeedBonus", 30f) ?? 30f;
            float scaledAtkSpeed = daelStat.CurAttackSpeed * (1f + atkSpeedBonus / 100f);
            float rawDelay       = slash3Data?.Get<float>("inputDelay", 0.2f) ?? 0.2f;
            SetInputDelay(rawDelay / (scaledAtkSpeed / 100f));
            daelController.CanInput = true;
            return;
        }

        // Slash1/2 end → inputDelay + restore input
        if (!string.IsNullOrEmpty(slashData.SlashId))
        {
            var sd    = GetSkillData(slashData.SlashId);
            float delay = sd?.Get<float>("inputDelay", 0.15f) ?? 0.15f;
            SetInputDelay(delay / (daelStat.CurAttackSpeed / 100f));
            daelController.CanInput = true;
        }
    }
}