using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

public interface IPNSkillActionPresentation
{
    bool HasTail { get; }
    void Show(double progress, bool driveWeapon, bool seek);
    void Hide();
    void End(bool completed);
}

// EN: G8b (Docs/SkillNetworkFrameworkPlan.md 4.6). What another player's machine does with an Action the authority began: it creates the SAME Action through the registry, runs it in an Observed invocation
//     context (Mode = Observed: no Ability, no motion, no hit, no Control; the services filter by mode) and advances it with the Action's own timeline (`HasAuxiliaryTimeline` / `AdvanceAuxiliary`) until it
//     says it is done, the authority says it ended, or a safety limit passes. Nothing here is written by a skill or an Action author: every Action is replayed unless its definition says `NoReplayOnObservers` (G8c); the Action stream drives it (`ReplayObservedRun`), the `skill_cast` event only when that stream is off.
//     An Action this machine does not know (another mod set or version) is a picture it cannot draw: dropped, never an error. Display only: not a trust boundary.
// VI: Máy người chơi khác làm gì với một Action authority vừa bắt đầu: tạo ĐÚNG Action đó qua registry, chạy ở ngữ cảnh Observed, tự tiến theo timeline của Action cho đến khi xong / authority báo kết thúc / quá giới hạn.
public partial class PNSkill
{
    public const int MaxObservedActions = 4;
    // G8c: an Action replayed from the Action stream is held by the run key, not by the cast; the cap only stops a leak (the 10 s limit stays).
    public const int MaxReplayedRuns = 16;
    public const float MaxObservedSeconds = 10f;
    private sealed class ObservedAction { public EveId Id; public PNSkillAction Action; public PNSkillActionContext Context; public float Age; public bool SupportsPresentation; public PNActionRunKey Run; public bool Seen; }
    private readonly List<ObservedAction> observedActions = new List<ObservedAction>();
    public int ObservedActionCount => observedActions.Count;
    public int ObservedActionsPlayed { get; private set; }
    // Runs of the Action stream this machine has started / dropped without drawing (an Action it does not know, a run it cannot build, one that threw).
    public int ReplayedRunsStarted { get; private set; }
    public int ReplayedRunsDropped { get; private set; }
    private readonly Dictionary<PNActionRunKey, ObservedAction> replayedRuns = new Dictionary<PNActionRunKey, ObservedAction>();
    public int ReplayedRunCount => replayedRuns.Count;
    // Diagnostics only: runs started here / dropped here, by the skill data id.
    public readonly Dictionary<string, int> ReplayedBySkill = new Dictionary<string, int>();
    public readonly Dictionary<string, int> DroppedBySkill = new Dictionary<string, int>();
    public readonly List<string> ReplayedKeys = new List<string>(); // "cast:ordinal:skill", at most 256
    public readonly List<string> SeenKeys = new List<string>();     // every key the stream listed here (even ones not started), at most 256
    private static void Tally(Dictionary<string, int> table, string id) { table.TryGetValue(id ?? "?", out int n); table[id ?? "?"] = n + 1; }
    private sealed class PresentedAction
    {
        public IPNSkillActionPresentation View;
        public bool Seen, Hidden = true;
    }
    private readonly Dictionary<PNActionRunKey,PresentedAction> presentedActions = new Dictionary<PNActionRunKey,PresentedAction>();
    private readonly List<PNActionRunKey> retiredPresentation = new List<PNActionRunKey>();
    private readonly List<IPNSkillActionPresentation> presentationTails = new List<IPNSkillActionPresentation>();
    private bool useActionPresentation;
    public bool CanUseObservedActionPresentation => useActionPresentation && IsReady && !HasGameplayAuthority && !IsOwnerClient;
    public int ObservedPresentationsStarted { get; private set; }

    public void SetObservedActionPresentation(bool enabled)
    {
        if (enabled == useActionPresentation) return;
        if (enabled)
        {
            for (int i=observedActions.Count-1;i>=0;i--)
                if (observedActions[i].SupportsPresentation) FinishObserved(observedActions[i],false);
        }
        else ClearObservedActionPresentation();
        useActionPresentation = enabled;
    }

    public bool CanPresentObservedAction(string dataId, string actionId)
    {
        if (!CanUseObservedActionPresentation || !EveId.TryParse(dataId,out var id)) return false;
        PNSkillData data;
        try { data=GetData(id); } catch (KeyNotFoundException) { return false; }
        if (data == null) return false;
        var definition = registry.Actions().Get(data.Action.Id);
        return definition.PresentationFactory != null && definition.Id.ToString() == actionId;
    }

    public void PresentObservedAction(PNActionRunKey key,string dataId,Vector3 direction,double progress,bool driveWeapon)
        => PresentObservedAction(key,dataId,direction,progress,driveWeapon,1);

