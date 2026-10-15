using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Action-owned value: raw amount or a percentage of the caster's evaluated stat.
// VI: Giá trị thuộc action: amount thô hoặc phần trăm stat đã Evaluate của người thi triển.
public class PNSkillValue
{
    public virtual double Amount { get; }
    public virtual EveId StatId { get; }

    public PNSkillValue(double amount, EveId statId = default)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount)) throw new ArgumentOutOfRangeException(nameof(amount));
        Amount = amount;
        StatId = statId;
    }

    public static PNSkillValue Parse(JToken data)
    {
        if (!(data is JObject value)) throw new FormatException("Value must be an object.");
        foreach (var field in value.Properties())
            if (field.Name != "amount" && field.Name != "statId") throw new FormatException($"Unknown value field: {field.Name}.");
        var amount = value["amount"];
        if (amount == null || (amount.Type != JTokenType.Integer && amount.Type != JTokenType.Float))
            throw new FormatException("Value amount must be a number.");
        var stat = value["statId"];
        if (stat != null && stat.Type != JTokenType.String) throw new FormatException("Value statId must be an ID string when supplied.");
        return new PNSkillValue(amount.Value<double>(), stat == null ? default : EveId.Parse(stat.Value<string>()));
    }

    public virtual double Evaluate(PNStat caster)
    {
        if (!StatId.IsValid) return Amount;
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (!caster.IsReady || !(caster.Set is PNStatSet stats)) throw new InvalidOperationException("Scaling requires initialized game stats.");
        stats.Registry.Stats.Get(StatId);
        if (!stats.TryGetBase(StatId, out _)) throw new InvalidOperationException($"Caster has no base/resource stat {StatId}.");
        double value = (double)Amount / 100d * caster.Evaluate(StatId);
        if (double.IsNaN(value) || double.IsInfinity(value) || value > double.MaxValue || value < -double.MaxValue)
            throw new OverflowException("Evaluated skill value exceeded the double range.");
        return (double)value;
    }
}
