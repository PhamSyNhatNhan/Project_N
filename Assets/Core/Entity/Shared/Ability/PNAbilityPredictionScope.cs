using System;
using System.Collections.Generic;
using System.Linq;

// A cast's passive writes, composed over an independent authority image.
// Elapsed comes from actual Game/Entity/raw advancement, never wall time.
public sealed class PNAbilityPredictionScope : IDisposable
{
    internal sealed class Write
    {
        internal PNAbility Ability;
        internal PNAbilityReplicaItem Before, After;
        internal PNAbilityClockValues At;
        internal long Order;
    }
    private readonly PNAbilityCollection owner;
    internal readonly List<Write> Writes = new List<Write>();
    private readonly HashSet<PNAbility> touched = new HashSet<PNAbility>();
    private PNAbility pending;
    private PNAbilityReplicaItem before;
    private bool recording = true, rolledBack;
    internal long CastKey;
    internal PNAbilityPredictionScope(PNAbilityCollection owner) { this.owner = owner; }
    public bool IsRecording => recording;
    public int Count => touched.Count;
    internal void Record(PNAbility ability)
    {
        pending = null;
        if (!recording) return;
        if (Writes.Count >= 512) throw new InvalidOperationException("Prediction passive-write limit exceeded.");
        pending = ability; before = ability.CaptureReplica(0);
    }
    internal void Complete(PNAbility ability)
    {
        if (!recording || !ReferenceEquals(pending, ability)) return;
        pending = null; touched.Add(ability);
        Writes.Add(new Write { Ability = ability, Before = before, After = ability.CaptureReplica(0), At = owner.ClockValues, Order = owner.NextPredictionWrite() });
    }
    internal void ClearPending() { pending = null; }
    internal void AcceptImage() { Writes.Clear(); pending = null; }
    public void End() { if (!recording) return; recording = false; pending = null; owner.EndPrediction(this); }
    public void Dispose() => End();
    // Compatibility hooks: ApplyReplica now recomposes all overlays atomically.
    public void CaptureOverlay() { }
    public void Rebase() { }
    public void Rollback()
    {
        if (rolledBack) return;
        rolledBack = true; End(); Writes.Clear(); owner.RemovePrediction(this);
    }
}

public readonly struct PNAbilityClockValues
{
    public readonly double Game, Entity, Raw;
    public PNAbilityClockValues(double game, double entity, double raw)
    { PNAbilityDefinition.RequireSeconds(game, true); PNAbilityDefinition.RequireSeconds(entity, true); PNAbilityDefinition.RequireSeconds(raw, true); Game = game; Entity = entity; Raw = raw; }
    public double Read(PNAbilityClock clock) => clock == PNAbilityClock.Game ? Game : clock == PNAbilityClock.Entity ? Entity : Raw;
    internal double Since(PNAbilityClockValues at, PNAbilityClock clock) => Math.Max(0, Read(clock) - at.Read(clock));
}

