using System;

namespace EveEngine
{
    // EN: Immutable initialization inputs bound to one entity and session.
    // VI: Đầu vào khởi tạo bất biến, gắn với một entity và một phiên.
    public sealed class EveEntityLoadContext
    {
        private int moduleCalls;
        internal EveEntityPublicationGate Gate;
        public bool IsValid { get; private set; } = true;
        public bool IsPublished { get; private set; }
        public long Generation { get; }
        public long LoadId { get; }
        public bool CanUse => IsValid && IsPublished && (Gate == null || Gate.IsOpen) && Session.IsActive && !Session.IsPublishing;
        public bool CanMutate => CanSimulate;
        // Publication/readability remains stable while a world or actor is held.
        public bool CanExecute => CanUse && !Identity.IsGameplayHeld && (Identity.World == null || Identity.World.CanExecute);
        public bool CanSimulate => CanExecute && Session.HasAuthority;
        public bool CanTarget => CanExecute;
        public double GameplayDelta(double delta) => CanExecute ? delta : 0;
        public bool AllowsModuleAccess => moduleCalls > 0;
        public EveSessionContext Session { get; }
        public EveEntityIdentity Identity { get; }
        public EveRegistryHub Registry => Session.Registry;
        public EveEntityLoadContext(EveSessionContext session, EveEntityIdentity identity, long generation = 1, long loadId = 0)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            if (generation <= 0 || loadId < 0) throw new ArgumentOutOfRangeException();
            Generation = generation; LoadId = loadId;
        }
        internal void Publish() { if (!IsValid || IsPublished) throw new InvalidOperationException("Context cannot be republished."); IsPublished = true; }
        internal void Invalidate() { IsValid = false; }
        internal IDisposable EnterModule() { moduleCalls++; return new ModuleScope(this); }
        private sealed class ModuleScope : IDisposable
        {
            private EveEntityLoadContext owner;
            public ModuleScope(EveEntityLoadContext owner) { this.owner = owner; }
            public void Dispose() { if (owner == null) return; owner.moduleCalls--; owner = null; }
        }
    }
    internal sealed class EveEntityPublicationGate { internal bool IsOpen; }
}
