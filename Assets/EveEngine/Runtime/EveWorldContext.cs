using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.SceneManagement;
#endif

namespace EveEngine
{
    public sealed class EveWorldCleanupPendingException : Exception
    {
        public EveWorldCleanupPendingException(string message) : base(message) { }
        public static bool IsPending(Exception error)
        {return error is EveWorldCleanupPendingException||error is AggregateException aggregate&&aggregate.InnerExceptions.Count>0&&aggregate.InnerExceptions.All(IsPending);}
    }
    public enum EveWorldState { Creating, Loading, Ready, Paused, Unloading, Failed, Disposed }
    public enum EveWorldLeaseKind { Activity,Resident }

    // A world belongs to one play. Neither Unity scene handles nor network IDs identify it.
    public sealed class EveWorldContext
    {
        public EveSessionContext Session { get; }
        public Guid InstanceId { get; }
        public ulong Incarnation { get; }
        public EveId DefinitionId { get; }
        public string ContentHash { get; }
        public EveWorldState State { get; private set; } = EveWorldState.Creating;
        public Exception Failure { get; private set; }
        // First failure of this world; kept after cleanup clears Failure so a later lifecycle error can still name the cause.
        public Exception FirstFailure { get; private set; }
        public bool CanExecute => Session.IsActive && State == EveWorldState.Ready;
        public int LeaseCount => leases.Count;
        public int ActivityLeaseCount=>leases.Values.Count(x=>x.Kind==EveWorldLeaseKind.Activity);
        public int PendingCleanup => cleanup.Count;
        public double GameplayTime { get; private set; }
#if UNITY_5_3_OR_NEWER
        public UnityEngine.SceneManagement.Scene PhysicsOwner { get; private set; }
        public bool OwnsPhysicsSimulation { get; private set; }
        public ulong PhysicsSteps { get; private set; }
        public bool IsSimulating { get; private set; }
        private ulong lastPhysicsBoundary;
        public void ConfigurePhysics(UnityEngine.SceneManagement.Scene scene,bool manual)
        {
            if(PhysicsOwner.IsValid()||!scene.IsValid()||State!=EveWorldState.Creating)throw new InvalidOperationException("Configure world physics once before loading.");
            PhysicsOwner=scene;OwnsPhysicsSimulation=manual;
        }
        public bool SimulatePhysics(ulong boundary,float delta)
        {
            if(boundary==0||delta<=0||float.IsNaN(delta)||float.IsInfinity(delta))throw new ArgumentOutOfRangeException();
            if(IsSimulating||boundary<=lastPhysicsBoundary)throw new InvalidOperationException("World physics boundary must increase; never simulate twice or reenter.");
            lastPhysicsBoundary=boundary;
            if(!CanExecute||!OwnsPhysicsSimulation)return false;
            var physics=PhysicsOwner.GetPhysicsScene();
            if(!physics.IsValid()||physics==UnityEngine.Physics.defaultPhysicsScene)throw new InvalidOperationException("Manual world simulation requires its own local physics scene.");
            IsSimulating=true;
            try{physics.Simulate(delta);PhysicsSteps=checked(PhysicsSteps+1);return true;}
            finally{IsSimulating=false;}
        }
#endif
        private long nextLease;
        private readonly Dictionary<long,(string Owner,EveWorldLeaseKind Kind)> leases = new Dictionary<long,(string,EveWorldLeaseKind)>();
        private readonly List<Action> cleanup = new List<Action>();
        internal EveWorldContext(EveSessionContext session, Guid id, ulong incarnation, EveId definition, string hash)
        {
            if (id == Guid.Empty || incarnation == 0 || !definition.IsValid || hash == null || hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("Invalid world descriptor.");
            Session=session; InstanceId=id; Incarnation=incarnation; DefinitionId=definition; ContentHash=hash;
        }
        public IDisposable AcquireLease(string owner)
            =>AcquireLease(owner,EveWorldLeaseKind.Activity);
        public IDisposable AcquireLease(string owner,EveWorldLeaseKind kind)
        {
            if(!Enum.IsDefined(typeof(EveWorldLeaseKind),kind))throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Session.IsActive || State == EveWorldState.Unloading || State == EveWorldState.Failed || State == EveWorldState.Disposed || string.IsNullOrWhiteSpace(owner))
                throw new InvalidOperationException("World cannot accept a lease.");
            long token=checked(++nextLease); leases.Add(token,(owner,kind)); return new Lease(this,token);
        }
        private sealed class Lease : IDisposable
        {
            private EveWorldContext world; private readonly long token;
            internal Lease(EveWorldContext world,long token) { this.world=world; this.token=token; }
            public void Dispose() { if(world==null)return; world.leases.Remove(token); world=null; }
        }
        public void RegisterCleanup(Action action)
        {
            if (State == EveWorldState.Disposed || State == EveWorldState.Unloading) throw new InvalidOperationException("World cleanup has begun.");
            cleanup.Add(action ?? throw new ArgumentNullException(nameof(action)));
        }
        public void BeginLoading() { Require(EveWorldState.Creating); State=EveWorldState.Loading; }
        public void CompleteLoading() { Require(EveWorldState.Loading); State=EveWorldState.Ready; }
        public void BeginUnloading() { if(State==EveWorldState.Disposed)throw new InvalidOperationException("World is disposed.");State=EveWorldState.Unloading; }
        public void SetPaused(bool paused)
        {
            if(State != EveWorldState.Ready && State != EveWorldState.Paused)throw new InvalidOperationException("Only a resident world can pause.");
            State=paused?EveWorldState.Paused:EveWorldState.Ready;
        }
        public void AdvanceClock(double delta)
        {
            if(double.IsNaN(delta)||double.IsInfinity(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
            if(CanExecute)GameplayTime+=delta;
        }
        public void RestoreClock(double time)
        {if(!Session.HasAuthority||State!=EveWorldState.Ready&&State!=EveWorldState.Paused||Session.Entities.Snapshot(this).Count!=0||double.IsNaN(time)||double.IsInfinity(time)||time<0)throw new InvalidOperationException("Restore clock before restoring resident entities.");GameplayTime=time;}
        public void Fail(Exception error) { if(State==EveWorldState.Disposed)throw new InvalidOperationException(); Failure=error??throw new ArgumentNullException(nameof(error)); FirstFailure=FirstFailure??error; State=EveWorldState.Failed; }
        // Retry retains failed resources and the directory identity. Callers retire members first.
        public void Dispose()
        {
            if(State==EveWorldState.Disposed)return;
            if(LeaseCount!=0)throw new InvalidOperationException("World still has leases: "+string.Join(",",leases.Values));
            State=EveWorldState.Unloading;
            var errors=new List<Exception>();
            for(int i=cleanup.Count-1;i>=0;i--)
                try { cleanup[i](); cleanup.RemoveAt(i); } catch(Exception error) { errors.Add(error);break; }
            if(errors.Count!=0) { var error=errors[0];if(EveWorldCleanupPendingException.IsPending(error)){Failure=null;throw error;}Failure=new AggregateException("World cleanup remains owned.",errors); State=EveWorldState.Failed; throw Failure; }
            Failure=null; State=EveWorldState.Disposed;
        }
        private void Require(EveWorldState state) { if(!Session.IsActive||State!=state)throw new InvalidOperationException("Invalid world lifecycle boundary."); }
    }

    public sealed class EveWorldDirectory
    {
        private readonly EveSessionContext session;
        private readonly Dictionary<Guid,EveWorldContext> worlds=new Dictionary<Guid,EveWorldContext>();
        private readonly Dictionary<Guid,ulong> retired=new Dictionary<Guid,ulong>();
        private readonly Dictionary<Guid,ulong> replicaTickets=new Dictionary<Guid,ulong>();
        private bool cleanupRegistered;
        public int MaximumResidents { get; } = 8;
        public int MaximumHistory { get; } = 4096;
        internal EveWorldDirectory(EveSessionContext session) { this.session=session; }
        public IReadOnlyList<EveWorldContext> Snapshot()=>worlds.Values.OrderBy(x=>x.InstanceId).ToArray();
        public EveWorldContext Create(EveId definition,string hash) => Create(Guid.NewGuid(),1,definition,hash);
        public EveWorldContext Create(Guid id,ulong incarnation,EveId definition,string hash)
            => CreateCore(id,incarnation,definition,hash,false);
        public EveWorldContext CreateReplica(Guid id,ulong incarnation,EveId definition,string hash,ulong subscription)
        {
            if(session.Role!=EveSessionRole.Client||subscription==0||replicaTickets.TryGetValue(id,out var previous)&&subscription<=previous)
                throw new InvalidOperationException("Replica recreation requires a new host subscription.");
            var world=CreateCore(id,incarnation,definition,hash,true);replicaTickets[id]=subscription;return world;
        }
        private EveWorldContext CreateCore(Guid id,ulong incarnation,EveId definition,string hash,bool replica)
        {
            if(!session.IsActive||retired.Count>=MaximumHistory&&!retired.ContainsKey(id)||worlds.Count>=MaximumResidents||worlds.ContainsKey(id)||retired.TryGetValue(id,out var old)&&(incarnation<old||incarnation==old&&!replica))
                throw new InvalidOperationException("World directory closed/full or stale incarnation.");
            var world=new EveWorldContext(session,id,incarnation,definition,hash);
            if(!cleanupRegistered){session.RegisterCleanup(Dispose,afterEntities:true);cleanupRegistered=true;
#if UNITY_5_3_OR_NEWER
                if(UnityEngine.Application.isPlaying)EveEntityCleanupPump.Attach();
#endif
            }
            worlds.Add(id,world); return world;
        }
        public bool TryGet(Guid id,ulong incarnation,out EveWorldContext world)
        { if(worlds.TryGetValue(id,out world)&&world.Incarnation==incarnation)return true; world=null; return false; }
        public void Remove(EveWorldContext world)
        {
            if(world!=null&&world.State==EveWorldState.Disposed&&!worlds.ContainsKey(world.InstanceId))return;
            if(world==null||!worlds.TryGetValue(world.InstanceId,out var current)||!ReferenceEquals(current,world))throw new InvalidOperationException("Foreign world.");
            world.Dispose(); retired[world.InstanceId]=world.Incarnation; worlds.Remove(world.InstanceId);
        }
        internal void Dispose()
        {
            var errors=new List<Exception>();
            foreach(var world in worlds.Values.ToArray())try{Remove(world);}catch(Exception error){errors.Add(error);}
            if(errors.Count!=0)throw new AggregateException("World directory cleanup pending.",errors);
        }
    }
}
