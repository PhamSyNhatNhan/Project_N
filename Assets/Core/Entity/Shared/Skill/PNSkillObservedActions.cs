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
//     says it is done, the authority says it ended, or a safety limit passes. Nothing here is written by a skill or an Action author: an Action asks for it once in its definition (`ReplayOnObservers`).
//     An Action this machine does not know (another mod set or version) is a picture it cannot draw: dropped, never an error. Display only: not a trust boundary.
// VI: Máy người chơi khác làm gì với một Action authority vừa bắt đầu: tạo ĐÚNG Action đó qua registry, chạy ở ngữ cảnh Observed, tự tiến theo timeline của Action cho đến khi xong / authority báo kết thúc / quá giới hạn.
public partial class PNSkill
{
    public const int MaxObservedActions = 4;
    public const float MaxObservedSeconds = 10f;
    private sealed class ObservedAction { public EveId Id; public PNSkillAction Action; public PNSkillActionContext Context; public float Age; public bool SupportsPresentation; }
    private readonly List<ObservedAction> observedActions = new List<ObservedAction>();
    public int ObservedActionCount => observedActions.Count;
    public int ObservedActionsPlayed { get; private set; }
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
            if (useActionPresentation && definition.PresentationFactory != null) return;
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

    private void EndObserved(string skillId)
    {
        if (!EveId.TryParse(skillId, out var id)) return;
        for (int i = observedActions.Count - 1; i >= 0; i--) if (observedActions[i].Id == id) { FinishObserved(observedActions[i], false); return; }
    }

    // Everything an observer drew stops with the cast, the entity or the session.
    public void EndObservedActions()
    {
        while (observedActions.Count > 0) FinishObserved(observedActions[0], false);
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
