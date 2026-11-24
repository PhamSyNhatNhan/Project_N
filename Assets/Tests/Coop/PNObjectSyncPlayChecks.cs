using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop.Tests
{
    // SpawnedObjectSyncPlan O3 over the real wire (flag --pn-object-sync, character Eve 2): client1 summons its pet, the host puts enemies near it, the pet shoots.
    // Client2 (and client1) must see the host's projectiles as copies through the object channel, and see the pet at the same size and near its owner (PNMove + summon_scale).
    public static class PNObjectSyncPlayChecks
    {
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("--pn-object-sync") || O4 || Refuse || NoGuess || O6 || Enemy || Melee || Quiet || O5 || O5b || Load || Hits || Dodge || DodgeBlast || DodgeMelee || DodgeSlam || DodgePoint || MeleeMany || Strike || StrikeAll || Slow;
        private static bool Dodge => Environment.GetCommandLineArgs().Contains("--pn-object-sync-dodge");
        // V2 variant of the dodge fixture: the host drops a cylinder blast where it sees the player (plan 4.4c); the machine of the player decides whether it hurt him.
        // O7 (--pn-object-sync-strike): the host spawns a Starkeeper next to the players and its AI sweeps them; every other machine must replay the sweep from the enemy's `skill_cast` block.
        // G8c (--pn-object-sync-strikeall): one of every enemy next to the players; each Action the host publishes must be replayed on the others, none dropped, whatever context it needs.
        // Time check (--pn-object-sync-slow): the host slows a Starkeeper's own clock (as a slow area would) 15 s in; every machine records how long its copy of each sweep ran against how long the stream listed the run.
        private static bool Slow => Environment.GetCommandLineArgs().Contains("--pn-object-sync-slow");
        private static bool StrikeAll => Environment.GetCommandLineArgs().Contains("--pn-object-sync-strikeall");
        private static bool Strike => Environment.GetCommandLineArgs().Contains("--pn-object-sync-strike");
        private static bool DodgeBlast => Environment.GetCommandLineArgs().Contains("--pn-object-sync-dodgeblast");
        private static bool DodgeMelee => Environment.GetCommandLineArgs().Contains("--pn-object-sync-dodgemelee");
        private static bool DodgeSlam => Environment.GetCommandLineArgs().Contains("--pn-object-sync-dodgeslam");
        private static bool DodgePoint => Environment.GetCommandLineArgs().Contains("--pn-object-sync-dodgepoint");
        // --pn-object-sync-late: the melee fixture with 30 slashes; the host tallies the Dealt reports no receiver took because the hitbox had already ended (late) against those a live hitbox refused (plan 4.4d, "late Dealt").
        private static bool MeleeMany => Environment.GetCommandLineArgs().Contains("--pn-object-sync-late");
        private static bool Load => Environment.GetCommandLineArgs().Contains("--pn-object-sync-load");
        private static bool Hits => Environment.GetCommandLineArgs().Contains("--pn-object-sync-hits");
        private static bool O5 => Environment.GetCommandLineArgs().Contains("--pn-object-sync-o5");
        private static bool O5b => Environment.GetCommandLineArgs().Contains("--pn-object-sync-o5b");
        // O4 (character Eve): client1 casts Skill (three homing projectiles) near enemies; the shooter must see a guess before any answer and no second copy, the host must take the shooter's reports, client2 sees copies.
        private static bool O4 => Environment.GetCommandLineArgs().Contains("--pn-object-sync-o4");
        // --pn-object-sync-o4r: the host refuses the shooter's cast (a Control lock the client did not know yet): the guess must go and nothing must show or hurt anywhere.
        private static bool Refuse => Environment.GetCommandLineArgs().Contains("--pn-object-sync-o4r");
        // --pn-object-sync-o4n: the shooter's machine does not guess (hook): the host's real projectiles hit the enemies themselves and the shooter sees ordinary copies.
        private static bool NoGuess => Environment.GetCommandLineArgs().Contains("--pn-object-sync-o4n");
        // --pn-object-sync-o6: late observers and the queue: a fresh baseline of living objects, a flood that overflows the host's queue, and the bytes the channel cost.
        // --pn-object-sync-enemy: the host fires the enemy and summon projectile definitions at client1's entity; every observer must show each one as a copy, tell OnImpact once per projectile, and draw the Starforger contact burst.
        private static bool Enemy => Environment.GetCommandLineArgs().Contains("--pn-object-sync-enemy");
        private static bool Capture => Environment.GetCommandLineArgs().Contains("--pn-capture-burst");
        // --pn-object-sync-melee: client1's Eve slashes enemies the host put next to her; the host tells each hit as Real, client1 as Guess, client2 (not the shooter) as Copy through the combat channel (plan 4.4b item 1).
        private static bool Melee => Environment.GetCommandLineArgs().Contains("--pn-object-sync-melee");
        // --pn-object-sync-quiet: nobody fights for 15 s; every machine tallies the combat events it is told (kind / target layer / source) to find what the background traffic is. Measurement only, it never fails on counts.
        private static bool Quiet => Environment.GetCommandLineArgs().Contains("--pn-object-sync-quiet");
        private static bool O6 => Environment.GetCommandLineArgs().Contains("--pn-object-sync-o6");
        private static IEnumerator Wait(Func<bool> ready, string label, double seconds = 60, Func<string> detail = null)
        { double until = Time.realtimeSinceStartupAsDouble + seconds; while (!ready()) { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException(label + (detail != null ? " [" + detail() + "]" : "")); yield return null; } }

        public static IEnumerator Run(string folder, string name, PNPlayerMove e1)
        {
            Exception failure = null; var log = new System.Text.StringBuilder();
            var stack = new System.Collections.Generic.Stack<IEnumerator>(); stack.Push(Body(folder, name, e1, log));
            while (stack.Count > 0)
            {
                var e = stack.Peek(); bool next = false;
                try { next = e.MoveNext(); } catch (Exception x) { failure = x; }
                if (failure != null) break;
                if (!next) { stack.Pop(); continue; }
                if (e.Current is IEnumerator child) { stack.Push(child); continue; }
                yield return e.Current;
            }
            File.WriteAllText(Path.Combine(folder, name + ".txt"), (failure == null ? "PASS " : "FAIL ") + "O3 object sync fixture " + log + (failure == null ? "" : " " + failure));
            if (failure != null) Debug.LogException(failure);
            Application.Quit(failure == null ? 0 : 1);
        }

        private static void Mark(string folder, string value) { var path = Path.Combine(folder, value); File.WriteAllText(path + ".pending", "ok"); File.Move(path + ".pending", path); }

        private static IEnumerator Body(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            var scene = e1.gameObject.scene;
            var projectiles = PNProjectileManager.Resolve(EveRegistrySession.Context, scene);
            if (projectiles.Session == null) throw new Exception("no projectile manager");
            if (O4 || Refuse || NoGuess) { yield return O4Body(folder, name, e1, log); yield break; }
            if (Enemy) { yield return EnemyBody(folder, name, e1, log); yield break; }
            if (Melee || MeleeMany) { yield return MeleeBody(folder, name, e1, log); yield break; }
            if (O5) { yield return O5Body(folder, name, e1, log); yield break; }
            if (O5b) { yield return O5bBody(folder, name, e1, log); yield break; }
            if (Load) { yield return LoadBody(folder, name, e1, log); yield break; }
            if (Hits) { yield return HitsBody(folder, name, e1, log); yield break; }
            if (Dodge || DodgeBlast) { yield return DodgeBody(folder, name, e1, log); yield break; }
            if (DodgeMelee) { yield return DodgeMeleeBody(folder, name, e1, log, "project_n:starkeeper", "tarkeeper"); yield break; }
            if (DodgeSlam) { yield return DodgeMeleeBody(folder, name, e1, log, "project_n:starforger", "tarforger"); yield break; }
            if (DodgePoint) { yield return DodgeMeleeBody(folder, name, e1, log, "project_n:starkeeper", "tarkeeper", true); yield break; }
            if (Strike) { yield return StrikeBody(folder, name, e1, log); yield break; }
            if (Slow) { yield return SlowBody(folder, name, e1, log); yield break; }
            if (StrikeAll) { yield return StrikeAllBody(folder, name, e1, log); yield break; }
            if (Quiet) { yield return QuietBody(folder, name, e1, log); yield break; }
            if (O6) { yield return O6Body(folder, name, e1, log); yield break; }
            var petSkill = e1.GetComponent<PNEve2Skill>();
            if (petSkill == null) throw new Exception("this fixture needs Eve 2 (--pn-skill-character=project_n:eve_2)");
            var summoner = e1.GetComponent<EveEntityIdentity>().Id;
            if (host)
            {
                // summon_scale x2 on the owner (host side stats), so the pet is made at double size and the clients must give their copy the same size.
                e1.GetComponent<PNStat>().SetSource(EveId.Parse("test:o3_summon_scale"), new[] { new EveStatModifier(PNContentIds.SummonScale, 1d, 1, PNContentIds.Scale) });
                Mark(folder, "host-scaled");
            }
            if (name == "client1")
            {
                yield return Wait(() => Has("host-scaled"), "host scaled");
                var abilities = e1.GetComponent<PNAbilityManager>().Abilities;
                int Charges() { foreach (var item in abilities.CaptureReplica()) if (item.Id == PNSummonGameplayContent.Eve2PetReady) return item.Amount; return -1; }
                var remote = e1.GetComponent<PNSkillRemoteInput>();
                yield return Wait(() => Charges() >= 1 && remote != null && remote.IsBound && petSkill.CanInput, "client1 pet ready");
                yield return new WaitForSecondsRealtime(2f);
                petSkill.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap));
                petSkill.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                Mark(folder, "client1-petcast");
            }
            if (host)
            {
                yield return Wait(() => Has("client1-petcast"), "client1 pet cast");
                yield return Wait(() => PNSummons.Count(summoner, "eve2_pet") >= 1, "the host summoned the pet");
                var world = PNLocalWorldContext.Current.World;
                for (int i = 0; i < 2; i++)
                {
                    var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:enemy" + i, EntityKey = "", Position = e1.transform.position + new Vector3(i == 0 ? 1.2f : -1.2f, 0, -0.6f) };
                    if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused an enemy");
                }
                Mark(folder, "host-enemies");
            }
            yield return Wait(() => Has("host-enemies"), "enemies");

            // Observe for a while: peak copies in the projectile manager, the pet's size and distance to its owner.
            int copies = 0, lastReplicas = 0, peakReplicas = 0, peakActive = 0; float peakDistance = 0, minDistance = float.MaxValue; Vector3 petScale = Vector3.zero; bool petSeen = false;
            double until = Time.realtimeSinceStartupAsDouble + 12;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                { int now = projectiles.ReplicaCount; if (now > lastReplicas) copies += now - lastReplicas; lastReplicas = now; } peakReplicas = Math.Max(peakReplicas, projectiles.ReplicaCount); peakActive = Math.Max(peakActive, projectiles.ActiveCount);
                var pet = UnityEngine.Object.FindObjectsByType<PNPetShooterBehavior>().FirstOrDefault(x => x.isActiveAndEnabled);
                if (pet != null) { petSeen = true; petScale = pet.transform.lossyScale; float d = Vector3.Distance(pet.transform.position, e1.transform.position); peakDistance = Math.Max(peakDistance, d); minDistance = Math.Min(minDistance, d); }
                yield return null;
            }
            log.Append(name + ": peakReplicas=" + peakReplicas + " copies=" + copies + " peakActive=" + peakActive + " petSeen=" + petSeen + " petScale=" + petScale + " dist=" + minDistance + ".." + peakDistance + "; ");
            if (host) File.WriteAllText(Path.Combine(folder, "host.petscale"), petScale.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (host) { if (Mathf.Abs(petScale.x - 2f) > .05f) throw new Exception("the host pet is not doubled " + log); if (peakActive < 1) throw new Exception("the pet shot nothing on the host (peakActive 0) " + log); Mark(folder, "host-observed"); }
            yield return Wait(() => Has("host-observed"), "host observed");
            if (!host)
            {
                if (peakReplicas < 1) throw new Exception("this client never saw a projectile copy " + log);
                if (!petSeen) throw new Exception("this client never saw the pet " + log);
                float hostScale = float.Parse(File.ReadAllText(Path.Combine(folder, "host.petscale")), System.Globalization.CultureInfo.InvariantCulture);
                if (Mathf.Abs(hostScale - petScale.x) > 1e-3f * Mathf.Max(1f, hostScale)) throw new Exception("the pet size differs: host " + hostScale + " here " + petScale.x + " " + log);
                if (peakDistance > 30f) throw new Exception("the pet strayed far from its owner (" + peakDistance + ") " + log);
            }
        }

        private static IEnumerator O4Body(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            var projectiles = PNProjectileManager.Resolve(EveRegistrySession.Context, e1.gameObject.scene);
            var eve = e1.GetComponent<EveCharacterSkill>(); var remote = e1.GetComponent<PNSkillRemoteInput>();
            if (eve == null) throw new Exception("O4 fixture needs Eve (--pn-skill-character=project_n:eve)");
            double Health() { double sum = 0; foreach (var stat in UnityEngine.Object.FindObjectsByType<PNStat>()) if (stat.gameObject.layer == PNLayers.Enemy && stat.IsReady) sum += stat.GetCurrent(PNContentIds.Health); return sum; }
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                foreach (var offset in new[] { new Vector3(3f, 0, 0), new Vector3(-3f, 0, 2f) })
                {
                    var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:o4enemy" + offset.x, EntityKey = "", Position = e1.transform.position + offset };
                    if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused an enemy");
                }
                yield return Wait(() => Health() > 0, "enemies exist");
                File.WriteAllText(Path.Combine(folder, "host.health0"), Health().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                Mark(folder, "host-enemies");
            }
            yield return Wait(() => Has("host-enemies"), "enemies");
            yield return new WaitForSecondsRealtime(1.5f); // enemies replicate to the clients
            // Every machine reacts to hits on the enemies through the hurt signal (the engine adds no reaction of its own): the phases it is told are counted.
            int early = 0, confirmed = 0, matched = 0, cancelled = 0; double applied = 0;
            foreach (var stat in UnityEngine.Object.FindObjectsByType<PNStat>())
                if (stat.gameObject.layer == PNLayers.Enemy && stat.IsReady)
                    PNHurtSignal.For(stat).Hurt += info =>
                    {
                        if (info.Phase == PNHurtPhase.Early) early++; else if (info.Phase == PNHurtPhase.Cancelled) cancelled++;
                        else { confirmed++; if (info.MatchedEarly) matched++; applied += info.Event.Applied; }
                    };
            int delivered0 = PNCombatEvents.Delivered, published0 = PNCombatEvents.Published;
            int peakAny = 0, peakReplicas = 0; bool guessBeforeAck = false; long sent = 0;
            var held = new System.Collections.Generic.List<IDisposable>();
            if (host && Refuse)
            {
                // (e1 is client1's entity on every machine.) Its skills are locked on the host before the cast; the shooter's machine is told to guess regardless, so the host refuses a cast that was guessed.
                if (e1.GetComponent<PNControlManager>() is PNControlManager controls && controls.IsReady) held.Add(controls.State.Acquire("o4 refuse", new[] { PNControlIds.SkillAll }));
                if (held.Count == 0) throw new Exception("the host could not lock client1's skills");
                Mark(folder, "host-locked");
            }
            if (Refuse && name != "host") yield return Wait(() => Has("host-locked"), "host locked");
            if (name == "client1")
            {
                yield return Wait(() => remote != null && remote.IsBound && !eve.IsBusy && eve.SkillRecovery.Current >= 1, "Eve ready for Skill");
                yield return new WaitForSecondsRealtime(1f);
                if (NoGuess) remote.GuessEnabled = false;
                if (Refuse) { yield return new WaitForSecondsRealtime(1f); remote.IgnorePause = true; }
                eve.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap));
                eve.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap));
                eve.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                sent = remote.LastSentKey; Mark(folder, "client1-cast");
            }
            yield return Wait(() => Has("client1-cast"), "client1 cast");
            double until = Time.realtimeSinceStartupAsDouble + 8;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                int active = projectiles.Snapshot().Count; peakAny = Math.Max(peakAny, active); peakReplicas = Math.Max(peakReplicas, projectiles.ReplicaCount);
                if (name == "client1" && active > 0 && remote.AckedKey < sent) guessBeforeAck = true;
                yield return null;
            }
            foreach (var lease in held) lease.Dispose();
            yield return new WaitForSecondsRealtime((float)PNCombatEvents.EarlyHoldSeconds + .5f);
            var combatManager = (PNNetworkManager)NetworkManager.singleton;
            log.Append(name + " combat: early=" + early + " confirmed=" + confirmed + " matched=" + matched + " cancelled=" + cancelled + " applied=" + applied + " delivered=" + (PNCombatEvents.Delivered - delivered0) + " published=" + (PNCombatEvents.Published - published0) + " pending=" + PNCombatEvents.PendingEarly + (host ? " bytes=" + combatManager.CombatBytesSent + " events=" + combatManager.CombatEventsSent + " packets=" + combatManager.CombatPacketsSent + " dropped=" + combatManager.CombatDropped : "") + "; ");
            if (PNCombatEvents.PendingEarly != 0) throw new Exception("an early hit is still waiting after its hold " + log);
            if (early != matched + cancelled) throw new Exception("an early hit was neither matched nor cancelled " + log);
            if (host && !Refuse && (confirmed < 1 || PNCombatEvents.Published - published0 < 1 || combatManager.CombatEventsSent < 1 || combatManager.CombatDropped != 0)) throw new Exception("the host published no combat event or lost one " + log);
            if (!host && !Refuse && confirmed < 1) throw new Exception("this client was told no confirmed hit " + log);
            if (Refuse && !host && confirmed != 0) throw new Exception("a refused cast hurt something that was told here " + log);
            if (name == "client1" && !Refuse && !NoGuess && (early < 1 || matched < 1)) throw new Exception("the shooter was told no early hit, or none was matched to the host's event " + log);
            if ((name == "client2" || NoGuess && name == "client1") && early != 0) throw new Exception("this machine guessed nothing but was told an early hit " + log);
            log.Append(name + ": peak=" + peakAny + (name == "client1" ? " adopted=" + projectiles.Adoptions + " turns=" + projectiles.AdoptTurns + " targetChanges=" + projectiles.AdoptTargetChanges + " factorFixes=" + projectiles.AdoptFactorFixes : "") + " replicas=" + peakReplicas + " guessBeforeAck=" + guessBeforeAck + " hitsApplied=" + remote.HitsApplied + " hitsRejected=" + remote.HitsRejected + " predicted=" + remote.PredictedCount + " rolledBack=" + remote.RolledBackCount + " refusals=" + string.Join(";", remote.Refusals.Select(x => x.Key + ":" + x.Reason)) + " acked=" + remote.AckedKey + " sent=" + sent + " health=" + Health() + (host ? " objectBytes=" + ((PNNetworkManager)NetworkManager.singleton).ObjectBytesSent + " records=" + ((PNNetworkManager)NetworkManager.singleton).ObjectRecordsSent + " packets=" + ((PNNetworkManager)NetworkManager.singleton).ObjectPacketsSent : "") + "; ");
            if (host)
            {
                double before = double.Parse(File.ReadAllText(Path.Combine(folder, "host.health0")), System.Globalization.CultureInfo.InvariantCulture);
                if (Refuse)
                {
                    if (remote.HitsApplied != 0 || !(Health() >= before)) throw new Exception("the refused cast still hurt an enemy " + log);
                }
                else
                {
                    if (NoGuess ? remote.HitsApplied != 0 : remote.HitsApplied < 1) throw new Exception((NoGuess ? "a hit was reported although the shooter did not guess " : "the host took no reported hit ") + log);
                    if (!(Health() < before)) throw new Exception("the enemies lost no health " + log);
                }
                Mark(folder, "host-observed");
            }
            yield return Wait(() => Has("host-observed"), "host observed");
            if (name == "client1")
            {
                if (Refuse)
                {
                    if (!guessBeforeAck) throw new Exception("(the refuse scenario needs the guess first) " + log);
                    if (remote.RolledBackCount < 1) throw new Exception("the refused guess was not rolled back " + log);
                    if (projectiles.Snapshot().Count != 0) throw new Exception("the refused cast left a projectile on the shooter's machine " + log);
                }
                else if (NoGuess)
                {
                    if (guessBeforeAck || remote.PredictedCount != 0) throw new Exception("the shooter guessed although the hook was off " + log);
                    if (peakReplicas < 1) throw new Exception("the shooter saw no copy of the real projectiles " + log);
                }
                else
                {
                    if (!guessBeforeAck) throw new Exception("the shooter saw no projectile before the host answered " + log);
                    if (peakAny > 3) throw new Exception("the shooter saw more than the three projectiles of the volley (a second copy?) " + log);
                }
            }
            if (name == "client2")
            {
                if (Refuse && peakReplicas != 0) throw new Exception("client2 saw a projectile of a refused cast " + log);
                if (!Refuse && peakReplicas < 1) throw new Exception("client2 saw no copy " + log);
            }
        }

        // Plan 4.4b part 1 on the real wire: the host fires each enemy / summon projectile definition at client1's entity. Every projectile ends in exactly one impact (hit, wall or life end); the host tells it as a Real
        // impact, each client as a Copy impact, and a client that never drew the Starforger contact burst for a Starforger bolt fails the run. (The host fires them itself so the count is exact; the enemies' own AI is not needed.)
        private static IEnumerator EnemyBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            var projectiles = PNProjectileManager.Resolve(EveRegistrySession.Context, e1.gameObject.scene);
            var scene = e1.gameObject.scene;
            var ids = new[] { "project_n:starforger_bolt", "project_n:starweaver_bolt", "project_n:starreaper_arrow", "project_n:starreaper_echo_arrow" };
            const int each = 3; int launched = ids.Length * each;
            int starforgerBolts = each;
            var seen = new System.Collections.Generic.HashSet<PNStarforgerBoltImpact>(); int peakReplicas = 0, shots = 0;
            int real0 = projectiles.ImpactsReal, copy0 = projectiles.ImpactsCopy;
            if (host)
            {
                yield return new WaitForSecondsRealtime(3f);
                var source = PNCombatSource.Capture(e1.GetComponent<PNStat>()); var layers = PNTargetLayers.Of(PNLayers.Player);
                var body = e1.GetComponentInChildren<Collider>(); var target = body != null ? body.bounds.center : e1.transform.position + Vector3.up; log.Append("aim=" + target + " feet=" + e1.transform.position + " layer=" + e1.gameObject.layer + " collider=" + (body != null) + "; ");
                foreach (var definition in ids)
                    for (int i = 0; i < each; i++)
                    {
                        var launch = new PNProjectileLaunch(EveId.Parse(definition), null, source, layers);
                        var receipt = projectiles.Create(launch, new PNProjectileSpawn(scene, target + new Vector3(2.5f + .8f * i, 0, 0), Vector3.left));
                        if (receipt.Status != PNProjectileStatus.Created) throw new Exception("the host could not make " + definition + " (" + receipt.Status + ")");
                    }
                Mark(folder, "enemy-fired");
            }
            yield return Wait(() => Has("enemy-fired"), "enemy fired");
            double until = Time.realtimeSinceStartupAsDouble + 8;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                peakReplicas = Math.Max(peakReplicas, projectiles.ReplicaCount);
                bool fresh = false; foreach (var burst in UnityEngine.Object.FindObjectsByType<PNStarforgerBoltImpact>()) fresh |= seen.Add(burst);
                if (fresh && Capture && !host && shots < 4)
                {
                    // --pn-capture-burst: a frame of this client's own window just after a burst began, for the user to review the look (the window must be visible, not batchmode).
                    yield return new WaitForSecondsRealtime(.12f); yield return new WaitForEndOfFrame();
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (texture != null) { File.WriteAllBytes(Path.Combine(folder, "burst-" + name + "-" + (++shots) + ".png"), texture.EncodeToPNG()); UnityEngine.Object.Destroy(texture); }
                }
                yield return null;
            }
            int real = projectiles.ImpactsReal - real0, copy = projectiles.ImpactsCopy - copy0;
            log.Append(name + ": launched=" + launched + " real=" + real + " copy=" + copy + " peakReplicas=" + peakReplicas + " bursts=" + seen.Count + " kinds(target/wall/expired/cancelled)=" + projectiles.ImpactsOfKind(PNImpactKind.Target) + "/" + projectiles.ImpactsOfKind(PNImpactKind.Wall) + "/" + projectiles.ImpactsOfKind(PNImpactKind.Expired) + "/" + projectiles.ImpactsOfKind(PNImpactKind.Cancelled) + "; ");
            if (host) { if (real < launched) throw new Exception("the host told " + real + " impacts for " + launched + " projectiles (a piercing one tells each hit) " + log); Mark(folder, "enemy-host-done"); }
            yield return Wait(() => Has("enemy-host-done"), "host done");
            if (!host)
            {
                if (peakReplicas < launched / 2) throw new Exception("this client saw too few copies (" + peakReplicas + ") " + log);
                if (copy != launched) throw new Exception("this client told " + copy + " impacts for " + launched + " projectiles " + log);
                if (seen.Count < starforgerBolts) throw new Exception("this client drew " + seen.Count + " Starforger contact bursts for " + starforgerBolts + " bolts " + log);
                if (projectiles.ReplicaCount != 0) throw new Exception("copies are left over (" + projectiles.ReplicaCount + ") " + log);
            }
        }

        // Tally of every combat event this process was told since it started (measurement of the traffic before any scenario begins).
        private static readonly System.Collections.Generic.SortedDictionary<string, int> StartupTally = new System.Collections.Generic.SortedDictionary<string, int>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void TraceStartup()
        {
            if (!Environment.GetCommandLineArgs().Contains("--pn-object-sync-o6")) return;
            PNCombatEvents.Observed += (e, m) =>
            {
                string layer = "?"; if (EveRegistrySession.IsReady && EveRegistrySession.Context.Entities.TryGet(new EveEntityId(e.TargetId), out var loader) && loader != null) layer = LayerMask.LayerToName(loader.gameObject.layer) + "/" + loader.gameObject.name;
                var k = e.Kind + " target=" + layer + " source=" + (e.SourceId == 0 ? "none" : "yes") + " applied=" + (e.Applied > 0 ? ">0" : "0") + " before=" + e.HealthBefore + " after=" + e.HealthAfter + " key=" + (e.HitKey == null ? "none" : "yes") + " t=" + (int)Time.realtimeSinceStartup;
                StartupTally[k] = (StartupTally.TryGetValue(k, out var n) ? n : 0) + 1;
            };
        }

        private static IEnumerator MeleeBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            var eve = e1.GetComponent<EveCharacterSkill>(); var remote = e1.GetComponent<PNSkillRemoteInput>();
            if (eve == null) throw new Exception("the melee fixture needs Eve (--pn-skill-character=project_n:eve)");
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                foreach (var offset in new[] { new Vector3(1.2f, 0, 0), new Vector3(-1.2f, 0, 0) })
                {
                    var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:melee" + offset.x, EntityKey = "", Position = e1.transform.position + offset };
                    if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused an enemy");
                }
                yield return Wait(() => UnityEngine.Object.FindObjectsByType<PNStat>().Any(x => x.gameObject.layer == PNLayers.Enemy && x.IsReady), "enemies exist");
                Mark(folder, "host-enemies");
            }
            yield return Wait(() => Has("host-enemies"), "enemies");
            yield return new WaitForSecondsRealtime(1.5f);
            int real = 0, guess = 0, copy = 0; var keys = new System.Collections.Generic.HashSet<string>();
            eve.ActionImpact += impact => { if (impact.Origin == PNActionImpactOrigin.Real) real++; else if (impact.Origin == PNActionImpactOrigin.Guess) guess++; else copy++; keys.Add(impact.CastKey + ":" + impact.ActionOrdinal + ":" + impact.HitIndex + ":" + impact.TargetId); };
            if (name == "client1")
            {
                yield return Wait(() => remote != null && remote.IsBound && !eve.IsBusy, "Eve ready");
                yield return new WaitForSecondsRealtime(1f);
                for (int i = 0; i < (MeleeMany ? 30 : 3); i++)
                {
                    eve.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap));
                    eve.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap));
                    eve.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                    long sent = remote.LastSentKey;
                    yield return Wait(() => remote.AckedKey >= sent && !eve.IsBusy, "the host answered the slash");
                    yield return new WaitForSecondsRealtime(.6f);
                }
                Mark(folder, "client1-cast");
            }
            yield return Wait(() => Has("client1-cast"), "client1 cast");
            yield return new WaitForSecondsRealtime(3f);
            int dApplied = 0, dRejected = 0, dLate = 0, dRefused = 0; foreach (var input in UnityEngine.Object.FindObjectsByType<PNSkillRemoteInput>()) { dApplied += input.HitsApplied; dRejected += input.HitsRejected; dLate += input.DealtLate; dRefused += input.DealtRefused; }
            log.Append(name + ": real=" + real + " guess=" + guess + " copy=" + copy + " keys=" + keys.Count + (host ? " dealt(applied/rejected/late/refusedAlive)=" + dApplied + "/" + dRejected + "/" + dLate + "/" + dRefused : "") + "; ");
            if (host)
            {
                File.WriteAllText(Path.Combine(folder, "host.real"), keys.Count.ToString());
                if (real < 1 || guess != 0 || copy != 0) throw new Exception("the host must tell Real impacts only " + log);
                Mark(folder, "host-counted");
            }
            yield return Wait(() => Has("host-counted"), "host counted");
            int hostKeys = int.Parse(File.ReadAllText(Path.Combine(folder, "host.real")));
            if (name == "client1" && (guess < 1 || real != 0 || copy != 0)) throw new Exception("the shooter must tell its own Guess impacts only " + log);
            if (name == "client2" && (copy != hostKeys || real != 0 || guess != 0 || copy < 1)) throw new Exception("client2 must tell one Copy for each hit the host told (" + hostKeys + ") " + log);
        }

        // O5 over the real wire (--pn-object-sync-o5, character Eve): client1 dashes (Eve's time field is an area) while the host's Starweaver casts its blasts. Every machine other than the authority must show the area and the blasts as copies,
        // the dasher's own guess must be taken over by the record (one area, not two), a copy deals nothing, and the offset between a copy's detonation and the first Damage event told afterwards is logged (measurement).
        private static IEnumerator O5Body(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            var eve = e1.GetComponent<EveCharacterSkill>(); var remote = e1.GetComponent<PNSkillRemoteInput>();
            if (eve == null) throw new Exception("the O5 fixture needs Eve (--pn-skill-character=project_n:eve)");
            var scene = e1.gameObject.scene;
            var areas = PNAreaManager.Resolve(EveRegistrySession.Context, scene); var blasts = PNBlastManager.Resolve(EveRegistrySession.Context, scene);
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = Environment.GetCommandLineArgs().Contains("--pn-object-sync-reaper") ? "project_n:starreaper" : "project_n:starweaver", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:o5weaver", EntityKey = "", Position = e1.transform.position + new Vector3(4f, 0, 0) };
                if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused the Starweaver");
                Mark(folder, "host-enemy");
            }
            yield return Wait(() => Has("host-enemy"), "the Starweaver");
            var damageTimes = new System.Collections.Generic.List<double>(); var detonations = new System.Collections.Generic.List<double>();
            Action<PNCombatEvent, bool> onEvent = (combatEvent, matched) => { if (combatEvent.Kind == PNCombatEventKind.Damage) damageTimes.Add(Time.realtimeSinceStartupAsDouble); };
            PNCombatEvents.Observed += onEvent;
            int peakAreaCopies = 0, peakBlastCopies = 0, peakAreaActive = 0, peakBlastActive = 0, lastDetonated = blasts.ReplicasDetonated;
            try
            {
                if (name == "client1")
                {
                    yield return Wait(() => remote != null && remote.IsBound && !eve.IsBusy, "Eve ready");
                    yield return new WaitForSecondsRealtime(1.5f);
                    eve.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap));
                    eve.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap));
                    eve.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                    Mark(folder, "client1-dash");
                }
                double until = Time.realtimeSinceStartupAsDouble + 45;
                while (Time.realtimeSinceStartupAsDouble < until)
                {
                    peakAreaCopies = Math.Max(peakAreaCopies, areas.ReplicaCount); peakBlastCopies = Math.Max(peakBlastCopies, blasts.ReplicaCount);
                    peakAreaActive = Math.Max(peakAreaActive, areas.ActiveCount); peakBlastActive = Math.Max(peakBlastActive, blasts.ActiveCount);
                    if (blasts.ReplicasDetonated > lastDetonated) { lastDetonated = blasts.ReplicasDetonated; detonations.Add(Time.realtimeSinceStartupAsDouble); }
                    yield return null;
                }
            }
            finally { PNCombatEvents.Observed -= onEvent; }
            var offsets = new System.Collections.Generic.List<string>();
            foreach (var detonation in detonations) { double hit = damageTimes.FirstOrDefault(x => x >= detonation - .5 && x <= detonation + 3); offsets.Add(hit > 0 ? ((hit - detonation) * 1000).ToString("F0") + "ms" : "none"); }
            log.Append(name + ": areaCopies=" + peakAreaCopies + " blastCopies=" + peakBlastCopies + " areaActive=" + peakAreaActive + " blastActive=" + peakBlastActive + " detonated=" + blasts.ReplicasDetonated + " adoptions=" + areas.Adoptions + " detToDamage=[" + string.Join(",", offsets) + "] objectRecords=" + ((PNNetworkManager)NetworkManager.singleton).ObjectRecordsSent + " objectBytes=" + ((PNNetworkManager)NetworkManager.singleton).ObjectBytesSent + "; ");
            if (host)
            {
                if (peakAreaCopies != 0 || peakBlastCopies != 0) throw new Exception("the authority must build no copies " + log);
                if (peakBlastActive < 1) throw new Exception("the Starweaver cast no blast on the host in 45 s " + log);
                Mark(folder, "host-observed");
            }
            yield return Wait(() => Has("host-observed"), "host observed");
            if (name == "client1" && (areas.Adoptions < 1 || peakAreaCopies != 1)) throw new Exception("the dasher's guess must be taken over by the record: one area copy and one adoption " + log);
            if (name == "client2" && peakAreaCopies < 1) throw new Exception("client2 never saw the dash field " + log);
            if (name != "host" && (peakBlastCopies < 1 || blasts.ReplicasDetonated < 1)) throw new Exception("this client never saw a blast copy detonate " + log);
        }

        // O5 blast guess over the real wire (--pn-object-sync-o5b, character Eve, CLONE-ONLY build where Eve's dash also creates a 1 s blast at its end): client1 dashes, so it draws a guess blast at cast time that the host's record must take over
        // (one blast on client1, no copy on the host), client2 shows a copy, and the offsets between a drawn detonation and the first Damage event told afterwards are logged on both (measurement of "guess starts one trip early").
        private static IEnumerator O5bBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            var eve = e1.GetComponent<EveCharacterSkill>(); var remote = e1.GetComponent<PNSkillRemoteInput>();
            if (eve == null) throw new Exception("the O5b fixture needs Eve (--pn-skill-character=project_n:eve)");
            var scene = e1.gameObject.scene; var blasts = PNBlastManager.Resolve(EveRegistrySession.Context, scene);
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                foreach (var offset in new[] { new Vector3(2f, 0, 0), new Vector3(-2f, 0, 0), new Vector3(0, 0, 2f), new Vector3(0, 0, -2f) })
                {
                    var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:o5b" + offset.x + "_" + offset.z, EntityKey = "", Position = e1.transform.position + offset };
                    if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused a dummy");
                }
                Mark(folder, "host-enemy");
            }
            yield return Wait(() => Has("host-enemy"), "the dummies");
            var damageTimes = new System.Collections.Generic.List<double>(); var detonations = new System.Collections.Generic.List<double>();
            Action<PNCombatEvent, bool> onEvent = (combatEvent, matched) => { if (combatEvent.Kind == PNCombatEventKind.Damage) damageTimes.Add(Time.realtimeSinceStartupAsDouble); };
            PNCombatEvents.Observed += onEvent;
            int peakCopies = 0, peakActive = 0, peakGuess = 0, lastDetonated = blasts.ReplicasDetonated;
            try
            {
                if (name == "client1")
                {
                    yield return Wait(() => remote != null && remote.IsBound && !eve.IsBusy, "Eve ready");
                    yield return new WaitForSecondsRealtime(1.5f);
                    eve.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap));
                    eve.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap));
                    eve.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                }
                double until = Time.realtimeSinceStartupAsDouble + 12;
                while (Time.realtimeSinceStartupAsDouble < until)
                {
                    peakCopies = Math.Max(peakCopies, blasts.ReplicaCount); peakActive = Math.Max(peakActive, blasts.ActiveCount); peakGuess = Math.Max(peakGuess, blasts.GuessCount);
                    if (blasts.ReplicasDetonated > lastDetonated) { lastDetonated = blasts.ReplicasDetonated; detonations.Add(Time.realtimeSinceStartupAsDouble); }
                    yield return null;
                }
            }
            finally { PNCombatEvents.Observed -= onEvent; }
            var offsets = new System.Collections.Generic.List<string>();
            foreach (var detonation in detonations) { double hit = damageTimes.FirstOrDefault(x => x >= detonation - .5 && x <= detonation + 3); offsets.Add(hit > 0 ? ((hit - detonation) * 1000).ToString("F0") + "ms" : "none"); }
            log.Append(name + ": copies=" + peakCopies + " active=" + peakActive + " guess=" + peakGuess + " detonated=" + blasts.ReplicasDetonated + " adoptions=" + blasts.Adoptions + " damageEvents=" + damageTimes.Count + " detToDamage=[" + string.Join(",", offsets) + "]; ");
            if (host) { if (peakCopies != 0 || peakGuess != 0) throw new Exception("the authority must build no copies or guesses " + log); Mark(folder, "host-observed"); }
            yield return Wait(() => Has("host-observed"), "host observed");
            if (name == "client1" && (blasts.Adoptions != 1 || peakGuess != 1 || peakActive != 1)) throw new Exception("the dasher's guess blast must be taken over by the record: one blast, one adoption " + log);
            if (name == "client2" && (peakCopies != 1 || blasts.ReplicasDetonated != 1)) throw new Exception("client2 must show exactly one blast copy and see it detonate " + log);
        }

        // O6 load (--pn-object-sync-load): the host keeps 100 long-lived homing projectiles flying toward enemies far away (so they curve and the audit has something to correct) and the run reports what the object channel cost:
        // bytes, records and packets per second on the host, the audit's copy error and corrections, overflows, and how many copies each client held at its peak. (Measurement; the checks are only "nothing was lost": no overflow, the clients saw nearly all.)
        // What the datagram fault transport (--pn-sim-kcp) dropped on this machine, so a loss run can show the loss was real.
        private static string FaultText() { var fault = UnityEngine.Object.FindFirstObjectByType<ProjectN.Composition.PNKcpFaultTransport>(); return fault == null ? " fault=off" : " dropped=" + fault.DroppedDatagrams + "/" + fault.AttemptedDatagrams + " datagrams"; }

        private static IEnumerator LoadBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host"; const int total = 100;
            var scene = e1.gameObject.scene; var projectiles = PNProjectileManager.Resolve(EveRegistrySession.Context, scene);
            var manager = (PNNetworkManager)NetworkManager.singleton;
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.PI * .5f; var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 12f;
                    var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:load" + i, EntityKey = "", Position = e1.transform.position + offset };
                    if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused an enemy");
                }
                Mark(folder, "host-enemy");
            }
            yield return Wait(() => Has("host-enemy"), "the far enemies");
            yield return new WaitForSecondsRealtime(3f);
            int peakReplicas = 0, peakAny = 0; var handles = new System.Collections.Generic.List<PNProjectileHandle>(); var spawnAt = new System.Collections.Generic.List<Vector3>();
            long bytes0 = manager.ObjectBytesSent; int records0 = manager.ObjectRecordsSent, packets0 = manager.ObjectPacketsSent, overflows0 = manager.ObjectOverflows;
            int checks0 = projectiles.AuditChecks, corrections0 = projectiles.AuditCorrections; double window = 0;
            if (host)
            {
                var source = PNCombatSource.Capture(e1.GetComponent<PNStat>()); var layers = PNTargetLayers.Of(PNLayers.Enemy); var id = EveId.Parse("base:eve_skill_homing");
                double started = Time.realtimeSinceStartupAsDouble;
                for (int burst = 0; burst < 10; burst++)
                {
                    for (int i = 0; i < total / 10; i++)
                    {
                        float angle = (burst * 10 + i) * 2.399963f; var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                        var launch = new PNProjectileLaunch(id, new Newtonsoft.Json.Linq.JObject(), source, layers, null, null, .02d, 1d, 30d);
                        var receipt = projectiles.Create(launch, new PNProjectileSpawn(scene, e1.transform.position + direction * 1.5f + Vector3.up * .5f, direction));
                        if (receipt.Status != PNProjectileStatus.Created) throw new Exception("the host could not make projectile " + (burst * 10 + i) + " (" + receipt.Status + ")");
                        handles.Add(receipt.Handle); spawnAt.Add(e1.transform.position + direction * 1.5f + Vector3.up * .5f);
                    }
                    yield return new WaitForSecondsRealtime(.5f);
                }
                Mark(folder, "load-made");
            }
            yield return Wait(() => Has("load-made"), "the load made", 90);
            double until = Time.realtimeSinceStartupAsDouble + 12; double begin = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                peakReplicas = Math.Max(peakReplicas, projectiles.ReplicaCount); peakAny = Math.Max(peakAny, projectiles.Snapshot().Count);
                yield return null;
            }
            window = Time.realtimeSinceStartupAsDouble - begin;
            int alive = host ? handles.Count(x => x.IsAlive) : projectiles.Snapshot().Count;
            float travelled = 0; int moving = 0;
            if (host) for (int i = 0; i < handles.Count; i++) { var shot = projectiles.Get(handles[i]); if (shot != null && shot.IsRunning) { float d = (shot.Position - spawnAt[i]).magnitude; travelled += d; if (d > .5f) moving++; } }
            if (host)
            {
                double span = 5d + window; // the launch took 5 s, then the 12 s window
                log.Append("host: made=" + handles.Count + " aliveAfterWindow=" + alive + " moving=" + moving + " meanTravel=" + (alive > 0 ? (travelled / alive).ToString("F1") : "-") + " objectBytes=" + (manager.ObjectBytesSent - bytes0) + " (" + ((manager.ObjectBytesSent - bytes0) / span).ToString("F0") + " B/s) records=" + (manager.ObjectRecordsSent - records0) + " packets=" + (manager.ObjectPacketsSent - packets0)
                    + " overflows=" + (manager.ObjectOverflows - overflows0) + " audits=" + (projectiles.AuditChecks - checks0) + " corrections=" + (projectiles.AuditCorrections - corrections0) + " meanError=" + (projectiles.AuditChecks > checks0 ? (projectiles.AuditErrorSum / projectiles.AuditChecks).ToString("F3") : "-") + " maxError=" + projectiles.AuditMaxError.ToString("F2") + FaultText() + "; ");
                File.WriteAllText(Path.Combine(folder, "host.alive"), peakAny.ToString());
                Mark(folder, "host-observed");
            }
            yield return Wait(() => Has("host-observed"), "host observed");
            if (!host) log.Append(name + ": peakCopies=" + peakReplicas + " aliveAfterWindow=" + alive + " baselinesApplied=" + manager.ObjectBaselinesApplied + FaultText() + "; ");
            if (host && handles.Count != total) throw new Exception("the host made fewer than " + total + " " + log);
            if (host && manager.ObjectOverflows - overflows0 != 0) throw new Exception("the object queue overflowed under the load " + log);
            if (!host) { int hostPeak = int.Parse(File.ReadAllText(Path.Combine(folder, "host.alive"))); if (peakReplicas < hostPeak * .9) throw new Exception("this client saw only " + peakReplicas + " of the host's " + hostPeak + " projectiles " + log); }
        }

        // O6 hits (--pn-object-sync-hits, character Eve): client1 casts Eve's Skill again and again beside two enemies for 30 s. The host counts the hits it applied and the hits it refused, client1 counts how many early phases
        // the host matched or cancelled; the log shows the refused rate. (Measurement; the checks are only "every early hit was matched or cancelled" and "the host applied some".)
        private static IEnumerator HitsBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            var eve = e1.GetComponent<EveCharacterSkill>(); var remote = e1.GetComponent<PNSkillRemoteInput>();
            if (eve == null) throw new Exception("the hits fixture needs Eve (--pn-skill-character=project_n:eve)");
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                foreach (var offset in new[] { new Vector3(3f, 0, 0), new Vector3(-3f, 0, 2f) })
                {
                    var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:hits" + offset.x, EntityKey = "", Position = e1.transform.position + offset };
                    if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused an enemy");
                }
                Mark(folder, "host-enemies");
            }
            yield return Wait(() => Has("host-enemies"), "enemies");
            yield return new WaitForSecondsRealtime(1.5f);
            int early = 0, confirmed = 0, matched = 0, cancelled = 0;
            foreach (var stat in UnityEngine.Object.FindObjectsByType<PNStat>())
                if (stat.gameObject.layer == PNLayers.Enemy && stat.IsReady)
                    PNHurtSignal.For(stat).Hurt += info => { if (info.Phase == PNHurtPhase.Early) early++; else if (info.Phase == PNHurtPhase.Cancelled) cancelled++; else { confirmed++; if (info.MatchedEarly) matched++; } };
            int casts = 0;
            if (name == "client1") yield return Wait(() => remote != null && remote.IsBound && !eve.IsBusy, "Eve ready");
            double until = Time.realtimeSinceStartupAsDouble + 30;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                if (name == "client1" && !eve.IsBusy && eve.SkillRecovery.Current >= 1)
                {
                    eve.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap));
                    eve.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap));
                    eve.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap)); casts++;
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime((float)PNCombatEvents.EarlyHoldSeconds + .5f);
            int applied = remote.HitsApplied, rejected = remote.HitsRejected; var manager = (PNNetworkManager)NetworkManager.singleton;
            log.Append(name + ": casts=" + casts + " early=" + early + " matched=" + matched + " cancelled=" + cancelled + " confirmed=" + confirmed + " hitsApplied=" + applied + " hitsRejected=" + rejected + " refusedRate=" + (applied + rejected > 0 ? (100d * rejected / (applied + rejected)).ToString("F1") + "%" : "-") + " refusals=" + string.Join(";", remote.Refusals.Select(x => x.Reason).GroupBy(x => x).Select(g => g.Key + "x" + g.Count())) + (host ? " combatBytes=" + manager.CombatBytesSent + " combatEvents=" + manager.CombatEventsSent + " dropped=" + manager.CombatDropped : "") + FaultText() + "; ");
            if (PNCombatEvents.PendingEarly != 0) throw new Exception("an early hit is still waiting after its hold " + log);
            if (early != matched + cancelled) throw new Exception("an early hit was neither matched nor cancelled " + log);
            if (host) { if (applied < 1) throw new Exception("the host applied no reported hit in 30 s " + log); Mark(folder, "host-observed"); }
            yield return Wait(() => Has("host-observed"), "host observed");
        }

        // O6 dodge (--pn-object-sync-dodge): the host (the enemy AI's stand-in) fires enemy bolts at client1's player, which keeps walking a square. The host decides every hit from its own view of the player (plan 4.4b part 2, way A);
        // client1 notes per shot whether the copy it sees ever overlapped its own player's body. The log gives the shots the host hit although client1 saw a dodge (the number the plan wants) and the reverse. (Measurement; the checks are
        // only "the host fired every shot, was told and counted each one, and client1 moved".)
        private static IEnumerator DodgeBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host"; const int shots = 20; const string definition = "project_n:starweaver_bolt";
            var scene = e1.gameObject.scene; var projectiles = PNProjectileManager.Resolve(EveRegistrySession.Context, scene);
            long playerId = e1.GetComponent<EveEntityIdentity>().Id.Value;
            var body = e1.GetComponentInChildren<Collider>(); if (body == null) throw new Exception("the player has no collider");
            int current = -1; var hostHit = new bool[shots]; var toldHit = new bool[shots]; var sawOverlap = new bool[shots]; var closest = new float[shots]; for (int i = 0; i < shots; i++) closest[i] = float.MaxValue;
            var kinds = new System.Collections.Generic.SortedDictionary<string, int>(); int target0 = projectiles.ImpactsOfKind(PNImpactKind.Target), wall0 = projectiles.ImpactsOfKind(PNImpactKind.Wall), expired0 = projectiles.ImpactsOfKind(PNImpactKind.Expired);
            Action<PNCombatEvent, bool> onEvent = (combatEvent, matched) => { { string layer = "?"; if (EveRegistrySession.Context.Entities.TryGet(new EveEntityId(combatEvent.TargetId), out var loader) && loader != null) layer = LayerMask.LayerToName(loader.gameObject.layer); var k = (combatEvent.TargetId == playerId ? "self/" : "other/") + layer + "/" + combatEvent.Kind + (combatEvent.Applied > 0 ? ">0" : "=0"); kinds[k] = (kinds.TryGetValue(k, out var n) ? n : 0) + 1; } if (combatEvent.Kind == PNCombatEventKind.Damage && combatEvent.TargetId == playerId && current >= 0) { if (host) hostHit[current] = true; toldHit[current] = true; } };
            PNCombatEvents.Observed += onEvent;
            UnityEngine.InputSystem.Keyboard keyboard = null; Vector3 start = e1.transform.position, low = start, high = start;
            PNStat shooter = null;
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:dodgeshooter", EntityKey = "", Position = e1.transform.position + new Vector3(30f, 0, 0) };
                if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused the shooter");
                yield return Wait(() => UnityEngine.Object.FindObjectsByType<PNStat>().Any(x => x.gameObject.layer == PNLayers.Enemy && x.IsReady), "the shooter exists");
                shooter = UnityEngine.Object.FindObjectsByType<PNStat>().First(x => x.gameObject.layer == PNLayers.Enemy && x.IsReady);
            }
            try
            {
                yield return new WaitForSecondsRealtime(3f);
                if (name == "client1") keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
                var keys = new[] { UnityEngine.InputSystem.Key.D, UnityEngine.InputSystem.Key.None, UnityEngine.InputSystem.Key.W, UnityEngine.InputSystem.Key.None, UnityEngine.InputSystem.Key.A, UnityEngine.InputSystem.Key.None, UnityEngine.InputSystem.Key.S, UnityEngine.InputSystem.Key.None };
                double nextShot = Time.realtimeSinceStartupAsDouble + .5, nextKey = 0; int keyIndex = 0, fired = 0;
                double until = nextShot + shots * 1.5 + 3;
                while (Time.realtimeSinceStartupAsDouble < until)
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    var position = e1.transform.position; low = Vector3.Min(low, position); high = Vector3.Max(high, position);
                    if (name == "client1" && now >= nextKey) { UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(keys[keyIndex++ % keys.Length])); nextKey = now + .45; }
                    if (host && fired < shots && now >= nextShot)
                    {
                        current = fired; var source = PNCombatSource.Capture(shooter); var target = body.bounds.center;
                        var launch = new PNProjectileLaunch(EveId.Parse(definition), null, source, PNTargetLayers.Of(PNLayers.Player), source.Snapshot.PrepareDamage(1d, false, null, EveId.Parse("base:magic")));
                        if (DodgeBlast)
                        {
                            var blasts = PNBlastManager.Resolve(EveRegistrySession.Context, scene);
                            var input = Newtonsoft.Json.Linq.JObject.Parse("{'delay':0.4,'tail':0.4,'radius':2.0,'height':2.0,'value':{'amount':1},'canCrit':false,'tags':['base:magic']}");
                            var made = blasts.Create(new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, input, source, scene, new Vector3(target.x, target.y - 0.5f, target.z), shooter, PNBlastDependency.Independent));
                            if (made.Status != PNBlastStatus.Created) throw new Exception("the host could not drop blast " + fired + " (" + made.Status + ")");
                        }
                        else
                        {
                        var receipt = projectiles.Create(launch, new PNProjectileSpawn(scene, target + new Vector3(3.5f, 0, 0), Vector3.left));
                        if (receipt.Status != PNProjectileStatus.Created) throw new Exception("the host could not fire shot " + fired + " (" + receipt.Status + ")");
                        }
                        Mark(folder, "shot-" + fired); fired++; nextShot += 1.5;
                    }
                    if (host && current >= 0) foreach (var snapshot in projectiles.Snapshot()) if (snapshot.IsRunning && snapshot.DefinitionId.ToString() == definition) closest[current] = Math.Min(closest[current], (snapshot.Position - body.bounds.center).magnitude);
                    if (!host)
                    {
                        for (int i = shots - 1; i >= 0; i--) if (Has("shot-" + i)) { current = i; break; }
                        if (current >= 0) foreach (var snapshot in projectiles.Snapshot()) if (snapshot.IsRunning && snapshot.DefinitionId.ToString() == definition) closest[current] = Math.Min(closest[current], (snapshot.Position - body.bounds.center).magnitude);
                        if (current >= 0)
                        {
                            var box = body.bounds; foreach (var snapshot in projectiles.Snapshot()) if (snapshot.IsRunning && snapshot.DefinitionId.ToString() == definition && new Bounds(snapshot.Position, snapshot.HalfExtents * 2f).Intersects(box)) sawOverlap[current] = true;
                        }
                    }
                    yield return null;
                }
                if (keyboard != null) UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            }
            finally { PNCombatEvents.Observed -= onEvent; if (keyboard != null) UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard); }
            string Bits(bool[] values) => new string(values.Select(x => x ? '1' : '0').ToArray());
            if (host) { File.WriteAllText(Path.Combine(folder, "host.dodge"), Bits(hostHit)); Mark(folder, "host-observed"); }
            yield return Wait(() => Has("host-observed"), "host observed");
            var hostBits = File.ReadAllText(Path.Combine(folder, "host.dodge")).Select(x => x == '1').ToArray();
            int hits = hostBits.Count(x => x), told = toldHit.Count(x => x);
            string claims = " claims(accepted/refused/sent)=" + projectiles.ClaimsAccepted + "/" + projectiles.ClaimsRefused + "/" + projectiles.ClaimsSent + " refusals{" + string.Join(",", projectiles.ClaimRefusals.Select(x => x.Key + "x" + x.Value)) + "}";
            string spread = claims + " impacts(target/wall/expired)=" + (projectiles.ImpactsOfKind(PNImpactKind.Target) - target0) + "/" + (projectiles.ImpactsOfKind(PNImpactKind.Wall) - wall0) + "/" + (projectiles.ImpactsOfKind(PNImpactKind.Expired) - expired0) + " eventsOnPlayer={" + string.Join(",", kinds.Select(x => x.Key + "x" + x.Value)) + "} walked=" + (high - low).ToString("F1") + " closest=" + string.Join(",", closest.Select(x => x == float.MaxValue ? "-" : x.ToString("F1")));
            if (name == "client1")
            {
                int saw = sawOverlap.Count(x => x); int dodgedButHit = 0, sawButMissed = 0; for (int i = 0; i < shots; i++) { if (hostBits[i] && !sawOverlap[i]) dodgedButHit++; if (!hostBits[i] && sawOverlap[i]) sawButMissed++; }
                log.Append("client1: shots=" + shots + " hostHit=" + hits + " toldHit=" + told + " sawOverlap=" + saw + " hitButSawDodge=" + dodgedButHit + " (" + (hits > 0 ? (100d * dodgedButHit / hits).ToString("F0") : "-") + "% of the host's hits) sawHitButHostMissed=" + sawButMissed + " hostBits=" + string.Concat(hostBits.Select(x => x ? '1' : '0')) + " sawBits=" + Bits(sawOverlap) + spread + FaultText() + "; ");
            }
            else log.Append(name + ": hostHit=" + hits + " toldHit=" + told + spread + "; ");
            if (host && !Has("shot-" + (shots - 1))) throw new Exception("the host did not fire every shot " + log);
            if (name == "client1" && (high - low).magnitude < .5f) throw new Exception("client1 did not move " + log);
            if (!host && told != hits) throw new Exception("this machine was told " + told + " hits but the host applied " + hits + " " + log);
        }


        // V4b/V4c melee dodge (--pn-object-sync-dodgemelee: Starkeeper sweeps, --pn-object-sync-dodgeslam: Starforger slam): the host spawns the monster next to the players and its AI attacks them while client1's player keeps walking a square.
        // The machine of a player decides his melee hits (door + ticket). Evidence: every machine is told the same hits the host applied, the host took melee claims (counted apart from projectile claims: the Starforger's stars also shoot),
        // and the melee claims the host judged never exceed what the clients sent. All machines start together (the host spawns once every client is ready), so none misses a hit by joining late.
        private static IEnumerator DodgeMeleeBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log, string character, string hint, bool needContact = false)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host"; double seconds = needContact ? 70 : 40;
            long playerId = e1.GetComponent<EveEntityIdentity>().Id.Value;
            int told = 0; float toldAmount = 0f;
            Action<PNCombatEvent, bool> onEvent = (combatEvent, matched) => { if (combatEvent.Kind == PNCombatEventKind.Damage && combatEvent.TargetId == playerId && combatEvent.Applied > 0) { told++; toldAmount += (float)combatEvent.Applied; } };
            PNCombatEvents.Observed += onEvent;
            int sent0 = PNMeleeDoorClaims.SentTotal, contact0 = PNMeleeDoorClaims.ContactSentTotal;
            UnityEngine.InputSystem.Keyboard keyboard = null; Vector3 start = e1.transform.position, low = start, high = start;
            try
            {
                if (!host) Mark(folder, name + "-melee-ready");
                if (host)
                {
                    yield return Wait(() => Has("client1-melee-ready") && Has("client2-melee-ready"), "the clients are ready");
                    var world = PNLocalWorldContext.Current.World;
                    var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = character, World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:dodgemelee", EntityKey = "", Position = e1.transform.position + new Vector3(4f, 0, 0) };
                    if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused the monster");
                    Mark(folder, "monster-spawned");
                }
                yield return Wait(() => Has("monster-spawned"), "the monster is spawned");
                yield return Wait(() => UnityEngine.Object.FindObjectsByType<PNSkill>().Any(x => x.gameObject.layer == PNLayers.Enemy && x.IsReady && x.name.ToLowerInvariant().Contains(hint)), "the monster exists");
                if (name == "client1") keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
                var keys = new[] { UnityEngine.InputSystem.Key.D, UnityEngine.InputSystem.Key.None, UnityEngine.InputSystem.Key.W, UnityEngine.InputSystem.Key.None, UnityEngine.InputSystem.Key.A, UnityEngine.InputSystem.Key.None, UnityEngine.InputSystem.Key.S, UnityEngine.InputSystem.Key.None };
                double until = Time.realtimeSinceStartupAsDouble + seconds, nextKey = 0; int keyIndex = 0;
                while (Time.realtimeSinceStartupAsDouble < until)
                {
                    double now = Time.realtimeSinceStartupAsDouble; var position = e1.transform.position; low = Vector3.Min(low, position); high = Vector3.Max(high, position);
                    if (name == "client1" && now >= nextKey) { UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(keys[keyIndex++ % keys.Length])); nextKey = now + .6; }
                    yield return null;
                }
                if (keyboard != null) UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            }
            finally { PNCombatEvents.Observed -= onEvent; if (keyboard != null) UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard); }
            yield return new WaitForSecondsRealtime(2f);
            int sent = PNMeleeDoorClaims.SentTotal - sent0, contactSent = PNMeleeDoorClaims.ContactSentTotal - contact0; int applied = 0, rejected = 0;
            foreach (var input in UnityEngine.Object.FindObjectsByType<PNSkillRemoteInput>()) { applied += input.MeleeClaimsApplied; rejected += input.MeleeClaimsRejected; }
            File.WriteAllText(Path.Combine(folder, name + ".melee"), told + "," + sent + "," + applied + "," + rejected + "," + contactSent); Mark(folder, name + "-melee-done");
            yield return Wait(() => Has("host-melee-done") && Has("client1-melee-done") && Has("client2-melee-done"), "every machine finished");
            int[] Read(string who) => File.ReadAllText(Path.Combine(folder, who + ".melee")).Split(',').Select(int.Parse).ToArray();
            var h = Read("host"); var c1 = Read("client1"); var c2 = Read("client2");
            int hostTold = h[0], clientSent = c1[1] + c2[1], hostApplied = h[2], hostRejected = h[3], contactClaims = c1[4] + c2[4];
            log.Append(name + ": hostHit=" + hostTold + " toldHere=" + told + " toldAmount=" + toldAmount.ToString("F1") + " meleeClaims(sent c1/c2)=" + c1[1] + "/" + c2[1] + " host(applied/rejected)=" + hostApplied + "/" + hostRejected + " contactLungeClaims=" + contactClaims + " walked=" + (high - low).ToString("F1") + FaultText() + "; ");
            if (hostTold < 1) throw new Exception("the monster never hurt the player in " + seconds + " s " + log);
            if (hostApplied < 1) throw new Exception("the host took no melee claim " + log);
            if (needContact && contactClaims < 1) throw new Exception("no player claimed a lunge (contact door) in " + seconds + " s " + log);
            if (hostApplied + hostRejected > clientSent) throw new Exception("the host judged more melee claims than the clients sent " + log);
            if (told != hostTold) throw new Exception("this machine was told " + told + " hits but the host applied " + hostTold + " " + log);
            if (name == "client1" && (high - low).magnitude < .5f) throw new Exception("client1 did not move " + log);
        }

        // O7: the enemy's own AI casts; the host counts the Actions it announced, each other machine counts the ones it replayed on ITS copy of that enemy (and the cast channel's bytes are logged for the budget).
        private static IEnumerator StrikeBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host"; int announced = 0; PNSkill watched = null;
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:starkeeper", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:strike", EntityKey = "", Position = e1.transform.position + new Vector3(5f, 0, 0) };
                if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused the Starkeeper");
            }
            yield return Wait(() => UnityEngine.Object.FindObjectsByType<PNSkill>().Any(x => x.gameObject.layer == PNLayers.Enemy && x.IsReady && x.name.Contains("tarkeeper")), "the Starkeeper exists");
            watched = UnityEngine.Object.FindObjectsByType<PNSkill>().First(x => x.gameObject.layer == PNLayers.Enemy && x.IsReady && x.name.Contains("tarkeeper"));
            long publishedBefore = watched.ActionRunsPublished; // the runs go through the Action stream
            var manager = (PNNetworkManager)NetworkManager.singleton;
            int played = 0, viewPlayed = 0, widest = 0; double until = Time.realtimeSinceStartupAsDouble + 25;
            var seenByKeeper = new Dictionary<int, int>(); var viewedAll = new HashSet<string>();
            try
            {
                while (Time.realtimeSinceStartupAsDouble < until)
                {
                    foreach (var keeper in UnityEngine.Object.FindObjectsByType<PNSkill>()) if (keeper != null && keeper.gameObject.layer == PNLayers.Enemy && keeper.name.Contains("tarkeeper")) { seenByKeeper[keeper.GetEntityId().GetHashCode()] = keeper.ViewedBySkill.Values.Sum(); foreach (var k in keeper.ViewedKeys) viewedAll.Add(k); }
                    played = seenByKeeper.Values.Sum();
                    if (watched != null && host) announced = Math.Max(announced, (int)(watched.ActionRunsPublished - publishedBefore));
                    yield return null;
                }
            }
            finally { }
            if (host) { File.WriteAllText(Path.Combine(folder, "host.announced"), announced.ToString()); Mark(folder, "host-strikes"); }
            yield return Wait(() => Has("host-strikes"), "host strikes");
            int hostCasts = int.Parse(File.ReadAllText(Path.Combine(folder, "host.announced")));
            log.Append(name + ": starkeeperSweeps(host announced)=" + hostCasts + " replayedHere=" + played + " dropped=" + viewPlayed + " instances=" + seenByKeeper.Count + " viewed=[" + string.Join(",", viewedAll) + "]; ");
            if (hostCasts < 1) throw new Exception("the Starkeeper never swept " + log);
            if (!host && played < 1) throw new Exception("this machine replayed no strike although the host announced " + hostCasts + " " + log);
        }

        private static IEnumerator SlowBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:starkeeper", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:slow", EntityKey = "", Position = e1.transform.position + new Vector3(4f, 0, 0) };
                if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused the Starkeeper");
            }
            double begin = Time.realtimeSinceStartupAsDouble, until = begin + 50; bool slowed = false; float hostScale = 1f;
            double actionStart = -1, streamStart = -1; bool actionOpen = false, streamOpen = false; bool slowAtStart = false; float scaleAtStart = 1f;
            var rows = new List<string>(); var leftovers = new HashSet<int>(); bool first = true;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                double t = Time.realtimeSinceStartupAsDouble - begin;
                PNSkill keeper = null;
                foreach (var skill in UnityEngine.Object.FindObjectsByType<PNSkill>())
                {
                    if (skill == null || skill.gameObject.layer != PNLayers.Enemy || !skill.name.Contains("tarkeeper")) continue;
                    if (first) leftovers.Add(skill.GetHashCode());
                    if (!leftovers.Contains(skill.GetHashCode())) keeper = skill;
                }
                first = false;
                if (host && !slowed && t > 15 && keeper != null)
                {
                    var clock = keeper.GetComponent<EveEngine.EveTimeScale>();
                    if (clock != null) { clock.AddTimedModifier(EveId.Parse("test:slow"), EveEngine.EveTimeModifierKind.Scale, -0.8f, 60f); slowed = true; File.WriteAllText(Path.Combine(folder, "slow-on"), t.ToString("0.0")); }
                }
                if (keeper != null)
                {
                    var clock = keeper.GetComponent<EveEngine.EveTimeScale>(); float scale = clock != null ? clock.Scale : -1f; if (host) hostScale = scale;
                    bool action = keeper.ObservedViewCount > 0, stream = keeper.ObservedViewCount > 0;
                    if (!host)
                    {
                        if (action && !actionOpen) { actionOpen = true; actionStart = t; slowAtStart = Has("slow-on"); scaleAtStart = scale; }
                        if (stream && !streamOpen) { streamOpen = true; streamStart = t; }
                        if (!action && actionOpen) { actionOpen = false; rows.Add((slowAtStart ? "SLOW" : "norm") + " action=" + (t - actionStart).ToString("0.00") + "s"); actionStart = -1; }
                        if (!stream && streamOpen) { streamOpen = false; rows.Add((slowAtStart ? "SLOW" : "norm") + " streamListed=" + (t - streamStart).ToString("0.00") + "s localScale=" + scaleAtStart.ToString("0.00")); }
                    }
                }
                yield return null;
            }
            if (host) { File.WriteAllText(Path.Combine(folder, "host.slow"), "hostScale=" + hostScale.ToString("0.00") + " slowed=" + slowed); Mark(folder, "host-slow"); }
            yield return Wait(() => Has("host-slow"), "host slow");
            log.Append(name + " slow: " + File.ReadAllText(Path.Combine(folder, "host.slow")) + " rows[" + string.Join(" | ", rows) + "]; ");
            if (host && !slowed) throw new Exception("the host never slowed the Starkeeper " + log);
            if (!host && rows.Count == 0) throw new Exception("this machine saw no sweep " + log);
        }

        private static IEnumerator StrikeAllBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            var characters = new[] { "project_n:starkeeper", "project_n:starforger", "project_n:starreaper", "project_n:starweaver", "project_n:starchanter", "project_n:starherald" };
            if (host)
            {
                var world = PNLocalWorldContext.Current.World;
                for (int i = 0; i < characters.Length; i++)
                {
                    var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = characters[i], World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "objectsync:strikeall" + i, EntityKey = "", Position = e1.transform.position + new Vector3((characters[i] == "project_n:starreaper" ? 1.2f : 4f + (i % 3) * 1.5f), 0, (i / 3 - 0.5f) * 3f) };
                    if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused " + characters[i]);
                }
            }
            var published = new Dictionary<(int, string), int>(); var replayed = new Dictionary<(int, string), int>(); var dropped = new Dictionary<(int, string), int>();
            var manager = (PNNetworkManager)NetworkManager.singleton; long packets0 = manager.ActionPacketsSent, bytes0 = manager.ActionBytesSent;
            var shotsFolder = Path.Combine(folder, "shots"); if (!host) Directory.CreateDirectory(shotsFolder);
            var shotCounts = new Dictionary<string, int>(); var shotSeen = new Dictionary<(int, string), int>(); var shotDue = new List<(double due, PNSkill skill, string id)>();
            var names = new SortedSet<string>(); var hurt = new HashSet<int>(); var instances = new Dictionary<int, string>(); var leftovers = new HashSet<int>(); bool firstFrameDone = false; var bytesSeen = new Dictionary<(int, string), long>(); var rowsSeen = new Dictionary<(int, string), int>(); var trace = new Dictionary<int, double[]>(); var traceInfo = new Dictionary<int, string>(); double nextPull = 0; bool pulled = false;
            double until = Time.realtimeSinceStartupAsDouble + 40;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                for (int d = shotDue.Count - 1; d >= 0; d--)
                {
                    if (Time.realtimeSinceStartupAsDouble < shotDue[d].due) continue;
                    var due = shotDue[d]; shotDue.RemoveAt(d);
                    if (due.skill == null) continue;
                    shotCounts.TryGetValue(due.id, out int taken); if (taken >= 2) continue; shotCounts[due.id] = taken + 1;
                    Texture2D texture = null;
                    try { texture = ShotFrame(due.skill.transform, e1.transform); File.WriteAllBytes(Path.Combine(shotsFolder, name + "_" + due.skill.name.Replace("(Clone)", "").Replace(' ', '_') + "_" + due.id.Replace("project_n:", "") + "_" + (taken + 1) + ".png"), texture.EncodeToPNG()); }
                    catch (Exception e) { log.Append("shot failed " + e.Message + "; "); }
                    finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
                }
                foreach (var skill in UnityEngine.Object.FindObjectsByType<PNSkill>())
                {
                    if (skill == null || skill.gameObject.layer != PNLayers.Enemy) continue;
                    int id = skill.GetHashCode();
                    if (!firstFrameDone) leftovers.Add(id); // enemies of an earlier phase that are still listed in the first frame: not this scenario's
                    if (leftovers.Contains(id)) continue;
                    names.Add(skill.name.Replace("(Clone)", ""));
                    if (host && skill.ActionPublication != null) { foreach (var pair in skill.ActionPublication.EmittedBytes) bytesSeen[(id, pair.Key)] = Math.Max(bytesSeen.TryGetValue((id, pair.Key), out long bb) ? bb : 0, pair.Value); foreach (var pair in skill.ActionPublication.EmittedRows) rowsSeen[(id, pair.Key)] = Math.Max(rowsSeen.TryGetValue((id, pair.Key), out int rr) ? rr : 0, pair.Value); }
                    if (host && skill.name.Contains("tarreaper") && Time.realtimeSinceStartupAsDouble > nextPull) { pulled = true; skill.transform.position = e1.transform.position + new Vector3(1f, 0f, 0f); }
                    double nowT = Time.realtimeSinceStartupAsDouble - (until - 40);
                    if (!trace.TryGetValue(id, out var life)) { var ni = skill.GetComponentInParent<NetworkIdentity>(); life = new[] { nowT, nowT }; trace[id] = life; traceInfo[id] = skill.name.Replace("(Clone)", "") + " net=" + (ni != null ? ni.netId.ToString() : "none") + " root=" + skill.transform.root.name + " pos=" + skill.transform.position.ToString("0.0") + " born=" + nowT.ToString("0.00") + " comps=" + string.Join("+", skill.GetComponents<Component>().Select(c => c.GetType().Name).Where(c => c.StartsWith("PNEnemy") || c.Contains("Identity") || c.Contains("Loader")).Take(6)); }
                    life[1] = nowT;
                    instances[id] = skill.name.Replace("(Clone)", "") + "@" + nowT.ToString("0") + "s";
                    if (host && skill.name.Contains("tarchanter") && hurt.Add(id)) { var stat = skill.GetComponent<PNStat>(); if (stat != null) stat.SetCurrent(PNContentIds.Health, stat.GetMax(PNContentIds.Health) * .5d); }
                    if (!host) foreach (var pair in skill.ViewedBySkill) { shotSeen.TryGetValue((id, pair.Key), out int before); if (pair.Value > before) { shotSeen[(id, pair.Key)] = pair.Value; shotDue.Add((Time.realtimeSinceStartupAsDouble + .2d, skill, pair.Key)); } }
                    foreach (var pair in skill.PublishedBySkill) published[(id, pair.Key)] = pair.Value;
                    foreach (var pair in skill.ViewedBySkill) replayed[(id, pair.Key)] = pair.Value;     // PV6: a run drawn by its view counts as seen
                }
                firstFrameDone = true;
                if (pulled) { pulled = false; nextPull = Time.realtimeSinceStartupAsDouble + 3d; }
                yield return null;
            }
            Dictionary<string, int> Sum(Dictionary<(int, string), int> table) { var total = new Dictionary<string, int>(); foreach (var pair in table) { total.TryGetValue(pair.Key.Item2, out int n); total[pair.Key.Item2] = n + pair.Value; } return total; }
            string Show(Dictionary<string, int> table) => string.Join(",", table.OrderBy(x => x.Key).Select(x => x.Key.Replace("project_n:", "") + "=" + x.Value));
            var hostTable = Sum(published);
            var publishedKeys = new List<string>(); var replayedKeys = new List<string>();
            foreach (var skill in UnityEngine.Object.FindObjectsByType<PNSkill>()) { if (skill == null || skill.gameObject.layer != PNLayers.Enemy) continue; publishedKeys.AddRange(skill.PublishedKeys); replayedKeys.AddRange(skill.ViewedKeys); }
            if (host)
            {
                var byData = new SortedDictionary<string, long>(); var rowsByData = new SortedDictionary<string, int>();
                foreach (var pair in bytesSeen) { byData.TryGetValue(pair.Key.Item2, out long v); byData[pair.Key.Item2] = v + pair.Value; }
                foreach (var pair in rowsSeen) { rowsByData.TryGetValue(pair.Key.Item2, out int v); rowsByData[pair.Key.Item2] = v + pair.Value; }
                log.Append("rowBytes{" + string.Join(",", byData.Select(x => x.Key.Replace("project_n:", "") + "=" + x.Value + "B/" + rowsByData[x.Key] + "r")) + "}; ");
            }
            if (host) { File.WriteAllText(Path.Combine(folder, "host.published"), Show(hostTable)); File.WriteAllLines(Path.Combine(folder, "host.keys"), publishedKeys); Mark(folder, "host-strikes-all"); }
            yield return Wait(() => Has("host-strikes-all"), "host strikes");
            var seen = Sum(replayed); var lost = Sum(dropped);
            log.Append(name + ": published{" + File.ReadAllText(Path.Combine(folder, "host.published")) + "} replayed{" + Show(seen) + "} dropped{" + Show(lost) + "}; ");
            log.Append(name + " actionStream: packets=" + (manager.ActionPacketsSent - packets0) + " bytes~=" + (manager.ActionBytesSent - bytes0) + " over 40 s; enemiesSeen=" + string.Join("/", names) + "; ");
            if (!host)
            {
                var hostKeys = File.ReadAllLines(Path.Combine(folder, "host.keys")); var mine = new HashSet<string>(replayedKeys);
                var missing = hostKeys.Where(k => !mine.Contains(k)).ToList();
                log.Append(name + " lives=" + string.Join(" | ", trace.OrderBy(t => t.Value[0]).Select(t => traceInfo[t.Key] + " seen " + t.Value[0].ToString("0.00") + ".." + t.Value[1].ToString("0.00"))) + "; ");
                log.Append(name + " instances(last seen s)=" + string.Join("|", instances.Select(x => x.Value + "#" + replayed.Where(r => r.Key.Item1 == x.Key).Sum(r => r.Value))) + "; seenKeys=" + string.Join(",", UnityEngine.Object.FindObjectsByType<PNSkill>().Where(k => k != null && k.gameObject.layer == PNLayers.Enemy).SelectMany(k => k.ViewedKeys).Where(k => k.EndsWith("star_sweep")).Take(40)) + "; ");
                log.Append(name + " keys: host=" + hostKeys.Length + " replayedHere=" + replayedKeys.Count + " missing=[" + string.Join(",", missing) + "] extra=" + replayedKeys.Count(k => !hostKeys.Contains(k)) + "; ");
            }
            if (hostTable.Count == 0 && host) throw new Exception("no enemy published an Action " + log);
            if (!host && lost.Count > 0) throw new Exception("this machine dropped replays " + log);
            if (!host && seen.Count == 0) throw new Exception("this machine replayed nothing " + log);
        }

        // The game camera's picture of one enemy rendered into a texture (the window may be hidden).
        private static Texture2D ShotFrame(Transform subject, Transform focus)
        {
            var cam = Camera.main; if (cam == null) cam = Camera.allCameras.Where(c => c.enabled).OrderByDescending(c => c.depth).FirstOrDefault();
            if (cam == null) throw new Exception("no camera");
            var rt = RenderTexture.GetTemporary(960, 540, 24); var previous = cam.targetTexture; var active = RenderTexture.active; var home = cam.transform.position;
            var shift = subject.position - focus.position; shift.y = 0; cam.transform.position = home + shift; cam.transform.position += cam.transform.forward * Vector3.Distance(cam.transform.position, subject.position) * .6f;
            try
            {
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply(); return texture;
            }
            finally { cam.transform.position = home; cam.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); }
        }

        private static IEnumerator QuietBody(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            var tally = new System.Collections.Generic.SortedDictionary<string, int>(); var manager = (PNNetworkManager)NetworkManager.singleton;
            Action<PNCombatEvent, bool> count = (e, m) =>
            {
                string layer = "?"; if (EveRegistrySession.Context.Entities.TryGet(new EveEntityId(e.TargetId), out var loader) && loader != null) layer = LayerMask.LayerToName(loader.gameObject.layer) + "/" + loader.gameObject.name;
                var k = e.Kind + " target=" + layer + " source=" + (e.SourceId == 0 ? "none" : "yes") + " applied=" + (e.Applied > 0 ? ">0" : "0") + " tags=" + string.Join("+", e.Tags ?? new string[0]);
                tally[k] = (tally.TryGetValue(k, out var n) ? n : 0) + 1;
            };
            PNCombatEvents.Observed += count; int sent0 = manager.CombatEventsSent, bytes0 = (int)manager.CombatBytesSent;
            yield return new WaitForSecondsRealtime(15f);
            PNCombatEvents.Observed -= count;
            int total = 0; foreach (var pair in tally) total += pair.Value;
            log.Append(name + " quiet: told=" + total + " sent=" + (manager.CombatEventsSent - sent0) + " bytes=" + (manager.CombatBytesSent - bytes0) + " {" + string.Join(" | ", tally.Select(x => x.Key + " x" + x.Value)) + "}; ");
        }

        // O6 on the real wire: the host makes long-lived projectiles, every observer gets a fresh baseline of them, then a flood overflows the host's queue and every observer must still end with the same living set.
        private static IEnumerator O6Body(string folder, string name, PNPlayerMove e1, System.Text.StringBuilder log)
        {
            bool Has(string value) => File.Exists(Path.Combine(folder, value));
            bool host = name == "host";
            var projectiles = PNProjectileManager.Resolve(EveRegistrySession.Context, e1.gameObject.scene);
            var manager = (PNNetworkManager)NetworkManager.singleton;
            const int living = 5;
            var handles = new System.Collections.Generic.List<PNProjectileHandle>();
            var scene = e1.gameObject.scene; double madeAt = 0;
            var o6Tally = new System.Collections.Generic.SortedDictionary<string, int>();
            Action<PNCombatEvent, bool> o6Count = (e, m) =>
            {
                string layer = "?"; if (EveRegistrySession.Context.Entities.TryGet(new EveEntityId(e.TargetId), out var loader) && loader != null) layer = LayerMask.LayerToName(loader.gameObject.layer) + "/" + loader.gameObject.name;
                var k = e.Kind + " target=" + layer + " source=" + (e.SourceId == 0 ? "none" : "yes") + " applied=" + (e.Applied > 0 ? ">0" : "0") + " key=" + (e.HitKey == null ? "none" : "yes");
                o6Tally[k] = (o6Tally.TryGetValue(k, out var n) ? n : 0) + 1;
            };
            PNCombatEvents.Observed += o6Count;
            if (host)
            {
                yield return new WaitForSecondsRealtime(2f);
                var source = PNCombatSource.Capture(e1.GetComponent<PNStat>()); var layers = PNTargetLayers.Of(PNLayers.Enemy); var id = EveId.Parse("base:eve_skill_homing");
                for (int i = 0; i < living; i++)
                {
                    var launch = new PNProjectileLaunch(id, new Newtonsoft.Json.Linq.JObject(), source, layers, null, null, .02d, 1d, 30d);
                    var receipt = projectiles.Create(launch, new PNProjectileSpawn(scene, e1.transform.position + new Vector3(0, 0, .3f * i), Vector3.right));
                    if (receipt.Status != PNProjectileStatus.Created) throw new Exception("the host could not make a long-lived projectile (" + receipt.Status + ")");
                    handles.Add(receipt.Handle);
                }
                yield return new WaitForSecondsRealtime(1f);
                if (handles.Count(x => x.IsAlive) != living) throw new Exception("the long-lived projectiles did not live (" + handles.Count(x => x.IsAlive) + " of " + living + ", active " + projectiles.ActiveCount + ")");
                madeAt = Time.realtimeSinceStartupAsDouble; Mark(folder, "o6-made");
            }
            yield return Wait(() => Has("o6-made"), "o6 made");
            if (!host) yield return Wait(() => projectiles.ReplicaCount == living, "this client sees the living set", 20, () => "replicas " + projectiles.ReplicaCount + " snapshot " + projectiles.Snapshot().Count + " baselinesApplied " + manager.ObjectBaselinesApplied);
            if (!host) Mark(folder, name + "-seen");
            int applied0 = manager.ObjectBaselinesApplied, sent0 = manager.ObjectBaselinesSent; long bytes0 = manager.ObjectBytesSent; int records0 = manager.ObjectRecordsSent;
            if (host)
            {
                yield return Wait(() => Has("client1-seen") && Has("client2-seen"), "both clients saw the set");
                applied0 = manager.ObjectBaselinesApplied; sent0 = manager.ObjectBaselinesSent; bytes0 = manager.ObjectBytesSent; records0 = manager.ObjectRecordsSent;
                manager.RebaselineObjects();
                yield return new WaitForSecondsRealtime(3f);
                if (manager.ObjectBaselinesSent < sent0 + 2) throw new Exception("the host sent no fresh baseline to the observers (" + manager.ObjectBaselinesSent + " vs " + sent0 + ")");
                Mark(folder, "o6-rebaselined");
            }
            yield return Wait(() => Has("o6-rebaselined"), "rebaselined");
            if (!host)
            {
                yield return Wait(() => manager.ObjectBaselinesApplied > applied0, "this client applied the fresh baseline");
                if (projectiles.ReplicaCount != living || projectiles.Snapshot().Count != living) throw new Exception("after the fresh baseline this client has " + projectiles.ReplicaCount + " copies / " + projectiles.Snapshot().Count + " projectiles, not " + living);
            }
            // The flood: waves of short-lived projectiles, far more records than the queue holds; the host answers with a baseline and no observer may end with a stray or a missing copy.
            int overflows0 = manager.ObjectOverflows, made = 0;
            if (host)
            {
                var source = PNCombatSource.Capture(e1.GetComponent<PNStat>()); var layers = PNTargetLayers.Of(PNLayers.Enemy); var id = EveId.Parse("base:eve_skill_homing");
                for (int wave = 0; wave < 4; wave++)
                {
                    for (int i = 0; i < 800; i++)
                    {
                        var launch = new PNProjectileLaunch(id, new Newtonsoft.Json.Linq.JObject(), source, layers, null, null, 1d, 1d, .01d);
                        var receipt = projectiles.Create(launch, new PNProjectileSpawn(scene, e1.transform.position + new Vector3(0, 1f, 3f), Vector3.forward));
                        if (receipt.Status == PNProjectileStatus.Created) made++;
                    }
                    yield return null; yield return null; yield return null;
                }
                yield return new WaitForSecondsRealtime(6f);
                if (handles.Count(x => x.IsAlive) != living) throw new Exception("the long-lived projectiles ended on the host before the flood was over (" + handles.Count(x => x.IsAlive) + " of " + living + ", " + (Time.realtimeSinceStartupAsDouble - madeAt) + " s after they were made); the scenario ran too long");
                if (manager.ObjectOverflows <= overflows0) throw new Exception("the flood of " + made + " projectiles never overflowed the queue (queued " + manager.ObjectRecordsQueued + ")");
                Mark(folder, "o6-flooded");
            }
            yield return Wait(() => Has("o6-flooded"), "flooded");
            yield return new WaitForSecondsRealtime(1f);
            if (!host && (projectiles.ReplicaCount != living || projectiles.Snapshot().Count != living)) throw new Exception("after the flood this client has " + projectiles.ReplicaCount + " copies / " + projectiles.Snapshot().Count + " projectiles, not " + living);
            PNCombatEvents.Observed -= o6Count; log.Append(name + " o6 combat tally: {" + string.Join(" | ", o6Tally.Select(x => x.Key + " x" + x.Value)) + "} startup={" + string.Join(" | ", StartupTally.Select(x => x.Key + " x" + x.Value)) + "} sent=" + manager.CombatEventsSent + "; ");
            log.Append(name + ": made=" + made + " overflows=" + (manager.ObjectOverflows - overflows0) + " baselinesSent=" + manager.ObjectBaselinesSent + " baselinesApplied=" + manager.ObjectBaselinesApplied + " bytes=" + (manager.ObjectBytesSent - bytes0) + " records=" + (manager.ObjectRecordsSent - records0) + " packets=" + manager.ObjectPacketsSent + "; ");
            if (host) { foreach (var handle in handles) handle.End(); Mark(folder, "o6-ended"); }
            yield return Wait(() => Has("o6-ended"), "o6 ended");
            if (!host) yield return Wait(() => projectiles.ReplicaCount == 0, "the copies end with the real ones", 15);
        }
    }
}
