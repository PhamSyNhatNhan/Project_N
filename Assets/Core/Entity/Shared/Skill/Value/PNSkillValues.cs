using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// Value components retain order and identity. Consumers choose the formula and stat source.
public class PNSkillValues
{
    public IReadOnlyList<PNSkillValue> Components { get; }
    public int Count => Components.Count;
    public PNSkillValue this[int index] => Components[index];

    public PNSkillValues(IEnumerable<PNSkillValue> components)
    {
        var values = (components ?? throw new ArgumentNullException(nameof(components))).ToArray();
        if (values.Length == 0 || values.Any(value => value == null))
            throw new ArgumentException("Value requires at least one non-null component.", nameof(components));
        Components = Array.AsReadOnly(values);
    }

    public static PNSkillValues Parse(JToken data)
    {
        if (data is JArray array) return new PNSkillValues(array.Select(PNSkillValue.Parse));
        return new PNSkillValues(new[] { PNSkillValue.Parse(data) });
    }

    // Compatibility access is explicit; a multi-component value is never silently truncated.
    public PNSkillValue RequireSingle()
    {
        if (Count != 1) throw new InvalidOperationException("This consumer requires one value component.");
        return Components[0];
    }

    public virtual IReadOnlyList<double> EvaluateEach(PNStat source)
        => Array.AsReadOnly(Components.Select(value => value.Evaluate(source)).ToArray());

    public void ValidateStats(EveRegistryHub hub)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        foreach (var value in Components)
            if (value.StatId.IsValid) hub.Stats.Get(value.StatId);
    }

    public void ValidateNonnegativeAmounts()
    {
        if (Components.Any(value => value.Amount < 0d))
            throw new FormatException("This consumer requires nonnegative value coefficients.");
    }
}
