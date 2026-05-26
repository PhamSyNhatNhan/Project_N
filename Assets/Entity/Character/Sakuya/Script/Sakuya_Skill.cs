using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SakuyaSkill : SubPlayerSkill
{
    // ── Components ────────────────────────────────────────────────
    private Sakuya           sakuyaStat;
    private SakuyaController sakuyaController;
    private Hitbox           hitbox;

    // ── Prefabs ───────────────────────────────────────────────────
    [Header("Prefabs")]
    [SerializeField] private GameObject prefab_Knife;
    [SerializeField] private GameObject prefab_KnifeSmall;
    [SerializeField] private GameObject prefab_KnifeFast;
    [SerializeField] private GameObject prefab_KnifeBroke;
    [SerializeField] private GameObject prefab_SilverKnife;
    [SerializeField] private GameObject prefab_Dagger;
    [SerializeField] private GameObject prefab_DaggerLight;
    [SerializeField] private GameObject prefab_TimeStopZone;
    [SerializeField] private GameObject prefab_MirrorShard;
    [SerializeField] private GameObject prefab_MirrorShardEx;
    [SerializeField] private GameObject prefab_KnifeEx;

    // ── Burst Zone ────────────────────────────────────────────────
    private TimeStopZone burstZone;

    // ── Burst Config ──────────────────────────────────────────────
    [Header("Burst - Spiral")]
    [SerializeField] private float spiralBaseRadius = 3f;
    [SerializeField] private float spiralRadiusStep = 0.3f;
    [SerializeField] private float spiralAngleStep  = 30f;

    // ── SilverAmbush Config ───────────────────────────────────────
    [Header("Clock12")]
    [SerializeField] private float daggerSpawnHeight = 3f;

    // ── Clock3_30 Config ──────────────────────────────────────────
    [Header("Clock7")]
    [SerializeField] private float parallelOffset = 0.3f;

    [Header("Clock10")]
    [SerializeField] private float clock10Radius = 1f;

    [Header("Clock11_5")]
    [SerializeField] private float mirrorShardOffsetX = 0.5f;

    // ── Ulti Config ───────────────────────────────────────────────
    [Header("UltiL2")]
    [SerializeField] private float orbitBaseRadius = 2f;

    [Header("UltiL3")]
    [SerializeField] private float daggerLightRadius = 4f;

    // ── Ulti State ────────────────────────────────────────────────
    private int   ultiComboIndex  = 0;
    private float ultiResetTimer  = 0f;
    private const float ULTI_RESET_TIME = 8f;

    private float clock4Timer = 0f;
    private float clock9Timer = 0f;
    private List<SakuyaOrbitKnife> orbitKnives = new List<SakuyaOrbitKnife>();

    // ── Clock6 Timer ──────────────────────────────────────────────
    private float clock6Timer = 0f;

    // ── Perfect Dash ──────────────────────────────────────────────
    [Header("Perfect Dash")]
    [SerializeField] private Vector2 perfectDashBoxSize = new Vector2(2f, 2f);
    [SerializeField] private LayerMask projectileLayer;
    [SerializeField] private float    perfectSlowScale  = 0.5f;

    private float timeStopTimer = 0f;


    // ── Object Pools ──────────────────────────────────────────────
    private EasyPoolingList poolKnife        = new EasyPoolingList();
    private EasyPoolingList poolKnifeSmall   = new EasyPoolingList();
    private EasyPoolingList poolKnifeFast    = new EasyPoolingList();
    private EasyPoolingList poolKnifeBroke   = new EasyPoolingList();
    private EasyPoolingList poolSilverKnife  = new EasyPoolingList();
    private EasyPoolingList poolDagger       = new EasyPoolingList();
    private EasyPoolingList poolDaggerLight  = new EasyPoolingList();
    private EasyPoolingList poolTimeStopZone  = new EasyPoolingList();
    private EasyPoolingList poolMirrorShard   = new EasyPoolingList();
    private EasyPoolingList poolMirrorShardEx = new EasyPoolingList();
    private EasyPoolingList poolKnifeEx       = new EasyPoolingList();

    // ── Burst State ───────────────────────────────────────────────
    private bool  isBurstActive    = false;
    private float burstGaugeDrain  = 0f;
    private float burstElapsed     = 0f;
    private float clock1Timer      = 0f;
    private int   spiralCount      = 0;
    private int   clockConsumeCount = 0;
    private int   clock8Charges    = 0;
    private Vector3 burstOrigin    = Vector3.zero;
    private List<SakuyaKnifeEx> spiralKnives = new List<SakuyaKnifeEx>();

    // ── Combo ─────────────────────────────────────────────────────
    private int   comboIndex      = 0;
    private float comboResetTimer = 0f;
    private const float COMBO_RESET_TIME = 5f;

    // ── Entity Key ────────────────────────────────────────────────
    private string entityKey => sakuyaStat != null
        ? $"{sakuyaStat.NameCharacter}_{sakuyaStat.GetInstanceID()}"
        : gameObject.name;

    // ═══════════════════════════════════════════════════════════════
    //  SETUP
    // ═══════════════════════════════════════════════════════════════

    protected override void AwakeSetUp()
    {
        sakuyaStat       = GetComponent<Sakuya>();
        sakuyaController = GetComponent<SakuyaController>();
        hitbox           = GetComponent<Hitbox>();

        var go = Instantiate(prefab_TimeStopZone, transform);
        go.SetActive(false);
        burstZone = go.GetComponent<TimeStopZone>();
    }

    protected override void StartSetUp()
    {
        poolKnife.SetPrefab(prefab_Knife);
        poolKnifeSmall.SetPrefab(prefab_KnifeSmall);
        poolKnifeFast.SetPrefab(prefab_KnifeFast);
        poolKnifeBroke.SetPrefab(prefab_KnifeBroke);
        poolSilverKnife.SetPrefab(prefab_SilverKnife);
        poolDagger.SetPrefab(prefab_Dagger);
        poolDaggerLight.SetPrefab(prefab_DaggerLight);
        poolTimeStopZone.SetPrefab(prefab_TimeStopZone);
        poolMirrorShard.SetPrefab(prefab_MirrorShard);
        poolMirrorShardEx.SetPrefab(prefab_MirrorShardEx);
        poolKnifeEx.SetPrefab(prefab_KnifeEx);

        EventManager.Projectile.OnProjectileHit
            .Get(entityKey + "_mirror")
            .AddListener(OnMirrorHit);

        EventManager.Projectile.OnProjectileHit
            .Get(entityKey)
            .AddListener(OnKnifeHit);

        EventManager.Entity.OnEntityMoveToEnd
            .Get(entityKey)
            .AddListener(OnMoveToEnd);

        EasyAttackRepeat(0.4f, 0.2f);

    }

    private void OnDisable()
    {
        EventManager.Projectile.OnProjectileHit
            .Get(entityKey)
            .RemoveListener(OnKnifeHit);

        EventManager.Projectile.OnProjectileHit
            .Get(entityKey + "_mirror")
            .RemoveListener(OnMirrorHit);

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
        TickTimeStop();
        TickClock6();
        TickClock4();
        TickClock9();
        TickUltiReset();
        TickBurst();
    }

    private void TickTimeStop()
    {
        if (timeStopTimer <= 0f) return;

        timeStopTimer -= Time.unscaledDeltaTime;
        if (timeStopTimer <= 0f)
        {
            timeStopTimer = 0f;
            if (!isBurstActive)
                EventManager.Time.OnSlowRemove.Get().Invoke(this, SlowData.Remove("timestop"));
        }
    }

    private void ActivateClock6()
    {
        var data = GetSkillData("Clock6");
        float duration = data?.Get<float>("clock6Duration", 5f) ?? 5f;
        clock6Timer = duration; // reset nếu đang active
        GetInfinite("Clock6")?.Activate();
    }

    private void ActivateClock4()
    {
        var data = GetSkillData("Clock4");
        if (data == null) return;
        clock4Timer = data.Get<float>("clock4Duration", 6f);
        GetInfinite("Clock4")?.Activate();
        sakuyaStat.BuffAttackSpeed.SetMultiplier("clock4", data.Get<float>("clock4AtkSpeedBonus", 50f));
        sakuyaStat.RecalculateStat();
    }

    private void ReleaseOrbit()
    {
        foreach (var knife in orbitKnives)
            if (knife != null && knife.gameObject.activeSelf)
                knife.ReleaseOrbit();
        orbitKnives.Clear();
        GetInfinite("Clock9")?.Deactivate();
        clock9Timer = 0f;
    }

    // ── Ulti ──────────────────────────────────────────────────────
    protected override void TapUlti()
    {
        if (!canInput) return;
        if (isAttack || isSkill || isUlti || isDash) return;

        switch (ultiComboIndex)
        {
            case 0: DoUltiL1(); break;
            case 1: DoUltiL2(); break;
            case 2: DoUltiL3(); break;
        }
    }

    private void DoUltiL1()
    {
        var data = GetSkillData("UltiL1");
        if (data == null) return;
        if (!SpendPWS(data.Get<int>("pwsCost", 40))) return;

        isUlti = true;
        EventManager.Player.OnPlayerUlti.Get(entityKey).Invoke(this, null);

        sakuyaController.CanInput = false;

        float speed       = data.Get<float>("speed", 600f);
        float retreatTime = data.Get<float>("retreatTime", 0.15f);
        sakuyaController.MoveTo(speed, retreatTime);

        ActivateTimeStop(data.Get<float>("timeStopDuration", 0.5f));
        StartCoroutine(SpawnRainKnives(data, retreatTime));

        ActivateClock4();
        GetInfinite("Clock5")?.Activate();
        if (isBurstActive)
            AddSpiralKnife(GetSkillData("Burst")?.Get<int>("spiralUltiAdd", 5) ?? 5);
        SetInputDelay(data.Get<float>("inputDelay", 0.3f));
        AdvanceUltiCombo();
    }

    private IEnumerator SpawnRainKnives(SkillData data, float duration)
    {
        int   knifeCount = 8;
        float interval   = duration / knifeCount;
        float finalDmg   = ApplyDamageBuffs(sakuyaStat.CaculateDamage(
            DamageType.Physical, data.damage[0] * sakuyaStat.BaseAttack / 100f));

        for (int i = 0; i < knifeCount; i++)
        {
            // Lấy position thực tế tại thời điểm spawn
            Vector3 spawnPos = transform.position + new Vector3(0f, daggerSpawnHeight, 0f);

            var knife = SpawnKnife(poolKnifeBroke, spawnPos);
            SetUpKnife(knife, data, new List<float> { finalDmg },
                data.Get<int>("pwsOnHit", 4), BulletMoveMode.Angle);
            knife.GetComponent<SakuyaKnife>().SetAngle(270f);

            yield return new WaitForSeconds(interval);
        }
    }

    private void DoUltiL2()
    {
        var data = GetSkillData("UltiL2");
        if (data == null) return;
        if (!SpendPWS(data.Get<int>("pwsCost", 50))) return;

        isUlti = true;
        EventManager.Player.OnPlayerUlti.Get(entityKey).Invoke(this, null);

        int   count    = data.Get<int>("orbitCount", 7);
        float radius   = orbitBaseRadius;
        float finalDmg = ApplyDamageBuffs(sakuyaStat.CaculateDamage(
            DamageType.Physical, data.damage[0] * sakuyaStat.BaseAttack / 100f));

        for (int i = 0; i < count; i++)
        {
            float startAngle = 360f / count * i;
            var   go         = poolSilverKnife.GetGameObject();
            var   orbit      = go.GetComponent<SakuyaOrbitKnife>();
            if (orbit == null) continue;

            go.transform.position = transform.position;
            go.transform.parent   = null;
            orbit.SetUp(entityKey, DamageType.Physical,
                new List<float> { finalDmg },
                sakuyaStat.CurCritRate, sakuyaStat.CurCritDamage,
                data.Get<float>("iFrameDuration", 0.05f),
                data.Get<int>("pwsOnHit", 4));
            orbit.EnableDamage = enemyLayer;
            orbit.SetupOrbit(transform, radius, startAngle);
            go.SetActive(true);
            orbitKnives.Add(orbit);
        }

        var clock9Data = GetSkillData("Clock9");
        clock9Timer = clock9Data?.Get<float>("clock9Duration", 4f) ?? 4f;
        GetInfinite("Clock9")?.Activate();

        SetInputDelay(data.Get<float>("inputDelay", 0.1f));
        AdvanceUltiCombo();
    }

    private void DoUltiL3()
    {
        var data = GetSkillData("UltiL3");
        if (data == null) return;
        if (!SpendPWS(data.Get<int>("pwsCost", 60))) return;

        isUlti = true;
        EventManager.Player.OnPlayerUlti.Get(entityKey).Invoke(this, null);
        sakuyaController.CanInput = false;

        var enemies = hitbox.detectObject(enemyLayer);
        Transform target = enemies != null && enemies.Count > 0 ? enemies[0].transform : null;
        if (target != null) sakuyaController.Flipping(target);

        float speed       = data.Get<float>("speed", 600f);
        float retreatTime = data.Get<float>("retreatTime", 0.15f);
        sakuyaController.MoveTo(speed, retreatTime);
        StartCoroutine(SpawnRainKnives(data, retreatTime));

        GetInfinite("Clock11")?.Activate();

        SetInputDelay(data.Get<float>("inputDelay", 0.3f));
        ultiComboIndex = 0;
        ultiResetTimer = 0f;
    }

    private void AdvanceUltiCombo()
    {
        ultiComboIndex = (ultiComboIndex + 1) % 3;
        ultiResetTimer = ULTI_RESET_TIME;
    }

    private void ActivateTimeStop(float duration)
    {
        timeStopTimer = Mathf.Max(timeStopTimer, duration);
        EventManager.Time.OnSlowApply.Get().Invoke(this, SlowData.Create("timestop", 0f));

        var go   = poolTimeStopZone.GetGameObject();
        var zone = go.GetComponent<TimeStopZone>();
        go.transform.position = transform.position;
        go.transform.parent   = null;
        zone?.Activate(duration);
    }

    private void TickComboReset()
    {
        if (comboIndex == 0) return;
        comboResetTimer -= Time.deltaTime;
        if (comboResetTimer <= 0f)
            comboIndex = 0;
    }

    private void TickClock6()
    {
        if (clock6Timer <= 0f) return;
        clock6Timer -= Time.deltaTime;
        if (clock6Timer <= 0f)
        {
            clock6Timer = 0f;
            GetInfinite("Clock6")?.Deactivate();
        }
    }

    private void TickClock4()
    {
        if (clock4Timer <= 0f) return;
        clock4Timer -= Time.deltaTime;
        if (clock4Timer <= 0f)
        {
            clock4Timer = 0f;
            GetInfinite("Clock4")?.Deactivate();
            sakuyaStat.BuffAttackSpeed.RemoveMultiplier("clock4");
            sakuyaStat.RecalculateStat();
        }
    }

    private void TickClock9()
    {
        if (clock9Timer <= 0f) return;
        clock9Timer -= Time.deltaTime;
        if (clock9Timer <= 0f)
        {
            clock9Timer = 0f;
            ReleaseOrbit();
        }
    }

    private void TickUltiReset()
    {
        if (ultiComboIndex == 0) return;
        ultiResetTimer -= Time.deltaTime;
        if (ultiResetTimer <= 0f)
            ultiComboIndex = 0;
    }

    // ── Burst ─────────────────────────────────────────────────────
    private void TickBurst()
    {
        if (!isBurstActive) return;

        burstElapsed += Time.unscaledDeltaTime;

        // Drain gauge
        var burstData = GetSkillData("Burst");
        float drainBase = burstData?.Get<float>("drainBase", 15f) ?? 15f;
        float drainRate = burstData?.Get<float>("drainPerSecond", 5f) ?? 5f;
        float drain = (drainBase + drainRate * burstElapsed) * Time.unscaledDeltaTime;
        GetCounter("BurstGauge")?.Use(Mathf.CeilToInt(drain));

        // Hết gauge → kết thúc Burst
        var burstGauge = GetCounter("BurstGauge");
        if (burstGauge == null || !burstGauge.IsReady)
        {
            EndBurst();
            return;
        }

        // Clock1 timer
        if (GetInfinite("Clock1")?.IsReady == true)
        {
            clock1Timer -= Time.unscaledDeltaTime;
            if (clock1Timer <= 0f)
            {
                var clock1Data = GetSkillData("Clock1");
                int add = clock1Data?.Get<int>("clock1Add", 5) ?? 5;
                clock1Timer = clock1Data?.Get<float>("clock1Interval", 2f) ?? 2f;
                for (int i = 0; i < add; i++) AddSpiralKnife();
            }
        }

        // Clock2 check
        var clock2Data = GetSkillData("Clock2");
        int threshold = clock2Data?.Get<int>("clock2Threshold", 28) ?? 28;
        if (spiralCount >= threshold && GetInfinite("Clock2")?.IsReady == false)
            GetInfinite("Clock2")?.Activate();
    }

    private void ActivateBurst()
    {
        isBurstActive  = true;
        EventManager.Player.OnPlayerBurst.Get(entityKey).Invoke(this, null);
        EventManager.Ui.OnBurstActive.Get().Invoke(this, new BurstActiveData
        {
            Name     = sakuyaStat.NameCharacter,
            IsActive = true
        });
        
        burstElapsed   = 0f;
        burstOrigin    = transform.position;
        spiralCount    = 0;
        spiralKnives.Clear();
        clockConsumeCount = 0;
        clock8Charges  = 0;

        var burstData  = GetSkillData("Burst");
        int initCount  = burstData?.Get<int>("spiralInitCount", 8) ?? 8;
        var clock1Data = GetSkillData("Clock1");
        clock1Timer    = clock1Data?.Get<float>("clock1Interval", 2f) ?? 2f;

        GetInfinite("Clock1")?.Activate();
        burstZone?.Activate(1000f);
        EventManager.Time.OnSlowApply.Get().Invoke(this, SlowData.Create("timestop", 0f));

        for (int i = 0; i < initCount; i++) AddSpiralKnife();
    }

    private void EndBurst()
    {
        isBurstActive = false;

        GetInfinite("Clock1")?.Deactivate();
        GetInfinite("Clock2")?.Deactivate();

        // Release tất cả spiral → homing
        var clock2Data    = GetSkillData("Clock2");
        float clock2Bonus = clock2Data?.Get<float>("clock2DmgBonus", 250f) ?? 250f;
        bool  hasClock2   = GetInfinite("Clock2")?.IsReady == true;

        foreach (var knife in spiralKnives)
        {
            if (knife == null || !knife.gameObject.activeSelf) continue;
            if (hasClock2) knife.SetClock2Bonus(clock2Bonus);
            knife.ReleaseSpiral();
        }
        spiralKnives.Clear();

        burstZone?.gameObject.SetActive(false);

        // Nếu không còn timestop nào khác đang chạy → tắt VFX
        if (timeStopTimer <= 0f)
            EventManager.Time.OnSlowRemove.Get().Invoke(this, SlowData.Remove("timestop"));
        EventManager.Ui.OnBurstActive.Get().Invoke(this, new BurstActiveData
        {
            Name     = sakuyaStat.NameCharacter,
            IsActive = false
        });
    }

    private void AddSpiralKnife(int count = 1)
    {
        var burstData = GetSkillData("Burst");
        var knifeData = GetSkillData("Attack1"); // dùng damage Attack1

        float finalDmg = ApplyDamageBuffs(sakuyaStat.CaculateDamage(
            DamageType.Physical, knifeData?.damage[0] * sakuyaStat.BaseAttack / 100f ?? 0f));

        for (int i = 0; i < count; i++)
        {
            int     idx    = spiralCount;
            float   radius = spiralBaseRadius + spiralRadiusStep * idx;
            float   angle  = spiralAngleStep  * idx;
            float   rad    = angle * Mathf.Deg2Rad;
            Vector3 pos    = burstOrigin + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;

            var go    = poolKnifeEx.GetGameObject();
            var knife = go.GetComponent<SakuyaKnifeEx>();
            if (knife == null) { spiralCount++; continue; }

            go.transform.position = pos;
            go.transform.parent   = null;

            // Hướng vào tâm (burstOrigin)
            Vector2 dir = ((Vector2)burstOrigin - (Vector2)pos).normalized;
            float   a   = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            go.transform.rotation = Quaternion.Euler(0f, 0f, a);

            knife.SetUp(entityKey, DamageType.Physical,
                new List<float> { finalDmg },
                sakuyaStat.CurCritRate, sakuyaStat.CurCritDamage,
                0.05f, 0); // pwsOnHit = 0 trong Burst
            knife.EnableDamage = enemyLayer;
            knife.SetSpiral(true);
            go.SetActive(true);

            spiralKnives.Add(knife);
            spiralCount++;
        }
    }

    /// <summary>Gọi mỗi khi 1 Clock state bị tiêu hao — tính Clock8.</summary>
    private void OnClockConsumed()
    {
        if (!isBurstActive) return; // Clock8 chỉ tích khi không trong Burst
        var clock8Data = GetSkillData("Clock8");
        int threshold  = clock8Data?.Get<int>("clock8Threshold", 3) ?? 3;

        clockConsumeCount++;
        if (clockConsumeCount >= threshold)
        {
            clockConsumeCount = 0;
            clock8Charges++;
            if (clock8Charges >= (clock8Data?.Get<int>("clock8Charges", 3) ?? 3))
            {
                clock8Charges = 0;
                GetInfinite("Clock8")?.Activate();
            }
        }
    }

    // ── TapBurst ──────────────────────────────────────────────────
    protected override void TapBurst()
    {
        if (isBurstActive) return; // Đang Burst không thể kích hoạt lại

        var burstGauge = GetCounter("BurstGauge");
        if (burstGauge == null) return;

        int threshold = GetSkillData("Burst")?.Get<int>("burstActivateThreshold", 170) ?? 170;
        if (burstGauge.CurCounter < threshold) return;

        ActivateBurst();
    }

    // ═══════════════════════════════════════════════════════════════
    //  CALLBACKS
    // ═══════════════════════════════════════════════════════════════

    private void OnKnifeHit(Component sender, object data)
    {
        if (data is not SakuyaHitData hitData) return;
        GetCounter("PocketWatchShard")?.AddCounter(hitData.PwsAmount);
    }

    private void OnMirrorHit(Component sender, object data)
    {
        var mirrorData = GetSkillData("Clock11_5");
        if (mirrorData == null) return;
        ActivateTimeStop(mirrorData.Get<float>("timeStopDuration", 1.5f));
    }

    private void DoMirrorShard()
    {
        
        
        var data = GetSkillData("Clock11_5");
        if (data == null) return;
        
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        var enemies = hitbox.detectObject(enemyLayer);
        Transform target  = enemies != null && enemies.Count > 0 ? enemies[0].transform : null;
        Vector3   basePos = target != null ? target.position : transform.position;

        float finalDamage = sakuyaStat.CaculateDamage(
            DamageType.Physical, data.damage[0] * sakuyaStat.BaseAttack / 100f);
        int pwsOnHit = data.Get<int>("pwsOnHit", 4);

        // Trái: -15° so với trục Y
        SpawnMirrorShard(poolMirrorShard,   basePos, target, data, finalDamage, pwsOnHit, false, -15f);
        // Phải: +30° so với trục Y
        SpawnMirrorShard(poolMirrorShard,   basePos, target, data, finalDamage, pwsOnHit, false, 30f);
        // Giữa Ex: 0° thẳng lên
        SpawnMirrorShard(poolMirrorShardEx, basePos, target, data, finalDamage, pwsOnHit, true,  0f);

        GetInfinite("Clock11_5")?.Use();
        UseWithAtkSpeed("Clock11_5", sakuyaStat.CurAttackSpeed);
    }

    private void SpawnMirrorShard(EasyPoolingList pool, Vector3 basePos, Transform target,
                                   SkillData data, float damage, int pwsOnHit, bool isEx, float angleFromY)
    {
        var go    = pool.GetGameObject();
        var knife = go.GetComponent<SakuyaKnife>();
        if (knife == null) return;

        // Tính offset theo góc so với trục Y
        float rad      = angleFromY * Mathf.Deg2Rad;
        float spawnDist = daggerSpawnHeight;
        Vector3 offset = new Vector3(Mathf.Sin(rad), Mathf.Cos(rad), 0f) * spawnDist;

        go.transform.position = basePos + offset;
        go.transform.rotation = Quaternion.identity;
        go.transform.parent   = null;

        string channel = isEx ? entityKey + "_mirror" : entityKey;
        knife.SetUp(channel, DamageType.Physical,
            new List<float> { damage },
            sakuyaStat.CurCritRate, sakuyaStat.CurCritDamage,
            data.Get<float>("iFrameDuration", 0.05f), pwsOnHit);
        knife.EasyModeChange(BulletMoveMode.Homing);
        if (target != null) knife.SetTarget(target);
        go.SetActive(true);
    }

    private void OnMoveToEnd(Component sender, object data)
    {
        sakuyaController.CanInput = true;
        sakuyaStat.CanDamge       = true;
    }


    // ── Dash ──────────────────────────────────────────────────────
    protected override void TapDash()
    {
        if (!canInput) return;
        if (isAttack || isSkill || isUlti || isDash) return;

        var data = GetSkillData("Dash");
        if (data == null) return;

        int pwsCost = data.Get<int>("pwsCost", 24);
        if (!SpendPWS(pwsCost)) return;

        float speed       = data.Get<float>("speed", 500f);
        float retreatTime = data.Get<float>("retreatTime", 0.15f);

        isDash = true;
        EventManager.Player.OnPlayerDash.Get(entityKey).Invoke(this, null);

        sakuyaController.CanInput = false;
        sakuyaStat.CanDamge = false;
        sakuyaController.MoveTo(speed, retreatTime);
        SetInputDelay(data.Get<float>("inputDelay", 0.15f));

        StartCoroutine(DashPerfectWindow(data));
    }

    private IEnumerator DashPerfectWindow(SkillData data)
    {
        bool  isPerfect  = isBurstActive; // Trong Burst luôn là Perfect
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

        if (isPerfect)
        {
            int refund = data.Get<int>("pwsPerfectRefund", 12);
            GetCounter("PocketWatchShard")?.AddCounter(refund);
            GetInfinite("Clock7")?.Activate();
            ActivateTimeStop(data.Get<float>("timeStopDuration", 2f));
        }
        else
        {
            GetInfinite("Clock3")?.Activate();
        }
    }



    // ═══════════════════════════════════════════════════════════════
    //  ATTACK
    // ═══════════════════════════════════════════════════════════════

    protected override void TapAttack()
    {
        if (!canInput) return;
        if (isAttack || isSkill || isUlti || isDash) return;

        // Trong Burst: thêm dao vào spiral khi attack
        if (isBurstActive)
        {
            var burstData = GetSkillData("Burst");
            AddSpiralKnife(burstData?.Get<int>("spiralAtkAdd", 1) ?? 1);
        }

        // ── Check & fire tất cả trạng thái Clock trước ────────────
        // Clock11_5 thay thế hoàn toàn attack
        var clock11_5 = GetInfinite("Clock11_5");
        if (clock11_5 != null && clock11_5.IsReady)
        {
            DoMirrorShard();
            return;
        }

        var clock12   = GetInfinite("Clock12");
        var clock3    = GetInfinite("Clock3");
        var clock7    = GetInfinite("Clock7");
        var clock10   = GetInfinite("Clock10");

        if (clock12 != null && clock12.IsReady)  DoClockBonus("Clock12");
        if (clock3  != null && clock3.IsReady)   DoClockBonus("Clock3");
        if (clock7  != null && clock7.IsReady)   DoClockBonus("Clock7");
        if (clock10 != null && clock10.IsReady)  DoClockBonus("Clock10");

        // Clock9 tiêu → orbit homing
        var clock9 = GetInfinite("Clock9");
        if (clock9 != null && clock9.IsReady) ReleaseOrbit();

        // Clock5, Clock11 bonus
        var clock5  = GetInfinite("Clock5");
        var clock11 = GetInfinite("Clock11");
        if (clock5  != null && clock5.IsReady)  DoClockBonus("Clock5");
        if (clock11 != null && clock11.IsReady) DoClockBonus("Clock11");
        // Clock9, Clock11, Clock11_30 sẽ implement khi làm Ulti

        // ── Attack combo bình thường ──────────────────────────────
        switch (comboIndex)
        {
            case 0: DoAttack1(); break;
            case 1: DoAttack2(); break;
            case 2: DoAttack3(); break;
            case 3: DoAttack4(); break;
        }
    }

    // ── Clock Bonus — spawn bonus theo từng trạng thái ────────────
    private void DoClockBonus(string clockId)
    {
        var data = GetSkillData(clockId);
        if (data == null) return;

        var enemies = hitbox.detectObject(enemyLayer);
        Transform target = enemies != null && enemies.Count > 0 ? enemies[0].transform : null;

        float finalDamage = data.damage != null && data.damage.Count > 0 && data.damage[0] > 0
            ? sakuyaStat.CaculateDamage(DamageType.Physical, data.damage[0] * sakuyaStat.BaseAttack / 100f)
            : 0f;

        switch (clockId)
        {
            case "Clock12": // 3 dagger từ trên xuống
            {
                Vector3 targetPos = target != null ? target.position : transform.position;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 spawnOffset = new Vector3(Random.Range(-0.5f, 0.5f), daggerSpawnHeight, 0f);
                    var dagger = SpawnKnife(poolDagger, targetPos + spawnOffset);
                    SetUpKnife(dagger, data, new List<float> { finalDamage },
                        data.Get<int>("pwsOnHit", 4), BulletMoveMode.Target);
                    dagger.GetComponent<SakuyaKnife>().SetTarget(target);
                }
                break;
            }
            case "Clock5": // 3 Dagger phía sau player homing về địch
            {
                Vector2 dir      = target != null
                    ? ((Vector2)target.position - (Vector2)transform.position).normalized
                    : new Vector2(sakuyaController.FlipDirect, 0f);
                Vector2 back     = -dir;
                Vector2 perp     = Vector2.Perpendicular(dir);
                float   backDist = 1f;
                float   sideDist = 0.5f;

                Vector3[] spawnOffsets = {
                    (Vector3)(back * backDist),
                    (Vector3)(back * backDist + perp * sideDist),
                    (Vector3)(back * backDist - perp * sideDist)
                };

                foreach (var offset in spawnOffsets)
                {
                    var dagger = SpawnKnife(poolDagger, transform.position + offset);
                    SetUpKnife(dagger, data, new List<float> { finalDamage },
                        data.Get<int>("pwsOnHit", 4), BulletMoveMode.Homing);
                    if (target != null)
                        dagger.GetComponent<SakuyaKnife>().SetTarget(target);
                }
                GetInfinite("Clock5")?.Use();
                break;
            }
            case "Clock3": // 1 dao nhỏ lệch phải 30° homing
            {
                Vector2 dir       = target != null
                    ? ((Vector2)target.position - (Vector2)transform.position).normalized
                    : new Vector2(sakuyaController.FlipDirect, 0f);
                float   baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                Vector2 perpRight = Vector2.Perpendicular(-dir); // sang phải
                Vector3 spawnPos  = transform.position + (Vector3)(perpRight * 0.5f);

                var small = SpawnKnife(poolKnifeSmall, spawnPos);
                SetUpKnife(small, data, new List<float> { finalDamage },
                    data.Get<int>("pwsOnHit", 3), BulletMoveMode.Homing);
                small.GetComponent<SakuyaKnife>().SetAngle(baseAngle + 30f);
                if (target != null)
                    small.GetComponent<SakuyaKnife>().SetTarget(target);
                break;
            }
            case "Clock7": // dao thẳng song song tốc độ cao
            {
                Vector2 dir = target != null
                    ? ((Vector2)target.position - (Vector2)transform.position).normalized
                    : new Vector2(sakuyaController.FlipDirect, 0f);
                float baseAngle    = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                Vector2 perpDir    = Vector2.Perpendicular(dir);
                int     knifeCount = data.Get<int>("knifeCount", 3);
                float   startOff   = -parallelOffset * (knifeCount - 1) / 2f;

                for (int i = 0; i < knifeCount; i++)
                {
                    Vector3 offset = (Vector3)(perpDir * (startOff + parallelOffset * i));
                    var     knife  = SpawnKnife(poolKnifeFast, transform.position + offset);
                    SetUpKnife(knife, data, new List<float> { finalDamage },
                        data.Get<int>("pwsOnHit", 4), BulletMoveMode.Angle);
                    knife.GetComponent<SakuyaKnife>().SetAngle(baseAngle);
                }
                break;
            }
            case "Clock11": // 3 DaggerLight từ random bán kính quanh địch → Target
            {
                Vector3 targetPos = target != null ? target.position : transform.position;
                for (int i = 0; i < 3; i++)
                {
                    float   randomAngle = Random.Range(0f, 360f);
                    float   rad         = randomAngle * Mathf.Deg2Rad;
                    Vector3 spawnPos    = targetPos + new Vector3(
                        Mathf.Cos(rad), Mathf.Sin(rad), 0f) * daggerLightRadius;
                    spawnPos.y += daggerSpawnHeight;

                    var dagger = SpawnKnife(poolDaggerLight, spawnPos);
                    SetUpKnife(dagger, data, new List<float> { finalDamage },
                        data.Get<int>("pwsOnHit", 0), BulletMoveMode.Target);
                    if (target != null)
                        dagger.GetComponent<SakuyaKnife>().SetTarget(target);
                }
                GetInfinite("Clock11")?.Use();
                GetInfinite("Clock11_5")?.Activate();
                break;
            }
            /*
            {
                Vector3 targetPos = target != null ? target.position : transform.position;
                int     count     = 5;

                for (int i = 0; i < count; i++)
                {
                    float   angle  = 360f / count * i;
                    float   rad    = angle * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * clock10Radius;
                    var     knife  = SpawnKnife(poolKnifeFast, targetPos + offset);
                    SetUpKnife(knife, data, new List<float> { finalDamage },
                        data.Get<int>("pwsOnHit", 4), BulletMoveMode.Homing);
                    if (target != null)
                        knife.GetComponent<SakuyaKnife>().SetTarget(target);
                }
                break;
            }
            */
        }

        GetInfinite(clockId)?.Use();
        OnClockConsumed();
    }

    // ── Skill Tap — 7 dao homing 180° arc + Clock6 ───────────────
    protected override void TapSkill()
    {
        if (!canInput) return;
        if (isAttack || isSkill || isUlti || isDash) return;
        
        var data = GetSkillData("SkillTap");
        if (data == null) return;

        int pwsCost = data.Get<int>("pwsCost", 30);
        if (!SpendPWS(pwsCost)) return;

        isSkill = true;
        EventManager.Player.OnPlayerSkill.Get(entityKey).Invoke(this, null);

        var enemies = hitbox.detectObject(enemyLayer);
        Transform target = enemies != null && enemies.Count > 0 ? enemies[0].transform : null;
        EasyFlipLock(0.2f, target);

        float finalDamage = sakuyaStat.CaculateDamage(
            DamageType.Physical, data.damage[0] * sakuyaStat.BaseAttack / 100f);

        int     knifeCount = data.Get<int>("knifeCount", 7);
        int     pwsOnHit   = data.Get<int>("pwsOnHit", 4);
        Vector2 dir        = target != null
            ? ((Vector2)target.position - (Vector2)transform.position).normalized
            : new Vector2(sakuyaController.FlipDirect, 0f);
        float baseAngle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float spreadStep = 180f / (knifeCount - 1);

        for (int i = 0; i < knifeCount; i++)
        {
            float angle = baseAngle - 90f + spreadStep * i;
            var   knife = SpawnKnife(poolKnife, transform.position);
            SetUpKnife(knife, data, new List<float> { finalDamage }, pwsOnHit, BulletMoveMode.Homing);
            knife.GetComponent<SakuyaKnife>().SetAngle(angle);
            if (target != null)
                knife.GetComponent<SakuyaKnife>().SetTarget(target);
        }

        ActivateClock6();
        if (isBurstActive)
            AddSpiralKnife(GetSkillData("Burst")?.Get<int>("spiralSkillAdd", 2) ?? 2);
        SetInputDelay(data.Get<float>("inputDelay", 0.1f));
    }

    // ── Skill Hold — Time Stop + Clock10 ─────────────────────────
    protected override void HoldSkill()
    {
        if (!canInput) return;
        if (isAttack || isSkill || isUlti || isDash) return;

        var data = GetSkillData("SkillHold");
        if (data == null) return;

        int pwsCost = data.Get<int>("pwsCost", 42);
        if (!SpendPWS(pwsCost)) return;
        
        isSkill = true;
        EventManager.Player.OnPlayerSkill.Get(entityKey).Invoke(this, null);


        float duration = data.Get<float>("timeStopDuration", 3f);
        ActivateTimeStop(duration);

        GetInfinite("Clock10")?.Activate();
        SetInputDelay(data.Get<float>("inputDelay", 0.1f));
    }

    // ═══════════════════════════════════════════════════════════════
    //  ATTACK
    // ═══════════════════════════════════════════════════════════════

    // ── Attack 1 — Single Knife homing ───────────────────────────
    private void DoAttack1()
    {
        if (!IsReady("Attack1")) return;
        
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        var data = GetSkillData("Attack1");
        if (data == null) return;

        var enemies = hitbox.detectObject(enemyLayer);
        Transform target = enemies != null && enemies.Count > 0 ? enemies[0].transform : null;
        EasyFlipLock(0.2f, target);
        isAttack = true;

        float finalDamage = sakuyaStat.CaculateDamage(
            DamageType.Physical, data.damage[0] * sakuyaStat.BaseAttack / 100f);

        var knife = SpawnKnife(poolKnife, transform.position);
        SetUpKnife(knife, data, new List<float> { finalDamage },
            data.Get<int>("pwsOnHit", 4), BulletMoveMode.Homing);

        UseWithAtkSpeed("Attack1", sakuyaStat.CurAttackSpeed);
        AdvanceCombo();
    }

    // ── Attack 2 — Knife + SmallKnife homing ─────────────────────
    private void DoAttack2()
    {
        if (!IsReady("Attack2")) return;
        
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);
        
        var data = GetSkillData("Attack2");
        if (data == null) return;

        var enemies = hitbox.detectObject(enemyLayer);
        Transform target = enemies != null && enemies.Count > 0 ? enemies[0].transform : null;
        EasyFlipLock(0.2f, target);
        isAttack = true;

        float dmgNormal = sakuyaStat.CaculateDamage(
            DamageType.Physical, data.damage[0] * sakuyaStat.BaseAttack / 100f);
        float dmgSmall  = sakuyaStat.CaculateDamage(
            DamageType.Physical, data.damage[1] * sakuyaStat.BaseAttack / 100f);

        int pwsOnHit = data.Get<int>("pwsOnHit", 4);

        var knife = SpawnKnife(poolKnife, transform.position);
        SetUpKnife(knife, data, new List<float> { dmgNormal }, pwsOnHit, BulletMoveMode.Homing);

        var small = SpawnKnife(poolKnifeSmall, transform.position + new Vector3(0f, 0.3f, 0f));
        SetUpKnife(small, data, new List<float> { dmgSmall }, pwsOnHit - 1, BulletMoveMode.Homing);

        UseWithAtkSpeed("Attack2", sakuyaStat.CurAttackSpeed);
        AdvanceCombo();
    }

    // ── Attack 3 — 5 dao quạt target ±30° ────────────────────────
    private void DoAttack3()
    {
        if (!IsReady("Attack3")) return;
        
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        var data = GetSkillData("Attack3");
        if (data == null) return;

        var enemies = hitbox.detectObject(enemyLayer);
        Transform target = enemies != null && enemies.Count > 0 ? enemies[0].transform : null;
        EasyFlipLock(0.2f, target);
        isAttack = true;

        float finalDamage = sakuyaStat.CaculateDamage(
            DamageType.Physical, data.damage[0] * sakuyaStat.BaseAttack / 100f);

        int   knifeCount = data.Get<int>("knifeCount", 5);
        int   pwsOnHit   = data.Get<int>("pwsOnHit", 4);

        Vector2 dir      = target != null
            ? ((Vector2)target.position - (Vector2)transform.position).normalized
            : new Vector2(sakuyaController.FlipDirect, 0f);
        float baseAngle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float spreadStep = 15f;
        float startAngle = baseAngle - spreadStep * (knifeCount / 2);

        for (int i = 0; i < knifeCount; i++)
        {
            float angle = startAngle + spreadStep * i;
            var   knife = SpawnKnife(poolKnife, transform.position);
            SetUpKnife(knife, data, new List<float> { finalDamage }, pwsOnHit, BulletMoveMode.Target);
            knife.GetComponent<SakuyaKnife>().SetAngle(angle);
        }

        UseWithAtkSpeed("Attack3", sakuyaStat.CurAttackSpeed);
        AdvanceCombo();
    }

    // ── Attack 4 — Spawn SmallKnife rồi lùi + set Clock12 ────────
    private void DoAttack4()
    {
        if (!IsReady("Attack4")) return;
        
        EventManager.Player.OnPlayerAttack.Get(entityKey).Invoke(this, null);

        var data = GetSkillData("Attack4");
        if (data == null) return;

        var enemies = hitbox.detectObject(enemyLayer);
        Transform target = enemies != null && enemies.Count > 0 ? enemies[0].transform : null;

        // Tính góc đến địch
        Vector2 dir      = target != null
            ? ((Vector2)target.position - (Vector2)transform.position).normalized
            : new Vector2(sakuyaController.FlipDirect, 0f);
        float baseAngle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        int   knifeCount = data.Get<int>("knifeCount", 2);
        float spreadStep = knifeCount > 1 ? 30f / (knifeCount - 1) : 0f;
        float startAngle = baseAngle - 15f;

        float finalDamage = sakuyaStat.CaculateDamage(
            DamageType.Physical, data.damage[0] * sakuyaStat.BaseAttack / 100f);

        for (int i = 0; i < knifeCount; i++)
        {
            float angle = knifeCount > 1 ? startAngle + spreadStep * i : baseAngle;
            var small = SpawnKnife(poolKnifeSmall, transform.position);
            SetUpKnife(small, data, new List<float> { finalDamage }, 0, BulletMoveMode.Homing);
            small.GetComponent<SakuyaKnife>().SetAngle(angle);
            if (target != null)
                small.GetComponent<SakuyaKnife>().SetTarget(target);
        }

        sakuyaController.CanInput = false;
        isAttack = true;

        sakuyaController.MoveTo(
            data.Get<float>("speed", 400f),
            data.Get<float>("retreatTime", 0.1f)
        );

        GetInfinite("Clock12")?.Activate();
        Use("Attack4");
        comboIndex      = 0;
        comboResetTimer = 0f;
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Tiêu PWS và tự động cộng BurstGauge.
    /// Mỗi 2 PWS tiêu → +1 BurstGauge (làm tròn lên).
    /// </summary>
    private bool SpendPWS(int amount)
    {
        var pws = GetCounter("PocketWatchShard");
        if (pws == null || !pws.IsReadyFor(amount)) return false;

        pws.Use(amount);

        int burstGain = Mathf.CeilToInt(amount / 2f);
        GetCounter("BurstGauge")?.AddCounter(burstGain);

        return true;
    }

    private GameObject SpawnKnife(EasyPoolingList pool, Vector3 position)
    {
        var go = pool.GetGameObject();
        go.transform.position = position;
        go.transform.rotation = Quaternion.identity;
        go.transform.parent   = null;

        bool isFacingRight    = sakuyaController.FlipDirect == 1;
        go.transform.rotation = Quaternion.Euler(0f, isFacingRight ? 0f : 180f, 0f);

        var knife = go.GetComponent<SakuyaKnife>();
        if (knife != null)
        {
            knife.FlipDirect   = isFacingRight ? 1 : -1;
            knife.EnableDamage = enemyLayer;
        }

        return go;
    }

    private void SetUpKnife(GameObject go, SkillData data, List<float> damage,
                            int pwsOnHit, BulletMoveMode moveMode)
    {
        var knife = go.GetComponent<SakuyaKnife>();
        if (knife == null) return;

        // Apply tất cả damage buff
        var buffedDamage = new List<float>(damage);
        for (int i = 0; i < buffedDamage.Count; i++)
            buffedDamage[i] = ApplyDamageBuffs(buffedDamage[i]);

        knife.SetUp(
            entityKey,
            DamageType.Physical,
            buffedDamage,
            sakuyaStat.CurCritRate,
            sakuyaStat.CurCritDamage,
            data.Get<float>("iFrameDuration", 0f),
            pwsOnHit
        );
        knife.EasyModeChange(moveMode);
        go.SetActive(true);
    }

    private void AdvanceCombo()
    {
        comboIndex      = (comboIndex + 1) % 4;
        comboResetTimer = COMBO_RESET_TIME;
    }

    /// <summary>Áp dụng tất cả damage buff đang active (Clock6, Clock4).</summary>
    private float ApplyDamageBuffs(float baseDamage)
    {
        float result = baseDamage;
        if (GetInfinite("Clock6")?.IsReady == true)
        {
            float mult = GetSkillData("Clock6")?.Get<float>("clock6Multiplier", 1.75f) ?? 1.75f;
            result *= mult;
        }
        if (GetInfinite("Clock4")?.IsReady == true)
        {
            float mult = GetSkillData("Clock4")?.Get<float>("clock4DamageMult", 1.5f) ?? 1.5f;
            result *= mult;
        }
        if (GetInfinite("Clock2")?.IsReady == true)
        {
            float bonus = GetSkillData("Clock2")?.Get<float>("clock2DmgBonus", 250f) ?? 250f;
            result += bonus;
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  GIZMOS
    // ═══════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        // Perfect Dash detect box
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawCube(transform.position, perfectDashBoxSize);
        Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
        Gizmos.DrawWireCube(transform.position, perfectDashBoxSize);

        // Clock12 spawn height
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawLine(
            transform.position,
            transform.position + new Vector3(0f, daggerSpawnHeight, 0f)
        );
        Gizmos.DrawWireSphere(
            transform.position + new Vector3(0f, daggerSpawnHeight, 0f),
            0.15f
        );
    }
}