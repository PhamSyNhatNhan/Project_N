using System;

namespace EveEngine
{
    // EN: Owns the published content registry for one session, across scene changes.
    // VI: Giữ registry nội dung của một phiên chơi, tồn tại xuyên các scene.
    public static class EveRegistrySession
    {
        private static EveSessionContext current;
        private static bool isEnding;
        private static EveCodeModSession mods;
        private static EveContentContext content;
        public static EveSessionState State { get; private set; }
        public static Exception LastError { get; private set; }
        public static bool IsReady => State == EveSessionState.Ready && current != null;
        public static EveSessionContext Context => IsReady ? current : throw new InvalidOperationException("Session is not ready.");
        public static EveRegistryHub Current => Context.Registry;
        public static int PendingCleanupCount => State == EveSessionState.Stopping ? (current?.Entities.Count ?? 0) + (current?.PendingServiceCleanupCount ?? 0) +
            (content != null && !content.UnloadRequested ? mods?.PendingPlayCleanupCount ?? 0 : mods?.PendingCleanupCount ?? 0) : 0;

        // EN: Recovery remains available even if the runtime GameObject has been destroyed.
        // VI: Có thể phục hồi cleanup ngay cả khi GameObject runtime đã bị hủy.
        public static void RetryCleanup()
        {
            if (State != EveSessionState.Stopping) throw new InvalidOperationException("No session cleanup is pending.");
            current?.Cleanup?.RetryQuarantines();
            End();
        }
        public static void PumpCleanup()
        {
            if (current == null) return;
            current.Cleanup?.AdvanceFrame();
            if (State == EveSessionState.Stopping && current.Cleanup?.Scheduler.StopState == Experimental.Lifecycle.CleanupStopState.Blocked && LastError == null)
                LastError = new InvalidOperationException("CleanupBlocked: pending entity cleanup requires explicit retry or restart.");
            if (State == EveSessionState.Stopping && (current.Cleanup?.ResidentCount ?? 0) == 0 && LastError == null) End();
        }

        internal static void Begin()
        {
            if (State == EveSessionState.Starting || State == EveSessionState.Ready || State == EveSessionState.Stopping)
                throw new InvalidOperationException("A session is already active.");
            State = EveSessionState.Starting;
            LastError = null;
        }

        internal static void Begin(EveContentContext loadedContent)
        {
            if (loadedContent == null || !loadedContent.IsReady) throw new InvalidOperationException("Ready content required.");
            Begin(); content = loadedContent; mods = loadedContent.Mods;
        }

        // Retain failed content teardown even if its runtime GameObject disappears.
        internal static void Unload(EveContentContext resources)
        {
            if (resources == null) return;
            if (current != null || State == EveSessionState.Starting || State == EveSessionState.Stopping && content != resources)
                throw new InvalidOperationException("Finish play cleanup before unloading content.");
            resources.RequestUnload(); content = resources; mods = resources.Mods;
            End();
        }

        internal static void FailContent(EveContentContext resources, Exception error)
        {
            try { Unload(resources); }
            catch (Exception cleanup) { LastError = new AggregateException(error, cleanup); return; }
            LastError = error; State = EveSessionState.Failed;
        }

        internal static void Fail(Exception error)
        {
            try { End(); }
            catch (Exception cleanup) { LastError = new AggregateException(error, cleanup); return; }
            if (State == EveSessionState.Stopping) { LastError = error; return; }
            LastError = error; State = EveSessionState.Failed;
        }

        internal static void AttachMods(EveCodeModSession resources)
        {
            if (State != EveSessionState.Starting || mods != null || resources == null)
                throw new InvalidOperationException("Code mods require a starting session without existing resources.");
            mods = resources;
        }

        // EN: Only publish a complete registry; failed bootstrap leaves the session unready.
        // VI: Chỉ công bố registry hoàn chỉnh; bootstrap lỗi sẽ chưa đánh dấu phiên sẵn sàng.
        internal static void Start(EveRegistryHub registry, EveSessionRole role, EveContentCompatibility compatibility)
        {
            if (State != EveSessionState.Starting) throw new InvalidOperationException("Session must be starting.");
            if (registry == null || !registry.IsSealed)
                throw new ArgumentException("A sealed registry is required.", nameof(registry));
            if (content != null && (!content.IsReady || !ReferenceEquals(content.Registry, registry) ||
                !ReferenceEquals(content.Compatibility, compatibility)))
                throw new InvalidOperationException("Use the loaded content generation selected for this play.");
            current = new EveSessionContext(registry, role, compatibility);
            mods?.Start(current);
            State = EveSessionState.Ready;
        }

        internal static void End()
        {
            if (isEnding) throw new InvalidOperationException("Session cleanup is already running.");
            isEnding = true;
            var previous = current;
            State = EveSessionState.Stopping;
            LastError = null;
            try
            {
                if (previous != null)
                {
                    previous.NotifyStopping();
                    previous.CleanupServices();
                    if (previous.Cleanup != null)
                    {
                        previous.Cleanup.BeginStop();
                        if (previous.Cleanup.ResidentCount != 0) return;
                    }
                    previous.Entities.Clear();
                    previous.CleanupServices(afterEntities: true);
                }
                if (content == null) mods?.Dispose(); // Detached/manual fixture sessions own their mod resources.
                else
                {
                    mods.EndPlay();
                    if (content.UnloadRequested) content.Dispose();
                }
                mods = null;
                content = null;
                current = null;
                State = EveSessionState.Stopped;
            }
            catch (Exception error)
            {
                if(EveWorldCleanupPendingException.IsPending(error)){LastError=null;return;}
                LastError = error;
                throw;
            }
            finally
            {
                // EN: On failure retain the inactive context and Stopping state, blocking Begin.
                // VI: Khi lỗi giữ context đã vô hiệu và trạng thái Stopping để chặn Begin.
                isEnding = false;
            }
        }
    }
}
