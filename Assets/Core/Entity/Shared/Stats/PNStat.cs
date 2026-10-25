using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using EveEngine;

// EN: Floor-transfer data containing only resource currents and persistent sources.
// VI: Dữ liệu chuyển ải chỉ gồm Current của resource và các nguồn được giữ lại.
public class PNStatFloorSnapshot
{
    public virtual Guid SessionId { get; }
    public virtual EveEntityId EntityId { get; }
    public virtual EveId CharacterId { get; }
    public virtual IReadOnlyDictionary<EveId, double> ResourceCurrents { get; }
    public virtual IReadOnlyDictionary<EveId, IReadOnlyList<EveStatModifier>> PersistentSources { get; }

    internal PNStatFloorSnapshot(
        Guid sessionId,
        EveEntityId entityId,
        EveId characterId,
        IDictionary<EveId, double> resourceCurrents,
        IDictionary<EveId, IReadOnlyList<EveStatModifier>> persistentSources)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Snapshot session is required.", nameof(sessionId));
        SessionId = sessionId;
        EntityId = entityId;
        CharacterId = characterId;
        ResourceCurrents = new ReadOnlyDictionary<EveId, double>(new Dictionary<EveId, double>(resourceCurrents));
        PersistentSources = new ReadOnlyDictionary<EveId, IReadOnlyList<EveStatModifier>>(
            new Dictionary<EveId, IReadOnlyList<EveStatModifier>>(persistentSources));
    }
}

// EN: Defense, critical rate, CDR and aggro remain signed; other stats/group multipliers are bounded at zero.
// VI: DEF, tỷ lệ chí mạng và CDR giữ dấu; các stat khác/hệ số nhóm được chặn tại 0.
public class PNStatSet : EveStatSet
{
    private readonly EveRegistry<PNStatCalculationDefinition> calculations;
    private readonly EveRegistry<PNDamageTypeDefinition> damageTypes;
    private readonly EveRegistry<PNElementDefinition> elements;
    private readonly EveRegistry<PNTagDefinition> tags;
    // Scoped to Evaluate's virtual Calculate call; restored even when a mod throws.
    private bool evaluatingStat;
    public EveRegistryHub Registry { get; }
    public virtual EveId DefaultCalculationId { get; }
    private PNStatSet combatCopy;
    private bool combatCopyIgnoresCurrent;
    private readonly Dictionary<(EveId, double, string, bool), double> pureCalculations = new Dictionary<(EveId, double, string, bool), double>();
    private readonly Dictionary<(EveId, double, EveId, EveId, bool, int), double> smallCalculations = new Dictionary<(EveId, double, EveId, EveId, bool, int), double>();
    private bool? immutableEntries;
    private readonly Dictionary<EveId, EveId> canonicalIds = new Dictionary<EveId, EveId>();
    private readonly Dictionary<(EveId, EveId, int), PNDamageTypeDefinition> smallDamageTypes = new Dictionary<(EveId, EveId, int), PNDamageTypeDefinition>();
    private sealed class PreparedInputs
    {
        internal readonly IReadOnlyList<EveId> Ids;
        internal readonly IReadOnlyList<EveStatModifier> Modifiers;
        internal PreparedInputs(EveId[] ids, EveStatModifier[] modifiers) { Ids = Array.AsReadOnly(ids); Modifiers = Array.AsReadOnly(modifiers); }
    }
    private readonly Dictionary<(EveId, string, bool), PreparedInputs> preparedModifiers = new Dictionary<(EveId, string, bool), PreparedInputs>();
    private readonly Dictionary<(EveId, EveId, EveId, bool, int), PreparedInputs> smallPreparedModifiers = new Dictionary<(EveId, EveId, EveId, bool, int), PreparedInputs>();
    private bool OrdinaryEntries
    {
        get
        {
            if (!immutableEntries.HasValue) immutableEntries = GetType() == typeof(PNStatSet) && BaseSnapshot().Values.All(x => x.GetType() == typeof(EveBaseStat) || x.GetType() == typeof(EveResourceStat)) && SourceSnapshot().Values.SelectMany(x => x).All(x => x.GetType() == typeof(EveStatModifier));
            return immutableEntries.Value;
        }
    }
    protected override bool CanCacheEvaluation => OrdinaryEntries && calculations.GetEffective(DefaultCalculationId).IsCurrentIndependentBuiltIn;
    public override void InvalidateCache() { combatCopy = null; combatCopyIgnoresCurrent = false; immutableEntries = null; penetrationModifiers = null; pureCalculations.Clear(); smallCalculations.Clear(); preparedModifiers.Clear(); smallPreparedModifiers.Clear(); base.InvalidateCache(); }

