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
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("--pn-object-sync") || O4 || Refuse || NoGuess || O6;
        // O4 (character Eve): client1 casts Skill (three homing projectiles) near enemies; the shooter must see a guess before any answer and no second copy, the host must take the shooter's reports, client2 sees copies.
        private static bool O4 => Environment.GetCommandLineArgs().Contains("--pn-object-sync-o4");
        // --pn-object-sync-o4r: the host refuses the shooter's cast (a Control lock the client did not know yet): the guess must go and nothing must show or hurt anywhere.
        private static bool Refuse => Environment.GetCommandLineArgs().Contains("--pn-object-sync-o4r");
        // --pn-object-sync-o4n: the shooter's machine does not guess (hook): the host's real projectiles hit the enemies themselves and the shooter sees ordinary copies.
        private static bool NoGuess => Environment.GetCommandLineArgs().Contains("--pn-object-sync-o4n");
        // --pn-object-sync-o6: late observers and the queue: a fresh baseline of living objects, a flood that overflows the host's queue, and the bytes the channel cost.
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
            log.Append(name + ": made=" + made + " overflows=" + (manager.ObjectOverflows - overflows0) + " baselinesSent=" + manager.ObjectBaselinesSent + " baselinesApplied=" + manager.ObjectBaselinesApplied + " bytes=" + (manager.ObjectBytesSent - bytes0) + " records=" + (manager.ObjectRecordsSent - records0) + " packets=" + manager.ObjectPacketsSent + "; ");
            if (host) { foreach (var handle in handles) handle.End(); Mark(folder, "o6-ended"); }
            yield return Wait(() => Has("o6-ended"), "o6 ended");
            if (!host) yield return Wait(() => projectiles.ReplicaCount == 0, "the copies end with the real ones", 15);
        }
    }
}