    public void PresentObservedAction(PNActionRunKey key,string dataId,Vector3 direction,double progress,bool driveWeapon,double scale)
    {
        if (!CanUseObservedActionPresentation) return;
        if (!presentedActions.TryGetValue(key,out var item))
        {
            item = new PresentedAction();
            presentedActions.Add(key,item);
            try
            {
                var data = GetData(EveId.Parse(dataId));
                item.View = registry.Actions().Get(data.Action.Id).CreatePresentation(this,data,direction,scale);
                if (item.View != null) ObservedPresentationsStarted++;
            }
            catch (Exception error) { Debug.LogException(error,this); }
        }
        try
        {
            item.View?.Show(progress,driveWeapon,item.Hidden);
            item.Hidden = false;
        }
        catch (Exception error)
        {
            Debug.LogException(error,this);
            FinishPresentation(item,false);
        }
        item.Seen = true;
    }

    private void FinishPresentation(PresentedAction item,bool completed)
    {
        try
        {
            item.View?.End(completed);
            if (item.View?.HasTail == true) presentationTails.Add(item.View);
        }
        catch (Exception error) { Debug.LogException(error,this); }
        finally { item.View = null; }
    }

    public void ReconcileObservedActionPresentation(bool covered,Func<PNActionRunKey,PNActionRunEnd?> terminal)
    {
        for (int i=presentationTails.Count-1;i>=0;i--)
            if (!presentationTails[i].HasTail) presentationTails.RemoveAt(i);
        foreach (var pair in presentedActions)
        {
            var item = pair.Value;
            if (covered && !item.Seen)
            {
                FinishPresentation(item,terminal?.Invoke(pair.Key) == PNActionRunEnd.Completed);
                retiredPresentation.Add(pair.Key);
            }
            else if (!covered && !item.Hidden)
            {
                try { item.View?.Hide(); }
                catch (Exception error) { Debug.LogException(error,this); FinishPresentation(item,false); }
                item.Hidden = true;
            }
            item.Seen = false;
        }
        foreach (var key in retiredPresentation) presentedActions.Remove(key);
        retiredPresentation.Clear();
        // The runs of the Action stream that are no longer listed ended on the authority (completed or cancelled): the replayed copy ends the same way.
        if (replayedRuns.Count > 0)
        {
            var gone = new List<PNActionRunKey>();
            foreach (var pair in replayedRuns)
            {
                if (pair.Value.Seen) { pair.Value.Seen = false; continue; }
                if (!covered) continue; // coverage is not known: keep what runs, it stops by its own timeline or the safety limit
                gone.Add(pair.Key);
            }
            foreach (var key in gone)
            {
                var item = replayedRuns[key]; replayedRuns.Remove(key);
                if (item.Action != null && observedActions.Contains(item)) FinishObserved(item, terminal?.Invoke(key) == PNActionRunEnd.Completed);
            }
        }
    }

    // G8c: an Action of the Action stream whose own view is the Action itself (no `PresentationFactory`). The Action is created through the registry and runs in an Observed
    //     invocation context with the context the authority froze for it; the services filter by mode (no Ability, no motion, no hit, no Control).
    public bool CanReplayObservedRun(string dataId, string actionId)
    {
        if (!CanUseObservedActionPresentation || !EveId.TryParse(dataId, out var id)) return false;
        PNSkillData data;
        try { data = GetData(id); } catch (KeyNotFoundException) { return false; }
        if (data == null) return false;
        var definition = registry.Actions().Get(data.Action.Id);
        return definition.PresentationFactory == null && definition.Id.ToString() == actionId;
    }

    public void ReplayObservedRun(PNActionRunKey key, string dataId, PNActionRunContext wire, double progress)
    {
        if (!CanUseObservedActionPresentation || wire == null) return;
        if (replayedRuns.TryGetValue(key, out var existing)) { existing.Seen = true; return; }
        if (SeenKeys.Count < 256) SeenKeys.Add(key.Cast + ":" + key.Ordinal + ":" + dataId);
        // A run is kept (even an instant one that already finished) until the stream stops listing it, so it never starts twice.
        var item = new ObservedAction { Run = key, Seen = true };
        if (replayedRuns.Count >= MaxReplayedRuns) { ReplayedRunsDropped++; return; }
        replayedRuns.Add(key, item);
        if (!EveId.TryParse(dataId, out var id)) { ReplayedRunsDropped++; return; }
        PNSkillAction action = null; PNSkillActionContext context = null;
        try
        {
            var data = GetData(id);
            var definition = registry.Actions().Get(data.Action.Id);
            var targets = new List<PNStat>();
            foreach (var targetId in wire.Targets)
                if (EveRegistrySession.Context.Entities.TryGet(new EveEntityId(targetId), out var found) && found != null)
                { var stat = found.GetComponent<PNStat>(); if (stat != null) targets.Add(stat); } // a target this machine does not have is simply left out
            Vector3? direction = wire.HasDirection ? new Vector3((float)wire.X, 0f, (float)wire.Z) : (Vector3?)null;
            Vector3? position = wire.HasPosition ? new Vector3((float)wire.Px, (float)wire.Py, (float)wire.Pz) : (Vector3?)null;
            context = new PNSkillActionContext(this, registry.Skills.ResolveIdentity(id), data, registry, targets, direction, null, true, position) { Mode = PNSkillMode.Observed };
            action = definition.Create();
            action.Start(context); action.Perform(context);
            ObservedActionsPlayed++; ReplayedRunsStarted++; Tally(ReplayedBySkill, dataId); if (ReplayedKeys.Count < 256) ReplayedKeys.Add(key.Cast + ":" + key.Ordinal + ":" + dataId);
            if (!action.HasAuxiliaryTimeline) { action.Exit(context); action.Dispose(); return; }
            item.Id = id; item.Action = action; item.Context = context;
            observedActions.Add(item);
        }
        catch (Exception error)
        {
            ReplayedRunsDropped++; Tally(DroppedBySkill, dataId);
            try { action?.Cancel(context); } catch (Exception) { }
            try { action?.Dispose(); } catch (Exception) { }
            item.Action = null;
            Debug.LogException(error, this); // once: the key stays in `replayedRuns`, so the same run is not tried again
        }
    }

