using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine.Experimental.Lifecycle;
using UnityEngine;

namespace EveEngine
{
    // Explicitly configured per session. No production budget is selected here.
    public sealed class EveEntityCleanupQueue
    {
        private readonly Dictionary<EveEntityLoader, CleanupLease> leases = new Dictionary<EveEntityLoader, CleanupLease>();
        private readonly Dictionary<EveEntityLoader, Action> returns = new Dictionary<EveEntityLoader, Action>();
        private readonly Func<double> remainingBudget;
#if UNITY_5_3_OR_NEWER
        private int lastPumpFrame = -1;
#endif
        public CleanupModel Scheduler { get; }
        public bool CanBeginPreparation => Scheduler.CanBeginPreparation;
        public int ResidentCount => Scheduler.ResidentCount;
        internal EveEntityCleanupQueue(CleanupPolicy policy, Func<double> clock, Func<double> remainingBudget)
        { Scheduler = new CleanupModel(policy, clock); this.remainingBudget = remainingBudget ?? throw new ArgumentNullException(nameof(remainingBudget)); }
        public bool Track(EveEntityLoader loader, Action returnObject)
        {
            if (loader == null || loader.LoadContext == null || returnObject == null || leases.ContainsKey(loader)) throw new ArgumentException("Prepared/ready loader and unique ownership required.");
            var actions = loader.DeferredCleanupActions();
            // Lease key is diagnostic; actual identity may be a negative offline ID.
            long id = loader.LoadContext.Identity.Id.Value;
            var key = new PresenceKey(id > 0 ? id : checked(-id), loader.Generation);
            if (!Scheduler.TryAcquire(key, actions.Select((x, i) => new CleanupStep("entity-unload-" + i, x)), () => { returnObject(); leases.Remove(loader); returns.Remove(loader); }, out var lease)) return false;
            leases.Add(loader, lease); returns.Add(loader, returnObject); return true;
        }
        public void Retire(EveEntityLoader loader)
        {
            if (!leases.TryGetValue(loader, out var lease)) throw new InvalidOperationException("Object was not reserved in cleanup accounting.");
            if(loader!=null)loader.InvalidateAndHide(); Scheduler.Retire(lease);
        }
        public void BeginStop() { foreach (var loader in leases.Keys.ToArray()) if(loader!=null)loader.InvalidateAndHide(); Scheduler.BeginStop(); }
        public void AdvanceFrame()
        {
#if UNITY_5_3_OR_NEWER
            if (Application.isPlaying) { if (lastPumpFrame == Time.frameCount) return; lastPumpFrame = Time.frameCount; }
#endif
            Scheduler.AdvanceFrame(remainingBudget());
        }
        public void RetryQuarantines()
        { foreach (var lease in leases.Values.Where(x => x.State == CleanupLeaseState.Quarantined).ToArray()) Scheduler.RetryCleanup(lease); }
    }
    public sealed class EveEntityCleanupPump : MonoBehaviour
    {
        private static EveEntityCleanupPump instance;
        public static void Attach()
        { if (instance != null) return; var root = new GameObject("Eve cleanup pump"); DontDestroyOnLoad(root); instance = root.AddComponent<EveEntityCleanupPump>(); }
        private void Update() => EveRegistrySession.PumpCleanup();
        private void OnDestroy() { if (instance == this) instance = null; }
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorPump()
        {
            UnityEditor.EditorApplication.update -= PumpStoppedEditor;
            UnityEditor.EditorApplication.update += PumpStoppedEditor;
        }
        private static void PumpStoppedEditor()
        {
            // Play's pump is destroyed on Stop; queued work still owns its resources.
            // Never automatically retry a failed/quarantined cleanup.
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ||
                EveRegistrySession.State != EveSessionState.Stopping || EveRegistrySession.LastError != null) return;
            try { EveRegistrySession.PumpCleanup(); }
            catch (Exception error) { Debug.LogException(error); }
        }
#endif
    }
}
