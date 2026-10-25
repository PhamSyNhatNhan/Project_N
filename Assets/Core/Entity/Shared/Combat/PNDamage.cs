using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Immutable outgoing damage already includes sender bonuses; receiving never applies them again.
// VI: Damage gửi bất biến đã gồm bonus phía gây; phía nhận không áp lại bonus đó.
internal sealed class PNCombatTags
{
    internal readonly EveId[] Values;
    internal readonly IReadOnlyList<EveId> ReadOnly;
    internal PNCombatTags(EveId[] owned) { Values = owned; ReadOnly = Array.AsReadOnly(owned); }
}
// EN: Outgoing damage computed before the critical roll. Immutable; created only by a combat snapshot.
// VI: Sát thương gây ra đã tính trước lúc tung crit. Bất biến; chỉ snapshot chiến đấu tạo được.
public sealed class PNPreparedDamage
{
    internal readonly PNCombatTags Canonical;
    public Guid SessionId { get; }
    public EveEntityId SourceId { get; }
    // Outgoing amount after the Scale route and before any critical multiplier.
    public double Amount { get; }
    public bool CanCrit { get; }
    public double CritRate { get; }
    public double CritDamage { get; }
    public double? IFrameDuration { get; }
    // The sender's penetration-route modifiers, applied by the receiver as its own for this hit (empty when none).
    public IReadOnlyList<EveStatModifier> Penetration { get; }
    public IReadOnlyList<EveId> Tags => Canonical.ReadOnly;
    internal PNPreparedDamage(Guid sessionId, EveEntityId sourceId, double amount, PNCombatTags tags,
        double? iframeDuration, bool canCrit, double critRate, double critDamage, IReadOnlyList<EveStatModifier> penetration = null)
    {
        if (tags == null) throw new ArgumentNullException(nameof(tags));
        if (iframeDuration.HasValue && (double.IsNaN(iframeDuration.Value) || double.IsInfinity(iframeDuration.Value)))
            throw new ArgumentOutOfRangeException(nameof(iframeDuration));
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0d) throw new ArgumentOutOfRangeException(nameof(amount));
        if (double.IsNaN(critRate) || double.IsInfinity(critRate) || double.IsNaN(critDamage) || double.IsInfinity(critDamage) || critDamage < 0d)
            throw new ArgumentException("Invalid critical-hit snapshot.");
        SessionId = sessionId; SourceId = sourceId; Amount = amount; Canonical = tags;
        IFrameDuration = iframeDuration; CanCrit = canCrit; CritRate = critRate; CritDamage = critDamage;
        Penetration = penetration ?? PNDamage.NoPenetration;
    }
}
public sealed class PNDamage
{
    private readonly PNCombatTags preparedTags;
    public Guid SessionId { get; }
    public EveEntityId SourceId { get; }
    public double Amount { get; }
    public IReadOnlyList<EveId> Tags { get; }
    public bool CanCrit { get; }
    public bool IsCritical { get; }
    public double CritRate { get; }
    public double CritDamage { get; }
    public double? IFrameDuration { get; }
    public IReadOnlyList<EveStatModifier> Penetration { get; }

    public PNDamage(Guid sessionId, EveEntityId sourceId, double amount, IEnumerable<EveId> tags,
        bool canCrit = false, bool isCritical = false, double critRate = 0d, double critDamage = 150d)
        : this(sessionId, sourceId, amount, tags, null, canCrit, isCritical, critRate, critDamage) { }