    // EN: Every modifier on the penetration route, captured into outgoing damage packets and applied by the receiver as its own for that hit.
    // VI: Mọi modifier thuộc đường penetration, được chụp vào gói damage gây ra và bên nhận áp như modifier của mình trong đòn đó.
    private IReadOnlyList<EveStatModifier> penetrationModifiers;
    private static readonly IReadOnlyList<EveStatModifier> noModifiers = Array.AsReadOnly(new EveStatModifier[0]);
    internal IReadOnlyList<EveStatModifier> PenetrationModifiers
    {
        get
        {
            if (penetrationModifiers != null) return penetrationModifiers;
            var found = SourceSnapshot().OrderBy(pair => pair.Key).SelectMany(pair => pair.Value)
                .Where(modifier => ResolveCalculationId(modifier) == PNContentIds.Penetration).Select(modifier => modifier.Clone()).ToArray();
            var result = found.Length == 0 ? noModifiers : Array.AsReadOnly(found);
            if (OrdinaryEntries) penetrationModifiers = result;
            return result;
        }
    }
    public override void SetCurrent(EveId id, double value) { if (!combatCopyIgnoresCurrent) combatCopy = null; base.SetCurrent(id, value); }
    internal PNStatSet CaptureCopy()
    {
        // Custom mutable entries/sets retain per-call capture; ordinary entries are immutable
        // and source writes invalidate this private immutable copy. Current-only writes
        // can reuse it solely when every involved built-in formula ignores Current.
        if (GetType() == typeof(PNStatSet) && combatCopy != null) return combatCopy;
        var bases = BaseSnapshot(); var sources = SourceSnapshot();
        bool ordinary = GetType() == typeof(PNStatSet) && bases.Values.All(x => x.GetType() == typeof(EveBaseStat) || x.GetType() == typeof(EveResourceStat)) && sources.Values.SelectMany(x => x).All(x => x.GetType() == typeof(EveStatModifier));
        var selected = sources.Values.SelectMany(x => x).Select(ResolveCalculationId).Concat(new[] { DefaultCalculationId, PNContentIds.Scale }).Distinct();
        bool independent = ordinary && selected.All(id => calculations.GetEffective(id).IsCurrentIndependentBuiltIn);
        var copy = new PNStatSet(Registry, DefaultCalculationId);
        if (independent) copy.ImportSnapshot(this);
        else
        {
            // Preserve the original construction/callback path for current-dependent or
            // stateful mod formulas and entries, including Current above base maximum.
            foreach (var pair in bases) copy.AddBase(pair.Value is EveResourceStat resource ? (EveBaseStat)new EveResourceStat(pair.Key, resource.Value, Math.Min(resource.Current, resource.Value), resource.MaxChangePolicy) : new EveBaseStat(pair.Key, pair.Value.Value));
            foreach (var pair in sources) copy.SetSource(pair.Key, pair.Value.Select(x => x.Clone()).ToArray());
            foreach (var pair in bases) if (pair.Value is EveResourceStat) copy.SetCurrent(pair.Key, GetCurrent(pair.Key));
        }
        if (independent) { combatCopy = copy; combatCopyIgnoresCurrent = true; }
        return copy;
    }

    public override void LoadCharacter(EveCharacterDefinition character)
    {
        base.LoadCharacter(character);
        // Older character documents inherit combat defaults without requiring JSON migration.
        if (Definitions.TryGet(PNContentIds.AttackSpeed, out _) && !TryGetBase(PNContentIds.AttackSpeed, out _))
            AddBase(new EveBaseStat(PNContentIds.AttackSpeed, 100d));
        if (Definitions.TryGet(PNContentIds.CooldownReduction, out _) && !TryGetBase(PNContentIds.CooldownReduction, out _))
            AddBase(new EveBaseStat(PNContentIds.CooldownReduction, 0d));
        if (Definitions.TryGet(PNContentIds.CritRate, out _) && !TryGetBase(PNContentIds.CritRate, out _))
            AddBase(new EveBaseStat(PNContentIds.CritRate, 0d));
        if (Definitions.TryGet(PNContentIds.CritDamage, out _) && !TryGetBase(PNContentIds.CritDamage, out _))
            AddBase(new EveBaseStat(PNContentIds.CritDamage, 150d));
        if (Definitions.TryGet(PNContentIds.Aggro, out _) && !TryGetBase(PNContentIds.Aggro, out _))
            AddBase(new EveBaseStat(PNContentIds.Aggro, 0d));
    }

