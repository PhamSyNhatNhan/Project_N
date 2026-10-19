using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EveEngine;
using UnityEngine;

[DisallowMultipleComponent]
public sealed partial class PNBuffManager : MonoBehaviour, EveIEntityLoadStep, EveIEntityLoadPreparation, EveIEntityCommitStep
{
    private static readonly ConditionalWeakTable<EveSessionContext, PNBuffMutationCoordinator> coordinators = new ConditionalWeakTable<EveSessionContext, PNBuffMutationCoordinator>();
    private PNBuffCollection collection;
    private EveSessionContext session;
    private PNStat target;
    private EveTimeScale clock;
    private bool deathObserved;
    private EveEntityLoadContext context;
    private IDisposable publication;
    public bool StateCaptureFaulted => RuntimeFailure != null || collection != null && (collection.Coordinator.IsFaulted || collection.IsDisposed);
    public bool CanCaptureState => IsReady && collection != null && !collection.Coordinator.IsMutating && !collection.Coordinator.IsFaulted && !publishingProjection;
    public bool CanCaptureReplication => IsReady && Publisher != null && !collection.Coordinator.IsMutating && !publishingProjection;
    public Exception RuntimeFailure { get; private set; }
    private PNBuffReplica replica;
    private double presentationTime;
    private bool advancingCollection;
    public PNBuffPublisher Publisher { get; private set; }
    public PNIBuffPresentationSource Presentation => IsReady ? StablePresentation() : null;
    public PNBuffReplica Replica => replica;
    public Func<double> AuthoritativePresentationTime { get; set; }
    public bool RenderPresentation { get; set; } = true;
    public PNBuffCollection Buffs => IsReady ? collection : throw new InvalidOperationException("Buff manager is not loaded.");
    public bool IsReady => collection != null && !collection.IsDisposed && context != null && context.CanUse;
    public int LoadOrder => 10;
    public IEnumerable<Type> Dependencies => new[] { typeof(PNStat), typeof(PNControlManager) };
    public void PrepareEntityLoad() { if (GetComponent<PNControlManager>() == null) gameObject.AddComponent<PNControlManager>(); }
    public void Load(EveEntityLoadContext context)
    {
        if (collection != null) throw new InvalidOperationException("Buff manager already loaded.");
        this.context = context;
        target = GetComponent<PNStat>(); clock = GetComponent<EveTimeScale>(); session = context.Session; deathObserved = false; RuntimeFailure = null;
        var coordinator = coordinators.GetValue(session, _ => new PNBuffMutationCoordinator());
        var skill = GetComponent<PNSkill>();
        collection = new PNBuffCollection(context.Registry, session.SessionId, coordinator,
            new PNBuffServices(target, GetComponent<PNControlManager>().State, reason => skill != null && skill.IsReady && skill.TryInterrupt(reason)),
            () => context.CanMutate);
        collection.Failed += Report; target.Died += OnDied;
        presentationTime = 0;
    }
    public void CommitEntity(EveEntityLoadContext context)
    {
        if (session.HasAuthority)
        {
            Publisher = new PNBuffPublisher(collection, target.EntityId, context.Generation, () => Math.Max(0, presentationTime - (advancingCollection ? collection.RemainingGameDelta : 0)));
            replica = new PNBuffReplica(context.Registry, session.SessionId, target.EntityId, target, RenderPresentation);
            publication = replica.DeferPresentation();
            replica.PresenterFailed += Report;
            Publisher.Message += LocalMessage; Publisher.OptionalCue += LocalCue;
            replica.ApplyLifecycle(Publisher.Snapshot(), presentationTime);
        }
    }
    public void EntityPublished(EveEntityLoadContext context)
    { typedStage?.Published(); if(replica!=null)StablePresentation(); publication?.Dispose(); publication = null; if (session.HasAuthority && context.CanSimulate && !isActiveAndEnabled) collection.Suspend(true); }
    private void LocalMessage(PNBuffWireMessage message)
    {double observed=presentationTime;PublishProjection(()=>LocalMessageCore(message,observed));}
    private void LocalMessageCore(PNBuffWireMessage message,double observed)
    {
        if (context==null||!context.CanUse || message.Kind == PNBuffWireKind.Closed) return;
        using (EveHotPathMetrics.Measure("buff.local-presentation")) replica?.ApplyLifecycle(message, observed);
        if(context==null||!context.CanUse||replica==null)return;
        ObserveTyped(message,observed);
    }
    private void LocalCue(PNBuffOptionalCueMessage message)
    {double observed=presentationTime;PublishProjection(()=>LocalCueCore(message,observed));}
    private void LocalCueCore(PNBuffOptionalCueMessage message,double observed)
    {
        if (context==null||!context.CanUse) return; using (EveHotPathMetrics.Measure("buff.local-cue-presentation")) replica?.Receive(message, observed);
        if(context==null||!context.CanUse||replica==null)return;
        ObserveTypedCue(message,observed);
    }
    // Called only after the network adapter authenticates the host/session and maps this target.
    public void BindClientPresentation(Guid authoritativeSession, EveEntityId targetId, PNBuffReplicaPolicy policy = null)
    {
        if (!IsReady || session.HasAuthority || targetId != target.EntityId) throw new InvalidOperationException("Bind requires a loaded client target.");
        replica?.Dispose(); replica = new PNBuffReplica(session.Registry, authoritativeSession, targetId, target, RenderPresentation, policy); replica.PresenterFailed += Report;
        replica.Failed += (code, message) => session.FailConnection(code.ToString(), message);
        StablePresentation();
    }
    private void Report(Exception error) => Debug.LogException(error, this);
    private void OnDied(PNDamageResult result) { if (IsReady && context.CanSimulate && !deathObserved) { deathObserved = true; collection.TargetDied(); } }
    public void Advance(double delta)
    {
        PNBuffCollection.Seconds(delta, true);
        if (!IsReady || !session.HasAuthority || !context.CanSimulate) return;
        var scale = clock != null && clock.isActiveAndEnabled ? clock.Scale : 1;
        var raw = clock != null && clock.isActiveAndEnabled ? Math.Max(0, clock.RawScale) : 1;
        Publisher?.CaptureDiagnostic(presentationTime, scale, raw, "Advance-pre");
        presentationTime = PNBuffCollection.Sum(presentationTime, delta);
        collection.Suspend(!isActiveAndEnabled);
        if (!isActiveAndEnabled) return;
        if (target.IsDead && !deathObserved) { deathObserved = true; collection.TargetDied(); }
        else if (!target.IsDead) deathObserved = false;
        advancingCollection = true;
        try { collection.Advance(delta, clock != null && clock.isActiveAndEnabled ? clock.Scale : 1, clock != null && clock.isActiveAndEnabled ? Math.Max(0, clock.RawScale) : 1); }
        finally { advancingCollection = false; }
        Publisher?.CaptureDiagnostic(presentationTime, clock != null && clock.isActiveAndEnabled ? clock.Scale : 1, clock != null && clock.isActiveAndEnabled ? Math.Max(0, clock.RawScale) : 1, "Advance-post");
        replica?.Advance(AuthoritativePresentationTime?.Invoke() ?? presentationTime);
    }
    private void Update() => AdvanceAutomatically(Time.deltaTime);
    // Same fault boundary for Unity Update and explicit/headless runtime schedulers.
    public void AdvanceAutomatically(double delta)
    {
        PNBuffCollection.Seconds(delta, true);
        if (RuntimeFailure != null) return;
        if (!IsReady) return;
        try
        {
            if (session != null && !session.HasAuthority) presentationTime = PNBuffCollection.Sum(presentationTime, delta);
            Advance(delta);
            if (session != null && !session.HasAuthority) replica?.Advance(AuthoritativePresentationTime?.Invoke() ?? presentationTime);
        }
        catch (Exception error)
        {
            RuntimeFailure = error;
            Report(error);
            try { collection?.Dispose(PNBuffEndReason.RuntimeError); } catch (Exception cleanup) { Report(cleanup); }
            try { Publisher?.Dispose(); Publisher = null; replica?.Dispose(); } catch (Exception cleanup) { Report(cleanup); }
        }
    }
    private void OnDisable() { if (GetComponent<EveEntityIdentity>()?.IsChangingWorld!=true && IsReady && session.HasAuthority && context.CanSimulate) collection.Suspend(true); }
    private void OnEnable() { if (GetComponent<EveEntityIdentity>()?.IsChangingWorld!=true && IsReady && session.HasAuthority && context.CanSimulate) collection.Suspend(false); }
    public void Unload()
    {
        typedStage?.Dispose();
        if (target != null) target.Died -= OnDied;
        collection?.Dispose(session != null && !session.IsActive ? PNBuffEndReason.SessionEnded : PNBuffEndReason.Unload);
        Publisher?.Dispose(); Publisher = null; replica?.Dispose(); replica = null; UnloadTyped();
        collection = null; target = null; clock = null; session = null;
        context = null; publication = null;
    }
    private void OnDestroy() => Unload();
}
