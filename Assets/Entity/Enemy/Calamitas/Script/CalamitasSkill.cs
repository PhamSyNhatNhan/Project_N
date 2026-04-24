using System.Collections.Generic;
using UnityEngine;

public class CalamitasSkill : EnemySkill
{
    // ── Phase components ──────────────────────────────────────────
    private CalamitasSkillImmortal0  immortal0;
    private CalamitasSkillImmortal1  immortal1;
    private CalamitasSkillImmortal2  immortal2;
    private CalamitasSkillImmortal3  immortal3;
    private CalamitasSkillPhase1     phase1Skill;
    private CalamitasSkillPhase2     phase2Skill;
    private CalamitasSkillPhase3     phase3Skill;
    private CalamitasSkillSubPhase3  subPhase3;

    // ── Shield ────────────────────────────────────────────────────
    [Header("Shield")]
    [SerializeField] private GameObject calamitasShield;

    // ── Bullet Prefabs ────────────────────────────────────────────
    [Header("Bullet Prefabs")]
    [SerializeField] private GameObject calHellblast2Prefab;
    [SerializeField] private GameObject calHellfirePrefab;
    [SerializeField] private GameObject calHellblastPrefab;
    [SerializeField] private GameObject calPillarRingBulletPrefab;
    [SerializeField] private GameObject calCondemnationArrowPrefab;
    [SerializeField] private GameObject calDaggerPrefab;
    [SerializeField] private GameObject calDartPrefab;
    [SerializeField] private GameObject calDarkSoulPrefab;
    [SerializeField] private GameObject calSuicideBomberPrefab;
    [SerializeField] private GameObject calBombPrefab;
    [SerializeField] private GameObject calSunPrefab;

    // ── Pools ─────────────────────────────────────────────────────
    private readonly EasyPoolingList calHellblast2Pool        = new EasyPoolingList();
    private readonly EasyPoolingList calHellfirePool          = new EasyPoolingList();
    private readonly EasyPoolingList calHellblastPool         = new EasyPoolingList();
    private readonly EasyPoolingList calPillarRingBulletPool  = new EasyPoolingList();
    private readonly EasyPoolingList calCondemnationArrowPool = new EasyPoolingList();
    private readonly EasyPoolingList calDaggerPool            = new EasyPoolingList();
    private readonly EasyPoolingList calDartPool              = new EasyPoolingList();
    private readonly EasyPoolingList calDarkSoulPool          = new EasyPoolingList();
    private readonly EasyPoolingList calSuicideBomberPool     = new EasyPoolingList();
    private readonly EasyPoolingList calBombPool              = new EasyPoolingList();
    private readonly EasyPoolingList calSunPool               = new EasyPoolingList();

    // ── Runtime ───────────────────────────────────────────────────
    private Stat              bossStat;
    private Move              bossMove;
    private CapsuleCollider2D bossCollider;
    private Transform         playerTransform;
    private bool              isImmortal;

    // ── HP Flags ──────────────────────────────────────────────────
    private bool hasEnteredImmortal1 = false;
    private bool hasEnteredImmortal2 = false;
    private bool hasEnteredImmortal3 = false;

