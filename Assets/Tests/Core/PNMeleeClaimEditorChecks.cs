#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native checks of V4b (Docs/SpawnedObjectSyncPlan.md 4.4d): the melee hitbox service leaves a door and a ticket for a player whose own machine decides his hits, the ticket takes his Taken-by-cast report once (even after
// the Action ended) and refuses the rest, and the machine being hit evaluates the door against its own body. Real colliders in a preview scene, a real Starkeeper sweep, Edit Mode.
public static class PNMeleeClaimEditorChecks
{
    private sealed class Policy : IPNHurtClaimPolicy
    {
        public readonly HashSet<long> Ids = new HashSet<long>();
        public bool ClaimsDecide(PNStat target) => target != null && Ids.Contains(target.EntityId.Value);
    }
    private sealed class Sink : IPNHitReportSink
    {
        public PNStat Who; public readonly List<PNHitReport> Sent = new List<PNHitReport>();
        public PNStat Claimant => Who;
        public bool SendHitReport(in PNHitReport report, IReadOnlyList<EveEntityId> victims) { Sent.Add(report); return true; }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        var scene = EditorSceneManager.NewPreviewScene(); bool started = false; int n = 0; GameObject go = null, a = null, b = null, c = null, host = null, floorPoint = null;
        void Check(bool ok, string what) { if (!ok) throw new Exception("Melee claim check " + n + ": " + what); n++; }
        try
        {
            // ---- the door's numbers: one fact, validated both ways ----
            var sector = EveHitboxShape.Sector(1.4f, .6f, 70f); var pose = EveHitboxPose.Planar(new Vector3(1f, .3f, 2f), new Vector3(0f, 0f, 1f));
            Check(PNMeleeDoor.TryFrom(sector, pose, 1, out var door) && door.Kind == PNMeleeDoor.Sector && door.Hit == 1, "a sector makes a door");
            var values = door.ToValues();
            Check(values.Length == 8 && PNMeleeDoor.TryRead(values, out var read) && read.Hit == 1 && read.Kind == PNMeleeDoor.Sector && Vector3.Distance(read.Origin, door.Origin) < 1e-4f && Mathf.Abs(Mathf.DeltaAngle(read.Yaw, 90f)) < .01f && read.A == 1.4f && read.B == .6f && read.C == 70f, "the numbers read back as written (yaw 90 for +Z)");
            Check(new PNActionFact(0, PNMeleeDoor.Key, values).Count == 8, "a run fact carries the door's eight numbers");
            bool nine = false; try { new PNActionFact(0, PNMeleeDoor.Key, new double[9]); } catch (ArgumentException) { nine = true; }
            Check(nine, "a ninth number is still refused");
            Check(PNMeleeDoor.TryFrom(EveHitboxShape.Cylinder(.7f, 1f), pose, 0, out var cyl) && cyl.Kind == PNMeleeDoor.Cylinder && PNMeleeDoor.TryRead(cyl.ToValues(), out var cylRead) && cylRead.Kind == PNMeleeDoor.Cylinder && cylRead.A == .7f, "a cylinder makes a door");
            Check(!PNMeleeDoor.TryFrom(EveHitboxShape.Box(Vector3.one), pose, 0, out _) && !PNMeleeDoor.Supports(EveHitboxShape.Sector(1.4f, .6f, 70f, .5f)), "a box and a ring sector carry no door (the host keeps sweeping them)");
            Check(!PNMeleeDoor.TryFrom(sector, pose, PNHurtClaimLimits.MaxHit + 1, out _), "a hit beyond the claim bound makes no door");
            var bad = (double[])values.Clone(); bad[1] = double.NaN; bool badNaN = PNMeleeDoor.TryRead(bad, out _);
            bad = (double[])values.Clone(); bad[5] = 0; bool badRadius = PNMeleeDoor.TryRead(bad, out _);
            bad = (double[])values.Clone(); bad[0] = 250; bool badKind = PNMeleeDoor.TryRead(bad, out _);
            bad = (double[])values.Clone(); bad[0] = 1.5; bool badFraction = PNMeleeDoor.TryRead(bad, out _);
            Check(!badNaN && !badRadius && !badKind && !badFraction && !PNMeleeDoor.TryRead(new double[4], out _) && !PNMeleeDoor.TryRead(null, out _), "a malformed door is dropped, never trusted");

            // ---- the report shape ----
            Check(PNHitReport.TakenByCast(9, 2, 1).IsValid(255) && PNHitReport.TakenByCast(9, 2, 1).ByCast && PNHitReport.TakenByCast(9, 2, 1).Role == PNHitReportRole.Taken, "a Taken-by-cast report is valid");
            Check(!PNHitReport.TakenByCast(9, 256, 0).IsValid(255) && !PNHitReport.TakenByCast(9, -1, 0).IsValid(255) && !PNHitReport.TakenByCast(9, 1, PNHurtClaimLimits.MaxHit + 1).IsValid(255) && !PNHitReport.TakenByCast(9, 1, -1).IsValid(255)
                && !PNHitReport.FromWire(PNHitReportRole.Taken, 9, 1, (int)PNObjectKind.Area, 5, 0).IsValid(255) && !PNHitReport.FromWire(PNHitReportRole.Taken, 0, 1, 0, 0, 0).IsValid(255), "a step or hit out of range, and a report naming a cast and an object at once, are not reports");

            // ---- the Starkeeper in a preview scene, activated so its casts have keys ----
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); started = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:melee-claim"));
            PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
            go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starkeeper/Prefab/Starkeeper.prefab")); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
            go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(978410), EveId.Parse("project_n:starkeeper"), 0); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true);
            go.GetComponent<PNVisualBody>().Begin(hub.Characters.Get(EveId.Parse("project_n:starkeeper")), hub);
            PNStat Dummy(string name, int id, Vector3 where)
            {
                var d = new GameObject(name); d.SetActive(false); SceneManager.MoveGameObjectToScene(d, scene); d.transform.position = where;
                d.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), 0);
                var s = d.AddComponent<PNPlayerStat>().AsPlayer(); d.AddComponent<Rigidbody>().isKinematic = true;
                var col = d.AddComponent<CapsuleCollider>(); col.radius = .2f; col.height = 1.4f; col.center = Vector3.up * .7f; d.layer = PNLayers.Player;
                d.AddComponent<EveEntityLoader>().Load(hub); d.SetActive(true); return s;
            }
            var near = Dummy("Melee claim near", 978411, new Vector3(1f, 0, 0)); a = near.gameObject;
            var wide = Dummy("Melee claim wide", 978412, new Vector3(2.6f, 0, 0)); b = wide.gameObject;          // outside the 1.4 m sector, inside the reach (sector + 1.5 m)
            var away = Dummy("Melee claim away", 978413, new Vector3(12f, 0, 0)); c = away.gameObject;           // far outside the reach
            var skill = go.GetComponent<PNStarkeeperSkill>(); var caster = go.GetComponent<PNStat>();
            host = new GameObject("Melee claim manager"); SceneManager.MoveGameObjectToScene(host, scene);
            var manager = host.AddComponent<PNProjectileManager>(); manager.Initialize(EveRegistrySession.Context, new PNProjectileProfile()); manager.ManualAdvance = true;
            double clock = 50d; manager.PredictionClock = () => clock;
            var book = new PNHurtClaimBook { Clock = () => clock }; manager.ClaimBook = book; PNHurtClaimBooks.Set(EveRegistrySession.Context, book);
            var policy = new Policy(); PNMeleeDoorClaims.Reset();
            Check(near.IsReady && wide.IsReady && away.IsReady && skill.IsReady, "the dummies and the Starkeeper are ready");
            float Health(PNStat s) => (float)s.GetCurrent(PNContentIds.Health);
            void Adv(PNSkillAction act, PNSkillActionContext ctx, float d) { using (var frame = skill.BeginActionExecutionFrame(d)) { if (d <= 0f) frame.Hold(ctx); else frame.Advance(ctx, d, 1, step => act.AdvanceAuxiliary(step)); } }
            void Activate() { if (!skill.CanActivateSkill(PNStarkeeperCombatContent.StarSweep)) throw new Exception("the Starkeeper cannot activate the sweep"); typeof(PNSkill).GetMethod("BeginActivation", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(skill, new object[] { PNStarkeeperCombatContent.StarSweep }); }
            void Deactivate() => typeof(PNSkill).GetMethod("EndActivation", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(skill, new object[] { true, "test" });
            void Reset() { go.transform.position = Vector3.zero; go.GetComponent<Rigidbody>().position = Vector3.zero; near.transform.position = new Vector3(1f, 0, 0); wide.transform.position = new Vector3(2.6f, 0, 0); Physics.SyncTransforms(); }

            // ---- 1. no policy: the host sweeps the player itself, no door, no ticket (the old behavior) ----
            float h0 = Health(near);
            Activate();
            var plain = skill.CreateAction(PNStarkeeperCombatContent.StarSweep, out var pc, new[] { near }, Vector3.right, null);
            try
            {
                if (plain is IPNSkillMotionAction m0) m0.BindMovementOwner(() => { });
                plain.Start(pc); Adv(plain, pc, 0f); Adv(plain, pc, .39f); Adv(plain, pc, 0f); Adv(plain, pc, .02f);
                Check(Health(near) < h0 && book.TicketCount == 0 && pc.PresentedFacts == 0, "with no policy the host hurts the player itself and opens no door");
                plain.Cancel(pc);
            }
            finally { plain.Dispose(); Deactivate(); }

            // ---- 2. the policy hands `near` and `wide` to their machines: the sweep leaves them, a door and a ticket stand in ----
            Reset(); policy.Ids.Add(near.EntityId.Value); policy.Ids.Add(wide.EntityId.Value); book.Policy = policy;
            h0 = Health(near); float w0 = Health(wide), a0 = Health(away);
            Activate(); long castKey = skill.CurrentInvocation.CastKey;
            var sweep = skill.CreateAction(PNStarkeeperCombatContent.StarSweep, out var sc, new[] { near }, Vector3.right, null);
            long ordinal = 0; bool exited = false;
            try
            {
                if (sweep is IPNSkillMotionAction motion) motion.BindMovementOwner(() => { });
                sweep.Start(sc); Adv(sweep, sc, 0f); ordinal = sc.ActionOrdinal;
                Check(castKey > 0 && ordinal >= 1 && book.TicketCount == 0, "the cast has a key and the Action an ordinal; no ticket before the sweep");
                Adv(sweep, sc, .39f); Adv(sweep, sc, 0f); Adv(sweep, sc, .02f);
                Check(Health(near) == h0 && Health(wide) == w0, "the first sweep leaves the players whose machine decides (health " + h0 + " -> " + Health(near) + ")");
                Check(book.TicketCount == 1 && sc.PresentedFacts == 1, "one ticket and one door fact stand in for the first sweep (tickets " + book.TicketCount + ", facts " + sc.PresentedFacts + ")");

                // the ticket takes the claim of the player's machine once
                int accepted = book.Accepted;
                Check(!book.AcceptTicket(PNClaimTicketKey.Melee(castKey, ordinal, 1), near, 1.5f) && book.Refusals.ContainsKey("gone"), "a claim for a hit with no ticket (the second sweep has not swung) is refused as gone");
                Check(!book.AcceptTicket(PNClaimTicketKey.Melee(castKey + 1, ordinal, 0), near, 1.5f), "a claim naming another cast is refused");
                Check(book.AcceptTicket(PNClaimTicketKey.Melee(castKey, ordinal, 0), near, 1.5f) && book.Accepted == accepted + 1 && Health(near) < h0, "the claim of the first sweep applies the prepared damage (health " + h0 + " -> " + Health(near) + ")");
                float afterClaim = Health(near);
                Check(!book.AcceptTicket(PNClaimTicketKey.Melee(castKey, ordinal, 0), near, 1.5f) && book.Refusals.ContainsKey("again") && Health(near) == afterClaim, "the same player cannot claim the same hit twice");
                Check(!book.AcceptTicket(PNClaimTicketKey.Melee(castKey, ordinal, 0), away, 1.5f) && book.Refusals.ContainsKey("policy") && Health(away) == a0, "a player the policy does not hand over is refused");
                policy.Ids.Add(away.EntityId.Value);
                Check(!book.AcceptTicket(PNClaimTicketKey.Melee(castKey, ordinal, 0), away, 1.5f) && book.Refusals.ContainsKey("rule") && Health(away) == a0, "a claim from 11 m beyond the sweep's reach is refused by the reach rule");
                policy.Ids.Remove(away.EntityId.Value);

                // the second sweep re-aims and leaves a second door and ticket
                Adv(sweep, sc, .2f); Adv(sweep, sc, .14f);
                Check(book.TicketCount == 2 && sc.PresentedFacts == 3 && Health(wide) == w0, "the second sweep leaves its own ticket (facts: aim + two doors; tickets " + book.TicketCount + ")");

                // the Action ends; the ticket outlives it and still applies the prepared damage, once, without calling back into the Action
                Adv(sweep, sc, .7f); sweep.Exit(sc); exited = true; sweep.Dispose(); Deactivate();
                float beforeLate = Health(wide);
                Check(book.AcceptTicket(PNClaimTicketKey.Melee(castKey, ordinal, 1), wide, 1.5f) && Health(wide) < beforeLate, "after the Action ended the ticket still applies the second sweep's damage (health " + beforeLate + " -> " + Health(wide) + ")");
                clock += PNHurtClaimLimits.ClaimGraceSeconds + .1;
                float beforeExpired = Health(near);
                Check(!book.AcceptTicket(PNClaimTicketKey.Melee(castKey, ordinal, 1), near, 1.5f) && Health(near) == beforeExpired && book.TicketCount == 0, "past the grace the ticket is gone and a claim hurts nobody");
            }
            finally { if (!exited) { try { sweep.Dispose(); } catch (Exception) { } } }

            // ---- 3. the machine being hit: its copy of the Starkeeper takes the door and answers once ----
            PNMeleeDoorClaims.Reset();
            var sink = new Sink { Who = near }; manager.RegisterClaimSink(sink);
            double[] Values(Vector3 direction, int hit) { PNMeleeDoor.TryFrom(EveHitboxShape.Sector(1.4f, .6f, 70f), EveHitboxPose.Planar(new Vector3(0, .3f, 0), direction), hit, out var made); return made.ToValues(); }
            var key = new PNActionRunKey(castKey, ordinal); var inside = Values(Vector3.right, 0);
            near.transform.position = new Vector3(1f, 0, 0); Physics.SyncTransforms();
            Check(PNMeleeDoorClaims.Offer(skill, key, inside) && sink.Sent.Count == 1 && sink.Sent[0].ByCast && sink.Sent[0].CastKey == castKey && sink.Sent[0].Step == (int)ordinal && sink.Sent[0].Detail == 0 && sink.Sent[0].Role == PNHitReportRole.Taken, "a body inside the door sends one Taken-by-cast report naming the cast, the Action and the hit");
            Check(!PNMeleeDoorClaims.Offer(skill, key, inside) && sink.Sent.Count == 1, "the same door answers once");
            var other = Values(Vector3.right, 1);
            Check(PNMeleeDoorClaims.Offer(skill, key, other) && sink.Sent.Count == 2 && sink.Sent[1].Detail == 1, "the second hit of the same run is its own door");
            var outside = Values(Vector3.left, 2);
            Check(!PNMeleeDoorClaims.Offer(skill, key, outside) && sink.Sent.Count == 2, "a body outside the shape (the swing went the other way) sends nothing");
            sink.Who = null;
            Check(!PNMeleeDoorClaims.Offer(skill, new PNActionRunKey(castKey, ordinal + 1), inside) && sink.Sent.Count == 2, "a machine that does not own the hit player (no claimant) sends nothing");
            sink.Who = near; var tooBig = (double[])inside.Clone(); tooBig[5] = 9999;
            Check(!PNMeleeDoorClaims.Offer(skill, new PNActionRunKey(castKey, ordinal + 2), tooBig) && sink.Sent.Count == 2, "a malformed door sends nothing");
            manager.UnregisterClaimSink(sink);

            // ---- 3b. the contact door (V4c): a sphere that follows the body, hit again every interval ----
            Check(PNMeleeDoor.TryContact(Vector3.zero, .2f, .5f, 0f, 1f, out var contactDoor) && contactDoor.IsContact && contactDoor.Hits == 6, "a contact door makes one hit per interval over its window (6 for 1 s every 0.2 s)");
            Check(PNMeleeDoor.TryRead(contactDoor.ToValues(), out var contactRead) && contactRead.IsContact && contactRead.Radius == .5f && contactRead.Interval == .2f && contactRead.WindowStart == 0f && contactRead.WindowEnd == 1f && contactRead.Hits == 6 && contactDoor.ToValues().Length == 8, "a contact door reads back as written in eight numbers");
            Check(PNMeleeDoor.TryContact(Vector3.zero, 0f, .5f, 0f, 1f, out var onceDoor) && onceDoor.Hits == 1, "a contact door with no repeat is one hit per player");
            Check(!PNMeleeDoor.TryContact(Vector3.zero, .01f, .5f, 0f, 1f, out _) && !PNMeleeDoor.TryContact(Vector3.zero, .2f, 0f, 0f, 1f, out _) && !PNMeleeDoor.TryContact(Vector3.zero, .2f, .5f, 1f, .5f, out _)
                && !PNMeleeDoor.TryContact(Vector3.zero, .2f, .5f, 0f, 61f, out _) && !PNMeleeDoor.TryContact(Vector3.zero, -1f, .5f, 0f, 1f, out _) && !PNMeleeDoor.TryContact(Vector3.zero, float.NaN, .5f, 0f, 1f, out _), "a contact door over the hit bound, with no radius, a reversed or too long window, or a bad number is not a door");
            var badContact = contactDoor.ToValues(); badContact[0] = 201; bool badHit = PNMeleeDoor.TryRead(badContact, out _);
            badContact = contactDoor.ToValues(); badContact[7] = double.PositiveInfinity; bool badInf = PNMeleeDoor.TryRead(badContact, out _);
            Check(!badHit && !badInf && PNMeleeDoorClaims.Watch(inside, 0) == null && PNMeleeDoorClaims.Watch(new double[3], 0) == null && PNMeleeDoorClaims.Watch(contactDoor.ToValues(), 0) != null, "a contact door names no hit; a swing door and a malformed one start no watch");

            // the machine being hit watches the window: a touch sends hit n, the next only after the interval; outside the window or the body nothing is sent
            sink.Sent.Clear(); sink.Who = near; manager.RegisterClaimSink(sink); PNMeleeDoorClaims.Reset();
            var watchKey = new PNActionRunKey(castKey, ordinal + 5); var watch = PNMeleeDoorClaims.Watch(contactDoor.ToValues(), 2.0);
            go.transform.position = Vector3.zero; near.transform.position = new Vector3(.3f, 0, 0); Physics.SyncTransforms();
            Check(!watch.Tick(skill, watchKey, 1.0) && sink.Sent.Count == 0, "before its window the watch sends nothing");
            Check(watch.Tick(skill, watchKey, 2.05) && sink.Sent.Count == 1 && sink.Sent[0].Detail == 0 && sink.Sent[0].CastKey == castKey && sink.Sent[0].Step == (int)(ordinal + 5), "a body touching the sphere inside the window sends hit 0");
            Check(!watch.Tick(skill, watchKey, 2.1) && sink.Sent.Count == 1, "the next hit waits for the interval");
            near.transform.position = new Vector3(10f, 0, 0); Physics.SyncTransforms();
            Check(!watch.Tick(skill, watchKey, 2.3) && sink.Sent.Count == 1, "a body away from the sphere sends nothing");
            near.transform.position = new Vector3(.3f, 0, 0); Physics.SyncTransforms();
            Check(watch.Tick(skill, watchKey, 2.35) && sink.Sent.Count == 2 && sink.Sent[1].Detail == 1, "touching again after the interval is hit 1");
            Check(!watch.Tick(skill, watchKey, 3.5) && watch.Done && sink.Sent.Count == 2, "past the window the watch ends");
            var onceWatch = PNMeleeDoorClaims.Watch(onceDoor.ToValues(), 0.0); sink.Sent.Clear();
            Check(onceWatch.Tick(skill, new PNActionRunKey(castKey, ordinal + 6), .1) && onceWatch.Done && !onceWatch.Tick(skill, new PNActionRunKey(castKey, ordinal + 6), .2) && sink.Sent.Count == 1, "a door with no repeat sends one hit and is done");
            manager.UnregisterClaimSink(sink);

            // the host: a contact hitbox with the door open leaves a ticket per hit (their grace runs to the window's end) and sweeps only the players it keeps
            Reset(); policy.Ids.Clear(); policy.Ids.Add(near.EntityId.Value); book.Policy = policy; book.ClearTickets();
            away.transform.position = new Vector3(0f, 0, .4f); Physics.SyncTransforms();
            Activate(); long contactCast = skill.CurrentInvocation.CastKey;
            var holder = skill.CreateAction(PNStarkeeperCombatContent.StarSweep, out var hc, new[] { near }, Vector3.right, null);
            try
            {
                if (holder is IPNSkillMotionAction hm) hm.BindMovementOwner(() => { });
                holder.Start(hc); Adv(holder, hc, 0f); long hordinal = hc.ActionOrdinal;
                using (var box = new PNSkillMeleeHitbox(hc, caster, PNSkillValues.Parse(JObject.Parse("{'amount':5}")), null, false))
                {
                    box.Open(EveHitboxShape.Sphere(.6f), Vector3.zero, .6f, 0, null, t => caster.Targets(t), .3);
                    int facts = hc.PresentedFacts; float n0 = Health(near), g0 = Health(away);
                    Check(hc.ShownToOthers && box.OpenContactDoor(Vector3.zero, new Vector3(1f, .7f, 0f), 2f, .3f, 0f, 1f) && hc.PresentedFacts == facts + 1 && book.TicketCount == 4, "a contact door opens with one fact and one ticket per hit (4 for 1 s every 0.3 s; tickets " + book.TicketCount + ")");
                    Check(!box.OpenContactDoor(Vector3.zero, new Vector3(1f, .7f, 0f), 2f, .3f, 0f, 1f), "a hitbox opens its door once");
                    var at = EveHitboxPose.Planar(new Vector3(0f, .7f, 0f), Vector3.right);
                    box.Sample(at, 0d); box.Sample(at, .1d); box.Sample(at, .35d);
                    float hostDrop = g0 - Health(away); Check(Health(near) == n0 && hostDrop > 0f, "the host sweeps the player it keeps (hit again after the interval: 2 hits) and leaves the one whose machine decides (health " + g0 + " -> " + Health(away) + ")");
                    clock += PNHurtClaimLimits.ClaimGraceSeconds + .6;
                    Check(book.TicketCount == 4, "the tickets outlive the grace of a hit already made: the window is still open");
                    float n1 = Health(near);
                    Check(book.AcceptTicket(PNClaimTicketKey.Melee(contactCast, hordinal, 0), near, 1.5f) && book.AcceptTicket(PNClaimTicketKey.Melee(contactCast, hordinal, 2), near, 1.5f) && Health(near) < n1 && Mathf.Abs(hostDrop - (n1 - Health(near))) < .01f, "claims of hits 0 and 2 each apply the prepared damage (health " + n1 + " -> " + Health(near) + ")");
                    Check(!book.AcceptTicket(PNClaimTicketKey.Melee(contactCast, hordinal, 2), near, 1.5f) && book.Refusals.ContainsKey("again") && !book.AcceptTicket(PNClaimTicketKey.Melee(contactCast, hordinal, 4), near, 1.5f), "a hit is claimed once, and there is no ticket beyond the window's hits");
                    box.Dispose();
                    float n2 = Health(near);
                    Check(book.AcceptTicket(PNClaimTicketKey.Melee(contactCast, hordinal, 1), near, 1.5f) && Health(near) < n2, "after the hitbox went the ticket still applies the prepared damage");
                    clock += 1.0;
                    Check(!book.AcceptTicket(PNClaimTicketKey.Melee(contactCast, hordinal, 3), near, 1.5f) && book.TicketCount == 0, "past the window and the grace the tickets are gone");
                }
                holder.Cancel(hc);
            }
            finally { holder.Dispose(); Deactivate(); book.ClearTickets(); away.transform.position = new Vector3(12f, 0, 0); policy.Ids.Clear(); policy.Ids.Add(near.EntityId.Value); policy.Ids.Add(wide.EntityId.Value); Physics.SyncTransforms(); }

            // ---- 3c. the Starkeeper's real star point (a contact lunge after a 2 s windup): the door opens when the lunge starts, not at Perform ----
            floorPoint = new GameObject("Melee claim floor"); SceneManager.MoveGameObjectToScene(floorPoint, scene); floorPoint.layer = 13;
            floorPoint.transform.position = new Vector3(1f, -.075f, 0f); floorPoint.AddComponent<BoxCollider>().size = new Vector3(12f, .15f, 12f);
            Reset(); policy.Ids.Clear(); policy.Ids.Add(near.EntityId.Value); book.Policy = policy; book.ClearTickets();
            if (!skill.CanActivateSkill(PNStarkeeperCombatContent.StarPoint)) throw new Exception("the Starkeeper cannot activate the star point");
            typeof(PNSkill).GetMethod("BeginActivation", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(skill, new object[] { PNStarkeeperCombatContent.StarPoint });
            long pointCast = skill.CurrentInvocation.CastKey;
            var point = skill.CreateAction(PNStarkeeperCombatContent.StarPoint, out var pcx, new[] { near }, Vector3.right, new Vector3(2f, 0f, 0f));
            try
            {
                ((PNAgentTrajectoryAction)point).BindMovementOwner(() => { }); point.Start(pcx); Adv(point, pcx, 0f); point.Perform(pcx);
                long pordinal = pcx.ActionOrdinal; int pfacts = pcx.PresentedFacts; float pn0 = Health(near);
                Adv(point, pcx, 1.5f);
                Check(book.TicketCount == 0 && pcx.PresentedFacts == pfacts, "no door while the Starkeeper winds up: a contact door that opened at Perform would watch a body that is not moving yet");
                Adv(point, pcx, .6f); Adv(point, pcx, .01f);
                Check(book.TicketCount == 1 && pcx.PresentedFacts > pfacts, "the door opens with the lunge: a ticket and new facts (tickets " + book.TicketCount + ", facts " + (pcx.PresentedFacts - pfacts) + " new)");
                for (int i = 0; i < 28; i++) Adv(point, pcx, .01f);
                Check(Health(near) == pn0, "the host leaves the player whose machine decides: its own sweep does not hurt him");
                Check(book.AcceptTicket(PNClaimTicketKey.Melee(pointCast, pordinal, 0), near, 1.5f) && Health(near) < pn0, "his machine's claim applies the lunge's damage (health " + pn0 + " -> " + Health(near) + ")");
            }
            finally { point.Dispose(); Deactivate(); book.ClearTickets(); policy.Ids.Clear(); policy.Ids.Add(near.EntityId.Value); policy.Ids.Add(wide.EntityId.Value); }

            // ---- 4.the ticket does not hold on: dropped every frame once its grace ends, and a gone caster drops the claim (no exception) ----
            (long Cast, long Ordinal) SweepOnce()
            {
                Reset(); Activate(); long k = skill.CurrentInvocation.CastKey;
                var act = skill.CreateAction(PNStarkeeperCombatContent.StarSweep, out var ctx, new[] { near }, Vector3.right, null); long o;
                try
                {
                    if (act is IPNSkillMotionAction m) m.BindMovementOwner(() => { });
                    act.Start(ctx); Adv(act, ctx, 0f); o = ctx.ActionOrdinal; Adv(act, ctx, .39f); Adv(act, ctx, 0f); Adv(act, ctx, .02f); act.Cancel(ctx);
                }
                finally { act.Dispose(); Deactivate(); }
                return (k, o);
            }
            var first = SweepOnce();
            Check(book.TicketCount == 1, "a new sweep leaves a ticket (" + book.TicketCount + ")");
            clock += PNHurtClaimLimits.ClaimGraceSeconds + .1; manager.Advance(.02f);
            Check(book.TicketCount == 0, "the manager's frame drops the ticket once its grace ended, with no claim coming (it held the hit's damage and the hitbox)");
            var second = SweepOnce(); float beforeGone = Health(near);
            Check(book.TicketCount == 1, "another sweep, another ticket");
            go.GetComponent<EveEntityLoader>().Unload();     // the despawn path: the caster is unloaded (a destroyed one is caught by the same null/readiness check)
            bool threw = false, took = true;
            try { took = book.AcceptTicket(PNClaimTicketKey.Melee(second.Cast, second.Ordinal, 0), near, 1.5f); } catch (Exception) { threw = true; }
            Check(!threw && !took && Health(near) == beforeGone, "a claim arriving after the Starkeeper was unloaded (despawned) is refused without an exception and hurts nobody");
            Check(first.Cast != second.Cast, "each sweep is its own cast");
            return "PASS " + n + " melee claim checks (door codec incl. the contact door and its watch and tickets (V4c), report shape, ticket accept/refuse/expire after the Action ended, frame pruning, an unloaded caster, the machine being hit answers once; real Starkeeper sweep and colliders in a preview scene)";
        }
        finally
        {
            try { if (started) { PNHurtClaimBooks.Remove(EveRegistrySession.Context); EveRegistrySession.End(); } }
            finally { foreach (var o in new[] { go, a, b, c, host, floorPoint }) if (o != null) UnityEngine.Object.DestroyImmediate(o); EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); }
        }
    }
}
#endif