    public PNStatSet(EveRegistryHub registry, EveId defaultCalculationId = default) : base(RequireRegistry(registry))
    {
        Registry = registry;
        calculations = registry.Calculations();
        var registries = registry.Snapshot();
        if (registries.ContainsKey(typeof(PNDamageTypeDefinition))) damageTypes = registry.DamageTypes();
        if (registries.ContainsKey(typeof(PNElementDefinition))) elements = registry.Elements();
        if (registries.ContainsKey(typeof(PNTagDefinition))) tags = registry.Tags();
        DefaultCalculationId = defaultCalculationId.IsValid ? defaultCalculationId : PNContentIds.Scale;
        calculations.GetEffective(calculations.ResolveIdentity(DefaultCalculationId));
    }

    private static EveRegistry<EveStatDefinition> RequireRegistry(EveRegistryHub registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (!registry.IsSealed) throw new ArgumentException("Stat calculations require a sealed registry hub.", nameof(registry));
        return registry.Stats;
    }

    // EN: Legacy modifiers use the Core default; explicit IDs resolve to registered calculation implementations.
    // VI: Modifier cũ dùng mặc định Core; ID tường minh tra triển khai đường tính đã đăng ký.
    public virtual EveId ResolveCalculationId(EveStatModifier modifier)
        => calculations.ResolveIdentity(modifier.CalculationId.IsValid ? modifier.CalculationId : DefaultCalculationId);
    public virtual EveId ResolveContributionId(EveStatModifier modifier)
    {
        if (modifier.Group != 0) throw new ArgumentException("Contribution selection requires group 0.", nameof(modifier));
        var registry = Registry.Contributions();
        var id = modifier.ContributionId.IsValid ? modifier.ContributionId : PNContentIds.After;
        registry.Get(id);
        return registry.ResolveIdentity(id);
    }

    public override EveId ResolveIdentity(EveId id)
    {
        var requested = id;
        if (GetType() == typeof(PNStatSet) && canonicalIds.TryGetValue(id, out var cached)) return cached;
        id = base.ResolveIdentity(id);
        if (tags != null) id = tags.ResolveIdentity(id);
        if (damageTypes != null) id = damageTypes.ResolveIdentity(id);
        if (elements != null) id = elements.ResolveIdentity(id);
        if (GetType() == typeof(PNStatSet)) { if (canonicalIds.Count >= 512) canonicalIds.Clear(); canonicalIds[requested] = id; }
        return id;
    }

    protected override void ValidateEvaluationId(EveId id)
    {
        if (Definitions.TryGet(id, out _) ||
            (damageTypes != null && damageTypes.TryGet(id, out _)) ||
            (elements != null && elements.TryGet(id, out _)) ||
            (tags != null && tags.TryGet(id, out _))) return;
        throw new KeyNotFoundException($"Unknown stat, damage type, element or tag ID: {id}.");
    }

    protected override void ValidateModifier(EveStatModifier modifier)
    {
        ValidateEvaluationId(modifier.Id);
        EveId calculationId = ResolveCalculationId(modifier);
        if (modifier.Group != 0 && modifier.ContributionId.IsValid)
            throw new ArgumentException("Contribution ID is only valid for group 0.", nameof(modifier));
        if (modifier.Group == 0)
        {
            if (IsBaseStatId(modifier.Id))
            {
                if (!TryGetBase(modifier.Id, out _))
                    throw new ArgumentException("Group 0 requires the registered base/resource stat on this entity.", nameof(modifier));
            }
            ResolveContributionId(modifier);
        }
        calculations.GetEffective(calculationId).Create().ValidateModifier(this, modifier);
    }

    // Registered stats remain base-backed even when this entity lacks their entry.
    public virtual bool IsBaseStatId(EveId id) => Definitions.TryGet(ResolveIdentity(id), out _);

    // EN: Filter routing before merging IDs/groups; every invocation gets a new calculator instance.
    // VI: Lọc định tuyến trước khi gộp ID/nhóm; mỗi lời gọi có instance bộ tính mới.
    public virtual double Calculate(EveId calculationId, double baseValue, params EveId[] ids)
        => CalculateCore(calculationId, baseValue, ids, null);

