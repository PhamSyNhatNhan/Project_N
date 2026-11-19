using System;
using System.Collections;
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
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("--pn-object-sync") || O4 || Refuse || NoGuess || O6 || Enemy || Melee || Quiet || O5 || O5b;
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
            if (Melee) { yield return MeleeBody(folder, name, e1, log); yield break; }
            if (O5) { yield return O5Body(folder, name, e1, log); yield break; }
            if (O5b) { yield return O5bBody(folder, name, e1, log); yield break; }
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
                for (int i = 0; i < 3; i++)
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
            log.Append(name + ": real=" + real + " guess=" + guess + " copy=" + copy + " keys=" + keys.Count + "; ");
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
