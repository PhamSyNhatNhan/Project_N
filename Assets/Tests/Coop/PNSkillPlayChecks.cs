using System;
using System.Collections;
using System.IO;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEngine;

namespace ProjectN.Coop.Tests
{
    // Docs/SkillNetworkingAPlan.md S1-S2 over the real wire: a host and two clients play a roguelite run. Flags: --pn-skill-test=<folder> --pn-skill-client=<name> (Host role = host).
    // Checks: the owner receives its own `project_n:skill_owner` block; the OTHER client receives none (owner-only); the owner's Dash press travels as `project_n:skill_input`, the host casts it
    // (the dash charge is consumed on the host), and the owner learns it through the replica Ability and `skill_cast_result`; the other client sees no result for that entity.
    [DefaultExecutionOrder(-20000)]
    public sealed class PNSkillPlayChecks : MonoBehaviour
    {
        private string folder, name; private int checks; private bool failed;
        private static string Arg(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
        private void Awake() { if (Arg("--pn-skill-test=") != null) PNGameBootstrap.Config.Network.Port = 7797; }
        private void Mark(string value) { var path = Path.Combine(folder, value); File.WriteAllText(path + ".pending", "ok"); File.Move(path + ".pending", path); }
        private bool Has(string value) => File.Exists(Path.Combine(folder, value));
        private void Check(bool ok, string value) { if (!ok) throw new Exception(value); checks++; Debug.Log("SKILL PASS " + name + " " + value); }
        private void Fail(Exception error) { if (failed) return; failed = true; File.WriteAllText(Path.Combine(folder, name + ".txt"), "FAIL " + error); Debug.LogException(error); try { PNGameBootstrap.End(); } catch { } Application.Quit(1); }
        private IEnumerator Wait(Func<bool> ready, string what)
        {
            double until = Time.realtimeSinceStartupAsDouble + 60;
            while (!failed)
            {
                bool done = false;
                try { done = ready(); if (!done && Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("waiting for " + what + " checks=" + checks + " " + (NetworkManager.singleton as PNNetworkManager)?.LastFailure); } catch (Exception e) { Fail(e); }
                if (done || failed) yield break; yield return null;
            }
        }
        private PNPlayerMove[] Players() => FindObjectsByType<PNPlayerMove>().Where(x => x.GetComponent<EveEntityLoader>()?.IsReady == true).ToArray();
        private static string Diag(PNSkillRemoteInput r, PNPlayerMove e) { var sk = e.GetComponent<PNPlayerSkill>(); return "ack=" + r.AckedKey + " queued=" + r.QueuedCount + " sent=" + r.LastSentKey + " bound=" + r.IsBound + " refusals=" + string.Join(";", r.Refusals.Select(x => x.Key + ":" + x.Reason)) + " canInput=" + sk.CanInput + " ready=" + sk.IsReady + " auth=" + EveRegistrySession.Context.HasAuthority + " dashing=" + sk.IsDashing + " charges=" + sk.DashRecovery.Current + " voluntary=" + sk.Controls.IsAllowed(PNControlIds.VoluntaryMotion) + " activate=" + sk.CanActivateSkill(PNControlIds.Dash, EveId.Parse("base:player_dash")); }
        private PNPlayerMove Of(long owner) => Players().SingleOrDefault(x => x.GetComponent<EveEntityIdentity>().OwnerId == owner);

        private IEnumerator Start()
        {
            folder = Arg("--pn-skill-test="); if (folder == null) yield break;
            bool host = PNGameBootstrap.Config.Role == EveSessionRole.Host; name = host ? "host" : Arg("--pn-skill-client=") ?? "client1";
            try { PNGameBootstrap.OpenRoom(PNGameBootstrap.Config); } catch (Exception e) { Fail(e); yield break; }
            yield return Wait(() => NetworkClient.isConnected && NetworkClient.connection.isAuthenticated, "room connection"); if (failed) yield break;
            var manager = (PNNetworkManager)NetworkManager.singleton; Mark(name + "-lobby");
            yield return Wait(() => host ? Has("client1-lobby") && Has("client2-lobby") : Has("host-lobby"), "lobby"); if (failed) yield break;
            if (host) { yield return Wait(() => manager.CanBeginPlay, "begin play"); if (failed) yield break; try { PNGameBootstrap.BeginPlay("roguelite"); } catch (Exception e) { Fail(e); yield break; } }
            yield return Wait(() => manager.IsPlaying && Players().Length == 3 && (host || manager.ClientInputReady), "three players in play"); if (failed) yield break;
            long owner = host ? 0 : manager.RoomOwner; // the client's own owner id
            // peers learn each other's owner id from the files: the host writes none, clients write theirs
            if (!host) File.WriteAllText(Path.Combine(folder, name + ".owner"), owner.ToString());
            yield return Wait(() => host ? Has("client1.owner") && Has("client2.owner") : Has("client1.owner") && Has("client2.owner"), "owner ids"); if (failed) yield break;
            long o1 = long.Parse(File.ReadAllText(Path.Combine(folder, "client1.owner"))), o2 = long.Parse(File.ReadAllText(Path.Combine(folder, "client2.owner")));
            long mine = name == "client1" ? o1 : name == "client2" ? o2 : -1, other = name == "client1" ? o2 : name == "client2" ? o1 : -1;
            if (!host) { yield return Wait(() => manager.ClientInputReady && manager.Worlds.DefaultWorld != null && manager.ClientWorldReady(manager.Worlds.DefaultWorld.InstanceId), "client world ready"); if (failed) yield break; Mark(name + "-ready"); }
            if (host) { yield return Wait(() => Has("client1-ready") && Has("client2-ready"), "clients ready"); if (failed) yield break; yield return new WaitForSecondsRealtime(2f); }
            if (host)
            {
                try { var runs = PNGameBootstrap.Runtime.GetComponent<PNGameSession>().Runs; if (!runs.Begin(PNGameFlow.DefaultRun, PNLocalWorldContext.Current.World, 12345UL, out var why)) throw new Exception("run did not begin: " + why); }
                catch (Exception e) { Fail(e); yield break; }
                yield return Wait(() => PNGameBootstrap.Runtime.GetComponent<PNGameSession>().Runs.Phase == PNRunPhase.Playing, "the run to play"); if (failed) yield break;
                Mark("run-playing");
            }
            yield return Wait(() => Has("run-playing"), "run playing"); if (failed) yield break;
            PNPlayerMove e1 = null, e2 = null;
            yield return Wait(() => (e1 = Of(o1)) != null && (e2 = Of(o2)) != null && e1.GetComponent<PNSkillOwnerView>() != null && e2.GetComponent<PNSkillOwnerView>() != null, "both client entities with the skill modules: " + string.Join(" | ", Players().Select(x => x.GetComponent<EveEntityIdentity>().OwnerId + ":" + string.Join(",", x.GetComponents<Component>().Select(c => c.GetType().Name)))) + " owners " + o1 + "," + o2); if (failed) yield break;

            // ---- owner-only block ----
            if (!host)
            {
                var ownView = (name == "client1" ? e1 : e2).GetComponent<PNSkillOwnerView>();
                yield return Wait(() => ownView.AppliedCount > 0, "own skill_owner block"); if (failed) yield break;
                try { Check(ownView.Bindings.Count >= 1 && ownView.Bindings.ContainsKey(PNSkillSlots.Dash), "the owner holds its slot -> profile view with Dash (" + string.Join(",", ownView.Bindings.Keys) + ")"); Check((name == "client1" ? e1 : e2).GetComponent<PNSkill>().InputForwarder != null, "the owner's skill forwards input"); Mark(name + "-viewed"); } catch (Exception e) { Fail(e); yield break; }
            }
            yield return Wait(() => Has("client1-viewed") && Has("client2-viewed"), "both owners got their block"); if (failed) yield break;
            yield return new WaitForSecondsRealtime(1.5f);
            if (!host)
            {
                var otherView = (name == "client1" ? e2 : e1).GetComponent<PNSkillOwnerView>();
                try { Check(otherView.AppliedCount == 0 && otherView.AppliedRevision == 0, "the other client's skill_owner block never reached this client"); } catch (Exception e) { Fail(e); yield break; }
            }

            // ---- input over the wire: client1 dashes ----
            var hostSkill1 = e1.GetComponent<PNPlayerSkill>(); var remote1 = e1.GetComponent<PNSkillRemoteInput>();
            if (host)
            {
                try { Check(remote1.AckedKey == 0 && hostSkill1.DashRecovery.Current >= 1, "host starts with a full dash charge for client1"); Mark("host-ready-dash"); } catch (Exception e) { Fail(e); yield break; }
            }
            yield return Wait(() => Has("host-ready-dash"), "host ready"); if (failed) yield break;
            double charges = hostSkill1.DashRecovery.Current;
            if (name == "client1")
            {
                var skill = e1.GetComponent<PNSkill>(); bool a = false, b = false, c = false;
                try { a = skill.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); b = skill.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap)); c = skill.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap)); Check(a && b && c, "the client press is accepted for forwarding"); var ps = e1.GetComponent<PNPlayerSkill>(); Check(remote1.PredictedCount == 1 && ps.DashRecovery.Current < charges, "the client spent its Dash charge on the replica at once, before any answer (charges " + charges + " -> " + ps.DashRecovery.Current + ")"); } catch (Exception e) { Fail(e); yield break; }
                yield return Wait(() => remote1.AckedKey >= 3, "skill_cast_result for the dash: " + Diag(remote1, e1)); if (failed) yield break;
                yield return Wait(() => hostSkill1.DashRecovery.Current < charges || hostSkill1.DashRecovery.CooldownRemaining > 0, "the replica shows the spent dash charge"); if (failed) yield break;
                try { Check(remote1.Refusals.Count == 0 && remote1.RolledBackCount == 0 && remote1.PendingPredictions == 0, "the host refused nothing: the guess was confirmed, not rolled back (pending " + remote1.PendingPredictions + ", rolled back " + remote1.RolledBackCount + ")"); Mark("client1-dashed"); } catch (Exception e) { Fail(e); yield break; }
            }
            if (host)
            {
                yield return Wait(() => remote1.AckedKey >= 3, "host accepted client1's input: " + Diag(remote1, e1)); if (failed) yield break;
                yield return Wait(() => hostSkill1.DashRecovery.Current < charges || hostSkill1.DashRecovery.CooldownRemaining > 0, "host spends the dash charge: " + Diag(remote1, e1)); if (failed) yield break;
                try { Check(true, "the host cast client1's dash (charge " + charges + " -> " + hostSkill1.DashRecovery.Current + ")"); } catch (Exception e) { Fail(e); yield break; }
            }
            yield return Wait(() => Has("client1-dashed"), "client1 done"); if (failed) yield break;
            if (name == "client2")
            {
                yield return new WaitForSecondsRealtime(1f);
                try { Check(remote1.AckedKey == 0 && remote1.Refusals.Count == 0, "client2 sees no cast result of client1's entity (owner-only)"); } catch (Exception e) { Fail(e); yield break; }
            }
            // ---- a stale guess is declined on the host and taken back on the client ----
            if (host)
            {
                yield return Wait(() => Has("client1-image"), "client1 image"); if (failed) yield break;
                try { int n = 0; while (hostSkill1.DashRecovery.TryConsume()) n++; Check(hostSkill1.DashRecovery.Current < 1, "the host spent every Dash charge of client1 (" + n + ")"); Mark("host-drained"); } catch (Exception e) { Fail(e); yield break; }
            }
            if (name == "client1")
            {
                var ps = e1.GetComponent<PNPlayerSkill>(); var abil = e1.GetComponent<PNAbilityManager>().Abilities;
                yield return Wait(() => !ps.IsDashing && ps.DashRecovery.Current >= 1, "dash over, a charge back");
                if (failed) yield break;
                var image = abil.CaptureReplica().ToList(); Mark("client1-image");
                yield return Wait(() => Has("host-drained"), "host drained"); if (failed) yield break;
                yield return Wait(() => ps.DashRecovery.Current < 1, "the replica shows the drained charges"); if (failed) yield break;
                int rolled = remote1.RolledBackCount, predicted = remote1.PredictedCount;
                try
                {
                    abil.ApplyReplica(image); // pretend the image is stale: the client believes a charge is left
                    bool a = ps.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)), b = ps.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap)), c = ps.RequestSlot(PNSkillSlots.Dash, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                    Check(a && b && c && remote1.PredictedCount == predicted + 1 && ps.IsPredictingDash, "the client guesses a dash on its stale replica");
                }
                catch (Exception e) { Fail(e); yield break; }
                yield return Wait(() => remote1.RolledBackCount > rolled, "the host's refusal: " + Diag(remote1, e1)); if (failed) yield break;
                yield return Wait(() => ps.DashRecovery.Current < 1, "the true Abilities come back after the refusal"); if (failed) yield break;
                try { Check(remote1.Refusals.Any(r => r.Reason == "declined") && !ps.IsDashing && remote1.PendingPredictions == 0, "the declined guess is taken back: dash stopped, replica shows the host's truth (refusals " + string.Join(";", remote1.Refusals.Select(r => r.Key + ":" + r.Reason)) + ")"); Mark("client1-declined"); } catch (Exception e) { Fail(e); yield break; }
            }
            yield return Wait(() => Has("client1-declined"), "client1 declined"); if (failed) yield break;
            // ---- S4-S5 over the wire: a hit report and a decision reach the host, which checks and queues them ----
            if (name == "client1")
            {
                try
                {
                    long keyNow = remote1.LastSentKey; var other2 = e2.GetComponent<EveEntityIdentity>().Id;
                    Check(remote1.SendDecision(keyNow, "repeat_continue", new Newtonsoft.Json.Linq.JObject { ["v"] = 1 }) && remote1.SendHits(keyNow, 0, new[] { other2 }), "the client sends a decision and a hit report");
                    Mark("client1-reported");
                } catch (Exception e) { Fail(e); yield break; }
            }
            if (host)
            {
                yield return Wait(() => Has("client1-reported"), "client1 reported"); if (failed) yield break;
                yield return Wait(() => remote1.DecisionsTaken + remote1.DecisionsWaiting + remote1.DecisionsExpired >= 1 && remote1.HitsRejected + remote1.HitsApplied >= 1, "the host received the decision and the report"); if (failed) yield break;
                try { Check(remote1.HitsApplied == 0 && remote1.HitsRejected == 1, "the host refused a hit report while no sweep was running (applied " + remote1.HitsApplied + ", rejected " + remote1.HitsRejected + ")"); } catch (Exception e) { Fail(e); yield break; }
            }
            Mark(name + "-s45");
            yield return Wait(() => Has("client1-s45") && Has("client2-s45") && Has("host-s45"), "everyone past S4-S5"); if (failed) yield break;
            Mark(name + "-done");
            yield return Wait(() => Has("host-done") || host && Has("client1-done") && Has("client2-done"), "everyone done"); if (failed) yield break;
            if (host) { try { PNGameBootstrap.EndPlay(); } catch (Exception e) { Fail(e); yield break; } yield return new WaitForSecondsRealtime(1f); }
            File.WriteAllText(Path.Combine(folder, name + ".txt"), "PASS " + checks); Application.Quit();
        }
    }
}
