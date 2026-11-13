// EN: Extension point for stat behavior shared by this game's enemies.
// VI: Điểm kế thừa cho hành vi chỉ số chung của các kẻ địch trong game.
using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

public class PNEnemyStat : PNStat
{
    private PNEnemyTypeDefinition enemyType;
    public PNEnemyTypeDefinition EnemyType => IsReady && enemyType != null ? enemyType
        : throw new InvalidOperationException("Enemy classification requires initialized character content.");
    public EveId EnemyTypeId => EnemyType.Id;
    protected override PNTargetLayers DefaultTargetLayers => PNTargetLayers.Of(PNLayers.Player);

    public override void Initialize(EveCharacterDefinition character, EveRegistryHub registry)
    {
        if (IsInitialized) throw new InvalidOperationException("Enemy stats are already initialized.");
        if (character == null) throw new ArgumentNullException(nameof(character));
        if (registry == null || !registry.IsSealed) throw new ArgumentException("Sealed registry required.", nameof(registry));
        var selected = PNEnemyContent.ResolveType(character.Data, registry);
        var lorica = PNLoricaSettings.Parse(character.Data);
        base.Initialize(character, registry);
        enemyType = selected; Lorica = lorica;
    }

    // Direct stat-set initialization has no character metadata, so it uses registered Normal.
    public override void Initialize(EveStatSet statSet)
    {
        if (IsInitialized) throw new InvalidOperationException("Enemy stats are already initialized.");
        if (!(statSet is PNStatSet gameSet)) throw new ArgumentException("Enemy stats require a PNStatSet.", nameof(statSet));
        var selected = PNEnemyContent.ResolveType(null, gameSet.Registry);
        base.Initialize(statSet);
        enemyType = selected; Lorica = PNLoricaSettings.Default;
    }

    public override void Unload()
    {
        enemyType = null; loricaLocks.Clear(); sidusLocks.Clear(); Lorica = PNLoricaSettings.Default;
        base.Unload();
    }

    // ---- Sidus: a state, not an enemy type (Docs/SidusPlan.md). Held by leases like the Lorica lock; the Buff that grants it owns the lease. Only a ready enemy classified normal can hold it. --------------
    private readonly List<SidusLock> sidusLocks = new List<SidusLock>();
    // True while at least one Sidus lease is held. Authority only: a client replica holds no lease (the Buff there is presentation).
    public bool IsSidus => sidusLocks.Count > 0;
    // True when this enemy can be Sidus at all: ready and registered as normal (a boss never is).
    public bool CanBeSidus => IsReady && enemyType != null && enemyType.Id == PNContentIds.NormalEnemy;
    // Takes a Sidus lease; null when the enemy cannot be Sidus (not ready, or a boss). Dispose returns only this lease; safe to repeat.
    public IDisposable LockSidus(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Lock owner required.", nameof(owner));
        if (!CanBeSidus) return null;
        var result = new SidusLock(this, owner); sidusLocks.Add(result); return result;
    }
    private sealed class SidusLock : IDisposable
    {
        private PNEnemyStat owner; public readonly string Owner;
        public SidusLock(PNEnemyStat stat, string name) { owner = stat; Owner = name; }
        public void Dispose() { var stat = owner; owner = null; stat?.sidusLocks.Remove(this); }
    }

    // ---- Lorica: a resource that shares every hit with health, breaks into a stun and then refills (docs: Docs/EnemyLoricaDesign.md) --------------
    public PNLoricaSettings Lorica { get; private set; } = PNLoricaSettings.Default;
    private readonly List<LoricaLock> loricaLocks = new List<LoricaLock>();
    // True when the enemy has the lorica stat with a maximum above 0; otherwise nothing below applies.
    public bool HasLorica => IsReady && Set.TryGetBase(PNContentIds.Lorica, out _) && GetMax(PNContentIds.Lorica) > 0d;
    public double LoricaCurrent => HasLorica ? GetCurrent(PNContentIds.Lorica) : 0d;
    public bool IsLoricaLocked => loricaLocks.Count > 0;
    // Takes a lock on lorica: while at least one is held it cannot go down (hits still share). Dispose returns only this lock; safe to repeat.
    public IDisposable LockLorica(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Lock owner required.", nameof(owner));
        var result = new LoricaLock(this, owner); loricaLocks.Add(result); return result;
    }
    private sealed class LoricaLock : IDisposable
    {
        private PNEnemyStat owner; public readonly string Owner;
        public LoricaLock(PNEnemyStat stat, string name) { owner = stat; Owner = name; }
        public void Dispose() { var stat = owner; owner = null; stat?.loricaLocks.Remove(this); }
    }
    protected override double AbsorbDamage(PNDamage damage, PNDamageTypeDefinition damageType, double calculated, out PNDamageAbsorption absorption)
    {
        absorption = default;
        // True damage ignores lorica completely; a broken lorica (0) shares nothing, so health takes the whole hit.
        if (!HasLorica || damageType.Id == PNContentIds.True) return calculated;
        double current = GetCurrent(PNContentIds.Lorica);
        if (current <= 0d) return calculated;
        double toLorica = calculated * Lorica.Share;
        double after = IsLoricaLocked ? current : Math.Max(0d, current - toLorica);
        absorption = new PNDamageAbsorption { LoricaBefore = current, LoricaAfter = after, LoricaBroke = after <= 0d };
        return calculated - toLorica;
    }
    protected override void CommitAbsorption(PNDamageResult result, PNDamageAbsorption absorption)
    {
        if (absorption.LoricaAfter != absorption.LoricaBefore) SetCurrent(PNContentIds.Lorica, absorption.LoricaAfter);
        if (absorption.LoricaBroke && !IsDead) BeginLoricaBreak();
    }
    // Lorica reached 0: the break Buff stuns the enemy and interrupts its cast. Without a Buff manager there is nothing to wait for, so it refills at once.
    private void BeginLoricaBreak()
    {
        var buffs = GetComponent<PNBuffManager>();
        if (buffs == null || !buffs.IsReady) { SetCurrent(PNContentIds.Lorica, GetMax(PNContentIds.Lorica)); return; }
        buffs.Buffs.Apply(new PNBuffApplyRequest(PNContentIds.LoricaBreak, PNCombatSource.Capture(this), new JObject { ["duration"] = Lorica.BreakSeconds }));
    }
    // Called by the break Buff when it ends (it ran out or was removed with bypass): lorica refills and, if configured, stays locked for a while.
    public void FinishLoricaBreak()
    {
        if (!IsReady || IsDead || !HasLorica) return;
        SetCurrent(PNContentIds.Lorica, GetMax(PNContentIds.Lorica));
        var buffs = GetComponent<PNBuffManager>();
        if (buffs != null && buffs.IsReady && Lorica.RecoverySeconds > 0d)
            buffs.Buffs.Apply(new PNBuffApplyRequest(PNContentIds.LoricaRecovery, PNCombatSource.Capture(this), new JObject { ["duration"] = Lorica.RecoverySeconds }));
    }
}
