using System;

namespace EveEngine
{
    // EN: Lifecycle of the published content session.
    // VI: Vòng đời của phiên nội dung được công bố.
    public enum EveSessionState { Stopped, Starting, Ready, Stopping, Failed }

    // EN: Authority role chosen before session initialization.
    // VI: Vai trò thẩm quyền được chọn trước khi khởi tạo phiên.
    public enum EveSessionRole { Offline, Host, Client }

    // EN: Owns content, compatibility and entity identities for a single session.
    // VI: Giữ nội dung, thông tin tương thích và danh tính entity của một phiên.
    public sealed class EveSessionContext
    {
        public Guid SessionId { get; }
        public EveRegistryHub Registry { get; }
        public EveSessionRole Role { get; }
        public EveEntityDirectory Entities { get; }
        public EveWorldDirectory Worlds { get; }
        public EveContentCompatibility Compatibility { get; }
        public EveCoopSession Coop { get; }
        public bool IsActive { get; internal set; } = true;
        private bool stoppingNotified;
        public event Action Stopping;
        private readonly System.Collections.Generic.List<Action> cleanup = new System.Collections.Generic.List<Action>();
        private readonly System.Collections.Generic.List<Action> afterEntityCleanup = new System.Collections.Generic.List<Action>();
        public int PendingServiceCleanupCount => cleanup.Count + afterEntityCleanup.Count;
        // Required teardown is retryable; unlike Stopping notifications, failures block the next play.
        public void RegisterCleanup(Action action) => RegisterCleanup(action, false);
        public void RegisterCleanup(Action action, bool afterEntities)
        {
            if (!IsActive) throw new InvalidOperationException("An active session is required.");
            (afterEntities ? afterEntityCleanup : cleanup).Add(action ?? throw new ArgumentNullException(nameof(action)));
        }
        internal void CleanupServices(bool afterEntities = false)
        {
            var actions = afterEntities ? afterEntityCleanup : cleanup;
            var errors = new System.Collections.Generic.List<Exception>();
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                try { actions[i](); actions.RemoveAt(i); }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count != 0) throw new AggregateException("Required session service cleanup failed.", errors);
        }
        internal void NotifyStopping()
        { IsActive = false; if (stoppingNotified) return; stoppingNotified = true; foreach (Action observer in Stopping?.GetInvocationList() ?? Array.Empty<Delegate>()) try { observer(); } catch (Exception error) { UnityEngine.Debug.LogException(error, null); } Stopping = null; }
        // Authenticated admission code assigns this once; UI is only a consumer.
        public long? LocalOwnerId { get; private set; }
        internal void AdmitLocalOwner(long ownerId)
        {
            if (!IsActive || Role != EveSessionRole.Client || ownerId < 0 ||
                LocalOwnerId.HasValue && LocalOwnerId.Value != ownerId)
                throw new InvalidOperationException("Active client admission identity required.");
            LocalOwnerId = ownerId;
        }
        public bool HasAuthority => Role != EveSessionRole.Client;
        public bool IsPublishing { get; internal set; }
        private long nextLoadId;
        private long nextConnectionIncarnation;
        internal long AllocateLoadId() => checked(++nextLoadId);
        internal void ReserveConnectionIncarnation(long value)
        {
            if (Role != EveSessionRole.Host || !IsActive || value != checked(nextConnectionIncarnation + 1)) throw new InvalidOperationException("Host connections require the next session-monotonic incarnation.");
            nextConnectionIncarnation = value;
        }
        public EveEntityCleanupQueue Cleanup { get; private set; }
        public EveEntityCommitPolicy CommitPolicy { get; private set; }
        public double LastCommitMilliseconds { get; internal set; }
        public int ConsecutiveCommitOverruns { get; internal set; }
        public int CommitWarnings { get; internal set; }
        public void ConfigureCommit(EveEntityCommitPolicy policy)
        { if (!IsActive || CommitPolicy != null) throw new InvalidOperationException("Configure commit profile once."); CommitPolicy = policy ?? throw new ArgumentNullException(nameof(policy)); }
        public EveActorSnapshotScheduler Snapshots { get; private set; }
        public EveActorSnapshotScheduler ConfigureSnapshots(int maximumJobs, EveEntityPreparationBudget serializationBudget, Func<double> monotonicMs)
        { if (!IsActive || Snapshots != null) throw new InvalidOperationException("Configure snapshots once."); return Snapshots = new EveActorSnapshotScheduler(maximumJobs, serializationBudget, monotonicMs); }
        public EveEntityCleanupQueue ConfigureCleanup(Experimental.Lifecycle.CleanupPolicy policy, Func<double> monotonicClock, Func<double> remainingFrameBudget)
        {
            if (!IsActive || Cleanup != null) throw new InvalidOperationException("Cleanup must be configured once in an active session.");
            Cleanup = new EveEntityCleanupQueue(policy, monotonicClock, remainingFrameBudget);
#if UNITY_5_3_OR_NEWER
            if (UnityEngine.Application.isPlaying)
            {
                EveEntityCleanupPump.Attach();
            }
#endif
            return Cleanup;
        }
        public string ConnectionFailureCode { get; private set; }
        public string ConnectionFailureMessage { get; private set; }
        public event Action<string, string> ConnectionFailed;
        // Client protocol failures invalidate the connection/session; the adapter closes its transport.
        public void FailConnection(string code, string message)
        {
            if (Role != EveSessionRole.Client) throw new InvalidOperationException("Connection failure belongs to a client session.");
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Connection error code required.");
            if (ConnectionFailureCode != null) return;
            ConnectionFailureCode = code; ConnectionFailureMessage = message ?? ""; IsActive = false;
            foreach (Action<string, string> listener in ConnectionFailed?.GetInvocationList() ?? Array.Empty<Delegate>()) try { listener(code, ConnectionFailureMessage); } catch { }
        }

        internal EveSessionContext(EveRegistryHub registry, EveSessionRole role,
            EveContentCompatibility compatibility)
        {
            SessionId = Guid.NewGuid();
            Registry = registry;
            Role = role;
            Compatibility = compatibility;
            Entities = new EveEntityDirectory(this);
            Worlds = new EveWorldDirectory(this);
            Coop = new EveCoopSession(this);
        }
    }
}