    public PNDamage(Guid sessionId, EveEntityId sourceId, double amount, IEnumerable<EveId> tags,
        double? iframeDuration, bool canCrit = false, bool isCritical = false, double critRate = 0d, double critDamage = 150d)
        : this(sessionId, sourceId, amount, tags, iframeDuration, canCrit, isCritical, critRate, critDamage, null) { }
    internal PNDamage(Guid sessionId, EveEntityId sourceId, double amount, PNCombatTags tags,
        double? iframeDuration, bool canCrit, bool isCritical, double critRate, double critDamage, IReadOnlyList<EveStatModifier> penetration = null)
        : this(sessionId, sourceId, amount, tags.ReadOnly, iframeDuration, canCrit, isCritical, critRate, critDamage, tags, penetration) { }
    private PNDamage(Guid sessionId, EveEntityId sourceId, double amount, IEnumerable<EveId> tags,
        double? iframeDuration, bool canCrit, bool isCritical, double critRate, double critDamage, PNCombatTags prepared,
        IReadOnlyList<EveStatModifier> penetration = null)
    {
        if (iframeDuration.HasValue && (double.IsNaN(iframeDuration.Value) || double.IsInfinity(iframeDuration.Value)))
            throw new ArgumentOutOfRangeException(nameof(iframeDuration));
        if (sessionId == Guid.Empty) throw new ArgumentException("Damage session is required.", nameof(sessionId));
        if (!sourceId.IsValid) throw new ArgumentException("Damage source entity is required.", nameof(sourceId));
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0d)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (tags == null) throw new ArgumentNullException(nameof(tags));
        if (double.IsNaN(critRate) || double.IsInfinity(critRate) || double.IsNaN(critDamage) ||
            double.IsInfinity(critDamage) || critDamage < 0d || (isCritical && !canCrit))
            throw new ArgumentException("Invalid critical-hit snapshot.");
        if (prepared == null)
        {
            var ids = new HashSet<EveId>(tags);
            if (ids.Count == 0 || ids.Any(id => !id.IsValid)) throw new ArgumentException("Damage requires valid tags.", nameof(tags));
            prepared = new PNCombatTags(ids.OrderBy(id => id).ToArray());
        }
        SessionId = sessionId;
        SourceId = sourceId;
        Amount = amount;
        IFrameDuration = iframeDuration;
        CanCrit = canCrit; IsCritical = isCritical; CritRate = critRate; CritDamage = critDamage;
        preparedTags = prepared; Tags = prepared.ReadOnly;
        Penetration = penetration ?? NoPenetration;
    }

    internal static readonly IReadOnlyList<EveStatModifier> NoPenetration = Array.AsReadOnly(new EveStatModifier[0]);

    public PNDamage WithIFrameDuration(double? duration)
        => new PNDamage(SessionId, SourceId, Amount, preparedTags, duration, CanCrit, IsCritical, CritRate, CritDamage, Penetration);

    // EN: Copy carrying the given penetration-route modifiers (stored as an immutable copy). Packets from the public constructors carry none.
    // VI: Bản sao mang các modifier đường penetration cho trước (lưu bản sao bất biến). Gói tạo từ constructor public không mang gì.
    public PNDamage WithPenetration(IEnumerable<EveStatModifier> penetration)
    {
        if (penetration == null) throw new ArgumentNullException(nameof(penetration));
        var copy = penetration.Select(modifier => modifier?.Clone() ?? throw new ArgumentException("Penetration modifiers cannot be null.", nameof(penetration))).ToArray();
        return new PNDamage(SessionId, SourceId, Amount, preparedTags, IFrameDuration, CanCrit, IsCritical, CritRate, CritDamage,
            copy.Length == 0 ? NoPenetration : Array.AsReadOnly(copy));
    }

    // Omission uses receiver fallback; explicit zero and signed durations remain distinct.
    public static double? ParseIFrameDuration(JObject payload)
    {
        var token = payload?["iframeDuration"];
        if (token == null) return null;
        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            throw new FormatException("iframeDuration must be a finite signed number.");
        double value = token.Value<double>();
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("iframeDuration must be finite.");
        return value;
    }
}

// EN: Distinguishes calculated damage from actual HP loss, including overkill and the death transition.
// VI: Phân biệt damage tính ra và HP thực mất, gồm overkill và thời điểm chuyển sang chết.
// EN: What a receiver resource (lorica) takes from one hit besides health; `default` = nothing.
// VI: Phần resource khác (lorica) của người nhận hấp thụ trong một đòn, ngoài máu; `default` = không có.
public struct PNDamageAbsorption
{
    public double LoricaBefore, LoricaAfter;
    public bool LoricaBroke;
}

public sealed class PNDamageResult
{
    public PNDamage Damage { get; }
    public bool IsCritical => Damage.IsCritical;
    public EveEntityId TargetId { get; }
    public double CalculatedDamage { get; }
    public double AppliedDamage => HealthBefore - HealthAfter;
    public double HealthBefore { get; }
    public double HealthAfter { get; }
    public bool Killed => HealthBefore > 0d && HealthAfter == 0d;
    public double IFrameMultiplier { get; }
    public double IFrameBefore { get; }
    public double IFrameAfter { get; }
    public double ResolvedIFrameDuration { get; }
    // Lorica part of the hit (all zero/false for receivers without lorica, or while it is down, or for true damage).
    public double LoricaBefore { get; }
    public double LoricaAfter { get; }
    public double LoricaAbsorbed => LoricaBefore - LoricaAfter;
    public bool LoricaBroke { get; }
    // True when the receiver has no health resource: nothing was applied (no iframe, popup or event); Killed is false.
    public bool Ignored { get; }

    internal PNDamageResult(PNDamage damage, EveEntityId targetId, double calculatedDamage,
        double healthBefore, double healthAfter, double iframeMultiplier = 1d,
        double iframeBefore = 0d, double iframeAfter = 0d, double resolvedIFrameDuration = 0d, PNDamageAbsorption absorption = default, bool ignored = false)
    {
        Ignored = ignored;
        LoricaBefore = absorption.LoricaBefore; LoricaAfter = absorption.LoricaAfter; LoricaBroke = absorption.LoricaBroke;
        Damage = damage;
        TargetId = targetId;
        CalculatedDamage = calculatedDamage;
        HealthBefore = healthBefore;
        HealthAfter = healthAfter;
        IFrameMultiplier = iframeMultiplier; IFrameBefore = iframeBefore;
        IFrameAfter = iframeAfter; ResolvedIFrameDuration = resolvedIFrameDuration;
    }
}