public sealed partial class PNAbilityCollection
{
    private PNAbilityPredictionScope prediction;
    private readonly List<PNAbilityPredictionScope> predictionLayers = new List<PNAbilityPredictionScope>();
    private Dictionary<PNAbility, PNAbilityReplicaItem> predictionTruth;
    private PNAbilityClockValues truthAt;
    private long predictionWrite, predictionGrant;
    private bool composingPrediction;
    internal bool ComposingPrediction => composingPrediction;
    public PNAbilityClockValues ClockValues { get; private set; }
    private PNAbilityClockValues ReplicaTruthClocks;
    public bool IsPredicting => prediction != null;
    internal long NextPredictionGrant() => checked(--predictionGrant);
    internal long NextPredictionWrite() => checked(++predictionWrite);
    public PNAbilityPredictionScope BeginPrediction()
    {
        if (disposed) throw new ObjectDisposedException(nameof(PNAbilityCollection));
        if (!IsReplica) throw new InvalidOperationException("Only a replica of the authority's Abilities is written as a guess.");
        if (prediction != null) throw new InvalidOperationException("A prediction scope is already open.");
        if (predictionLayers.Count >= 64) throw new InvalidOperationException("Prediction scope limit exceeded.");
        if (predictionLayers.Count == 0) { predictionTruth = Instances.ToDictionary(a => a, a => a.CaptureReplica(0)); truthAt = ReplicaTruthClocks; }
        prediction = new PNAbilityPredictionScope(this); predictionLayers.Add(prediction); return prediction;
    }
    internal void EndPrediction(PNAbilityPredictionScope scope) { if (ReferenceEquals(prediction, scope)) prediction = null; }
    internal void RemovePrediction(PNAbilityPredictionScope scope)
    {
        if (disposed || !predictionLayers.Remove(scope)) return;
        RecomposePrediction();
        if (predictionLayers.Count == 0) predictionTruth = null;
    }
    // A result ACK alone never removes an overlay before its matching image.
    public int ApplyAuthorityReplica(IEnumerable<PNAbilityReplicaItem> items, long capturedCastKey)
    {
        if (capturedCastKey < 0) throw new ArgumentOutOfRangeException(nameof(capturedCastKey));
        if (items == null) throw new ArgumentNullException(nameof(items));
        var copy = items.ToArray();
        foreach (var item in copy)
            if (definitions.TryGetValue(item.Id, out var definition)) PNAbilityReplicaProjection.Validate(item, definition);
        if (disposed || !IsReplica) throw new InvalidOperationException("Authority image requires a live replica.");
        int result = 0;
        Batch(() =>
        {
            SynchronizeReplicaShape(copy);
            foreach (var scope in predictionLayers)
                if (scope.CastKey > 0 && scope.CastKey <= capturedCastKey) scope.AcceptImage();
            result = ApplyReplica(copy);
        });
        predictionLayers.RemoveAll(s => !s.IsRecording && s.Writes.Count == 0);
        if (predictionLayers.Count == 0) predictionTruth = null;
        return result;
    }
    private int ReplacePredictionTruth(PNAbilityReplicaItem[] items)
    {
        prediction?.ClearPending();
        bool was = composingPrediction; composingPrediction = true;
        try
        {
            int count = 0;
            Batch(() =>
            {
                count = ApplyReplicaCore(items);
                predictionTruth = Instances.ToDictionary(a => a, a => a.CaptureReplica(0)); truthAt = ReplicaTruthClocks;
                RecomposePrediction();
            });
            return count;
        }
        finally { composingPrediction = was; }
    }
    private void RecomposePrediction()
    {
        if (predictionTruth == null) return;
        bool was = composingPrediction; composingPrediction = true;
        try
        {
            var state = predictionTruth.Where(p => p.Key.IsAttached).ToDictionary(p => p.Key,
                p => PNAbilityReplicaProjection.Advance(p.Value, p.Key.Definition, ReplicaTruthClocks.Since(truthAt, p.Key.Definition.Clock)));
            var written = new HashSet<PNAbility>();
            foreach (var write in predictionLayers.SelectMany(s => s.Writes).OrderBy(w => w.Order))
                if (state.TryGetValue(write.Ability, out var current))
                    state[write.Ability] = PNAbilityReplicaProjection.Overlay(current, write.Before, write.After,
                        write.Ability.Definition, ClockValues.Since(write.At, write.Ability.Definition.Clock), written.Add(write.Ability));
            Batch(() => { foreach (var p in state) p.Key.ApplyReplica(p.Value); });
        }
        finally { composingPrediction = was; }
    }
    internal void RecordPredictionWrite(PNAbility ability) { if (!composingPrediction) prediction?.Complete(ability); }
    internal void RequireMutable(PNAbility who)
    {
        if (prediction != null && who != null && !disposed && IsReplica) { prediction.Record(who); return; }
        RequireMutable();
    }
}
