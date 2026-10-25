using System;

// EN: How a chooser ranks the candidates of one target query. Autotarget is the player side (the player's setting): higher aggro first,
//     then the mode, then a fixed tie-break (lower absolute health, nearer, lower entity ID). Aggro is the enemy side (Terraria style):
//     distance minus the candidate's aggro, smallest first, then lower entity ID. Layers only filter; their order is not a priority.
// VI: Cách bên chọn xếp hạng ứng viên. Autotarget là phía người chơi (theo cài đặt): aggro cao hơn trước, rồi chế độ, rồi phá hoà cố định
//     (máu tuyệt đối thấp hơn, gần hơn, entity ID nhỏ hơn). Aggro là phía quái: khoảng cách trừ aggro của ứng viên, nhỏ nhất trước.
public enum PNTargetMode { Nearest, LowestHealth, LowestHealthPercent }
public enum PNTargetRuleKind { Autotarget, Aggro }

public readonly struct PNTargetRule : IEquatable<PNTargetRule>
{
    public PNTargetRuleKind Kind { get; }
    public PNTargetMode Mode { get; }
    private PNTargetRule(PNTargetRuleKind kind, PNTargetMode mode) { Kind = kind; Mode = mode; }
    // default(PNTargetRule) is Autotarget / Nearest.
    public static PNTargetRule Autotarget(PNTargetMode mode)
    {
        if (!Enum.IsDefined(typeof(PNTargetMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return new PNTargetRule(PNTargetRuleKind.Autotarget, mode);
    }
    public static PNTargetRule Aggro => new PNTargetRule(PNTargetRuleKind.Aggro, PNTargetMode.Nearest);
    public const string NearestId = "nearest", LowestHealthId = "lowest_health", LowestHealthPercentId = "lowest_health_percent";
    public static string ModeId(PNTargetMode mode) => mode == PNTargetMode.Nearest ? NearestId : mode == PNTargetMode.LowestHealth ? LowestHealthId :
        mode == PNTargetMode.LowestHealthPercent ? LowestHealthPercentId : throw new ArgumentOutOfRangeException(nameof(mode));
    public static PNTargetMode ParseMode(string id)
    {
        if (TryParseMode(id, out var mode)) return mode;
        throw new FormatException("Target mode must be nearest, lowest_health or lowest_health_percent.");
    }
    public static bool TryParseMode(string id, out PNTargetMode mode)
    {
        mode = PNTargetMode.Nearest;
        if (id == NearestId) return true;
        if (id == LowestHealthId) { mode = PNTargetMode.LowestHealth; return true; }
        if (id == LowestHealthPercentId) { mode = PNTargetMode.LowestHealthPercent; return true; }
        return false;
    }
    public bool Equals(PNTargetRule other) => Kind == other.Kind && Mode == other.Mode;
    public override bool Equals(object obj) => obj is PNTargetRule other && Equals(other);
    public override int GetHashCode() => (int)Kind * 8 + (int)Mode;
    public override string ToString() => Kind == PNTargetRuleKind.Aggro ? "aggro" : ModeId(Mode);
}

// Values of one candidate that a rule compares. Health is double.MaxValue and HealthMax 0 for an entity without a Health resource
// (it ranks after every measurable one in the health modes); Distance is in world meters, the same unit as aggro.
public struct PNTargetScore
{
    public double Distance, Health, HealthMax, Aggro;
    public long EntityId;
    public double HealthRatio => HealthMax > 0d ? Health / HealthMax : 1d;
}

public static class PNTargetRanking
{
    // The held target gives way to a nearer one only when the new distance is at most this fraction of the held one (20% nearer).
    public const double SwitchRatio = .8d;
    // True when a ranks strictly before b under the rule.
    public static bool Better(in PNTargetScore a, in PNTargetScore b, PNTargetRule rule)
    {
        if (rule.Kind == PNTargetRuleKind.Aggro)
        {
            double sa = a.Distance - a.Aggro, sb = b.Distance - b.Aggro;
            if (sa != sb) return sa < sb;
            return a.EntityId < b.EntityId;
        }
        if (a.Aggro != b.Aggro) return a.Aggro > b.Aggro;
        switch (rule.Mode)
        {
            case PNTargetMode.LowestHealth: if (a.Health != b.Health) return a.Health < b.Health; break;
            case PNTargetMode.LowestHealthPercent: { double ra = a.HealthRatio, rb = b.HealthRatio; if (ra != rb) return ra < rb; break; }
            default: if (a.Distance != b.Distance) return a.Distance < b.Distance; break;
        }
        if (a.Health != b.Health) return a.Health < b.Health;
        if (a.Distance != b.Distance) return a.Distance < b.Distance;
        return a.EntityId < b.EntityId;
    }
    // True when a valid held target should give way to the best candidate. Autotarget: a higher aggro takes over at once; at equal aggro the
    // Nearest mode switches only to a candidate at least 20% nearer and the health modes keep the held target until it dies or leaves the query.
    // Aggro: the score (distance - aggro) must be lower, and when both scores are positive at most 80% of the held one.
    public static bool ShouldSwitch(in PNTargetScore held, in PNTargetScore best, PNTargetRule rule)
    {
        if (best.EntityId == held.EntityId) return false;
        if (rule.Kind == PNTargetRuleKind.Aggro)
        {
            double sh = held.Distance - held.Aggro, sb = best.Distance - best.Aggro;
            if (!(sb < sh)) return false;
            return sh <= 0d || sb <= 0d || sb <= sh * SwitchRatio;
        }
        if (best.Aggro != held.Aggro) return best.Aggro > held.Aggro;
        return rule.Mode == PNTargetMode.Nearest && best.Distance <= held.Distance * SwitchRatio;
    }
}