    // EN: `carried` are extra modifiers (a hit's penetration) for this call only. When none selects one of `ids` the ordinary cached path runs unchanged;
    //     otherwise result caches are bypassed, the prepared own-modifier cache is still used, and nothing is stored on this entity.
    // VI: `carried` là modifier thêm (penetration của đòn) chỉ cho lần gọi này. Nếu không cái nào chọn `ids` thì chạy đường cache thường như cũ;
    //     ngược lại bỏ qua cache kết quả, vẫn dùng cache modifier của chính entity, và không lưu gì lên entity này.
    private double CalculateCore(EveId calculationId, double baseValue, EveId[] ids, IReadOnlyList<EveStatModifier> carried)
    {
        using (EveEngine.EveHotPathMetrics.Measure("stat.calculate"))
        {
        if (double.IsNaN(baseValue) || double.IsInfinity(baseValue)) throw new ArgumentOutOfRangeException(nameof(baseValue));
        calculationId = calculations.ResolveIdentity(calculationId);
        var definition = calculations.GetEffective(calculationId);
        var calculation = definition.Create();
        if (ids == null) throw new ArgumentNullException(nameof(ids));
        bool ordinary = OrdinaryEntries;
        bool pure = ordinary && definition.IsCurrentIndependentBuiltIn;
        bool withCarried = false;
        bool small = ordinary && ids.Length > 0 && ids.Length <= 2;
        (EveId, double, EveId, EveId, bool, int) smallKey = default;
        (EveId, double, string, bool) cacheKey = default;
        HashSet<EveId> selected = null;
        PreparedInputs prepared = null;
        (EveId, EveId, EveId, bool, int) smallPreparationKey = default;
        (EveId, string, bool) preparationKey = default;
        if (small)
        {
            var first = ResolveIdentity(ids[0]); var second = ids.Length == 2 ? ResolveIdentity(ids[1]) : default;
            int count = ids.Length == 1 || first == second ? 1 : 2;
            if (count == 2 && first.CompareTo(second) > 0) { var swap = first; first = second; second = swap; }
            if (count == 1) second = default;
            ValidateEvaluationId(first); if (count == 2) ValidateEvaluationId(second);
            if (carried != null && HasCarriedMatch(carried, first, second, count, null)) { withCarried = true; pure = false; }
            smallKey =(calculationId, baseValue, first, second, evaluatingStat, count);
            if (pure && smallCalculations.TryGetValue(smallKey, out var cached)) return cached;
            smallPreparationKey = (calculationId, first, second, evaluatingStat, count);
            if (!smallPreparedModifiers.TryGetValue(smallPreparationKey, out prepared))
            { selected = new HashSet<EveId> { first }; if (count == 2) selected.Add(second); }
        }
        else
        {
            selected = new HashSet<EveId>(ids.Select(ResolveIdentity));
            if (selected.Count == 0) throw new ArgumentException("At least one ID is required.", nameof(ids));
            foreach (var id in selected) ValidateEvaluationId(id);
            if (carried != null && HasCarriedMatch(carried, default, default, 0, selected)) { withCarried = true; pure = false; }
            cacheKey =(calculationId, baseValue, pure ? string.Join("\u001d", selected.OrderBy(x => x).Select(x => x.ToString())) : "", evaluatingStat);
            if (pure && pureCalculations.TryGetValue(cacheKey, out var cached)) return cached;
            preparationKey = (calculationId, string.Join("\u001d", selected.OrderBy(id => id).Select(x => x.ToString())), evaluatingStat);
            if (ordinary) preparedModifiers.TryGetValue(preparationKey, out prepared);
        }
        if (prepared == null)
        {
            var modifiers = SourceSnapshot().OrderBy(pair => pair.Key).SelectMany(pair => pair.Value)
                .Where(modifier => ResolveCalculationId(modifier) == calculationId && selected.Contains(ResolveIdentity(modifier.Id)))
                .Where(modifier => !evaluatingStat || modifier.Group != 0 || IsBaseStatId(modifier.Id)).ToArray();
            prepared = new PreparedInputs(selected.OrderBy(id => id).ToArray(), modifiers);
            if (ordinary)
            {
                if (preparedModifiers.Count + smallPreparedModifiers.Count >= 128) { preparedModifiers.Clear(); smallPreparedModifiers.Clear(); }
                if (small) smallPreparedModifiers[smallPreparationKey] = prepared; else preparedModifiers[preparationKey] = prepared;
            }
        }
        bool previousEvaluation = evaluatingStat;
        double result;
        // A calculator may make nested raw Calculate calls; those are not stat evaluation.
        evaluatingStat = false;
        try
        {
            IReadOnlyList<EveStatModifier> inputs = prepared.Modifiers;
            if (withCarried)
            {
                // One exact-size array: own prepared modifiers first, then the carried ones this entity would select. Group sums do not depend on order.
                bool statEvaluation = previousEvaluation;
                int extra = 0;
                for (int i = 0; i < carried.Count; i++) if (CarriedSelected(carried[i], prepared.Ids, statEvaluation)) extra++;
                var merged = new EveStatModifier[prepared.Modifiers.Count + extra]; int used = 0;
                for (int i = 0; i < prepared.Modifiers.Count; i++) merged[used++] = prepared.Modifiers[i];
                for (int i = 0; i < carried.Count; i++)
                    if (CarriedSelected(carried[i], prepared.Ids, statEvaluation)) merged[used++] = carried[i];
                inputs = merged;
            }
            result = calculation.Calculate(new PNStatCalculationContext(this, calculationId, baseValue,
                prepared.Ids, inputs, true));
        }
        finally { evaluatingStat = previousEvaluation; }
        if (double.IsNaN(result) || double.IsInfinity(result)) throw new OverflowException("Calculation returned a non-finite value.");
        if (pure) { if (pureCalculations.Count + smallCalculations.Count >= 128) { pureCalculations.Clear(); smallCalculations.Clear(); } if (small) smallCalculations[smallKey] = result; else pureCalculations[cacheKey] = result; }
        return result;
    
        }
    }

