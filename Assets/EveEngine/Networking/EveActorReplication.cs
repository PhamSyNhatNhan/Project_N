using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EveEngine.Experimental.Lifecycle;

namespace EveEngine
{
    public interface EveIReplicaStage : IDisposable { void Commit(); void Published(); }
    public interface EveIEntityReplicationPart
    {
        EveId ReplicationId { get; }
        bool CanCaptureReplication { get; }
        byte[] CaptureReplication();
        EveIReplicaStage StageReplication(byte[] payload, Guid authoritativeSession, long generation);
        void ApplyReplicationDelta(byte[] payload, Guid authoritativeSession, long generation);
        event Action<byte[]> ReplicationDelta;
    }
    public enum EveReplicationTraffic { Required, Optional }
    // Optional opt-in. Pure classification of the bytes emitted on the existing event.
    public interface EveIReplicationDeltaClassifier
    {
        EveReplicationTraffic ClassifyReplicationDelta(byte[] payload);
    }
    internal sealed class EveActorData
    {
        internal string Character;
        internal long Owner;
        internal float[] Pose = new float[] { 0, 0, 0, 0, 0, 0, 1, 1, 1, 1 };
        internal void CapturePose(EveEntityLoader loader)
        {
#if UNITY_5_3_OR_NEWER
            var p = loader.transform.position; var r = loader.transform.rotation; var s = loader.transform.localScale;
            Pose = new[] { p.x, p.y, p.z, r.x, r.y, r.z, r.w, s.x, s.y, s.z };
#endif
        }
        internal void ApplyPose(EveEntityLoader loader)
        {
#if UNITY_5_3_OR_NEWER
            loader.transform.SetPositionAndRotation(new UnityEngine.Vector3(Pose[0], Pose[1], Pose[2]), new UnityEngine.Quaternion(Pose[3], Pose[4], Pose[5], Pose[6]));
            loader.transform.localScale = new UnityEngine.Vector3(Pose[7], Pose[8], Pose[9]);
#endif
        }
        internal Dictionary<EveId, byte[]> Parts = new Dictionary<EveId, byte[]>();
        internal EveId DeltaPart;
        internal byte[] Delta;
        internal byte[] Encode(int cap)
        {
            using (EveHotPathMetrics.Measure("actor.encode"))
            {
            if (Delta != null)
            {
                var deltaObject = new JObject { ["version"] = 2, ["character"] = Character, ["owner"] = Owner,
                    ["deltaPart"] = DeltaPart.ToString(), ["delta"] = Convert.ToBase64String(Delta) };
                var encoded = Encoding.UTF8.GetBytes(deltaObject.ToString(Formatting.None));
                if (encoded.Length > cap) throw new FormatException("Actor payload exceeds configured limit.");
                return encoded;
            }
            var obj = new JObject { ["version"] = 1, ["character"] = Character, ["owner"] = Owner,
                ["pose"] = new JArray(Pose),
                ["parts"] = new JObject(Parts.OrderBy(x => x.Key).Select(x => new JProperty(x.Key.ToString(), Convert.ToBase64String(x.Value)))) };
            if (Delta != null) { obj["deltaPart"] = DeltaPart.ToString(); obj["delta"] = Convert.ToBase64String(Delta); }
            var bytes = Encoding.UTF8.GetBytes(obj.ToString(Formatting.None));
            if (bytes.Length > cap) throw new FormatException("Actor payload exceeds configured limit."); return bytes;
            }
        }
        internal static EveActorData Decode(byte[] bytes, int cap)
        {
            if (bytes == null || bytes.Length > cap) throw new FormatException("Actor payload size.");
            using (var reader = new JsonTextReader(new System.IO.StringReader(Encoding.UTF8.GetString(bytes))) { MaxDepth = 8 })
            {
                var obj = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if ((int?)obj["version"] == 2)
                {
                    if (reader.Read() || obj.Count != 5 || obj.Properties().Any(x => !new[] { "version", "character", "owner", "deltaPart", "delta" }.Contains(x.Name))) throw new FormatException("Actor delta schema.");
                    var delta = new EveActorData { Character = (string)obj["character"], Owner = (long?)obj["owner"] ?? -1,
                        DeltaPart = EveId.Parse((string)obj["deltaPart"]), Delta = Convert.FromBase64String((string)obj["delta"]) };
                    EveId.Parse(delta.Character); if (delta.Owner < 0) throw new FormatException("Actor delta descriptor.");
                    return delta;
                }
                if (reader.Read() || obj.Properties().Any(x => !new[] { "version", "character", "owner", "pose", "parts", "deltaPart", "delta" }.Contains(x.Name)) || (int?)obj["version"] != 1) throw new FormatException("Actor schema.");
                var result = new EveActorData { Character = (string)obj["character"], Owner = (long?)obj["owner"] ?? -1 };
                EveId.Parse(result.Character); if (result.Owner < 0 || !(obj["parts"] is JObject parts)) throw new FormatException("Actor descriptor.");
                if (!(obj["pose"] is JArray pose) || pose.Count != 10 || pose.Any(x => x.Type != JTokenType.Float && x.Type != JTokenType.Integer)) throw new FormatException("Actor pose.");
                result.Pose = pose.Select(x => x.Value<float>()).ToArray();
                if (result.Pose.Any(x => float.IsNaN(x) || float.IsInfinity(x)) || result.Pose.Skip(3).Take(4).Sum(x => (double)x * x) < .0001) throw new FormatException("Non-finite actor pose.");
                foreach (var part in parts.Properties()) result.Parts.Add(EveId.Parse(part.Name), Convert.FromBase64String(part.Value.Value<string>()));
                if (obj["delta"] != null) { result.DeltaPart = EveId.Parse((string)obj["deltaPart"]); result.Delta = Convert.FromBase64String((string)obj["delta"]); if (!result.Parts.ContainsKey(result.DeltaPart)) throw new FormatException("Delta part not selected."); }
                return result;
            }
        }
    }
    // Caller explicitly enrolls actors. A loader, Area, projectile or GameObject is not enrolled automatically.
    public sealed class EveActorHostConnection : IDisposable
    {
        private sealed class DeltaCache { internal byte[] Data; internal readonly Dictionary<(EveEntityLoader, long, EveId), byte[]> Encoded = new Dictionary<(EveEntityLoader, long, EveId), byte[]>(); }
        private static readonly ConditionalWeakTable<byte[], DeltaCache> deltaCache = new ConditionalWeakTable<byte[], DeltaCache>();
        private sealed class Actor
        { internal EveEntityLoader Loader; internal ActorState Last; internal List<(EveIEntityReplicationPart, Action<byte[]>)> Subscriptions = new List<(EveIEntityReplicationPart, Action<byte[]>)>(); }
        private readonly EveSessionContext session;
        private readonly ModelPolicy policy;
        private readonly Func<double> clock;
        private readonly Dictionary<long, Actor> actors = new Dictionary<long, Actor>();
        private bool captureQueued;
        public HostModel Protocol { get; }
        public long Incarnation { get; }
        public long ClassifierErrors { get; private set; }
        public string LastReplicationError { get; private set; }
        public EveActorHostConnection(EveSessionContext session, long incarnation, ModelPolicy policy, Func<double> monotonicMs, EveContentCompatibility clientContent)
            : this(session, incarnation, policy, monotonicMs, clientContent, null) { }
        public EveActorHostConnection(EveSessionContext session, long incarnation, ModelPolicy policy, Func<double> monotonicMs,
            EveContentCompatibility clientContent, OptionalReplicationPolicy optionalPolicy)
        {
            if (session == null || !session.IsActive || session.Role != EveSessionRole.Host || session.Snapshots == null) throw new InvalidOperationException("Host with explicit snapshot scheduler required.");
            this.session = session; this.policy = policy ?? throw new ArgumentNullException(nameof(policy)); clock = monotonicMs ?? throw new ArgumentNullException(nameof(monotonicMs)); Incarnation = incarnation;
            session.Compatibility.Validate(clientContent); session.ReserveConnectionIncarnation(incarnation); Protocol = new HostModel(session.SessionId, incarnation, policy, clock(), optionalPolicy); Protocol.Admit(true, true, clock());
        }
        private ActorState Capture(Actor actor, byte[] delta = null, EveId partId = default)
        {
            using (EveHotPathMetrics.Measure("actor.capture"))
            {
            EveEntityIdentity id; EveActorData data;
            using (EveHotPathMetrics.Measure("actor.meta"))
            { if (actor.Loader == null || !actor.Loader.IsReady) throw new InvalidOperationException("Published actor required.");
            id = actor.Loader.GetComponent<EveEntityIdentity>(); data = new EveActorData { Character = id.CharacterId.ToString(), Owner = id.OwnerId, Delta = delta, DeltaPart = partId }; }
            if (delta != null)
            {
                using (EveHotPathMetrics.Measure("actor.cache"))
                {
                var cache = deltaCache.GetValue(delta, _ => new DeltaCache()); var key = (actor.Loader, id.Generation, partId);
                if (cache.Data == null || !cache.Data.SequenceEqual(delta)) { cache.Data = (byte[])delta.Clone(); cache.Encoded.Clear(); }
                if (!cache.Encoded.TryGetValue(key, out var encoded)) { EveHotPathMetrics.Increment("actor.cache.miss"); encoded = data.Encode(policy.MaximumPayloadBytes); cache.Encoded.Add(key, encoded); EveHotPathMetrics.Increment("wire.actor.encoded-bytes", encoded.Length); }
                else EveHotPathMetrics.Increment("actor.cache.hit");
                if (encoded.Length > policy.MaximumPayloadBytes) throw new FormatException("Actor payload exceeds configured limit.");
                return new ActorState(new PresenceKey(id.Id.Value, id.Generation), false, encoded, true);
                }
            }
            data.CapturePose(actor.Loader);
            foreach (var part in actor.Loader.GetComponents<UnityEngine.MonoBehaviour>().OfType<EveIEntityReplicationPart>())
            { if (delta == null && !part.CanCaptureReplication) throw new InvalidOperationException("Capture must be at a committed mutation boundary."); data.Parts.Add(part.ReplicationId, part.CaptureReplication()); }
            return new ActorState(new PresenceKey(id.Id.Value, id.Generation), false, data.Encode(policy.MaximumPayloadBytes), true);
            }
        }
        public void Track(EveEntityLoader loader)
        {
            if (!session.IsActive || loader == null || !loader.IsReady) throw new InvalidOperationException("Published host actor required.");
            long id = loader.GetComponent<EveEntityIdentity>().Id.Value;
            if (id <= 0 || actors.Count >= policy.MaximumActors && !actors.ContainsKey(id)) throw new InvalidOperationException("Actor scope capacity/identity.");
            if (actors.TryGetValue(id, out var old)) { if (loader.Generation <= old.Last.Key.Generation) throw new InvalidOperationException("Generation must increase."); Unsubscribe(old); }
            var actor = new Actor { Loader = loader }; actor.Last = Capture(actor); actors[id] = actor;
            Protocol.DeltaOwned(MessageKind.Spawn, actor.Last, clock());
            foreach (var part in loader.GetComponents<UnityEngine.MonoBehaviour>().OfType<EveIEntityReplicationPart>())
            {
                var selected = part;
                Action<byte[]> listener = delta =>
                {
                    using (EveHotPathMetrics.Measure("actor.listener"))
                    {
                    if (EveHotPathMetrics.Active) EveHotPathMetrics.Increment("actor.listener.peer." + Incarnation);
                    if (!session.IsActive || !loader.IsReady || !actors.TryGetValue(id, out var current) || !ReferenceEquals(current, actor)) return;
                    try
                    {
                        var traffic = EveReplicationTraffic.Required;
                        if (selected is EveIReplicationDeltaClassifier classifier)
                        {
                            try {
                                traffic = classifier.ClassifyReplicationDelta(delta);
                                if (traffic != EveReplicationTraffic.Required && traffic != EveReplicationTraffic.Optional)
                                    throw new InvalidOperationException("Invalid replication classification.");
                            } catch { ClassifierErrors++; throw; }
                        }
                        bool optional = traffic == EveReplicationTraffic.Optional && Protocol.OptionalPolicy != null;
                        if (optional && Protocol.RejectOptionalBeforeCapture(clock())) return;
                        var captured = Capture(actor, delta, selected.ReplicationId);
                        actor.Last = captured;
                        using (EveHotPathMetrics.Measure("host.deltaowned"))
                        {
                            if (optional) Protocol.TryDeltaOptionalOwned(MessageKind.Updated, captured, clock());
                            else Protocol.DeltaOwned(MessageKind.Updated, captured, clock());
                        }
                    }
                    catch (Exception error) {
                        if (LastReplicationError == null) { var message=error.GetType().Name+": "+error.Message; LastReplicationError=message.Substring(0, Math.Min(message.Length,256)); }
                        Protocol.Close(ErrorCode.SnapshotTooLarge); // existing replication-capture failure code
                    }
                    }
                };
                part.ReplicationDelta += listener; actor.Subscriptions.Add((part, listener));
            }
        }
        public void CaptureSnapshot()
        {
            if (session.IsPublishing || !session.IsActive) throw new InvalidOperationException("Committed host boundary required.");
            if (captureQueued || Protocol.Phase != HostPhase.WaitCapture) return;
            captureQueued = true; int round = Protocol.Round;
            var scopeActors = actors.Values.ToArray();
            string scope = session.SessionId + ":" + policy.MaximumPayloadBytes + ":" + policy.MaximumSnapshotBytes + ":" + string.Join(",", actors.Values.OrderBy(x => x.Last.Key.EntityId).Select(x => x.Last.Key.ToString()));
            try
            {
                session.Snapshots.RequestCapture(scope, () => Freeze(scopeActors), () => { if (Protocol.Phase == HostPhase.WaitCapture && Protocol.Round == round) Protocol.BeginSnapshotCapture(clock()); },
                    states => { captureQueued = false; if (Protocol.Phase == HostPhase.PreparingSnapshot && Protocol.Round == round) Protocol.Capture(states, clock()); },
                    error => { captureQueued = false; Protocol.Close(ErrorCode.SnapshotTooLarge); });
            }
            catch { captureQueued = false; throw; }
        }
        private EveFrozenActorSnapshot Freeze(IEnumerable<Actor> scopeActors)
        {
            var states = new List<ActorState>(); var work = new List<Action>();
            foreach (var actor in scopeActors.OrderBy(x => x.Last.Key.EntityId))
            {
                if (actor.Last.IsDormant) { states.Add(actor.Last); continue; }
                if (!actor.Loader.IsReady) throw new InvalidOperationException("Actor lost before capture boundary.");
                var identity = actor.Loader.GetComponent<EveEntityIdentity>(); var key = new PresenceKey(identity.Id.Value, identity.Generation);
                var data = new EveActorData { Character = identity.CharacterId.ToString(), Owner = identity.OwnerId };
                data.CapturePose(actor.Loader);
                foreach (var part in actor.Loader.GetComponents<UnityEngine.MonoBehaviour>().OfType<EveIEntityReplicationPart>())
                {
                    if (!part.CanCaptureReplication) throw new InvalidOperationException("Subsystem mutation still pending.");
                    var id = part.ReplicationId; Func<byte[]> serialize;
                    if (part is EveIFrozenEntityReplicationPart frozen) serialize = frozen.FreezeReplication();
                    else { var bytes = part.CaptureReplication(); serialize = () => bytes; }
                    work.Add(() => data.Parts.Add(id, serialize()));
                }
                work.Add(() => states.Add(new ActorState(key, false, data.Encode(policy.MaximumPayloadBytes))));
            }
            return new EveFrozenActorSnapshot(work, () => states.OrderBy(x => x.Key.EntityId).ToArray());
        }
        public void RetainDormant(long entityId)
        {
            var actor = actors[entityId]; actor.Last = Capture(actor); Unsubscribe(actor);
            actor.Last = new ActorState(actor.Last.Key, true, actor.Last.Bytes, true);
            Protocol.DeltaOwned(MessageKind.Dormant, actor.Last, clock());
        }
        public void Despawn(long entityId)
        {
            if (!actors.TryGetValue(entityId, out var actor)) return;
            Protocol.Delta(MessageKind.Despawn, actor.Last.Key, Array.Empty<byte>(), clock()); Unsubscribe(actor); actors.Remove(entityId);
        }
        public bool Send(int maximumBatch, Func<IReadOnlyList<byte[]>, bool> accept) => Protocol.TrySend(maximumBatch, accept, clock());
        public void AdvanceFrame() { Protocol.Tick(clock()); if (Protocol.Phase == HostPhase.WaitCapture) CaptureSnapshot(); }
        public void Applied(AppliedAck ack) => Protocol.Applied(ack.Incarnation, ack.SnapshotId, ack.EndSequence, clock());
        private static void Unsubscribe(Actor actor) { foreach (var pair in actor.Subscriptions) pair.Item1.ReplicationDelta -= pair.Item2; actor.Subscriptions.Clear(); }
        public void Dispose() { foreach (var actor in actors.Values) Unsubscribe(actor); actors.Clear(); Protocol.Close(); }
    }
}