    // One event from the authority. The owner's client drew the cast itself and the authority is the source, so both ignore it.
    public void PlayObserved(PNSkillCastEvent value)
    {
        if (HasGameplayAuthority || IsOwnerClient || !IsReady || registry == null) return;
        switch (value.Kind)
        {
            case ActionKind: StartObserved(value); break;
            case ActionEndKind: EndObserved(value.Detail); break;
        }
    }

    private void StartObserved(PNSkillCastEvent value)
    {
        if (!EveId.TryParse(value.Detail, out var id)) return;
        PNSkillData data;
        try { data = GetData(id); } catch (Exception) { return; }
        if (data == null) return;
        Vector3? direction = value.X * value.X + value.Z * value.Z < 1e-6f ? (Vector3?)null : new Vector3(value.X, 0f, value.Z);
        PNSkillAction action = null; PNSkillActionContext context = null;
        try
        {
            var definition = registry.Actions().Get(data.Action.Id);
            if (useActionPresentation) return; // G8c: this machine takes the Action stream; the legacy event is for a machine without it
            if (definition.RequiredContext.HasFlag(PNSkillContextRequirements.Direction) && !direction.HasValue) return;
            context = new PNSkillActionContext(this, registry.Skills.ResolveIdentity(id), data, registry, null, direction) { Mode = PNSkillMode.Observed };
            action = registry.Actions().Get(data.Action.Id).Create();
            action.Start(context); action.Perform(context);
            ObservedActionsPlayed++;
            if (!action.HasAuxiliaryTimeline) { action.Exit(context); action.Dispose(); return; }
            while (observedActions.Count >= MaxObservedActions) FinishObserved(observedActions[0], false);
            observedActions.Add(new ObservedAction { Id = id, Action = action, Context = context, SupportsPresentation=definition.PresentationFactory != null });
        }
        catch (Exception)
        {
            try { action?.Cancel(context); } catch (Exception) { }
            try { action?.Dispose(); } catch (Exception) { }
            throw;
        }
    }

    // The observed Actions run by their own timelines; the machine's skill component calls this every frame (PNSkillCastView), a check calls it by hand.
    public void AdvanceObservedActions(float delta)
    {
        if (observedActions.Count == 0 || delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta)) return;
        foreach (var item in observedActions.ToArray())
        {
            if (!observedActions.Contains(item)) continue;
            try
            {
                item.Age += delta;
                bool done = item.Action.AdvanceAuxiliary(delta);
                if (done || item.Age > MaxObservedSeconds) FinishObserved(item, done);
            }
            catch (Exception error) { FinishObserved(item, false); Debug.LogException(error, null); }
        }
    }

    // The two callers that may drive observed Actions (the cast view and the Action stream adapter) share one advance per frame.
    private int lastAdvancedFrame = -1;
    public void AdvanceObservedActionsThisFrame(float delta)
    {
#if UNITY_5_3_OR_NEWER
        if (observedActions.Count == 0 || lastAdvancedFrame == Time.frameCount) return;
        lastAdvancedFrame = Time.frameCount;
#endif
        AdvanceObservedActions(delta);
    }

    private void EndObserved(string skillId)
    {
        if (!EveId.TryParse(skillId, out var id)) return;
        for (int i = observedActions.Count - 1; i >= 0; i--) if (observedActions[i].Id == id) { FinishObserved(observedActions[i], false); return; }
    }

    // Everything an observer drew stops with the cast, the entity or the session.
    public void EndObservedActions()
    {
        while (observedActions.Count > 0) FinishObserved(observedActions[0], false);
        replayedRuns.Clear();
        ClearObservedActionPresentation();
    }

    public void ClearObservedActionPresentation()
    {
        foreach (var item in presentedActions.Values) FinishPresentation(item,false);
        presentedActions.Clear();
        foreach (var tail in presentationTails)
            try { tail.End(false); } catch (Exception error) { Debug.LogException(error,this); }
        presentationTails.Clear();
    }

    private void FinishObserved(ObservedAction item, bool completed)
    {
        observedActions.Remove(item);
        try { if (completed) item.Action.Exit(item.Context); else item.Action.Cancel(item.Context); }
        finally { item.Action.Dispose(); }
    }
}