    protected override double EvaluateUncached(HashSet<EveId> selected)
    {
        double baseValue = 0d;
        foreach (var id in selected.OrderBy(id => id))
            if (TryGetBase(id, out var stat)) baseValue += stat.Value;
        if (baseValue > double.MaxValue || baseValue < -double.MaxValue) throw new OverflowException("Stat base exceeded the double range.");
        bool previousEvaluation = evaluatingStat;
        double result;
        evaluatingStat = true;
        try { result = Calculate(DefaultCalculationId, (double)baseValue, selected.ToArray()); }
        finally { evaluatingStat = previousEvaluation; }
        return selected.Count == 1 && (selected.Contains(PNContentIds.Defense) || selected.Contains(PNContentIds.CritRate) || selected.Contains(PNContentIds.CooldownReduction) ||
            selected.Contains(PNContentIds.Aggro)) ? result : Math.Max(0d, result);
    }

    // EN: Incoming damage already includes outgoing bonuses; apply defense, then tagged mitigation without changing HP.
    // VI: Damage đầu vào đã gồm bonus phía gây; áp DEF rồi kháng theo tag, không thay đổi HP.
    public double CalculateDamage(double damage, params EveId[] tags)
        => CalculateDamage(damage, noModifiers, tags);

    // EN: Same as above; `penetration` are the sender's penetration-route modifiers carried by the hit. They join this entity's own modifiers for this call only:
    //     defense-targeted ones in the defense evaluation, tag-targeted ones in the Mitigation selected by the hit's tags. Nothing is stored on this entity.
    // VI: Như trên; `penetration` là modifier đường penetration của bên gây đi kèm đòn. Chúng chỉ nhập vào modifier của entity này trong lần tính này:
    //     loại nhắm DEF vào phép tính DEF, loại nhắm tag vào Mitigation chọn theo tag của đòn. Không lưu gì lên entity này.
    public virtual double CalculateDamage(double damage, IReadOnlyList<EveStatModifier> penetration, params EveId[] tags)
    {
        using (EveHotPathMetrics.Measure("combat.calculate-incoming"))
        {
        if (double.IsNaN(damage) || double.IsInfinity(damage) || damage < 0d)
            throw new ArgumentOutOfRangeException(nameof(damage));
        if (penetration == null) throw new ArgumentNullException(nameof(penetration));
        var type = ResolveDamageType(tags);
        var selected = PrepareDamageTags(tags);
        double defense = type.DefenseFactor == 0d ? 0d : penetration.Count == 0 ? Evaluate(PNContentIds.Defense) : EvaluateDefenseWith(penetration);
        double afterDefense = damageCalculation.Calculate(damage, defense, type);
        var ids = GetType() == typeof(PNStatSet) ? selected.Values : (EveId[])selected.Values.Clone();
        return penetration.Count == 0 ? Calculate(PNContentIds.Mitigation, afterDefense, ids) : CalculateCore(PNContentIds.Mitigation, afterDefense, ids, penetration);
        }
    }

    // Defense is signed and base-backed, so this mirrors EvaluateUncached for the single defense id; the cached Evaluate path is never touched.
    private static readonly EveId[] defenseIds = { PNContentIds.Defense };
    private double EvaluateDefenseWith(IReadOnlyList<EveStatModifier> carried)
    {
        bool any = false;
        for (int i = 0; i < carried.Count && !any; i++) any = ResolveIdentity(carried[i].Id) == PNContentIds.Defense;
        if (!any) return Evaluate(PNContentIds.Defense);
        double baseValue = TryGetBase(PNContentIds.Defense, out var stat) ? stat.Value : 0d;
        if (baseValue > double.MaxValue || baseValue < -double.MaxValue) throw new OverflowException("Stat base exceeded the double range.");
        bool previousEvaluation = evaluatingStat;
        evaluatingStat = true;
        try { return CalculateCore(DefaultCalculationId, baseValue, defenseIds, carried); }
        finally { evaluatingStat = previousEvaluation; }
    }

