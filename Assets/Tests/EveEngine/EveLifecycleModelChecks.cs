using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine.Experimental.Lifecycle;

// Shared by standalone smoke and native Unity; no scene or static registry session required.
public static class EveLifecycleModelChecks
{
    public static ModelPolicy Policy(int queued = 64, int actors = 64, int remembered = 4096)
        => new ModelPolicy(actors, 65536, 4096, 128, 512, queued, 65536, 131072, 64, remembered, 3, 1000, 1000, 1000, 1500, 4000, 12000);
    private sealed class Pair
    {
        public readonly Guid Session = Guid.NewGuid();
        public readonly ModelPolicy Policy;
        public readonly HostModel Host;
        public readonly ClientModel Client;
        public readonly List<byte[]> Wire = new List<byte[]>();
        public double Now;
        public Pair(ModelPolicy policy = null, long incarnation = 1)
        { Policy = policy ?? EveLifecycleModelChecks.Policy(); Host = new HostModel(Session, incarnation, Policy, 0); Client = new ClientModel(Session, incarnation, Policy, 0); Host.Admit(true, true, 0); }
        public void Capture(params ActorState[] actors) => Host.Capture(actors, Now);
        public void Pump(bool deliver = true)
        {
            int safety = 10000;
            while (Host.TrySend(4, batch => { Wire.AddRange(batch); return true; }, Now))
            {
                if (--safety == 0) throw new Exception("Send loop did not converge.");
                if (deliver) Deliver();
            }
        }
        public void Deliver()
        { foreach (var wire in Wire.ToArray()) { Host.ReleaseAccepted(wire.Length); Client.Receive(wire, Now); } Wire.Clear(); }
        public void Commit()
        {
            foreach (var key in Client.NeedsPreparation()) Client.CompletePreparation(Client.BeginPreparation(key), true, Now);
            if (!Client.Commit(true, false, Now)) throw new Exception("Fixture commit failed.");
            var ack = Client.TakeAppliedAck().Value; Host.Applied(ack.Incarnation, ack.SnapshotId, ack.EndSequence, Now);
            Pump();
        }
        public void Live(params ActorState[] actors) { Capture(actors); Pump(); Commit(); }
        public void Delta(MessageKind kind, PresenceKey key, byte[] payload = null)
        { Host.Delta(kind, key, payload ?? Array.Empty<byte>(), Now); Pump(); }
    }
    private static ActorState Actor(long id, long generation = 1, bool dormant = false, int value = 0)
        => new ActorState(new PresenceKey(id, generation), dormant, BitConverter.GetBytes(value));

