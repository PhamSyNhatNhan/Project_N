using System;
using System.Collections.Generic;
using System.Diagnostics;
using EveEngine;
using UnityEngine;
using Debug = UnityEngine.Debug;

// EN: The engine side of the Observed View (Docs/ObservedViewPlan.md PV1). One item per run that has a view; the same item type serves a run this machine runs itself (Authority / Predicted:
//     the age is the Action's execution progress) and a run it only watches (Observed: the age is the progress the Action stream lists). The catch-up policy lives in `PNActionViewPolicy`.
public partial class PNSkill
{
    private sealed class ViewItem
    {
        public IPNActionView View;
        public PNSkillActionContext Context;     // a run this machine runs itself
        public double Age, Target, Rate;
        public bool Placed, Seen, Hidden = true, Dead, Lead = true;
        public int FactsTaken;     // facts of the stream already queued (the stream's list only grows)
        public readonly List<PendingFact> Facts = new List<PendingFact>();
    }
    private struct PendingFact { public string Key; public double[] Values; public double Age; }

    private readonly Dictionary<PNActionRunKey, ViewItem> observedViews = new Dictionary<PNActionRunKey, ViewItem>();
    private readonly Dictionary<PNSkillActionContext, ViewItem> localViews = new Dictionary<PNSkillActionContext, ViewItem>();
    private readonly List<PNActionRunKey> retiredViews = new List<PNActionRunKey>();
    private readonly List<IPNActionView> viewTails = new List<IPNActionView>();
    private bool useActionPresentation;

    public bool CanUseObservedActionPresentation => useActionPresentation && IsReady && !HasGameplayAuthority && !IsOwnerClient;

    // Counters (the PV1 measurements): views started / running, seeks, the biggest lag the policy met, facts dropped by the one-shot window, view code failures, and the view time.
    public int ActionViewsStarted { get; private set; }
    public int ObservedPresentationsStarted => ActionViewsStarted;
    public int ActionViewsRunning => localViews.Count + observedViews.Count;
    public int ActionViewSeeks { get; private set; }
    public int ActionViewResults { get; private set; }     // hits the engine handed to a run's view (PV4)
    public double ActionViewMaxLag { get; private set; }
    public int ActionViewFactsDropped { get; private set; }
    public int ActionViewFailures { get; private set; }
    public long ActionViewTicks { get; private set; }
    // Measurement for the Player fixtures (PV6): the runs of other machines this one DREW by a view, per skill and by key ("cast:ordinal:skill", at most 256), the counterpart of the legacy replay tallies.
    public readonly Dictionary<string, int> ViewedBySkill = new Dictionary<string, int>();
    public readonly List<string> ViewedKeys = new List<string>();
    // Runs first drawn when already under way (a machine that joined late, or recovered): how many, and which. Measurement only.
    public int ViewedLate { get; private set; }
    public readonly List<string> ViewedLateKeys = new List<string>();
    public int ObservedViewCount => observedViews.Count;

    public void SetObservedActionPresentation(bool enabled)
    {
        if (enabled == useActionPresentation) return;
        if (!enabled) ClearObservedActionPresentation();
        useActionPresentation = enabled;
    }

    private IPNActionView BuildView(PNSkillData data, PNSkillActionDefinition definition, Vector3 direction, Vector3? position, double scale)
    {
        var view = definition.CreateView(new PNActionViewBuild(this, data, direction, position, scale));
        if (view == null && PNLookData.Declared(data) is EveId lookId && registry.Get<PNLookDefinition>().TryGet(lookId, out var lookDefinition))
        {
            var look = lookDefinition.Create(new PNLookBuild(this, lookId, data, direction, position, scale));
            if (look != null) view = new PNLookRunView(look);
        }
        if (view != null) { ActionViewsStarted++; RouteImpactsToViews(); }
        return view;
    }

    // ---- a run this machine runs itself (Authority, Predicted) ----
    internal void BeginActionView(PNSkillActionContext context)
    {
        if (context.Mode == PNSkillMode.Observed || context.Data == null) return;
        PNSkillActionDefinition definition;
        try { definition = context.Registry.Actions().Get(context.Data.Action.Id); } catch (Exception) { return; }
        if (!definition.HasViewFor(context.Data) || !definition.ViewOnRunningMachines) return;
        var item = new ViewItem { Context = context };
        localViews[context] = item;
        // PV7: a run that has only a look (a dash trail) has no frame step to build it from (its Action may be advanced by its owner), and the look needs no age: it starts with the run.
        if (!definition.HasView) EnsureLocalView(item);
    }

