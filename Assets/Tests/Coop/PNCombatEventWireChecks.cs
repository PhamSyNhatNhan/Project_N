using System;
using System.IO;
using EveEngine;
using Mirror;

namespace ProjectN.Coop.Tests
{
    // Checks of the combat event channel (Docs/SpawnedObjectSyncPlan.md 4.4b part 3): the hub tells each event once and matches an early hit to the host's event by key (or cancels it), and a packet written and read back
    // keeps every field while a malformed one is refused whole.
    public static class PNCombatEventWireChecks
    {
        private sealed class Capture : IPNCombatEventTransport { public int Count; public PNCombatEvent Last; public void Send(PNCombatEvent e) { Count++; Last = e; } }

        public static string Run()
        {
            int count = 0; void Check(bool value, string name) { if (!value) throw new Exception("Combat event check " + count + ": " + name); count++; }
            void Reject(Action action, string name) { try { action(); } catch (FormatException) { count++; return; } throw new Exception("Combat event check " + count + ": " + name); }
            var ticket = new PNWorldTicket { Room = Guid.NewGuid(), Play = Guid.NewGuid(), World = Guid.NewGuid(), RoomIncarnation = 1, PlayRevision = 2, Incarnation = 3, Subscription = 4 };
            var world = new PNObjectWorld(ticket.World, ticket.Incarnation);
            PNCombatEvent Make(string key = "7001:0:0") => new PNCombatEvent
            {
                Kind = PNCombatEventKind.Damage, World = world, TargetId = 55, SourceId = 66, Applied = 12.5f, Calculated = 20f, LoricaAbsorbed = 7.5f, HealthBefore = 100f, HealthAfter = 87.5f,
                Critical = true, Killed = false, LoricaBroke = true, InIFrame = false, HasPoint = true, PointX = 1.5f, PointY = 2f, PointZ = -3f, HasNormal = true, NormalX = 0f, NormalY = 1f, NormalZ = 0f, HitKey = key, Tags = new[] { "base:physical", "base:slash" }
            };

            // ---- hub ----
            double now = 100d; PNCombatEvents.Reset(); PNCombatEvents.Clock = () => now; PNCombatEvents.EarlyHoldSeconds = 1.5d;
            var capture = new Capture(); PNCombatEvents.Transport = capture;
            int seen = 0, early = 0, cancelled = 0; bool lastMatched = false; PNCombatEvent lastSeen = null;
            Action<PNCombatEvent, bool> onObserved = (e, matched) => { seen++; lastMatched = matched; lastSeen = e; };
            Action<PNEarlyHit> onEarly = hit => early++; Action<PNEarlyHit> onCancelled = hit => cancelled++;
            PNCombatEvents.Observed += onObserved; PNCombatEvents.Early += onEarly; PNCombatEvents.EarlyCancelled += onCancelled;
            try
            {
                PNCombatEvents.Publish(Make("none"));
                Check(seen == 1 && capture.Count == 1 && !lastMatched && lastSeen.Serial == 1 && lastSeen.Clock == 100d, "an event the authority publishes is told here once, handed to the wire once, stamped with a serial and the clock");
                Check(PNCombatEvents.Published == 1 && PNCombatEvents.Told == 1, "(and counted)");
                // early, then the host's event with the same key: one match, no cancel
                PNCombatEvents.RaiseEarly(55, "7001:0:0", true, 1, 2, 3);
                Check(early == 1 && PNCombatEvents.PendingEarly == 1, "an early hit is told and kept");
                PNCombatEvents.RaiseEarly(55, "7001:0:0", true, 1, 2, 3);
                Check(early == 1, "the same early hit is not told twice");
                PNCombatEvents.Deliver(Make("7001:0:0"));
                Check(seen == 2 && lastMatched && PNCombatEvents.PendingEarly == 0 && PNCombatEvents.EarlyMatched == 1, "the event with the same key is matched to the early hit");
                now += 5d; PNCombatEvents.Expire(now);
                Check(cancelled == 0, "a matched early hit is never cancelled");
                // early that nobody confirms: cancelled after the hold, not before
                PNCombatEvents.RaiseEarly(55, "7002:0:0", false, 0, 0, 0);
                now += 1d; PNCombatEvents.Expire(now);
                Check(cancelled == 0 && PNCombatEvents.PendingEarly == 1, "an early hit waits for the host's event for the hold time");
                now += 1d; PNCombatEvents.Expire(now);
                Check(cancelled == 1 && PNCombatEvents.PendingEarly == 0 && PNCombatEvents.EarlyCancels == 1, "an early hit nobody confirms is cancelled after the hold");
                PNCombatEvents.Deliver(Make("7002:0:0"));
                Check(!lastMatched && seen == 3, "the event of a cancelled hit still comes (as an ordinary one)");
                // an event with another key does not take the early hit
                PNCombatEvents.RaiseEarly(55, "7003:0:0", false, 0, 0, 0); PNCombatEvents.Deliver(Make("7003:1:0"));
                Check(!lastMatched && PNCombatEvents.PendingEarly == 1, "an event with another key leaves the early hit waiting");
                now += 3d; PNCombatEvents.Expire(now);
                // bad events are refused and counted
                int refused = PNCombatEvents.Refused; var bad = Make(); bad.Applied = float.NaN; PNCombatEvents.Deliver(bad); bad = Make(); bad.TargetId = 0; PNCombatEvents.Deliver(bad); bad = Make(); bad.Tags = new string[9]; PNCombatEvents.Deliver(bad);
                Check(PNCombatEvents.Refused == refused + 3, "a NaN amount, a missing target and too many tags are refused (" + (PNCombatEvents.Refused - refused) + ")");
                // a listener that throws does not stop the others or the caller
                Action<PNCombatEvent, bool> faulty = (e, matched) => throw new InvalidOperationException("listener fault"); PNCombatEvents.Observed += faulty;
                int before = seen; try { PNCombatEvents.Deliver(Make("x")); } catch (Exception) { throw new Exception("a faulty listener reached the caller"); }
                Check(seen == before + 1, "a faulty listener is contained"); PNCombatEvents.Observed -= faulty;
            }
            finally { PNCombatEvents.Observed -= onObserved; PNCombatEvents.Early -= onEarly; PNCombatEvents.EarlyCancelled -= onCancelled; PNCombatEvents.Reset(); PNCombatEvents.Clock = null; }

            // ---- wire ----
            PNCombatPacket RoundTrip(PNCombatPacket packet)
            {
                using (var writer = NetworkWriterPool.Get()) { writer.WriteCombatPacket(packet); using (var reader = NetworkReaderPool.Get(writer.ToArraySegment())) return reader.ReadCombatPacket(); }
            }
            var second = Make(null); second.Kind = PNCombatEventKind.Heal; second.HasPoint = false; second.HasNormal = false; second.Tags = PNCombatEvent.NoTags; second.Critical = second.Killed = second.LoricaBroke = false; second.Serial = 9; second.Clock = 321.25;
            var first = Make(); first.Serial = 8; first.Clock = 320.5; first.Killed = true;
            var back = RoundTrip(new PNCombatPacket { Ticket = ticket, Events = new[] { first, second } });
            Check(back.Ticket.Equals(ticket) && back.Events.Length == 2, "a packet keeps its ticket and its events");
            var a = back.Events[0];
            Check(a.Kind == PNCombatEventKind.Damage && a.World.Equals(world) && a.Serial == 8 && a.TargetId == 55 && a.SourceId == 66 && a.Applied == 12.5f && a.Calculated == 20f && a.LoricaAbsorbed == 7.5f && a.HealthBefore == 100f && a.HealthAfter == 87.5f
                && a.Critical && a.Killed && a.LoricaBroke && !a.InIFrame && a.HasPoint && a.PointX == 1.5f && a.PointY == 2f && a.PointZ == -3f && a.HasNormal && a.NormalY == 1f && a.HitKey == "7001:0:0" && a.Tags.Length == 2 && a.Tags[1] == "base:slash" && a.Clock == 320.5, "a damage event survives the wire field by field");
            var b = back.Events[1];
            Check(b.Kind == PNCombatEventKind.Heal && !b.HasPoint && !b.HasNormal && b.HitKey == null && b.Tags.Length == 0 && !b.Critical && b.Clock == 321.25 && b.Serial == 9, "a heal event without point, key or tags stays without them");
            var other = Make(); other.World = new PNObjectWorld(Guid.NewGuid(), 3);
            Reject(() => RoundTrip(new PNCombatPacket { Ticket = ticket, Events = new[] { other } }), "an event of another world than the ticket's is refused");
            Reject(() => RoundTrip(new PNCombatPacket { Ticket = ticket, Events = new PNCombatEvent[0] }), "an empty packet is refused");
            var many = new PNCombatEvent[PNCombatLimits.EventsPerPacket + 1]; for (int i = 0; i < many.Length; i++) many[i] = first;
            Reject(() => RoundTrip(new PNCombatPacket { Ticket = ticket, Events = many }), "more events than a packet may hold are refused");
            Reject(() => RoundTrip(new PNCombatPacket { Ticket = default, Events = new[] { first } }), "a packet without a valid ticket is refused");
            var longKey = Make(new string('k', PNCombatEvent.MaxText + 1));
            Reject(() => RoundTrip(new PNCombatPacket { Ticket = ticket, Events = new[] { longKey } }), "a hit key above the bound is refused");
            var nan = Make(); nan.HealthAfter = float.PositiveInfinity;
            Reject(() => RoundTrip(new PNCombatPacket { Ticket = ticket, Events = new[] { nan } }), "a non-finite number is refused");
            using (var writer = NetworkWriterPool.Get())
            {
                writer.WriteCombatPacket(new PNCombatPacket { Ticket = ticket, Events = new[] { first } });
                var bytes = writer.ToArray();
                Reject(() => { using (var reader = NetworkReaderPool.Get(new ArraySegment<byte>(bytes, 0, bytes.Length - 3))) { try { reader.ReadCombatPacket(); } catch (EndOfStreamException) { throw new FormatException("cut"); } } }, "a cut packet is refused");
                var padded = new byte[bytes.Length + 1]; Array.Copy(bytes, padded, bytes.Length);
                Reject(() => { using (var reader = NetworkReaderPool.Get(new ArraySegment<byte>(padded))) reader.ReadCombatPacket(); }, "a padded packet is refused");
                var wrong = (byte[])bytes.Clone(); wrong[0] = 2;
                Reject(() => { using (var reader = NetworkReaderPool.Get(new ArraySegment<byte>(wrong))) reader.ReadCombatPacket(); }, "an unknown schema is refused");
                Check(bytes.Length < PNCombatLimits.PacketBytes, "(one event is small: " + bytes.Length + " bytes)");
            }
            // The size of a full packet stays inside the budget the pump reserves.
            var full = new PNCombatEvent[PNCombatLimits.EventsPerPacket]; for (int i = 0; i < full.Length; i++) full[i] = first;
            using (var writer = NetworkWriterPool.Get()) { writer.WriteCombatPacket(new PNCombatPacket { Ticket = ticket, Events = full }); Check(writer.Position <= PNCombatLimits.PacketBytes, "a full packet fits the packet bound (" + writer.Position + " bytes)"); }
            return "PASS " + count + " combat event checks (hub, early match and cancel, wire, malformed packets)";
        }
    }
}
