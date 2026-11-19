using System;
using System.IO;
using EveEngine;
using Mirror;

namespace ProjectN.Coop.Tests
{
    // Checks of the wire of the spawned-object sync (Docs/SpawnedObjectSyncPlan.md O3): a packet written and read back gives the same records, and a malformed packet is refused, never taken in part.
    public static class PNObjectWireChecks
    {
        public static string Run()
        {
            int count = 0; void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
            void Reject(Action action, string name) { try { action(); } catch (FormatException) { count++; return; } throw new Exception(name); }
            var ticket = new PNWorldTicket { Room = Guid.NewGuid(), Play = Guid.NewGuid(), World = Guid.NewGuid(), RoomIncarnation = 1, PlayRevision = 2, Incarnation = 3, Subscription = 4 };
            var world = new PNObjectWorld(ticket.World, ticket.Incarnation);
            var spawn = PNObjectRecord.Spawn(PNObjectKind.Projectile, 17, world, EveId.Parse("project_n:eve_star"), "{\"n\":2}", new PNObjectVector(1.5f, 2, -3), new PNObjectVector(0, 0, 1), 55, 66, 0, "cast-7", 2, 1.25, 2d, .5, 123.5, .75f, 3.5f);
            var retarget = PNObjectRecord.Retarget(PNObjectKind.Projectile, 17, world, 77, new PNObjectVector(4, 5, 6), new PNObjectVector(1, 0, 0), 124.25);
            var end = PNObjectRecord.End(PNObjectKind.Projectile, 17, world, 3, new PNObjectVector(9, 8, 7));
            PNObjectPacket RoundTrip(PNObjectPacket packet)
            {
                using (var writer = NetworkWriterPool.Get()) { writer.WriteObjectPacket(packet); using (var reader = NetworkReaderPool.Get(writer.ToArraySegment())) return reader.ReadObjectPacket(); }
            }
            var back = RoundTrip(new PNObjectPacket { Ticket = ticket, Flags = 0, Records = new[] { spawn, retarget, end } });
            Check(back.Ticket.Equals(ticket) && back.Flags == 0 && back.Records.Length == 3, "a packet keeps its ticket and its three records");
            var s = back.Records[0];
            Check(s.Type == PNObjectRecordType.Spawn && s.Kind == PNObjectKind.Projectile && s.Id == 17 && s.World.Equals(world) && s.DefinitionId == spawn.DefinitionId && s.Input == "{\"n\":2}" && s.Position.X == 1.5f && s.Position.Z == -3f
                && s.Direction.Z == 1f && s.Target == 55 && s.Owner == 66 && s.CastKey == "cast-7" && s.CastIndex == 2 && s.Speed == 1.25 && s.Scale == 2d && s.Lifetime == .5 && s.Stamp == 123.5 && s.Age == .75f && s.LifeHint == 3.5f, "a spawn record survives the wire field by field");
            var r = back.Records[1]; var e = back.Records[2];
            Check(r.Type == PNObjectRecordType.Retarget && r.Id == 17 && r.Target == 77 && r.Position.Y == 5f && r.Direction.X == 1f && r.Stamp == 124.25 && e.Type == PNObjectRecordType.End && e.Reason == 3 && e.Position.X == 9f, "retarget and end records survive the wire");
            var plain = RoundTrip(new PNObjectPacket { Ticket = ticket, Flags = 0, Records = new[] { PNObjectRecord.Spawn(PNObjectKind.Projectile, 18, world, EveId.Parse("project_n:eve_star"), null, new PNObjectVector(0, 0, 0), new PNObjectVector(0, 0, 1), 0, 0, 0, null, 0, 1, 1, 1, 1, 0f, 0f) } });
            Check(plain.Records[0].Input == null && plain.Records[0].CastKey == null, "an absent input and cast key stay absent");

            var baseline = RoundTrip(new PNObjectPacket { Ticket = ticket, Flags = (byte)(PNObjectPacketFlags.Baseline | PNObjectPacketFlags.Start | PNObjectPacketFlags.End), Records = new PNObjectRecord[0] });
            Check(baseline.Records.Length == 0 && baseline.Flags == 7, "an empty baseline (start and end in one packet) is allowed");
            Reject(() => RoundTrip(new PNObjectPacket { Ticket = ticket, Flags = 0, Records = new PNObjectRecord[0] }), "an empty packet that is no baseline is refused");
            Reject(() => RoundTrip(new PNObjectPacket { Ticket = ticket, Flags = PNObjectPacketFlags.Start, Records = new[] { spawn } }), "Start without Baseline is refused");
            Reject(() => RoundTrip(new PNObjectPacket { Ticket = ticket, Flags = 8, Records = new[] { spawn } }), "an unknown flag is refused");
            Reject(() => RoundTrip(new PNObjectPacket { Ticket = ticket, Flags = PNObjectPacketFlags.Baseline, Records = new[] { end } }), "a baseline that lists an End is refused");
            var other = PNObjectRecord.End(PNObjectKind.Projectile, 17, new PNObjectWorld(Guid.NewGuid(), 3), 0, new PNObjectVector(0, 0, 0));
            Reject(() => RoundTrip(new PNObjectPacket { Ticket = ticket, Flags = 0, Records = new[] { other } }), "a record of another world than the ticket's is refused");
            var many = new PNObjectRecord[PNObjectLimits.RecordsPerPacket + 1]; for (int i = 0; i < many.Length; i++) many[i] = end;
            Reject(() => RoundTrip(new PNObjectPacket { Ticket = ticket, Flags = 0, Records = many }), "more records than a packet may hold are refused");
            Reject(() => RoundTrip(new PNObjectPacket { Ticket = default, Flags = 0, Records = new[] { end } }), "a packet without a valid ticket is refused");

            // A packet that was cut or padded is refused at read time.
            using (var writer = NetworkWriterPool.Get())
            {
                writer.WriteObjectPacket(new PNObjectPacket { Ticket = ticket, Flags = 0, Records = new[] { retarget } });
                var bytes = writer.ToArray();
                Reject(() => { using (var reader = NetworkReaderPool.Get(new ArraySegment<byte>(bytes, 0, bytes.Length - 3))) { try { reader.ReadObjectPacket(); } catch (EndOfStreamException) { throw new FormatException("cut"); } } }, "a cut packet is refused");
                var padded = new byte[bytes.Length + 1]; Array.Copy(bytes, padded, bytes.Length);
                Reject(() => { using (var reader = NetworkReaderPool.Get(new ArraySegment<byte>(padded))) reader.ReadObjectPacket(); }, "a padded packet is refused");
                var wrong = (byte[])bytes.Clone(); wrong[0] = 2;
                Reject(() => { using (var reader = NetworkReaderPool.Get(new ArraySegment<byte>(wrong))) reader.ReadObjectPacket(); }, "an unknown schema is refused");
            }
            // The summon_scale a client gives its copy of a summon (4.8): applied once, replaced rather than stacked, and given back when the object is disabled (it is pooled).
            var go = new UnityEngine.GameObject("scale view"); go.transform.localScale = UnityEngine.Vector3.one * 2f;
            try
            {
                var view = go.AddComponent<PNScaleView>();
                view.Apply(1.5f); Check(UnityEngine.Mathf.Abs(go.transform.localScale.x - 3f) < 1e-4f && view.Factor == 1.5f, "a summon scale of 1.5 grows the object from its prefab size");
                view.Apply(2f); Check(UnityEngine.Mathf.Abs(go.transform.localScale.x - 4f) < 1e-4f, "a second factor replaces the first instead of stacking");
                view.Clear(); Check(UnityEngine.Mathf.Abs(go.transform.localScale.x - 2f) < 1e-4f && view.Factor == 1f, "a disabled (pooled) object gets its prefab size back");
                view.Apply(float.NaN); view.Apply(0f); view.Apply(-1f); view.Apply(1f);
                Check(UnityEngine.Mathf.Abs(go.transform.localScale.x - 2f) < 1e-4f, "a bad or unit factor leaves the prefab size");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            return "PASS " + count + " object wire checks (round trip of spawn / retarget / end, baseline flags, bounds, world and schema, summon scale view)";
        }
    }
}