    // Allocation-free: does any carried modifier select one of the call's IDs (small: first/second, else the selected set)?
    private bool HasCarriedMatch(IReadOnlyList<EveStatModifier> carried, EveId first, EveId second, int count, HashSet<EveId> selected)
    {
        for (int i = 0; i < carried.Count; i++)
        {
            var modifier = carried[i]; var id = ResolveIdentity(modifier.Id);
            bool hit = selected != null ? selected.Contains(id) : id == first || (count == 2 && id == second);
            if (hit && (!evaluatingStat || modifier.Group != 0 || IsBaseStatId(modifier.Id))) return true;
        }
        return false;
    }
    private bool CarriedSelected(EveStatModifier modifier, IReadOnlyList<EveId> ids, bool statEvaluation)
    {
        var id = ResolveIdentity(modifier.Id); bool hit = false;
        for (int i = 0; i < ids.Count && !hit; i++) hit = ids[i] == id;
        return hit && (!statEvaluation || modifier.Group != 0 || IsBaseStatId(modifier.Id));
    }
    private static readonly PNDamageCalculation damageCalculation = new PNDamageCalculation();
    private readonly Dictionary<string, PNCombatTags> combatTags = new Dictionary<string, PNCombatTags>();
    private readonly Dictionary<(EveId, EveId, int), PNCombatTags> smallCombatTags = new Dictionary<(EveId, EveId, int), PNCombatTags>();
    internal PNCombatTags PrepareDamageTags(IEnumerable<EveId> ids)
    {
        if (ids == null) throw new ArgumentNullException(nameof(ids));
        if (GetType() == typeof(PNStatSet) && ids is IReadOnlyList<EveId> list && list.Count > 0 && list.Count <= 2)
        {
            var first = ResolveIdentity(list[0]); var second = list.Count == 2 ? ResolveIdentity(list[1]) : default;
            int count = list.Count == 1 || first == second ? 1 : 2;
            if (count == 2 && first.CompareTo(second) > 0) { var swap = first; first = second; second = swap; }
            if (count == 1) second = default;
            var smallKey = (first, second, count);
            if (smallCombatTags.TryGetValue(smallKey, out var known)) return known;
            var values = count == 1 ? new[] { first } : new[] { first, second };
            ResolveDamageType(values);
            var prepared = new PNCombatTags(values);
            if (smallCombatTags.Count >= 128) smallCombatTags.Clear(); smallCombatTags[smallKey] = prepared;
            return prepared;
        }
        var canonical = ids.Select(ResolveIdentity).Distinct().OrderBy(x => x).ToArray();
        var key = string.Join("\u001d", canonical.Select(x => x.ToString()));
        if (GetType() == typeof(PNStatSet) && combatTags.TryGetValue(key, out var found)) return found;
        ResolveDamageType(canonical);
        var value = new PNCombatTags(canonical);
        if (GetType() == typeof(PNStatSet)) { if (combatTags.Count >= 128) combatTags.Clear(); combatTags[key] = value; }
        return value;
    }

    // EN: Both send and receive validate damage tags, independently of modifier calculation routes.
    // VI: Cả gửi và nhận đều kiểm tra tag damage, độc lập với đường tính modifier.
    public virtual PNDamageTypeDefinition ResolveDamageType(IEnumerable<EveId> damageTags)
    {
        if (damageTags == null) throw new ArgumentNullException(nameof(damageTags));
        if (GetType() == typeof(PNStatSet) && damageTags is IReadOnlyList<EveId> list && list.Count > 0 && list.Count <= 2)
        {
            var first = ResolveIdentity(list[0]); var second = list.Count == 2 ? ResolveIdentity(list[1]) : default;
            int count = list.Count == 1 || first == second ? 1 : 2;
            if (count == 2 && first.CompareTo(second) > 0) { var swap = first; first = second; second = swap; }
            if (count == 1) second = default;
            var key = (first, second, count);
            if (smallDamageTypes.TryGetValue(key, out var known)) return known;
            if (this.tags == null) throw new InvalidOperationException("Damage requires a tag registry.");
            this.tags.Get(first); if (count == 2) this.tags.Get(second);
            bool one = damageTypes != null && damageTypes.TryGet(first, out _), two = count == 2 && damageTypes != null && damageTypes.TryGet(second, out _);
            if (one == two) throw new ArgumentException("Damage requires exactly one registered damage type tag.", nameof(damageTags));
            var type = damageTypes.GetEffective(one ? first : second);
            if (smallDamageTypes.Count >= 128) smallDamageTypes.Clear(); smallDamageTypes[key] = type; return type;
        }
        var selected = new HashSet<EveId>(damageTags.Select(ResolveIdentity));
        if (this.tags == null) throw new InvalidOperationException("Damage requires a tag registry.");
        foreach (var id in selected) this.tags.Get(id);
        var types = selected.Where(id => damageTypes != null && damageTypes.TryGet(id, out _)).ToArray();
        if (types.Length != 1) throw new ArgumentException("Damage requires exactly one registered damage type tag.", nameof(damageTags));
        return damageTypes.GetEffective(types[0]);
    }