    public static string Run()
    {
        int checks = 0, steps = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("Lifecycle model: " + label); checks++; }
        void Reject(Action action, string label)
        { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } catch (FormatException) { rejected = true; } Check(rejected, label); }
        var policy = Policy(); var key = new PresenceKey(1, 1);
        Reject(() => new PresenceKey(0, 1), "positive IDs");
        Reject(() => new ModelPolicy(1, 100, 40, 10, 9, 1, 1000, 1000, 1, 10, 2, 1, 1, 1, 1, 10, 19), "capacity/cycle cross limits");
        var input = new byte[] { 1, 2 }; var actor = new ActorState(key, false, input); input[0] = 9; var output = actor.Data; output[0] = 8;
        Check(actor.Data[0] == 1, "state defensive copies");
        var encoded = ModelCodec.EncodeActors(new[] { Actor(2), actor }, policy);
        Check(ModelCodec.DecodeActors(encoded, policy)[0].Key.Equals(key), "sorted snapshot round trip");
        Reject(() => ModelCodec.EncodeActors(new[] { Actor(1), Actor(1, 2) }, policy), "duplicate identity in snapshot");
        Reject(() => ModelCodec.DecodeActors(encoded.Concat(new byte[] { 0 }).ToArray(), policy), "trailing snapshot bytes");
        var packet = new Envelope(Guid.NewGuid(), 1, 1, MessageKind.Updated, input, key);
        var wire = ModelCodec.Encode(packet); Check(wire.Length == packet.WireBytes && ModelCodec.Decode(wire, policy).Key.Equals(key), "wire header/count and codec");
        using (var legacy = new System.IO.MemoryStream()) using (var writer = new System.IO.BinaryWriter(legacy))
        {
            writer.Write(Envelope.Protocol); writer.Write(packet.Session.ToByteArray()); writer.Write(packet.Incarnation); writer.Write((byte)0);
            writer.Write(packet.Sequence); writer.Write((byte)packet.Kind); writer.Write(key.EntityId); writer.Write(key.Generation);
            writer.Write(packet.SnapshotId); writer.Write(packet.Index); writer.Write(packet.Count); writer.Write(packet.Payload.Length); writer.Write(packet.Payload);
            Check(wire.SequenceEqual(legacy.ToArray()), "exact-sized codec preserves protocol bytes");
        }
        var publicPayload = packet.Payload; publicPayload[0] = 33;
        Check(packet.Payload[0] == 9, "envelope public getter cannot mutate shared internal payload");
        Reject(() => ModelCodec.Decode(wire.Take(wire.Length - 1).ToArray(), policy), "truncated wire");
        wire[0] = 99; Reject(() => ModelCodec.Decode(wire, policy), "unsupported protocol");

        var p = new Pair(); p.Capture(Actor(1));
        byte[][] rejectedBatch = null, retriedBatch = null;
        Check(!p.Host.TrySend(2, b => { rejectedBatch = b.Select(x => x.ToArray()).ToArray(); return false; }, 0) && p.Host.SentSequence == 0, "send-full does not commit sequence");
        p.Host.IntentResult(new byte[] { 3 }, 0);
        p.Host.TrySend(2, b => { retriedBatch = b.Select(x => x.ToArray()).ToArray(); p.Wire.AddRange(b); return true; }, 0);
        Check(rejectedBatch.Zip(retriedBatch, (a, b) => a.SequenceEqual(b)).All(x => x), "pending batch unchanged when control arrives");
        var retry = new Pair(); retry.Capture(Actor(1)); byte[][] expectedRetry = null;
        retry.Host.TrySend(2, b => { expectedRetry = b.Select(x => x.ToArray()).ToArray(); b[0][0] = 77; return false; }, 0);
        bool retryProtected = false;
        retry.Host.TrySend(2, b => { retryProtected = expectedRetry.Zip(b, (a, x) => a.SequenceEqual(x)).All(x => x); return true; }, 0);
        Check(retryProtected, "mutating rejected transport callback cannot corrupt retry bytes");
        p.Deliver(); p.Pump(); Check(p.Client.Phase == ClientPhase.Preparing && p.Client.ControlResults == 1, "control accepted amid chunks");
        Check(p.Client.TakeIntentResult()[0] == 3, "opaque control delivered independently");
        p.Host.Delta(MessageKind.Updated, key, new byte[] { 7 }, 0);
        Check(!p.Host.TrySend(1, _ => true, 0) && p.Host.BufferedMessages == 1 && !p.Host.AcceptIntent(0), "delta/intent gate before Applied");
        p.Host.Applied(1, p.Host.SnapshotId - 1, p.Host.EndSequence, 0); Check(p.Host.Phase == HostPhase.AwaitApplied, "stale ack ignored");
        p.Commit(); Check(p.Host.Phase == HostPhase.Live && p.Client.Snapshot()[0].Data[0] == 7 && p.Client.CanSendIntent, "commit ack drain preserves latest state");
        Check(p.Client.PreparedCount == 0, "commit transfers all prepared keys");
        var duplicate = ModelCodec.Encode(new Envelope(p.Session, 1, p.Client.Sequence, MessageKind.Despawn, null, key));
        p.Client.Receive(duplicate, 0); Check(p.Client.CurrentCount == 1 && p.Client.DuplicateMessages == 1, "duplicate cannot despawn twice");
        p.Delta(MessageKind.Dormant, key); Check(!p.Client.IsReady(key) && p.Client.CurrentCount == 1, "dormant retains identity without ready lease");
        p.Delta(MessageKind.Despawn, key); Check(p.Client.CurrentCount == 0, "dormant terminal despawn");
        p.Delta(MessageKind.Spawn, new PresenceKey(1, 2)); Check(p.Client.Phase == ClientPhase.Recovering, "terminal ID cannot be reused");

        p = new Pair(); p.Live(Actor(1));
        p.Delta(MessageKind.Spawn, new PresenceKey(2, 1)); p.Delta(MessageKind.Updated, new PresenceKey(2, 1), new byte[] { 12 });
        p.Delta(MessageKind.Updated, key, new byte[] { 13 });
        Check(!p.Client.IsReady(new PresenceKey(2, 1)) && p.Client.IsReady(key) && p.Client.Snapshot()[0].Data[0] == 13 && p.Client.BufferedMessages == 1, "slow entity does not block other entity");
        p.Client.CompletePreparation(p.Client.BeginPreparation(new PresenceKey(2, 1)), true, 0);
        Check(p.Client.IsReady(new PresenceKey(2, 1)) && p.Client.Snapshot()[1].Data[0] == 12 && p.Client.BufferedMessages == 0, "per-entity queue drains after load");
        p.Delta(MessageKind.Spawn, new PresenceKey(1, 2)); Check(!p.Client.IsReady(key), "new generation invalidates old ready key");
        var canceledToken = p.Client.BeginPreparation(new PresenceKey(1, 2));
        p.Delta(MessageKind.Despawn, new PresenceKey(1, 2));
        Check(!p.Client.CompletePreparation(canceledToken, true, 0), "despawn cancels late load");
        p.Delta(MessageKind.Spawn, new PresenceKey(3, 1));
        var validToken = p.Client.BeginPreparation(new PresenceKey(3, 1));
        Check(!p.Client.CompletePreparation(new PreparationToken(99, validToken.LoadId, validToken.Key), true, 0), "old connection completion rejected");
        p.Client.Receive(ModelCodec.Encode(new Envelope(p.Session, 99, 999, MessageKind.Despawn, null, new PresenceKey(3, 1))), 0);
        Check(p.Client.StaleConnectionMessages == 1 && p.Client.Phase == ClientPhase.Live, "old incarnation cannot advance sequence");
        p.Client.Close(); p.Host.Close(); Check(p.Client.CurrentCount == 0 && p.Client.PreparedCount == 0 && p.Client.BufferedMessages == 0 && p.Host.BufferedMessages == 0, "disconnect releases model state");

        p = new Pair(); p.Capture(Actor(1), Actor(2)); p.Pump(); p.Client.CompletePreparation(p.Client.BeginPreparation(key), true, 0);
        p.Now = 1501; p.Host.Tick(p.Now); p.Capture(Actor(1), Actor(3)); p.Pump();
        Check(p.Client.PreparedCount == 1 && p.Client.NeedsPreparation().Single().Equals(new PresenceKey(3, 1)), "S2 reuses matching S1 prepared key");
        p.Commit(); Check(p.Client.Snapshot().Select(x => x.Key.EntityId).SequenceEqual(new long[] { 1, 3 }), "S2 replaces candidate atomically");
        p.Host.RequestSnapshot(p.Now); p.Capture(Actor(1, 2)); p.Pump();
        Check(p.Client.Snapshot().Count == 2 && p.Client.NeedsPreparation().Count == 1, "old view retained until complete commit");
        p.Client.CompletePreparation(p.Client.BeginPreparation(new PresenceKey(1, 2)), true, p.Now);
        Check(p.Client.Commit(true, false, p.Now), "valid commit publishes after soft-overrun flag omitted");
        Check(!p.Client.CanSendIntent && p.Client.TakeAppliedAck().HasValue && p.Client.CanSendIntent, "ack queued before intent enabled");

        p = new Pair(); p.Live(Actor(1)); p.Delta(MessageKind.Spawn, key);
        Check(p.Client.RecoveryReason == ErrorCode.InvalidEntityLifecycle, "duplicate spawn at new sequence is protocol error");
        p.Client.Receive(ModelCodec.Encode(new Envelope(p.Session, 1, p.Client.Sequence + 1, MessageKind.IntentResult, new byte[] { 9 })), 0);
        Check(p.Client.TakeIntentResult()[0] == 9, "result remains usable during recovery");
        p = new Pair(Policy(queued: 1)); p.Capture(Actor(1)); p.Pump();
        p.Host.Delta(MessageKind.Updated, key, new byte[] { 1 }, 0); p.Host.Delta(MessageKind.Updated, key, new byte[] { 2 }, 0);
        Check(p.Host.Phase == HostPhase.WaitCapture && p.Host.BufferedMessages == 0 && p.Host.Round == 2, "overflow replaces unsent gameplay with new snapshot");
        p.Capture(Actor(1, value: 2)); p.Pump(); p.Commit(); Check(BitConverter.ToInt32(p.Client.Snapshot()[0].Data, 0) == 2, "overflow recovery captures current state");
        p = new Pair(); p.Capture(Actor(1)); p.Pump(false); p.Now = 1400; p.Deliver(); p.Commit(); p.Host.Tick(1401);
        Check(p.Host.Phase == HostPhase.Live && !p.Host.Failure.HasValue, "late End and ack within configured bound does not fault");
        p = new Pair(); p.Capture(Actor(1)); p.Pump(); p.Host.RequestSnapshot(1); p.Host.RequestSnapshot(2);
        Check(p.Host.IgnoredSnapshotRequests == 2 && p.Host.Round == 1, "duplicate requests coalesced while awaiting ack");
        p.Host.Tick(1500); Check(p.Host.Round == 2, "ack timeout opens next round");
        p.Host.Tick(5500); p.Host.Tick(9500); Check(p.Host.Failure == ErrorCode.RecoveryExhausted, "round retry budget terminates recovery");
        p = new Pair(Policy(actors: 1)); p.Capture(Actor(1), Actor(2)); Check(p.Host.Failure == ErrorCode.SnapshotTooLarge, "host oversized snapshot does not resync forever");
        p = new Pair(); p.Capture(Actor(1)); p.Pump(); p.Client.CompletePreparation(p.Client.BeginPreparation(key), false, 0);
        Check(p.Client.Failure == ErrorCode.EntityPrepareFailed && p.Client.PreparedCount == 0, "prepare failure closes and cleans");
        p = new Pair(); p.Capture(Actor(1)); p.Pump(); p.Client.CompletePreparation(p.Client.BeginPreparation(key), true, 0); p.Client.Commit(false, false, 0);
        Check(p.Client.Failure == ErrorCode.EntityCommitFailed && p.Client.CurrentCount == 0, "commit hook failure freezes dispatch and closes");
        p = new Pair(); p.Capture(Actor(1)); p.Pump(); p.Client.CompletePreparation(p.Client.BeginPreparation(key), true, 0); p.Client.Commit(true, true, 0);
        Check(p.Client.Failure == ErrorCode.EntityCommitFailed, "hard/repeated overrun closes");
        p = new Pair(); p.Capture(Actor(1)); p.Pump(); p.Client.Tick(1000);
        Check(p.Client.RecoveryReason == ErrorCode.EntityPreparationTimeout, "preparation timeout requests recovery");
        var h = new HostModel(Guid.NewGuid(), 1, policy, 0); h.Tick(1000); Check(h.Failure == ErrorCode.HandshakeTimeout, "handshake timeout");
        h = new HostModel(Guid.NewGuid(), 1, policy, 0); h.Admit(false, true, 0); Check(h.Failure == ErrorCode.ContentMismatch, "content admission rejection");
        h = new HostModel(Guid.NewGuid(), 1, policy, 0); h.Admit(true, false, 0); Check(h.Failure == ErrorCode.PolicyMismatch, "policy admission rejection");

        var world = new WorldModel(Policy(actors: 2)); var w1 = world.ReserveNew(); var w2 = world.ReserveNew();
        bool capacityBlocked = false; try { world.ReserveNew(); } catch (InvalidOperationException) { capacityBlocked = true; }
        Check(capacityBlocked && world.IdentityCount == 2, "world reserves count before publication");
        world.Cancel(w2); var w3 = world.ReserveNew(); Check(w3.EntityId > w2.EntityId, "failed load ID never reused");
        Check(world.Publish(w1, new byte[] { 1 }) && world.Publish(w3, new byte[] { 2 }), "world publication retains representable state");
        Check(!world.Update(w1, new byte[5000]) && world.Snapshot()[0].Data[0] == 1, "payload growth rejected before state mutation");
        world.RetainDormant(w3, "floor-transfer"); Check(world.DormantCount == 1 && world.IdentityCount == 2, "dormant counted in world capacity");
        Check(!world.EndRetention(w3, "other-mode") && world.EndRetention(w3, "floor-transfer") && world.IdentityCount == 1, "retention owner ends dormant terminally");
        var generation2 = world.ReserveGeneration(w1.EntityId); world.Cancel(generation2); var generation3 = world.ReserveGeneration(w1.EntityId);
        Check(generation3.Generation == 3 && world.IdentityCount == 1 && world.Publish(generation3, new byte[] { 3 }), "failed generation is not reused and replacement does not consume identity slot");
        world.EndSession(); Check(world.IdentityCount == 0 && world.DormantCount == 0, "world session cleanup");

        p = new Pair(); p.Capture(Actor(1)); p.Host.TrySend(1, batch => { p.Wire.AddRange(batch); return true; }, 0); p.Deliver();
        p.Client.Receive(ModelCodec.Encode(new Envelope(p.Session, 1, 2, MessageKind.Updated, null, key)), 0);
        Check(p.Client.RecoveryReason == ErrorCode.SnapshotInvalid, "gameplay cannot interleave snapshot chunks");
        p = new Pair(); p.Capture(Actor(1)); p.Pump(false);
        var chunk = p.Wire.FindIndex(x => ModelCodec.Decode(x, policy).Kind == MessageKind.SnapshotChunk); p.Wire[chunk][p.Wire[chunk].Length - 1] ^= 1; p.Deliver();
        Check(p.Client.RecoveryReason == ErrorCode.SnapshotInvalid && p.Client.CurrentCount == 0, "hash mismatch never publishes candidate");
        p = new Pair(); p.Live(Actor(1));
        p.Client.Receive(ModelCodec.Encode(new Envelope(p.Session, 1, p.Client.Sequence + 2, MessageKind.Updated, null, key)), 0);
        Check(p.Client.RecoveryReason == ErrorCode.SequenceGap, "ordered stream gap requests recovery immediately");
        p = new Pair(Policy(actors: 1, remembered: 1)); p.Live(Actor(1));
        for (long id = 1; id <= 1200; id++)
        {
            p.Delta(MessageKind.Despawn, new PresenceKey(id, 1));
            p.Delta(MessageKind.Spawn, new PresenceKey(id + 1, 1));
            p.Client.CompletePreparation(p.Client.BeginPreparation(new PresenceKey(id + 1, 1)), true, 0);
            Check(p.Client.Phase == ClientPhase.Live && p.Client.HistoryCount == 1 && p.Client.GenerationFloorCount == 1, "long run bounded diagnostic history");
        }
        Check(p.Client.HistoryEvictions == 1199 && !p.Client.Failure.HasValue, "history eviction is not a disconnect reason");
        p.Delta(MessageKind.Despawn, new PresenceKey(1201, 1)); p.Delta(MessageKind.Spawn, key);
        var firstReuseToken = p.Client.BeginPreparation(key);
        p.Delta(MessageKind.Despawn, key); p.Delta(MessageKind.Spawn, new PresenceKey(1300, 1)); p.Delta(MessageKind.Despawn, new PresenceKey(1300, 1)); p.Delta(MessageKind.Spawn, key);
        var secondReuseToken = p.Client.BeginPreparation(key);
        Check(secondReuseToken.LoadId > firstReuseToken.LoadId && !p.Client.CompletePreparation(firstReuseToken, true, 0), "evicted ID reused by bad host cannot accept old load completion");
        Check(p.Client.CompletePreparation(secondReuseToken, true, 0) && p.Client.CurrentCount == 1 && p.Client.IsReady(key), "reused ID still has one valid presence");
        p.Host.RequestSnapshot(0); p.Capture(Actor(1)); p.Pump(); p.Commit();
        Check(p.Client.HistoryCount == 0 && p.Client.GenerationFloorCount == 1 && p.Client.HistoryEvictions > 0, "snapshot resets diagnostics without erasing current floors or cumulative evictions");
        p = new Pair(Policy(remembered: 1)); p.Capture(Actor(1, 3)); p.Pump();
        var sharedToken = p.Client.BeginPreparation(new PresenceKey(1, 3));
        Check(sharedToken.Equals(p.Client.BeginPreparation(sharedToken.Key)), "same pending load retains its one attempt token");
        p.Now = 1500; p.Host.Tick(p.Now); p.Capture(Actor(1, 3), Actor(2));
        p.Host.TrySend(1, batch => { p.Wire.AddRange(batch); return true; }, p.Now); p.Deliver();
        Check(p.Client.Phase == ClientPhase.Assembling && p.Client.CompletePreparation(sharedToken, true, p.Now), "matching pending completion can finish during replacement assembly");
        p.Pump();
        Check(p.Client.GenerationFloorCount == 2 && p.Client.PreparedCount == 1 && p.Client.NeedsPreparation().Single().Equals(new PresenceKey(2, 1)), "candidate floors survive independently of tiny history ring");
        Check(!p.Client.CompletePreparation(sharedToken, true, p.Now), "duplicate callback for same load is ignored");
        p.Commit(); Check(p.Client.GenerationFloorCount == 2 && p.Client.HistoryCount == 0, "commit resets only diagnostic history");
        p = new Pair(); p.Live(Actor(1)); p.Delta(MessageKind.Spawn, new PresenceKey(2, 1)); p.Client.Tick(1000);
        Check(!p.Client.IsReady(new PresenceKey(2, 1)) && p.Client.IsReady(key), "recovery does not turn canceled load into a ready actor");
        p = new Pair(); p.Capture(Actor(1));
        bool reentrant = false;
        p.Host.TrySend(1, _ => { try { p.Host.Close(); } catch (InvalidOperationException) { reentrant = true; } return false; }, 0);
        Check(reentrant && p.Host.SentSequence == 0 && p.Host.Phase == HostPhase.SendingSnapshot, "reentrant adapter mutation rejected without committing batch");
        p = new Pair(); p.Capture(Actor(1, 2)); p.Pump(); p.Now = 1500; p.Host.Tick(p.Now); p.Capture(Actor(1, 1)); p.Pump();
        Check(p.Client.RecoveryReason == ErrorCode.SnapshotInvalid, "verified candidate generation cannot decrease in replacement snapshot");

        // Ordered delivery with load delay, bounded send rejection, replacement and disconnect.
        for (int seed = 0; seed < 24; seed++)
        {
            var random = new Random(seed); p = new Pair(); p.Live(Actor(1)); long nextId = 2;
            var authoritative = new Dictionary<long, ActorState> { [1] = Actor(1) };
            for (int step = 0; step < 160; step++)
            {
                p.Now += 1;
                var id = authoritative.Keys.ElementAt(random.Next(authoritative.Count)); var state = authoritative[id];
                int choice = random.Next(6);
                if (choice == 0 && authoritative.Count < 20)
                {
                    var added = Actor(nextId++); authoritative.Add(added.Key.EntityId, added);
                    p.Host.Delta(MessageKind.Spawn, added.Key, added.Data, p.Now);
                }
                else if (choice == 1 && id != 1)
                { authoritative.Remove(id); p.Host.Delta(MessageKind.Despawn, state.Key, null, p.Now); }
                else if (choice == 2)
                {
                    var replacement = Actor(id, state.Key.Generation + 1); authoritative[id] = replacement;
                    p.Host.Delta(MessageKind.Spawn, replacement.Key, replacement.Data, p.Now);
                }
                else
                {
                    var changed = Actor(id, state.Key.Generation, value: step); authoritative[id] = changed;
                    p.Host.Delta(MessageKind.Updated, changed.Key, changed.Data, p.Now);
                }
                if (random.Next(4) == 0) p.Host.TrySend(2, _ => false, p.Now);
                p.Pump();
                if (random.Next(3) == 0) foreach (var pendingKey in p.Client.NeedsPreparation()) p.Client.CompletePreparation(p.Client.BeginPreparation(pendingKey), true, p.Now);
                if (step % 31 == 30)
                { p.Host.RequestSnapshot(p.Now); p.Capture(authoritative.Values.ToArray()); p.Pump(); p.Commit(); }
                Check(p.Client.Phase == ClientPhase.Live && p.Client.Snapshot().Select(x => x.Key.EntityId).Distinct().Count() == p.Client.CurrentCount, "seed " + seed + " step " + step + " identity invariant");
                Check(p.Client.BufferedMessages <= policy.MaximumQueuedMessages && p.Host.AcceptedBytes <= policy.MaximumAcceptedBytes, "seed capacity invariant");
                steps++;
            }
            foreach (var pendingKey in p.Client.NeedsPreparation()) p.Client.CompletePreparation(p.Client.BeginPreparation(pendingKey), true, p.Now);
            Check(p.Client.Snapshot().All(x => authoritative[x.Key.EntityId].Data.SequenceEqual(x.Data)), "seed final data parity");
            foreach (var state in authoritative.Values) p.Delta(MessageKind.Despawn, state.Key);
            Check(p.Client.CurrentCount == 0 && p.Client.PreparedCount == 0 && p.Client.BufferedMessages == 0, "seed final resources zero");
            p.Client.Close(); p.Host.Close();
        }
        return "PASS " + checks + " lifecycle model checks; " + steps + " seeded simulation steps (24 seeds); transport/Unity lifecycle integration not exercised";
    }
}
