using System;
using System.Collections.Generic;
using System.Linq;

// Immutable read model. It never owns/advances timers or changes gameplay state.
public sealed class PNAbilityDisplay
{
    public PNAbilityDefinition Definition { get; }
    public int Amount { get; }
    public int Maximum { get; }
    public int ActiveInstances { get; }
    public bool HasPermanentInstance { get; }
    public bool IsAvailable { get; }
    public double Remaining { get; }
    public double Progress { get; }
    private PNAbilityDisplay(PNAbilityDefinition definition, PNAbility[] instances)
    {
        Definition = definition;
        var active = instances.Where(a=>a.IsActive).ToArray();
        ActiveInstances = active.Length; HasPermanentInstance=active.Any(a=>!a.DurationRemaining.HasValue);
        Amount = instances.Sum(a=>a.Amount??0); Maximum=instances.FirstOrDefault()?.Maximum??definition.Maximum;
        if(definition.Kind==PNAbilityKind.State)
        {
            IsAvailable=ActiveInstances>0;
            Remaining=active.Where(a=>a.DurationRemaining.HasValue).Select(a=>a.DurationRemaining.Value).DefaultIfEmpty(0).Min();
            if(!IsAvailable) Remaining=instances.Select(a=>a.CooldownRemaining).DefaultIfEmpty(0).Max();
            Progress=IsAvailable ? (Remaining>0 ? Remaining/Math.Max(Remaining,definition.Duration??Remaining) : 1) : 0;
        }
        else
        {
            IsAvailable=Amount>0;
            Remaining=definition.Kind==PNAbilityKind.Cooldown ? instances.Select(a=>a.CooldownRemaining).DefaultIfEmpty(0).Max()
                : instances.Where(a=>a.DurationRemaining.HasValue).Select(a=>a.DurationRemaining.Value).DefaultIfEmpty(0).Min();
            Progress=Maximum>0?(double)Amount/Maximum:0;
        }
    }
    public static IReadOnlyList<PNAbilityDisplay> Read(PNAbilityCollection collection)
    {
        var instances=collection.Instances;
        return collection.Definitions.Where(d=>d.ShowInUI).OrderBy(d=>d.DisplayOrder).ThenBy(d=>d.Id)
            .Select(d=>new PNAbilityDisplay(d,instances.Where(a=>a.Id==d.Id).ToArray())).ToList().AsReadOnly();
    }
}