    private bool EnsureLocalView(ViewItem item)
    {
        if (item.View != null) return true;
        if (item.Dead) return false;
        try
        {
            var context = item.Context;
            var definition = context.Registry.Actions().Get(context.Data.Action.Id);
            item.View = BuildView(context.Data, definition, context.Direction ?? new Vector3(0f, 0f, 1f), context.Position, context.PresentationScale);
        }
        catch (Exception error) { item.Dead = true; ActionViewFailures++; Debug.LogException(error, this); }
        if (item.View == null) { item.Dead = true; return false; }
        item.View.Lead = true;
        return true;
    }

    // Called by the execution frame with the step the Action just took (gameplay seconds) and the rate it took it at.
    internal void AdvanceActionView(PNSkillActionContext context, double step, double rate)
    {
        if (context == null || localViews.Count == 0 || !localViews.TryGetValue(context, out var item) || item.Dead) return;
        item.Target += step; item.Rate = rate;
        if (!EnsureLocalView(item)) return;
        DriveView(item, 0);
    }

    // A fact the Action presents: the same bound everywhere (PNActionFact), published with the run (Authority) and handed to this machine's view at once.
    // A key or number out of bounds is an author error (thrown); past the per-run bound the fact is dropped and counted, on every machine alike.
    public int ActionFactsOverBound { get; private set; }
    internal void PresentActionFact(PNSkillActionContext context, string key, double[] values)
    {
        if (context == null) return;
        localViews.TryGetValue(context, out var item);
        double age = item != null ? item.Target : context.RunProgress;
        var fact = new PNActionFact(age, key, values);
        if (context.PresentedFacts >= PNActionFact.MaxPerRun) { ActionFactsOverBound++; return; }
        context.PresentedFacts++;
        if (context.Mode == PNSkillMode.Authority) PublishActionFact(context, fact);
        if (item == null || item.Dead) return;
        item.Facts.Add(new PendingFact { Key = fact.Key, Values = fact.CopyValues(), Age = fact.Age });
        if (!EnsureLocalView(item)) return;
        DriveView(item, 0);
    }

    internal void PresentActionResult(PNSkillActionContext context, PNActionViewResult result)
    {
        if (context == null || !localViews.TryGetValue(context, out var item) || item.Dead || !EnsureLocalView(item)) return;
        SafeView(item, () => item.View.Result(result));
    }

    // ---- PV4: where a hit landed, and who was helped, reach the view of the run ----
    // A hit is told ONCE on every machine that shows the cast (`PNSkill.ActionImpact`: Real on the authority, Guess on the shooter, Copy on the others); the engine hands it to the view of the run that
    // made it (the cast key and the Action's ordinal name the run on every machine), so a view draws its spark the same way in every role.
    private bool viewImpactsRouted;
    private void RouteImpactsToViews()
    {
        if (viewImpactsRouted) return;
        viewImpactsRouted = true; ActionImpact += RouteImpactToView;
    }
    private void StopRoutingImpacts()
    {
        if (!viewImpactsRouted) return;
        viewImpactsRouted = false; ActionImpact -= RouteImpactToView;
    }
    private void RouteImpactToView(PNActionImpact impact)
    {
        ViewItem item = null;
        foreach (var pair in localViews)
        {
            var context = pair.Key;
            if (context.Invocation == null || context.Invocation.CastKey != impact.CastKey || context.ActionOrdinal != impact.ActionOrdinal) continue;
            item = pair.Value; break;
        }
        if (item == null && impact.CastKey != 0 && impact.ActionOrdinal >= 1) observedViews.TryGetValue(new PNActionRunKey(impact.CastKey, impact.ActionOrdinal), out item);
        if (item == null || item.Dead) return;
        if (item.View == null && item.Context != null && !EnsureLocalView(item)) return;
        if (item.View == null) return;
        var result = new PNActionViewResult("hit", impact.Point, impact.TargetId, item.Age);
        ActionViewResults++;
        SafeView(item, () => item.View.Result(result));
    }

    // The entity an id names on this machine (a view finds the person a run helped by the id of the `recipient` fact); false when it is not loaded here.
    public bool TryFindEntity(long entityId, out PNStat stat)
    {
        stat = null;
        if (entityId == 0 || !EveRegistrySession.IsReady || !EveRegistrySession.Context.Entities.TryGet(new EveEntityId(entityId), out var loader) || loader == null) return false;
        stat = loader.GetComponent<PNStat>(); return stat != null;
    }

