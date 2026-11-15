using System;
using System.Collections.Generic;
using EveEngine;

// EN: A window in which the owner's CLIENT may write its replica Abilities as a guess about a cast it is running ahead of the authority (Docs/SkillNetworkingAPlan.md S3, N1/N7). Inside it the ordinary
//     per-Ability mutators work on a replica; the value each Ability held BEFORE its first write is kept, so `Rollback` puts all of them back (less the time that passed). Structural changes (define,
//     grant, remove) stay refused. The authority's next image overwrites the Abilities anyway; the scope only decides what the owner sees until then. `End` stops recording and keeps the undo data.
// VI: Cửa sổ trong đó client CHỦ NHÂN được ghi Ability replica của mình như một lần đoán cho cast nó chạy trước authority. Giá trị trước lần ghi đầu của mỗi Ability được giữ để `Rollback` trả lại.
//     Thay đổi cấu trúc (define, grant, remove) vẫn bị từ chối. `End` ngừng ghi nhưng giữ dữ liệu hoàn tác.
public sealed class PNAbilityPredictionScope : IDisposable
{
    private readonly PNAbilityCollection owner;
    private readonly Dictionary<PNAbility, PNAbilityReplicaItem> before = new Dictionary<PNAbility, PNAbilityReplicaItem>();
    private long at = System.Diagnostics.Stopwatch.GetTimestamp();
    private bool recording = true, rolledBack;
    // G6b2: what this guess had written, taken just before a newer authoritative image replaced the replica; `Rebase` lays it back on top of the image.
    private Dictionary<PNAbility, PNAbilityReplicaItem> overlay;
    internal PNAbilityPredictionScope(PNAbilityCollection owner) { this.owner = owner; }
    public bool IsRecording => recording;
    public int Count => before.Count;
    internal void Record(PNAbility ability) { if (recording && !before.ContainsKey(ability)) before.Add(ability, ability.CaptureReplica(0)); }
    public void End() { if (!recording) return; recording = false; owner.EndPrediction(this); }
    public void Dispose() => End();
    // Call just BEFORE a newer authoritative image is applied, for a guess the authority has not answered yet: remembers the values the guess wrote, so `Rebase` can show them again.
    public void CaptureOverlay()
    {
        if (rolledBack) return;
        overlay = new Dictionary<PNAbility, PNAbilityReplicaItem>();
        foreach (var key in before.Keys) if (key.IsAttached) overlay[key] = key.CaptureReplica(0);
    }
    // Call just AFTER the image was applied: what the scope would put back is now what the image holds (a refusal must restore that, not the older value), and the guess it captured is laid back on top,
    // so a waiting guess does not blink away while the authority has not answered.
    public void Rebase()
    {
        var lay = overlay; overlay = null;
        if (rolledBack) return;
        foreach (var key in new List<PNAbility>(before.Keys)) if (key.IsAttached) before[key] = key.CaptureReplica(0);
        at = System.Diagnostics.Stopwatch.GetTimestamp();
        if (lay != null) foreach (var pair in lay) if (pair.Key.IsAttached) pair.Key.ApplyReplica(pair.Value);
    }
    // Puts every Ability the scope wrote back to what it held at the guess, less the time since. Safe to call once, after End or not.
    public void Rollback()
    {
        if (rolledBack) return; rolledBack = true; overlay = null; End();
        double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - at) / (double)System.Diagnostics.Stopwatch.Frequency;
        foreach (var pair in before)
        {
            if (!pair.Key.IsAttached) continue;
            var item = pair.Value;
            pair.Key.ApplyReplica(new PNAbilityReplicaItem(item.Id, item.Index, item.Amount, item.Maximum, item.Active, Math.Max(0, item.CooldownRemaining - elapsed), item.HasDuration, Math.Max(0, item.DurationRemaining - elapsed)));
        }
        before.Clear();
    }
}

public sealed partial class PNAbilityCollection
{
    private PNAbilityPredictionScope prediction;
    public bool IsPredicting => prediction != null;
    // Client only (a replica). One scope at a time.
    public PNAbilityPredictionScope BeginPrediction()
    {
        if (disposed) throw new ObjectDisposedException(nameof(PNAbilityCollection));
        if (!IsReplica) throw new InvalidOperationException("Only a replica of the authority's Abilities is written as a guess.");
        if (prediction != null) throw new InvalidOperationException("A prediction scope is already open.");
        return prediction = new PNAbilityPredictionScope(this);
    }
    internal void EndPrediction(PNAbilityPredictionScope scope) { if (ReferenceEquals(prediction, scope)) prediction = null; }
    // The per-Ability mutators call this: on a replica an open scope takes the write (and remembers the old value), otherwise it is refused as before.
    internal void RequireMutable(PNAbility who)
    {
        if (prediction != null && who != null && !disposed && IsReplica) { prediction.Record(who); return; }
        RequireMutable();
    }
}