    // ── Lifecycle ─────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        bossStat     = GetComponent<Stat>();
        bossMove     = GetComponent<Move>();
        bossCollider = GetComponent<CapsuleCollider2D>();
        immortal0    = GetComponent<CalamitasSkillImmortal0>();
        immortal1    = GetComponent<CalamitasSkillImmortal1>();
        immortal2    = GetComponent<CalamitasSkillImmortal2>();
        immortal3    = GetComponent<CalamitasSkillImmortal3>();
        phase1Skill  = GetComponent<CalamitasSkillPhase1>();
        phase2Skill  = GetComponent<CalamitasSkillPhase2>();
        phase3Skill  = GetComponent<CalamitasSkillPhase3>();
        subPhase3    = GetComponent<CalamitasSkillSubPhase3>();
    }

    protected override void Start()
    {
        base.Start();

        // ── Setup pools ───────────────────────────────────────────
        if (calHellblast2Prefab)        calHellblast2Pool       .SetPrefab(calHellblast2Prefab);
        if (calHellfirePrefab)          calHellfirePool         .SetPrefab(calHellfirePrefab);
        if (calHellblastPrefab)         calHellblastPool        .SetPrefab(calHellblastPrefab);
        if (calPillarRingBulletPrefab)  calPillarRingBulletPool .SetPrefab(calPillarRingBulletPrefab);
        if (calCondemnationArrowPrefab) calCondemnationArrowPool.SetPrefab(calCondemnationArrowPrefab);
        if (calDaggerPrefab)            calDaggerPool           .SetPrefab(calDaggerPrefab);
        if (calDartPrefab)              calDartPool             .SetPrefab(calDartPrefab);
        if (calDarkSoulPrefab)          calDarkSoulPool         .SetPrefab(calDarkSoulPrefab);
        if (calSuicideBomberPrefab)     calSuicideBomberPool    .SetPrefab(calSuicideBomberPrefab);
        if (calBombPrefab)              calBombPool             .SetPrefab(calBombPrefab);
        if (calSunPrefab)               calSunPool              .SetPrefab(calSunPrefab);

        // ── Disable tất cả phase, wire callbacks & data ───────────
        if (immortal0 != null)
        {
            immortal0.enabled    = false;
            immortal0.OnComplete = OnImmortal0Complete;
            immortal0.LoadData(
                calHellblast2Pool, calHellfirePool, calPillarRingBulletPool,
                GetDamageList("CalamitasImmortal0Hellblast"),
                GetDamageList("CalamitasImmortal0Hellfire")
            );
        }

        if (immortal1 != null)
        {
            immortal1.enabled    = false;
            immortal1.OnComplete = OnImmortal1Complete;
            immortal1.SetShieldObject(calamitasShield);
            immortal1.LoadData(
                calHellfirePool, calDaggerPool,
                GetDamageList("CalamitasImmortal1Hellfire"),
                GetDamageList("CalamitasImmortal1Dagger"),
                GetDamageList("CalamitasImmortal1Dash")
            );
        }

        if (phase1Skill != null)
        {
            phase1Skill.enabled = false;
            phase1Skill.LoadData(
                calHellfirePool, calCondemnationArrowPool, calHellblastPool,
                GetDamageList("CalamitasPhase1Hellfire"),
                GetDamageList("CalamitasPhase1CondemnationArrow"),
                GetDamageList("CalamitasPhase1Hellblast"),
                GetDamageList("CalamitasPhase1Dash")
            );
        }

        if (phase2Skill != null)
        {
            phase2Skill.enabled = false;
            phase2Skill.LoadData(
                calHellfirePool, calHellblastPool, calDaggerPool,
                calDarkSoulPool, calSuicideBomberPool, calBombPool, calSunPool,
                GetDamageList("CalamitasPhase2Hellfire"),
                GetDamageList("CalamitasPhase2Hellblast"),
                GetDamageList("CalamitasPhase2Dagger"),
                GetDamageList("CalamitasPhase2DarkSoul"),
                GetDamageList("CalamitasPhase2SuicideBomber"),
                GetDamageList("CalamitasPhase2Bomb"),
                GetDamageList("CalamitasPhase2Sun")
            );
        }

        if (phase3Skill != null)
        {
            phase3Skill.enabled = false;
            phase3Skill.LoadData(
                calHellfirePool, calHellblastPool, calCondemnationArrowPool,
                calDartPool, calDarkSoulPool, calBombPool,
                GetDamageList("CalamitasPhase3Hellfire"),
                GetDamageList("CalamitasPhase3Hellblast"),
                GetDamageList("CalamitasPhase3CondemnationArrow"),
                GetDamageList("CalamitasPhase3Dart"),
                GetDamageList("CalamitasPhase3DarkSoul"),
                GetDamageList("CalamitasPhase3Bomb"),
                GetDamageList("CalamitasPhase3Dash")
            );
        }

        if (subPhase3 != null)
        {
            subPhase3.enabled = false;
            subPhase3.LoadData(
                calDaggerPool, calDartPool, calBombPool,
                GetDamageList("CalamitasSubPhase3Dagger"),
                GetDamageList("CalamitasSubPhase3Dart"),
                GetDamageList("CalamitasSubPhase3Bomb")
            );
        }

        if (immortal2 != null)
        {
            immortal2.enabled    = false;
            immortal2.OnComplete = OnImmortal2Complete;
            immortal2.LoadData(
                calCondemnationArrowPool,
                calHellfirePool,
                GetDamageList("CalamitasImmortal2Hellfire")
            );
        }

        if (immortal3 != null)
        {
            immortal3.enabled    = false;
            immortal3.OnComplete = OnImmortal3Complete;
            immortal3.LoadData(
                calDaggerPool, calDartPool,
                GetDamageList("CalamitasImmortal3Dagger"),
                GetDamageList("CalamitasImmortal3Dart")
            );
        }

        SetShield(false);
        FindPlayer();

        // Đăng ký event để chặn đòn đánh kết liễu và giữ 1 HP
        if (bossStat != null)
        {
            bossStat.OnBeforeTakeDamage += OnBeforeTakeDamageHandler;
        }

        // Bắt đầu bằng Immortal0
        EnterImmortal0();
    }

    private void OnDestroy()
    {
        if (bossStat != null)
        {
            bossStat.OnBeforeTakeDamage -= OnBeforeTakeDamageHandler;
        }
    }

    protected override void Update()
    {
        base.Update();
        if (!isImmortal) FacePlayer();

        CheckHPThresholds();
    }

    // ── Logic chuyển Phase theo Máu ───────────────────────────────
    private void CheckHPThresholds()
    {
        if (bossStat == null) return;

        float hpPercent = bossStat.CurHealth / bossStat.MaxHealth;

        if (!hasEnteredImmortal1 && hpPercent <= 0.5f && phase1Skill != null && phase1Skill.enabled)
        {
            hasEnteredImmortal1 = true;
            EnterImmortal1();
        }
        else if (!hasEnteredImmortal2 && hpPercent <= 0.2f && phase2Skill != null && phase2Skill.enabled)
        {
            hasEnteredImmortal2 = true;
            EnterImmortal2();
        }
    }

    // Hook chặn sát thương chết người để giữ 1HP và chạy Immortal3
    private float OnBeforeTakeDamageHandler(float damage, DamageType type)
    {
        if (hasEnteredImmortal3) return damage;

        if (bossStat.CurHealth - damage <= 0)
        {
            hasEnteredImmortal3 = true;
            
            float modifiedDamage = bossStat.CurHealth - 1f;
            modifiedDamage = Mathf.Max(0f, modifiedDamage);

            EnterImmortal3();
            return modifiedDamage;
        }

        return damage;
    }

    // ── CLEAR POOLS (Dọn Dẹp Đạn) ─────────────────────────────────
    private void ClearAllProjectiles()
    {
        calHellblast2Pool?.ClearPool();
        calHellfirePool?.ClearPool();
        calHellblastPool?.ClearPool();
        calPillarRingBulletPool?.ClearPool();
        calCondemnationArrowPool?.ClearPool();
        calDaggerPool?.ClearPool();
        calDartPool?.ClearPool();
        calDarkSoulPool?.ClearPool();
        calSuicideBomberPool?.ClearPool();
        calBombPool?.ClearPool();
        calSunPool?.ClearPool();
    }

    // ── Flip Sửa Lỗi Xoay Vòng Tròn ───────────────────────────────
    private void FacePlayer()
    {
        if (playerTransform == null) { FindPlayer(); return; }
        if (phase2Skill != null && phase2Skill.enabled && phase2Skill.LockFacing) return;
        if (phase3Skill != null && phase3Skill.enabled && phase3Skill.LockFacing) return;

        float toPlayerX = playerTransform.position.x - transform.position.x;
        int wantedFlip = toPlayerX >= 0f ? 1 : -1;

        if (wantedFlip != bossMove.FlipDirect)
        {
            bossMove.FlipDirect = wantedFlip;
            transform.rotation = Quaternion.Euler(0, wantedFlip == 1 ? 0 : 180, 0);
        }
    }

    // ── Shield ────────────────────────────────────────────────────
    private void SetShield(bool active)
    {
        if (calamitasShield != null) calamitasShield.SetActive(active);
    }

    // ── Phase transitions ─────────────────────────────────────────
    private void EnterImmortal0()
    {
        ClearAllProjectiles();
        isImmortal        = true;
        bossStat.CanDamge = false;
        if (bossCollider != null) bossCollider.enabled = false;
        SetShield(true);
        if (immortal0 != null) immortal0.enabled = true;
    }

    private void OnImmortal0Complete()
    {
        SetShield(false);
        // Delay cinematic một chút
        Invoke(nameof(StartPhase1), 1f);
    }

    private void StartPhase1()
    {
        ClearAllProjectiles();
        isImmortal = false;
        bossStat.CanDamge = true;
        if (bossCollider != null) bossCollider.enabled = true;
        if (phase1Skill != null) phase1Skill.enabled = true;
    }

    private void EnterImmortal1()
    {
        ClearAllProjectiles();
        isImmortal        = true;
        bossStat.CanDamge = false;
        if (bossCollider  != null) bossCollider.enabled  = false;
        if (phase1Skill   != null) phase1Skill.enabled   = false;
        SetShield(true);
        if (immortal1     != null) immortal1.enabled      = true;
    }

    private void OnImmortal1Complete()
    {
        if (immortal1 != null) immortal1.enabled = false;
        // Chờ 1.5 giây để đạn cuối cùng của Immortal 1 bay hết rồi mới sang Phase 2
        Invoke(nameof(StartPhase2), 1.5f);
    }

    private void StartPhase2()
    {
        ClearAllProjectiles();
        SetShield(false);
        isImmortal = false;
        bossStat.CanDamge = true;
        if (bossCollider != null) bossCollider.enabled = true;
        if (phase2Skill != null) phase2Skill.enabled = true;
    }

    private void EnterImmortal2()
    {
        ClearAllProjectiles();
        isImmortal        = true;
        bossStat.CanDamge = false;
        if (bossCollider != null) bossCollider.enabled = false;
        if (phase2Skill  != null) phase2Skill.enabled  = false; 
        SetShield(true);
        if (immortal2    != null) immortal2.enabled    = true;
    }

    private void OnImmortal2Complete()
    {
        if (immortal2 != null) immortal2.enabled = false;
        Invoke(nameof(StartPhase3), 1f);
    }

    private void StartPhase3()
    {
        ClearAllProjectiles();
        SetShield(false);
        isImmortal = false;
        bossStat.CanDamge = true;
        if (bossCollider != null) bossCollider.enabled = true;
        if (phase3Skill != null) phase3Skill.enabled = true;
        if (subPhase3   != null) subPhase3.enabled   = true;
    }

    private void EnterImmortal3()
    {
        isImmortal = true;
        bossStat.CanDamge = false;
    
        if (bossCollider != null) bossCollider.enabled = false;
        if (phase3Skill != null)  phase3Skill.enabled  = false;
        if (subPhase3 != null)    subPhase3.enabled    = false;


        transform.position = Vector2.zero;

        if (playerTransform != null)
        {
            playerTransform.position = Vector2.zero;
        }

        if (immortal3 != null) 
        {
            immortal3.enabled = true;
        }
    
        SetShield(true);
        ClearAllProjectiles(); 
    }

    private void OnImmortal3Complete()
    {
        // Phase kết thúc, Boss tắt khiên và chờ chết
        SetShield(false);
        ClearAllProjectiles(); 
        isImmortal        = false;
        bossStat.CanDamge = true;
        if (bossCollider != null) bossCollider.enabled = true;
    }

    // ── Helpers ───────────────────────────────────────────────────
    private void FindPlayer()
    {
        var go = GameObject.FindWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    private List<float> GetDamageList(string id)
    {
        if (skillData.TryGetValue(id, out var data) && data.damage?.Count > 0)
            return data.damage;
        return new List<float> { 200f };
    }
}