    // `context.PresentRecipient(target)`: the Action says who it helped, at the age it decided. An id outside the fact bound is dropped and counted (a picture, not state).
    internal void PresentActionRecipient(PNSkillActionContext context, PNStat target)
    {
        if (context == null || target == null || !target.EntityId.IsValid) return;
        double id = target.EntityId.Value;
        if (!PNActionFact.ValidValue(id)) { ActionFactsOverBound++; return; }
        PresentActionFact(context, PNActionFact.RecipientKey, new[] { id });
    }

    internal void EndActionView(PNSkillActionContext context, bool cancelled)
    {
        if (!localViews.TryGetValue(context, out var item)) return;
        localViews.Remove(context);
        if (item.Dead) return;
        if (item.View == null && !cancelled && EnsureLocalView(item)) DriveView(item, 0); // an instant Action still shows its picture once
        FinishView(item, cancelled ? PNActionViewEnd.Cancelled : PNActionViewEnd.Completed);
    }

    // ---- a run this machine only watches (Observed) ----
    public bool CanPresentObservedAction(string dataId, string actionId)
    {
        if (!CanUseObservedActionPresentation || !EveId.TryParse(dataId, out var id)) return false;
        PNSkillData data;
        try { data = GetData(id); } catch (KeyNotFoundException) { return false; }
        if (data == null) return false;
        var definition = registry.Actions().Get(data.Action.Id);
        return definition.HasViewFor(data) && definition.Id.ToString() == actionId;
    }

    public void PresentObservedAction(PNActionRunKey key, string dataId, Vector3 direction, double progress, bool driveWeapon)
        => PresentObservedAction(key, dataId, direction, progress, driveWeapon, 1, 0);
    public void PresentObservedAction(PNActionRunKey key, string dataId, Vector3 direction, double progress, bool driveWeapon, double scale)
        => PresentObservedAction(key, dataId, direction, progress, driveWeapon, scale, 0);

    // `dt` is this frame's real time and `rate` the run's speed from the stream: the policy bounds how fast the view catches up with `progress` by them (dt 0 = no clock: the view takes the progress at once).
    public void PresentObservedAction(PNActionRunKey key, string dataId, Vector3 direction, double progress, bool lead, double scale, double dt, double rate = 1)
        => PresentObservedRun(key, dataId, direction, null, progress, lead, scale, dt, rate, null);

    // One frame of a watched run: its context, the progress the stream lists at the pose stamp, and the facts the stream knows so far (new ones are queued; a view
    // takes a fact when its age reaches it, and drops it if a seek passed it by more than the one-shot window).
    public void PresentObservedRun(PNActionRunKey key, string dataId, Vector3 direction, Vector3? position, double progress, bool lead, double scale, double dt, double rate, IReadOnlyList<PNActionFact> facts)
    {
        if (!CanUseObservedActionPresentation) return;
        if (!observedViews.TryGetValue(key, out var item))
        {
            item = new ViewItem();
            observedViews.Add(key, item);
            try
            {
                var data = GetData(EveId.Parse(dataId));
                item.View = BuildView(data, registry.Actions().Get(data.Action.Id), direction, position, scale);
                if (item.View != null) { ViewedBySkill.TryGetValue(dataId, out int viewed); ViewedBySkill[dataId] = viewed + 1; if (progress > .1) { ViewedLate++; if (ViewedLateKeys.Count < 64) ViewedLateKeys.Add(key.Cast + ":" + key.Ordinal + "@" + progress.ToString("0.00")); } if (ViewedKeys.Count < 256) ViewedKeys.Add(key.Cast + ":" + key.Ordinal + ":" + dataId); }
            }
            catch (Exception error) { ActionViewFailures++; Debug.LogException(error, this); }
        }
        item.Seen = true;
        if (item.View == null || item.Dead) return;
        item.Target = progress;
        item.Rate = rate;
        if (facts != null)
            for (; item.FactsTaken < facts.Count; item.FactsTaken++)
            { var fact = facts[item.FactsTaken]; item.Facts.Add(new PendingFact { Key = fact.Key, Values = fact.CopyValues(), Age = fact.Age }); }
        if (item.Lead != lead) { item.Lead = lead; SafeView(item, () => item.View.Lead = lead); if (item.Dead) return; }
        DriveView(item, dt);
    }

