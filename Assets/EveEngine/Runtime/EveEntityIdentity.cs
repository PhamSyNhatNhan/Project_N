using System;
using UnityEngine;

namespace EveEngine
{
    // EN: Holds the unique identity of this GameObject's runtime entity.
    // VI: Giữ ID duy nhất của entity runtime trên GameObject này.
    [DisallowMultipleComponent]
    public class EveEntityIdentity : MonoBehaviour
    {
        private EveEntityId id;
        public virtual long OwnerId { get; protected set; }
        public virtual EveId CharacterId { get; protected set; }
        public virtual Guid SessionId { get; protected set; }
        public long Generation { get; private set; } = 1;
        public EveWorldContext World { get; private set; }
        public string ContentSlot { get; private set; }
        public ulong MembershipRevision { get; private set; }
        public bool IsGameplayHeld => gameplayHolds.Count > 0;
        public bool IsChangingWorld { get; private set; }
        private long nextHold;
        private readonly System.Collections.Generic.HashSet<long> gameplayHolds=new System.Collections.Generic.HashSet<long>();
        private IDisposable worldLease;
#if UNITY_5_3_OR_NEWER
        public WorldChange PrepareWorldChange(EveWorldContext destination,string slot)
            =>new WorldChange(this,destination,slot);
        public sealed class WorldChange : IDisposable
        {
            private readonly EveEntityIdentity owner;
            private readonly EveWorldContext source,destination;
            private readonly string oldSlot,newSlot;
            private readonly ulong revision;
            private readonly Transform oldParent;
            private IDisposable previousLease,nextLease;
            private bool applied,committed,disposed;
            internal WorldChange(EveEntityIdentity owner,EveWorldContext destination,string slot)
            {
                if(!owner.IsGameplayHeld||owner.World==null||!ReferenceEquals(owner.World.Session,destination?.Session)||
                    !owner.World.Session.HasAuthority||owner.MembershipRevision==ulong.MaxValue||!destination.CanExecute||string.IsNullOrWhiteSpace(slot)||slot.Length>128||owner.World.IsSimulating||destination.IsSimulating)
                    throw new InvalidOperationException("World membership change requires held authority and a resident destination.");
                this.owner=owner;source=owner.World;this.destination=destination;oldSlot=owner.ContentSlot;newSlot=slot;revision=owner.MembershipRevision;oldParent=owner.transform.parent;
                nextLease=destination.AcquireLease("entity membership transaction",EveWorldLeaseKind.Resident);
            }
            public void Apply()
            {
                if(applied||disposed||committed||!owner.IsGameplayHeld||owner.World!=source||owner.MembershipRevision!=revision||!destination.CanExecute)throw new InvalidOperationException("Stale world membership transaction.");
                Move(destination.PhysicsOwner);previousLease=owner.worldLease;owner.worldLease=nextLease;nextLease=null;
                owner.World=destination;owner.ContentSlot=newSlot;owner.MembershipRevision=checked(revision+1);applied=true;
                source.Session.Entities.ReindexWorld(owner.GetComponent<EveEntityLoader>(),source,destination);
            }
            private void Move(UnityEngine.SceneManagement.Scene scene)
            {owner.IsChangingWorld=true;try{owner.transform.SetParent(null,true);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner.gameObject,scene);}finally{owner.IsChangingWorld=false;}}
            public void Commit()
            {if(!applied||disposed)throw new InvalidOperationException("Unapplied membership.");committed=true;previousLease?.Dispose();previousLease=null;}
            public void Dispose()
            {
                if(disposed)return;
                if(applied&&!committed&&owner!=null&&owner.HasId&&owner.World==destination&&owner.MembershipRevision==revision+1)
                {
                    Move(source.PhysicsOwner);var lease=owner.worldLease;owner.worldLease=previousLease;previousLease=null;
                    if(oldParent!=null&&oldParent.gameObject.scene==source.PhysicsOwner)owner.transform.SetParent(oldParent,true);
                    owner.World=source;owner.ContentSlot=oldSlot;owner.MembershipRevision=revision;
                    source.Session.Entities.ReindexWorld(owner.GetComponent<EveEntityLoader>(),destination,source);lease?.Dispose();
                }
                previousLease?.Dispose();previousLease=null;nextLease?.Dispose();nextLease=null;disposed=true;
            }
        }
#endif
        // Initial membership is bound before preparation. Published transfers use a later coordinator.
        public void BindWorld(EveWorldContext world, string slot)
        {
            var loader=GetComponent<EveEntityLoader>();
            if(World!=null || loader!=null&&loader.State!=EveEntityLoadState.Idle || world==null ||
                string.IsNullOrWhiteSpace(slot)||slot.Length>128 || SessionId!=Guid.Empty&&SessionId!=world.Session.SessionId)
                throw new InvalidOperationException("Initial world membership requires an idle detached entity.");
            worldLease=world.AcquireLease("entity",EveWorldLeaseKind.Resident); World=world; ContentSlot=slot; MembershipRevision=1;
        }
        public void BindReplicaWorld(EveWorldContext world,string slot,ulong membership)
        {if(world?.Session.Role!=EveSessionRole.Client||membership==0)throw new InvalidOperationException("Replica membership must be host-issued.");BindWorld(world,slot);MembershipRevision=membership;}
        public IDisposable HoldGameplay()
        {
            if(!HasId)throw new InvalidOperationException("Hold requires an identified actor.");
            long token=checked(++nextHold);gameplayHolds.Add(token);return new GameplayHold(this,token);
        }
#if UNITY_EDITOR
        // Native S3 relocation fixture only. Runtime transfer orchestration belongs to S5.
        internal void RelocateWorldFixture(EveWorldContext destination,string slot)
        {
            if(!IsGameplayHeld||World==null||!ReferenceEquals(World.Session,destination.Session)||!destination.CanExecute||string.IsNullOrWhiteSpace(slot))throw new InvalidOperationException("Held same-play relocation required.");
            var lease=destination.AcquireLease("entity",EveWorldLeaseKind.Resident);IsChangingWorld=true;
            try{UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject,destination.PhysicsOwner);}
            catch{lease.Dispose();throw;}
            finally{IsChangingWorld=false;}
            var previous=World;worldLease?.Dispose();worldLease=lease;World=destination;ContentSlot=slot;MembershipRevision=checked(MembershipRevision+1);
            destination.Session.Entities.ReindexWorld(GetComponent<EveEntityLoader>(),previous,destination);
        }
