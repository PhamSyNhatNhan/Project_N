using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EveEngine;
using Mirror;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ProjectN.Coop
{
    // EN: The wire half of the shared state-and-command layer (Docs/NetworkStatePlan.md N3, Assets/Core/Sync). Two Reliable messages: PNSyncStateMessage (host -> client, the latest value of one block of one
    //     target) and PNSyncCommandMessage (client -> host). The host keeps, for each peer, at most one waiting message per (target, block): a newer value replaces the older one, so nothing queues without bound.
    //     A peer gets a first image of the Session target when it joins the play and of a world's objects once it reports the world loaded; later changes go only to peers that have the image.
    //     Targets are never trusted: the client takes a world object's state only for a world it has loaded under the host's current ticket, the host runs a command only for a world the sender observes.
    //     An Entity target is one life of an entity (id + generation): the host sends it only to peers that observe the entity's shell, a first image when the peer starts to see it, and a client keeps the state (even before the entity appears) in its service.
    // VI: Nửa đường dây của lớp đồng bộ dùng chung: hai thông điệp Reliable, mỗi peer giữ tối đa một bản chờ cho mỗi (đích, khối), ảnh đầu cho Session và cho world khi peer báo đã nạp.
    public struct PNSyncStateMessage : NetworkMessage
    {
        public Guid Play; public ulong PlayRevision; public byte Kind; public Guid World; public ulong Incarnation; public long Entity, Generation;
        public string Slot, Key, State, Payload; public int Schema; public ulong Revision;
    }
    public struct PNSyncCommandMessage : NetworkMessage
    {
        public Guid Play; public ulong PlayRevision; public byte Kind; public Guid World; public ulong Incarnation; public long Entity, Generation;
        public string Slot, Key, Command, Payload;
    }

    public sealed partial class PNNetworkManager
    {
        public const int SyncCommandsPerFrame = 32;
        // Set by the composition once the play session exists; the service is the game's (PNGameSession.Sync).
        public Func<PNSyncService> SyncProvider;
        private PNSyncService sync;
        private SyncTransport syncTransport;
        // Host: what each non-local peer is waiting for and has already been given a first image of.
        private sealed class PeerSync
        {
            public readonly Dictionary<(PNSyncTarget, EveId), PNSyncStateMessage> Waiting = new Dictionary<(PNSyncTarget, EveId), PNSyncStateMessage>();
            public readonly List<(PNSyncTarget, EveId)> Order = new List<(PNSyncTarget, EveId)>();
            public bool Session;
            public readonly HashSet<WorldSubscription> Worlds = new HashSet<WorldSubscription>();
            // Entities (id, generation) this peer observes and has been given a first image of.
            public readonly HashSet<(long, long)> Entities = new HashSet<(long, long)>();
        }
        // Client: the entity lives whose state arrived, by world, so a closing world forgets them even if the entity never appeared.
        private readonly Dictionary<Guid, HashSet<(long, long)>> clientSyncEntities = new Dictionary<Guid, HashSet<(long, long)>>();
        private readonly Dictionary<int, PeerSync> syncPeers = new Dictionary<int, PeerSync>();
        private int syncCommandFrame = -1;
        private readonly Dictionary<int, int> syncCommandCount = new Dictionary<int, int>();
        public int SyncWaitingMessages => syncPeers.Values.Sum(x => x.Waiting.Count);
        public int SyncPeerCount => syncPeers.Count;

        private void InitializeSync()
        {
            sync = SyncProvider?.Invoke();
            if (sync == null) return;
            syncTransport = new SyncTransport(this); sync.Transport = syncTransport;
        }
        private void DisposeSync()
        {
            if (sync != null && ReferenceEquals(sync.Transport, syncTransport)) sync.Transport = null;
            syncTransport = null; sync = null; syncPeers.Clear(); syncCommandCount.Clear(); clientSyncEntities.Clear();
        }

        private sealed class SyncTransport : IPNSyncTransport
        {
            private readonly PNNetworkManager owner;
            public SyncTransport(PNNetworkManager owner) { this.owner = owner; }
            public void SendState(PNSyncTarget target, PNSyncStateDefinition state, ulong revision, JToken payload) => owner.QueueState(target, state, revision, payload);
            public void SendCommand(PNSyncTarget target, PNSyncCommandDefinition command, JToken payload) => owner.SendSyncCommand(target, command, payload);
        }

        // ---- host: state ----
        // An Entity target is addressed by id + generation; the world of that life (where the peer must have it loaded) is taken from the binding, never from the caller.
        private Binding SyncBinding(long entity, long generation)
            => bindings.Values.FirstOrDefault(x => !x.Retiring && x.Metadata.Entity == entity && x.Metadata.Generation == generation);
        private void Describe(PNSyncTarget target, out byte kind, out Guid world, out ulong incarnation, out string slot, out string key, out long entity, out long generation)
        {
            kind = (byte)target.Kind; world = target.World; incarnation = target.Incarnation; slot = target.Slot; key = target.Key; entity = 0; generation = 0;
            if (target.Kind != PNSyncTargetKind.Entity) return;
            entity = target.Entity.Value; generation = target.Generation;
            var binding = SyncBinding(entity, generation);
            if (binding == null) { world = Guid.Empty; incarnation = 0; return; } // not (or no longer) a live binding here: nobody observes it
            world = binding.Metadata.World; incarnation = binding.Metadata.WorldIncarnation;
        }
        // The peer may see the entity life: it is a live binding whose shell the peer observes (spawned for it, world loaded and granted).
        private bool EntityVisibleTo(NetworkConnectionToClient connection, long entity, long generation)
        {
            var binding = SyncBinding(entity, generation);
            return binding != null && binding.Shell != null && binding.Shell.netId != 0 && binding.Shell.netIdentity.observers.ContainsKey(connection.connectionId) && CanObserveActor(connection, binding.Shell);
        }
        private PNSyncStateMessage Pack(PNSyncTarget target, PNSyncStateDefinition state, ulong revision, JToken payload)
        {
            Describe(target, out var kind, out var world, out var incarnation, out var slot, out var key, out var entity, out var generation);
            return new PNSyncStateMessage { Play = PlaySession, PlayRevision = PlayRevision, Kind = kind, World = world, Incarnation = incarnation, Entity = entity, Generation = generation, Slot = slot, Key = key, State = state.Id.ToString(), Schema = state.Schema, Revision = revision, Payload = (payload ?? JValue.CreateNull()).ToString(Newtonsoft.Json.Formatting.None) };
        }
        private static void Enqueue(PeerSync peer, PNSyncTarget target, PNSyncStateDefinition state, PNSyncStateMessage message)
        {
            var key = (target, state.Id);
            if (!peer.Waiting.ContainsKey(key)) peer.Order.Add(key);
            peer.Waiting[key] = message;
        }
        private PeerSync SyncPeer(NetworkConnectionToClient connection)
        {
            if (!syncPeers.TryGetValue(connection.connectionId, out var peer)) syncPeers.Add(connection.connectionId, peer = new PeerSync());
            return peer;
        }
        private bool SyncRemote(NetworkConnectionToClient connection) => !(connection is LocalConnectionToClient) && connection.authenticationData is PNPeer peer && peer.PlayRevision == PlayRevision;
        private WorldSubscription SyncWorldEntry(NetworkConnectionToClient connection, Guid world, ulong incarnation)
            => worldPeers.TryGetValue((connection.connectionId, world), out var entry) && entry.Loaded && !entry.Closing && entry.World != null && entry.World.Incarnation == incarnation ? entry : null;

        private void QueueState(PNSyncTarget target, PNSyncStateDefinition state, ulong revision, JToken payload)
        {
            if (!NetworkServer.active || !IsPlaying || fatalSerialization) return;
            var message = Pack(target, state, revision, payload);
            foreach (var connection in NetworkServer.connections.Values)
            {
                if (!SyncRemote(connection)) continue;
                var peer = SyncPeer(connection);
                if (target.Kind == PNSyncTargetKind.Session) { if (!peer.Session) continue; }
                else if (target.Kind == PNSyncTargetKind.Entity) { if (!peer.Entities.Contains((message.Entity, message.Generation)) || !EntityVisibleTo(connection, message.Entity, message.Generation)) continue; }
                else { var entry = SyncWorldEntry(connection, target.World, target.Incarnation); if (entry == null || !peer.Worlds.Contains(entry)) continue; }
                Enqueue(peer, target, state, message);
            }
        }

        // Called every frame by the host's pump: first images for peers that just qualified, then the waiting messages within this frame's send budget.
        private void PumpSync()
        {
            if (sync == null || !NetworkServer.active || !sync.IsAuthority) return;
            foreach (var id in syncPeers.Keys.Where(id => !NetworkServer.connections.ContainsKey(id)).ToArray()) syncPeers.Remove(id);
            foreach (var connection in NetworkServer.connections.Values.ToArray())
            {
                if (!SyncRemote(connection)) { syncPeers.Remove(connection.connectionId); continue; }
                var peer = SyncPeer(connection);
                if (!peer.Session)
                {
                    peer.Session = true;
                    foreach (var item in sync.Snapshot(t => t.Kind == PNSyncTargetKind.Session)) Enqueue(peer, item.Target, item.State, Pack(item.Target, item.State, item.Revision, item.Payload));
                }
                peer.Worlds.RemoveWhere(x => x.Closing || !worldPeers.ContainsValue(x));
                foreach (var entry in worldPeers.Values.Where(x => x.Connection == connection && x.Loaded && !x.Closing && x.World != null && !peer.Worlds.Contains(x)).ToArray())
                {
                    peer.Worlds.Add(entry); var world = entry.World;
                    foreach (var item in sync.Snapshot(t => t.InWorld(world.InstanceId, world.Incarnation))) Enqueue(peer, item.Target, item.State, Pack(item.Target, item.State, item.Revision, item.Payload));
                }
                // Entities: a life the peer can now see gets its first image (before the client binds the entity, the state waits in the client's service); a life it can no longer see is forgotten.
                peer.Entities.RemoveWhere(x => !EntityVisibleTo(connection, x.Item1, x.Item2));
                foreach (var binding in bindings.Values.Where(x => !x.Retiring && x.Shell != null && x.Shell.netId != 0).ToArray())
                {
                    long entity = binding.Metadata.Entity, generation = binding.Metadata.Generation;
                    if (peer.Entities.Contains((entity, generation)) || !EntityVisibleTo(connection, entity, generation)) continue;
                    peer.Entities.Add((entity, generation));
                    foreach (var item in sync.Snapshot(t => t.Kind == PNSyncTargetKind.Entity && t.Entity.Value == entity && t.Generation == generation)) Enqueue(peer, item.Target, item.State, Pack(item.Target, item.State, item.Revision, item.Payload));
                }
                int sent = 0;
                while (sent < peer.Order.Count)
                {
                    var key = peer.Order[sent];
                    var message = peer.Waiting[key];
                    if (key.Item1.Kind == PNSyncTargetKind.Entity && !peer.Entities.Contains((message.Entity, message.Generation))) { peer.Waiting.Remove(key); peer.Order.RemoveAt(sent); continue; } // its life ended while it waited
                    if (!TakeSendBudget(connection.connectionId, (message.Payload?.Length ?? 0) + 160)) break;
                    connection.Send(message, Channels.Reliable); peer.Waiting.Remove(key); sent++;
                }
                if (sent > 0) peer.Order.RemoveRange(0, sent);
            }
        }

        // ---- client: state ----
        private void ReceiveSyncState(PNSyncStateMessage message)
        {
            if (stopping || !IsPlaying || fatalSerialization || NetworkServer.active || sync == null || sync.IsAuthority) return;
            try
            {
                if (message.Play != PlaySession || message.PlayRevision != PlayRevision) return; // an old play: stale, not hostile
                if (message.State == null || message.State.Length > 128 || message.Payload == null || message.Payload.Length > PNSyncLimits.StateBytes * 2) throw new FormatException("Sync state message bounds.");
                PNSyncTarget target;
                if (message.Kind == (byte)PNSyncTargetKind.Session) target = PNSyncTarget.Session;
                else if (message.Kind == (byte)PNSyncTargetKind.WorldObject)
                {
                    if (!clientWorlds.TryGetValue(message.World, out var entry) || !entry.Loaded || entry.Closing || entry.World == null || entry.World.Incarnation != message.Incarnation) return; // world gone or not ours: stale
                    target = PNSyncTarget.ForWorldObject(message.World, message.Incarnation, message.Slot, message.Key);
                }
                else if (message.Kind == (byte)PNSyncTargetKind.Entity)
                {
                    if (message.Entity <= 0 || message.Generation <= 0) throw new FormatException("Sync entity target.");
                    // The entity's world must be one this client has loaded under the host's current ticket; state may arrive before the entity itself (the service holds it for the entity to read).
                    if (!clientWorlds.TryGetValue(message.World, out var entry) || !entry.Loaded || entry.Closing || entry.World == null || entry.World.Incarnation != message.Incarnation) return;
                    target = PNSyncTarget.ForEntity(new EveEntityId(message.Entity), message.Generation);
                    if (!clientSyncEntities.TryGetValue(message.World, out var lives)) clientSyncEntities.Add(message.World, lives = new HashSet<(long, long)>());
                    if (!lives.Contains((message.Entity, message.Generation)) && lives.Count >= PNSyncLimits.MaxEntries) throw new FormatException("Sync entity bound.");
                    lives.Add((message.Entity, message.Generation));
                }
                else throw new FormatException("Sync target kind.");
                var payload = JToken.Parse(message.Payload);
                sync.ReceiveState(target, EveId.Parse(message.State), message.Schema, message.Revision, payload);
            }
            catch (Exception error) { ProtocolFailure(error, false); }
        }

        // A client world is closing: the entity lives whose state arrived for it are over, even those that never appeared (their entries would otherwise stay until the play ends).
        private void ForgetClientSyncEntities(Guid world)
        {
            if (!clientSyncEntities.TryGetValue(world, out var lives)) return;
            clientSyncEntities.Remove(world);
            if (sync == null || sync.IsAuthority) return;
            foreach (var life in lives) { try { sync.RemoveEntity(new EveEntityId(life.Item1), life.Item2); } catch (Exception error) { Debug.LogWarning("Sync entity forget failed: " + error.Message); } }
        }

        // ---- client: command ----
        private void SendSyncCommand(PNSyncTarget target, PNSyncCommandDefinition command, JToken payload)
        {
            if (!NetworkClient.isConnected || NetworkServer.active || !IsPlaying) return;
            Describe(target, out var kind, out var world, out var incarnation, out var slot, out var key, out var entity, out var generation);
            if (target.Kind == PNSyncTargetKind.WorldObject && !ClientWorldReady(world)) return; // the world is not (or no longer) ours: nothing to address
            if (target.Kind == PNSyncTargetKind.Entity && (world == Guid.Empty || !ClientWorldReady(world))) return; // no live binding of that life here: nothing to address
            NetworkClient.Send(new PNSyncCommandMessage { Play = PlaySession, PlayRevision = PlayRevision, Kind = kind, World = world, Incarnation = incarnation, Entity = entity, Generation = generation, Slot = slot, Key = key, Command = command.Id.ToString(), Payload = (payload ?? JValue.CreateNull()).ToString(Newtonsoft.Json.Formatting.None) }, Channels.Reliable);
        }

        // ---- host: command ----
        private void ReceiveSyncCommand(NetworkConnectionToClient connection, PNSyncCommandMessage message)
        {
            if (stopping || !IsPlaying || fatalSerialization || sync == null || !sync.IsAuthority) return;
            try
            {
                if (!(connection.authenticationData is PNPeer peer) || peer.Owner <= 0 || peer.PlayRevision != PlayRevision) return;
                if (syncCommandFrame != Time.frameCount) { syncCommandFrame = Time.frameCount; syncCommandCount.Clear(); }
                syncCommandCount.TryGetValue(connection.connectionId, out var count); syncCommandCount[connection.connectionId] = count + 1;
                if (count >= SyncCommandsPerFrame) return;
                if (message.Play != PlaySession || message.PlayRevision != PlayRevision) return;
                if (message.Command == null || message.Command.Length > 128 || message.Payload == null || message.Payload.Length > PNSyncLimits.CommandBytes * 2) throw new FormatException("Sync command bounds.");
                PNSyncTarget target;
                if (message.Kind == (byte)PNSyncTargetKind.Session) target = PNSyncTarget.Session;
                else if (message.Kind == (byte)PNSyncTargetKind.WorldObject)
                {
                    if (SyncWorldEntry(connection, message.World, message.Incarnation) == null) return; // the sender does not (or no longer) observe that world: stale
                    target = PNSyncTarget.ForWorldObject(message.World, message.Incarnation, message.Slot, message.Key);
                }
                else if (message.Kind == (byte)PNSyncTargetKind.Entity)
                {
                    if (message.Entity <= 0 || message.Generation <= 0) throw new FormatException("Sync entity target.");
                    if (!EntityVisibleTo(connection, message.Entity, message.Generation)) return; // the sender does not (or no longer) see that life of the entity: stale
                    target = PNSyncTarget.ForEntity(new EveEntityId(message.Entity), message.Generation);
                }
                else throw new FormatException("Sync target kind.");
                sync.ReceiveCommand(peer.Owner, target, EveId.Parse(message.Command), JToken.Parse(message.Payload));
            }
            catch (Exception error) { LastFailure = error.Message; Debug.LogWarning("Sync command refused: " + error.Message); connection.Disconnect(); }
        }
    }
}