    public void PresentObservedFact(PNActionRunKey key, string factKey, double[] values, double age)
    {
        if (!observedViews.TryGetValue(key, out var item) || item.Dead || item.View == null) return;
        item.Facts.Add(new PendingFact { Key = factKey, Values = values ?? Array.Empty<double>(), Age = age });
    }

    public void PresentObservedResult(PNActionRunKey key, PNActionViewResult result)
    {
        if (!observedViews.TryGetValue(key, out var item) || item.Dead || item.View == null) return;
        SafeView(item, () => item.View.Result(result));
    }

    // ---- shared ----
    private void DriveView(ViewItem item, double dt)
    {
        long begin = Stopwatch.GetTimestamp();
        try
        {
            double lag = Math.Abs(item.Target - item.Age);
            double age = PNActionViewPolicy.Follow(item.Age, item.Target, dt, item.Rate, item.Placed, out bool seeking);
            if (item.Placed && lag > ActionViewMaxLag) ActionViewMaxLag = lag;
            bool wasHidden = item.Hidden;
            // Showing a hidden view again is a placement too: it must not replay what it missed.
            if (wasHidden) seeking = true;
            double rate = dt > 0 && item.Placed && !seeking ? (age - item.Age) / dt : item.Rate;
            item.Age = age; item.Placed = true; item.Hidden = false;
            if (seeking && !wasHidden) ActionViewSeeks++;
            SafeView(item, () => item.View.Advance(age, rate, seeking));
            if (item.Dead) return;
            for (int i = 0; i < item.Facts.Count;)
            {
                var fact = item.Facts[i];
                if (fact.Age > age + 1e-9) { i++; continue; }
                item.Facts.RemoveAt(i);
                if (PNActionViewPolicy.DropsFact(fact.Age, age, seeking)) { ActionViewFactsDropped++; continue; }
                SafeView(item, () => item.View.Fact(fact.Key, fact.Values, fact.Age));
                if (item.Dead) return;
            }
        }
        finally { ActionViewTicks += Stopwatch.GetTimestamp() - begin; }
    }

    private void SafeView(ViewItem item, Action call)
    {
        try { call(); }
        catch (Exception error)
        {
            Debug.LogException(error, this); ActionViewFailures++;
            item.Dead = true;
            try { item.View?.End(PNActionViewEnd.Cancelled); } catch (Exception) { }
            item.View = null;
        }
    }

    private void FinishView(ViewItem item, PNActionViewEnd kind)
    {
        var view = item.View; item.View = null;
        if (view == null) return;
        try
        {
            view.End(kind);
            for (int i = viewTails.Count - 1; i >= 0; i--) if (!viewTails[i].HasTail) viewTails.RemoveAt(i);
            if (view.HasTail) viewTails.Add(view);
        }
        catch (Exception error) { Debug.LogException(error, this); ActionViewFailures++; }
    }

    public void ReconcileObservedActionPresentation(bool covered, Func<PNActionRunKey, PNActionRunEnd?> terminal)
    {
        for (int i = viewTails.Count - 1; i >= 0; i--)
            if (!viewTails[i].HasTail) viewTails.RemoveAt(i);
        foreach (var pair in observedViews)
        {
            var item = pair.Value;
            if (covered && !item.Seen)
            {
                FinishView(item, terminal?.Invoke(pair.Key) == PNActionRunEnd.Completed ? PNActionViewEnd.Completed : PNActionViewEnd.Cancelled);
                retiredViews.Add(pair.Key);
            }
            else if (!covered && !item.Hidden && item.View != null)
            {
                SafeView(item, () => item.View.Hide());
                item.Hidden = true;
            }
            item.Seen = false;
        }
        foreach (var key in retiredViews) observedViews.Remove(key);
        retiredViews.Clear();
    }

    public void ClearObservedActionPresentation()
    {
        foreach (var item in observedViews.Values) FinishView(item, PNActionViewEnd.Cancelled);
        observedViews.Clear();
        ClearViewTails();
    }

    private void ClearViewTails()
    {
        foreach (var tail in viewTails)
            try { tail.End(PNActionViewEnd.Cancelled); } catch (Exception error) { Debug.LogException(error, this); }
        viewTails.Clear();
    }

    // The runs this machine runs itself stop with the cast, the entity or the session.
    private void ClearLocalActionViews()
    {
        StopRoutingImpacts();
        foreach (var item in localViews.Values) FinishView(item, PNActionViewEnd.Cancelled);
        localViews.Clear();
        ClearViewTails();
    }
}
