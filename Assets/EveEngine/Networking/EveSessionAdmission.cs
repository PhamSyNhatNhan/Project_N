using System;
using System.Collections.Generic;
using UnityEngine;

namespace EveEngine
{
    // EN: Client intent; the transport supplies the authenticated sender separately.
    // VI: Ý định từ client; transport cung cấp danh tính người gửi đã xác thực riêng.
    public sealed class EveEntityCommand
    {
        public Guid SessionId { get; }
        public long EntityId { get; }
        public long Sequence { get; }
        public string CommandId { get; }
        public string PayloadJson { get; }
        public EveEntityCommand(Guid sessionId, long entityId, long sequence, string commandId, string payloadJson)
        { SessionId = sessionId; EntityId = entityId; Sequence = sequence; CommandId = commandId; PayloadJson = payloadJson; }
    }

    // EN: Transport-independent peer/host admission and host command validation for one session.
    // VI: Kiểm tra tham gia và lệnh tại host cho một phiên, độc lập transport.
    public class EveSessionAdmission
    {
        private readonly EveSessionContext session;
        private readonly Dictionary<long, long> peers = new Dictionary<long, long>();
        private readonly HashSet<long> usedPeerIds = new HashSet<long>();
        private readonly Dictionary<EveId, Action<EveEntityLoader, string>> commands = new Dictionary<EveId, Action<EveEntityLoader, string>>();
        private Guid remoteHostSession;
        public Guid AdmittedHostSession => remoteHostSession;
        public bool IsPeerAdmitted(long peerId) => session.IsActive && session.Role == EveSessionRole.Host && peers.ContainsKey(peerId);
        public EveSessionAdmission(EveSessionContext session)
        { this.session = session ?? throw new ArgumentNullException(nameof(session)); }

        protected virtual void RequireRole(EveSessionRole role)
        {
            if (!session.IsActive || session.Role != role) throw new InvalidOperationException($"An active {role} session is required.");
        }

        // EN: Peer IDs come from the transport connection, never from an incoming command payload.
        // VI: Peer ID lấy từ kết nối transport, không lấy từ payload lệnh gửi đến.
        public virtual void AdmitPeer(long peerId, EveContentCompatibility compatibility)
        {
            RequireRole(EveSessionRole.Host);
            if (peerId <= 0) throw new ArgumentOutOfRangeException(nameof(peerId));
            session.Compatibility.Validate(compatibility);
            if (!usedPeerIds.Add(peerId)) throw new InvalidOperationException("Peer ID already used in this session; reconnect with a fresh ID.");
            peers.Add(peerId, 0);
        }

        public virtual void RemovePeer(long peerId)
        { RequireRole(EveSessionRole.Host); peers.Remove(peerId); }

        // EN: Call only for the trusted host connection after exchanging compatibility data.
        // VI: Chỉ gọi cho kết nối host đáng tin sau khi trao đổi dữ liệu tương thích.
        public virtual void AdmitHost(Guid hostSessionId, EveContentCompatibility compatibility)
        {
            RequireRole(EveSessionRole.Client);
            if (hostSessionId == Guid.Empty) throw new ArgumentException("Host session ID required.");
            if (remoteHostSession != Guid.Empty) throw new InvalidOperationException("Host already admitted.");
            session.Compatibility.Validate(compatibility);
            remoteHostSession = hostSessionId;
        }

        // localOwnerId is trusted admission metadata, never an actor payload/UI selection.
        public void AdmitHost(Guid hostSessionId, EveContentCompatibility compatibility, long localOwnerId)
        {
            RequireRole(EveSessionRole.Client);
            if (localOwnerId <= 0) throw new ArgumentOutOfRangeException(nameof(localOwnerId));
            AdmitHost(hostSessionId, compatibility);
            session.AdmitLocalOwner(localOwnerId);
        }

        // EN: Gameplay modules register explicit handlers; local events never become network messages automatically.
        // VI: Module gameplay đăng ký handler tường minh; event nội bộ không tự trở thành message mạng.
        public virtual void RegisterCommand(EveId commandId, Action<EveEntityLoader, string> handler)
        {
            RequireRole(EveSessionRole.Host);
            if (!commandId.IsValid || handler == null) throw new ArgumentException("Command ID and handler required.");
            commands.Add(commandId, handler);
        }

        public virtual void HandleCommand(long authenticatedPeerId, EveEntityCommand command)
        {
            RequireRole(EveSessionRole.Host);
            if (command == null || command.SessionId != session.SessionId) throw new InvalidOperationException("Stale command session.");
            if (!peers.TryGetValue(authenticatedPeerId, out long sequence)) throw new InvalidOperationException("Peer is not admitted.");
            if (command.Sequence <= sequence) throw new InvalidOperationException("Replayed or out-of-order command.");
            if (!session.Entities.TryGet(new EveEntityId(command.EntityId), out var loader)) throw new InvalidOperationException("Entity is not ready.");
            if (loader.GetComponent<EveEntityIdentity>().OwnerId != authenticatedPeerId) throw new InvalidOperationException("Peer does not own this entity.");
            if (!commands.TryGetValue(EveId.Parse(command.CommandId), out var handler)) throw new InvalidOperationException("Unknown command.");
            peers[authenticatedPeerId] = command.Sequence;
            handler(loader, command.PayloadJson);
        }
    }
}
