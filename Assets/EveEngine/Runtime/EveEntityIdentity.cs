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