    protected override double ResolveGroupMultiplier(int group, double percent)
        => Math.Max(0d, base.ResolveGroupMultiplier(group, percent));

    public virtual double ResolveCalculationMultiplier(int group, double percent) => ResolveGroupMultiplier(group, percent);

}

// EN: Defines this game's stat lifetime rules when moving between floors.
// VI: Định nghĩa quy tắc vòng đời chỉ số của game này khi chuyển ải.
public partial class PNStat : EveStat, EveIEntityLoadPreparation
{
    public void PrepareEntityLoad()
    {
        if (GetComponent<PNControlManager>() == null) gameObject.AddComponent<PNControlManager>();
        if (GetComponent<PNBuffManager>() == null) gameObject.AddComponent<PNBuffManager>();
        if (GetComponent<PNStatReplication>() == null) gameObject.AddComponent<PNStatReplication>();
    }
    private readonly HashSet<EveId> persistentSourceIds = new HashSet<EveId>();
    private readonly HashSet<EveId> buffSourceIds = new HashSet<EveId>();

    public override void Unload()
    {
#if UNITY_5_3_OR_NEWER
        RetireDamagePopupPool();
#endif
        ClearCombatListeners();
        combatLoader = null;
        persistentSourceIds.Clear();
        buffSourceIds.Clear();
        ResetTargetLayers();
        HasHealth = false; DamageMode = PNDamageMode.Normal; HealthBar = PNHealthBarMode.Auto; AggroMode = PNAggroMode.Normal;
        CharacterId = default;
        base.Unload();
    }

    public virtual EveId CharacterId { get; protected set; }
    public virtual Guid SessionId
    {
        get
        {
            var identity = loadContext?.Identity ?? GetComponent<EveEntityIdentity>();
            if (identity == null || !EveRegistrySession.IsReady ||
                identity.SessionId == Guid.Empty || identity.SessionId != EveRegistrySession.Context.SessionId)
                throw new InvalidOperationException("Floor snapshots require an entity bound to the active session.");
            return identity.SessionId;
        }
    }
    public virtual EveEntityId EntityId
    {
        get
        {
            EveEntityIdentity identity = loadContext?.Identity ?? GetComponent<EveEntityIdentity>();
            if (identity == null) throw new InvalidOperationException("EveEntityIdentity component is required for floor snapshots.");
            return identity.Id;
        }
    }

    protected override void LoadDefinition(EveRegistryHub registry, EveId id)
    {
        if (IsInitialized) return;
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (!registry.IsSealed) throw new InvalidOperationException("Content registry must be sealed before loading stats.");
        Initialize(registry.Characters.Get(id), registry);
    }

    // EN: Standalone initialization takes the full hub so calculation/mod registries remain available.
    // VI: Khởi tạo độc lập nhận đủ hub để dùng registry đường tính/mod.
    public virtual void Initialize(EveCharacterDefinition character, EveRegistryHub registry)
    {
        if (character == null) throw new ArgumentNullException(nameof(character));
        if (IsInitialized) throw new InvalidOperationException("EveStat is already initialized.");
        var combat = PNCombatSettings.Parse(character.Data);
        var body = PNBodySettings.Parse(character.Data);
        var gameSet = new PNStatSet(registry);
        gameSet.LoadCharacter(character);
        Initialize(gameSet);
        if (combat != null) SetBaseTargetLayers(combat.TargetLayers);
        DamageMode = body.DamageMode; HealthBar = body.HealthBar; AggroMode = body.Aggro;
        CharacterId = character.Id;
    }

    public override void Initialize(EveStatSet statSet)
    {
        base.Initialize(statSet);
        HasHealth = statSet.TryGetBase(PNContentIds.Health, out var health) && health is EveResourceStat;
        DamageMode = PNDamageMode.Normal; HealthBar = PNHealthBarMode.Auto; AggroMode = PNAggroMode.Normal;
        SetBaseTargetLayers(DefaultTargetLayers);
        InitializeCombatAbilities();
    }

