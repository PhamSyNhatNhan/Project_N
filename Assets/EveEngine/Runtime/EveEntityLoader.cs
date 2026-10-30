using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EveEngine
{
    // EN: Loading state; gameplay must only run while Ready.
    // VI: Trạng thái nạp; gameplay chỉ chạy khi Ready.
    public enum EveEntityLoadState { Idle, Loading, Ready, Unloading, Failed, Preparing, Prepared, Committing }

    // EN: Loads dependency-ordered modules and rolls back partially loaded entities.
    // VI: Nạp module theo phụ thuộc và hoàn tác entity mới nạp một phần.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EveEntityIdentity))]
    public class EveEntityLoader : MonoBehaviour
    {
        private EveEventSubscription readySubscription;
        private readonly List<EveIEntityLoadStep> loaded = new List<EveIEntityLoadStep>();
        private EveEntityLoadContext context;
        private EveIEntityLoadStep[] ordered;
        private int preparationIndex;
        private bool invoking;
        public EveEntityLoadContext LoadContext => context;
        public long Generation => context?.Generation ?? GetComponent<EveEntityIdentity>().Generation;
        public virtual EveEntityLoadState State { get; protected set; }
        public virtual Exception LastError { get; protected set; }
        public virtual bool IsReady => State == EveEntityLoadState.Ready && context != null && context.CanUse;
        public virtual Guid SessionId => context?.Session.SessionId ?? Guid.Empty;

        // EN: Subscribe then check state to cover both early and late spawns.
        // VI: Đăng ký rồi kiểm tra trạng thái để hỗ trợ spawn sớm và muộn.
        protected virtual void OnEnable()
        {
            readySubscription?.Dispose();
            readySubscription = EveEventManager.Global.Subscribe<EveRuntimeReady>(EveRuntimeEventIds.Ready,
                (eventContext, notice) => LoadIfReady());
            LoadIfReady();
        }

        protected virtual void LoadIfReady()
        {
            if (State == EveEntityLoadState.Preparing || State == EveEntityLoadState.Prepared || State == EveEntityLoadState.Committing) return;
            if (!EveRegistrySession.IsReady) return;
            var session = EveRegistrySession.Context;
            var identity = GetComponent<EveEntityIdentity>();
            if (session.Role != EveSessionRole.Offline && (identity == null || !identity.HasId)) return;
            try { Load(session.Registry); }
            catch (Exception ex) { Debug.LogException(ex, this); }
        }

        protected virtual void OnDisable()
        {
            readySubscription?.Dispose();
            readySubscription = null;
        }

        protected virtual void OnDestroy()
        {
            OnDisable();
            try
            {
                Unload();
                // Destruction bypasses the factory's normal pool-return identity reset.
                // Release membership only after successful module cleanup.
                GetComponent<EveEntityIdentity>()?.ResetIdentity();
            }
            catch (Exception ex) { Debug.LogException(ex, this); }
        }

        public virtual void Load(EveRegistryHub registry)
        {
            var session = EveRegistrySession.Context;
            if (!ReferenceEquals(registry, session.Registry)) throw new InvalidOperationException("Use the current session registry.");
            if (IsReady && SessionId == session.SessionId) return;
            if (State == EveEntityLoadState.Prepared) return;
            Prepare(registry); CommitBatch(new[] { this });
        }
        public void Prepare(EveRegistryHub registry)
        { BeginPrepare(registry); while (State == EveEntityLoadState.Preparing) PrepareNext(); }
        public void BeginPrepare(EveRegistryHub registry)
        {
            if (invoking || State == EveEntityLoadState.Unloading || State == EveEntityLoadState.Committing || State == EveEntityLoadState.Preparing) throw new InvalidOperationException("Lifecycle is running.");
            var session = EveRegistrySession.Context;
            if (!ReferenceEquals(registry, session.Registry)) throw new InvalidOperationException("Use current session registry.");
            if (State == EveEntityLoadState.Prepared) return;
            if (IsReady) throw new InvalidOperationException("Unload before preparing a new presence.");
            if (loaded.Count != 0) throw new InvalidOperationException("Cleanup is incomplete. Unload before retrying.");
            State = EveEntityLoadState.Preparing;
            LastError = null;
            try
            {
                var identity = GetComponent<EveEntityIdentity>();
                if (identity == null) throw new InvalidOperationException("EveEntityIdentity is required.");
                if (session.Role == EveSessionRole.Offline) identity.EnsureLocalId();
                else if (!identity.HasId) throw new InvalidOperationException("Waiting for a host-assigned identity.");
                foreach (var preparation in GetComponents<MonoBehaviour>().OfType<EveIEntityLoadPreparation>().ToArray())
                    preparation.PrepareEntityLoad();
                ordered = OrderSteps(GetComponents<MonoBehaviour>().OfType<EveIEntityLoadStep>().ToArray()); preparationIndex = 0;
                long loadId = session.AllocateLoadId(); session.Entities.Register(identity, this);
                context = new EveEntityLoadContext(session, identity, identity.Generation, loadId);
            }
            catch (Exception error)
            {
                var failures = Cleanup();
                LastError = failures.Count == 0 ? error : new AggregateException(new[] { error }.Concat(failures));
                State = EveEntityLoadState.Failed;
                throw new InvalidOperationException("Entity initialization failed; inspect InnerException.", LastError);
            }
        }
        public bool PrepareNext()
        {
            if (State == EveEntityLoadState.Prepared) return true;
            if (State != EveEntityLoadState.Preparing || invoking) throw new InvalidOperationException("No pending preparation.");
            try
            {
                var step = ordered[preparationIndex]; loaded.Add(step); invoking = true;
                try { using (context.EnterModule()) step.Load(context); } finally { invoking = false; }
                if (!context.Session.IsActive || !context.IsValid) throw new InvalidOperationException("Preparation canceled.");
                if (++preparationIndex == ordered.Length) State = EveEntityLoadState.Prepared;
                return State == EveEntityLoadState.Prepared;
            }
            catch (Exception error)
            {
                context?.Invalidate(); var failures = Cleanup(); State = EveEntityLoadState.Failed;
                LastError = failures.Count == 0 ? error : new AggregateException(new[] { error }.Concat(failures));
                throw new InvalidOperationException("Entity preparation failed.", LastError);
            }
        }
        public static void CommitBatch(IEnumerable<EveEntityLoader> entities, Action stagedStateSwap = null, EveSessionContext publicationSession = null)
        {
            var batch = entities?.ToArray() ?? throw new ArgumentNullException(nameof(entities));
            var session = publicationSession ?? batch.FirstOrDefault()?.context?.Session ?? throw new ArgumentException("Publication session required.");
            if (session.IsPublishing || !session.IsActive || batch.Distinct().Count() != batch.Length || batch.Any(x => x.State != EveEntityLoadState.Prepared || x.context == null || !x.context.IsValid || !ReferenceEquals(session, x.context.Session))) throw new InvalidOperationException("Invalid commit batch.");
            var gate = new EveEntityPublicationGate(); session.IsPublishing = true;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                foreach (var loader in batch)
                {
                    loader.State = EveEntityLoadState.Committing; loader.context.Gate = gate;
                    foreach (var hook in loader.GetComponents<MonoBehaviour>().OfType<EveIEntityCommitStep>())
                        using (loader.context.EnterModule()) hook.CommitEntity(loader.context);
                }
                stagedStateSwap?.Invoke();
                session.LastCommitMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                var policy = session.CommitPolicy;
                if (policy != null)
                {
                    if (session.LastCommitMilliseconds > policy.SoftMilliseconds)
                    { session.ConsecutiveCommitOverruns++; session.CommitWarnings++; Debug.LogWarning("Entity commit soft budget exceeded: " + session.LastCommitMilliseconds + " ms / " + batch.Length + " new entities."); }
                    else session.ConsecutiveCommitOverruns = 0;
                    if (session.LastCommitMilliseconds > policy.HardMilliseconds || session.ConsecutiveCommitOverruns >= policy.ConsecutiveLimit) throw new InvalidOperationException("Entity commit hard/repeated overrun.");
                }
                foreach (var loader in batch) { loader.context.Publish(); loader.State = EveEntityLoadState.Ready; session.Entities.Publish(loader); }
                gate.IsOpen = true;
            }
            catch
            {
                foreach (var loader in batch) { loader.context?.Invalidate(); loader.State = EveEntityLoadState.Failed; }
                if (session.Role == EveSessionRole.Client) session.FailConnection("2003", "Entity batch commit failed.");
                throw;
            }
            finally { session.IsPublishing = false; }
            foreach (var loader in batch)
            {
                foreach (var hook in loader.GetComponents<MonoBehaviour>().OfType<EveIEntityCommitStep>())
                    try { hook.EntityPublished(loader.context); } catch (Exception error) { Debug.LogException(error, loader); }
                foreach (var error in EveEventManager.Global.PublishSafely(EveRuntimeEventIds.EntityReady, new EveEntityReady(loader, loader.context))) Debug.LogException(error, loader);
            }
        }
        public void InvalidateAndHide()
        {
            context?.Invalidate();
            using (context?.EnterModule()) gameObject.SetActive(false);
        }
        internal IReadOnlyList<Action> DeferredCleanupActions()
        {
            var actions = new List<Action>();
            foreach (var step in (ordered ?? loaded.ToArray()).Reverse())
            {
                var owned = step;
                actions.Add(() => { if (!loaded.Contains(owned)) return; using (context?.EnterModule()) owned.Unload(); loaded.Remove(owned); });
            }
            actions.Add(() =>
            {
                if (loaded.Count != 0) throw new InvalidOperationException("Module cleanup remains incomplete.");
                context?.Session.Entities.Remove(context.Identity, this); context = null; State = EveEntityLoadState.Idle; LastError = null;
            });
            return actions;
        }

        // EN: Dependencies determine correctness; order and type name break independent ties.
        // VI: Phụ thuộc quyết định tính đúng; thứ tự và tên kiểu sắp các bước độc lập.
        protected virtual EveIEntityLoadStep[] OrderSteps(EveIEntityLoadStep[] steps)
        {
            if (steps.Length == 0) throw new InvalidOperationException("No entity load steps.");
            var result = new List<EveIEntityLoadStep>();
            var visiting = new HashSet<EveIEntityLoadStep>();
            if (steps.GroupBy(step => step.GetType()).Any(group => group.Count() > 1))
                throw new InvalidOperationException("Duplicate module type on entity.");
            Action<EveIEntityLoadStep> visit = null;
            visit = step =>
            {
                if (result.Contains(step)) return;
                if (!visiting.Add(step)) throw new InvalidOperationException("Cyclic entity module dependency.");
                foreach (Type required in step.Dependencies ?? Array.Empty<Type>())
                {
                    if (required == null) throw new InvalidOperationException("Null module dependency.");
                    var matches = steps.Where(candidate => required.IsInstanceOfType(candidate)).ToArray();
                    if (matches.Length != 1) throw new InvalidOperationException($"Dependency {required.Name} requires exactly one matching module.");
                    visit(matches[0]);
                }
                visiting.Remove(step);
                result.Add(step);
            };
            foreach (var step in steps.OrderBy(step => step.LoadOrder).ThenBy(step => step.GetType().FullName, StringComparer.Ordinal)) visit(step);
            return result.ToArray();
        }

        public virtual void Unload()
        {
            if (invoking || State == EveEntityLoadState.Committing || State == EveEntityLoadState.Unloading)
                throw new InvalidOperationException("Cannot unload during a lifecycle callback.");
            State = EveEntityLoadState.Unloading;
            context?.Invalidate();
            var failures = Cleanup();
            LastError = failures.Count == 0 ? null : new AggregateException("Module cleanup failed.", failures);
            State = failures.Count == 0 ? EveEntityLoadState.Idle : EveEntityLoadState.Failed;
            if (LastError != null) throw LastError;
        }

        private List<Exception> Cleanup()
        {
            var failures = new List<Exception>();
            for (int index = loaded.Count - 1; index >= 0; index--)
                try { using (context?.EnterModule()) loaded[index].Unload(); loaded.RemoveAt(index); }
                catch (Exception ex) { failures.Add(ex); }
            if (failures.Count == 0)
            {
                context?.Session.Entities.Remove(context.Identity, this);
                context = null;
            }
            return failures;
        }
    }
}
