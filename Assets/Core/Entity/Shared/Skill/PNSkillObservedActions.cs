using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: G8b (Docs/SkillNetworkFrameworkPlan.md 4.6). What another player's machine does with an Action the authority began: it creates the SAME Action through the registry, runs it in an Observed invocation
//     context (Mode = Observed: no Ability, no motion, no hit, no Control; the services filter by mode) and advances it with the Action's own timeline (`HasAuxiliaryTimeline` / `AdvanceAuxiliary`) until it
//     says it is done, the authority says it ended, or a safety limit passes. Nothing here is written by a skill or an Action author: an Action asks for it once in its definition (`ReplayOnObservers`).
//     An Action this machine does not know (another mod set or version) is a picture it cannot draw: dropped, never an error. Display only: not a trust boundary.
// VI: Máy người chơi khác làm gì với một Action authority vừa bắt đầu: tạo ĐÚNG Action đó qua registry, chạy ở ngữ cảnh Observed, tự tiến theo timeline của Action cho đến khi xong / authority báo kết thúc / quá giới hạn.
public partial class PNSkill
{
    public const int MaxObservedActions = 4;
    public const float MaxObservedSeconds = 10f;
    private sealed class ObservedAction { public EveId Id; public PNSkillAction Action; public PNSkillActionContext Context; public float Age; }
    private readonly List<ObservedAction> observedActions = new List<ObservedAction>();
    public int ObservedActionCount => observedActions.Count;
    public int ObservedActionsPlayed { get; private set; }

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
            if (registry.Actions().Get(data.Action.Id).RequiredContext.HasFlag(PNSkillContextRequirements.Direction) && !direction.HasValue) return; // an Action that needs a direction and got none draws nothing
            context = new PNSkillActionContext(this, registry.Skills.ResolveIdentity(id), data, registry, null, direction) { Mode = PNSkillMode.Observed };
            action = registry.Actions().Get(data.Action.Id).Create();
            action.Start(context); action.Perform(context);
            ObservedActionsPlayed++;
            if (!action.HasAuxiliaryTimeline) { action.Exit(context); action.Dispose(); return; }
            while (observedActions.Count >= MaxObservedActions) FinishObserved(observedActions[0], false);
            observedActions.Add(new ObservedAction { Id = id, Action = action, Context = context });
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
    public void EndObservedActions() { while (observedActions.Count > 0) FinishObserved(observedActions[0], false); }

    private void FinishObserved(ObservedAction item, bool completed)
    {
        observedActions.Remove(item);
        try { if (completed) item.Action.Exit(item.Context); else item.Action.Cancel(item.Context); }
        finally { item.Action.Dispose(); }
    }
}