#endif
        private sealed class GameplayHold : IDisposable
        {
            private EveEntityIdentity owner;
            private readonly long token;
            internal GameplayHold(EveEntityIdentity owner,long token) { this.owner=owner;this.token=token; }
            public void Dispose() { if(owner==null)return; owner.gameplayHolds.Remove(token);owner=null; }
        }

        public virtual bool HasId => id.IsValid;
        public virtual EveEntityId Id => HasId ? id : throw new InvalidOperationException("Entity ID has not been assigned.");

        // EN: Bind a host ID or transferred floor ID before enabling the entity loader.
        // VI: Gán ID từ host hoặc ải trước trước khi bật entity loader.
        public virtual void Assign(EveEntityId value)
            => Assign(value, default, 0);

        public virtual void Assign(EveEntityId value, EveId characterId, long ownerId, long generation = 1)
        {
            if (!value.IsValid) throw new ArgumentException("Entity ID is required.", nameof(value));
            if (HasId) throw new InvalidOperationException("Entity ID is already assigned.");
            if (ownerId < 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
            if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
            id = value;
            CharacterId = characterId;
            OwnerId = ownerId;
            Generation = generation;
        }
        internal void AdvanceGeneration(long generation) { if (generation <= Generation) throw new InvalidOperationException("Generation must increase."); Generation = generation; }

        internal void BindSession(EveSessionContext session)
        {
            if (SessionId != Guid.Empty && SessionId != session.SessionId)
                throw new InvalidOperationException("Entity belongs to a previous session. Reset its identity before reuse.");
            if (session.Role != EveSessionRole.Offline && (!Id.IsHostAssigned || !CharacterId.IsValid))
                throw new InvalidOperationException("Network entities require a host ID and character definition before loading.");
            SessionId = session.SessionId;
            if(World!=null&&!ReferenceEquals(World.Session,session))throw new InvalidOperationException("World belongs to another play.");
        }

        // EN: Reset a detached entity before reusing it in another session.
        // VI: Đặt lại entity đã tháo khỏi phiên trước khi dùng lại ở phiên khác.
        public virtual void ResetIdentity()
        {
            var loader = GetComponent<EveEntityLoader>();
            if (loader != null && loader.State != EveEntityLoadState.Idle)
                throw new InvalidOperationException("Unload the entity before resetting its identity.");
            id = default;
            OwnerId = 0;
            CharacterId = default;
            SessionId = Guid.Empty;
            Generation = 1;
            worldLease?.Dispose(); worldLease=null; World=null; ContentSlot=null; MembershipRevision=0;
            gameplayHolds.Clear();
        }

        // EN: Offline entities receive a distinct local ID when they load.
        // VI: Entity offline được cấp ID nội bộ riêng khi được nạp.
        public virtual EveEntityId EnsureLocalId()
        {
            if (!HasId) Assign(EveEntityId.NewLocal());
            return Id;
        }
    }
}