    public override void Initialize(EveCharacterDefinition character, EveRegistry<EveStatDefinition> definitions)
    {
        if (!EveRegistrySession.IsReady || !ReferenceEquals(EveRegistrySession.Current.Stats, definitions))
            throw new InvalidOperationException("Use Initialize(character, registryHub) outside the active session.");
        Initialize(character, EveRegistrySession.Current);
    }

    public virtual double Calculate(EveId calculationId, double baseValue, params EveId[] ids)
        => ((PNStatSet)Set).Calculate(calculationId, baseValue, ids);

    public double CalculateDamage(double damage, params EveId[] tags)
        => ((PNStatSet)Set).CalculateDamage(damage, tags);
    public virtual double CalculateDamage(double damage, IReadOnlyList<EveStatModifier> penetration, params EveId[] tags)
        => ((PNStatSet)Set).CalculateDamage(damage, penetration, tags);

    // EN: Ordinary SetSource calls are floor-local; mark equipment or lasting effects explicitly.
    // VI: SetSource thông thường chỉ có hiệu lực trong ải; đánh dấu trang bị hoặc hiệu ứng lâu dài riêng.
    public virtual void SetPersistentSource(EveId sourceId, IEnumerable<EveStatModifier> modifiers)
    {
        if (buffSourceIds.Contains(sourceId)) throw new InvalidOperationException("A Buff-owned source cannot become a floor snapshot source.");
        var values = new List<EveStatModifier>(modifiers ?? throw new ArgumentNullException(nameof(modifiers)));
        SetSource(sourceId, values);
        if (values.Count == 0) persistentSourceIds.Remove(sourceId);
        else persistentSourceIds.Add(sourceId);
    }

    public override bool RemoveSource(EveId sourceId)
    {
        bool removed = base.RemoveSource(sourceId);
        persistentSourceIds.Remove(sourceId);
        buffSourceIds.Remove(sourceId);
        return removed;
    }

    // EN: Remove floor-local sources; live Buff-owned sources retain their own lifecycle and are not serialized.
    // VI: Gỡ nguồn thuộc tầng; nguồn Buff đang sống giữ vòng đời riêng và không được ghi vào snapshot.
    public virtual PNStatFloorSnapshot EndFloorAndCaptureSnapshot()
    {
        // EN: Validate the session before removing any temporary sources.
        // VI: Kiểm tra phiên trước khi gỡ bất kỳ nguồn tạm nào.
        Guid sessionId = SessionId;
        EveEntityId entityId = EntityId;
        foreach (EveId sourceId in Set.SourceSnapshot().Keys.ToArray())
            if (!persistentSourceIds.Contains(sourceId) && !buffSourceIds.Contains(sourceId)) RemoveSource(sourceId);

        var currents = new Dictionary<EveId, double>();
        foreach (var pair in Set.BaseSnapshot())
            if (pair.Value is EveResourceStat) currents.Add(pair.Key, GetCurrent(pair.Key));

        var persistent = new Dictionary<EveId, IReadOnlyList<EveStatModifier>>();
        foreach (var pair in Set.SourceSnapshot())
            if (persistentSourceIds.Contains(pair.Key))
                persistent.Add(pair.Key, Array.AsReadOnly(pair.Value
                    .Select(modifier => modifier.Clone()).ToArray()));

        return new PNStatFloorSnapshot(sessionId, entityId, CharacterId, currents, persistent);
    }

    // EN: Restore lasting sources before currents; temporary buffs are absent from the snapshot.
    // VI: Khôi phục nguồn lâu dài trước Current; snapshot không chứa buff tạm.
    public virtual void RestoreFloorSnapshot(PNStatFloorSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.SessionId != SessionId)
            throw new ArgumentException("Floor snapshot belongs to another session.", nameof(snapshot));
        if (snapshot.CharacterId != CharacterId)
            throw new ArgumentException("Floor snapshot belongs to another character.", nameof(snapshot));
        if (snapshot.EntityId != EntityId)
            throw new ArgumentException("Floor snapshot belongs to another entity instance.", nameof(snapshot));
        if (Set.SourceSnapshot().Count != 0)
            throw new InvalidOperationException("Restore into a new stat instance before adding sources.");

        foreach (var pair in snapshot.PersistentSources)
            SetPersistentSource(pair.Key, pair.Value);
        foreach (var pair in snapshot.ResourceCurrents)
            SetCurrent(pair.Key, pair.Value);
    }
}
