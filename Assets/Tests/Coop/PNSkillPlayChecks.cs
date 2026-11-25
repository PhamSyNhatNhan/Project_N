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
        private void Awake() { if (Arg("--pn-skill-test=") != null) { PNGameBootstrap.Config.Network.Port = 7797; var character = Arg("--pn-skill-character="); if (!string.IsNullOrEmpty(character)) PNPlayerProfileService.SetLastCharacter(character); } }
        private void Mark(string value) { var path = Path.Combine(folder, value); File.WriteAllText(path + ".pending", "ok"); File.Move(path + ".pending", path); }
        private bool Has(string value) => File.Exists(Path.Combine(folder, value));
        private void Check(bool ok, string value) { if (!ok) throw new Exception(value); checks++; Debug.Log("SKILL PASS " + name + " " + value); }
        private void Fail(Exception error) { if (failed) return; failed = true; File.WriteAllText(Path.Combine(folder, name + ".txt"), "FAIL " + error); Debug.LogException(error); try { PNGameBootstrap.End(); } catch { } Application.Quit(1); }
        private IEnumerator Wait(Func<bool> ready, string what, Func<string> detail = null)
        {
            double until = Time.realtimeSinceStartupAsDouble + 60;
            while (!failed)
            {
                bool done = false;
                try { done = ready(); if (!done && Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("waiting for " + what + " checks=" + checks + " " + (detail != null ? detail() + " " : "") + (NetworkManager.singleton as PNNetworkManager)?.LastFailure); } catch (Exception e) { Fail(e); }
                if (done || failed) yield break; yield return null;
            }
        }
        // The game camera's picture rendered into a texture (works with a minimized or hidden window, where the screen itself is not drawn).
        private static Texture2D RenderFrame(Transform subject, Transform focus)
        {
            var cam = Camera.main; if (cam == null) cam = Camera.allCameras.Where(c => c.enabled).OrderByDescending(c => c.depth).FirstOrDefault();
            if (cam == null) throw new Exception("no camera");
            var rt = RenderTexture.GetTemporary(960, 540, 24); var previous = cam.targetTexture; var active = RenderTexture.active; var home = cam.transform.position;
            // The game camera follows this machine's own player: move it over the observed Eve and bring it closer so the picture shows what the observer sees of that Eve.
            var shift = subject.position - focus.position; shift.y = 0; cam.transform.position = home + shift; var distance = Vector3.Distance(cam.transform.position, subject.position); cam.transform.position += cam.transform.forward * distance * .6f;
            try
            {
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply(); return texture;
            }
            finally { cam.transform.position = home; cam.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); }
        }
        // Review mode (`--pn-skill-review`, set by runskill.ps1 -Visible): client1 repeats its casts slowly, one at a time with a pause, so a person watching client2's window can see each one.
        private IEnumerator ReviewCasts(EveCharacterSkill eve, PNSkillRemoteInput remote)
        {
            var steps = new[] { PNSkillSlots.Attack, PNSkillSlots.Attack, PNSkillSlots.Attack, PNSkillSlots.Dash, PNSkillSlots.Skill, PNSkillSlots.Ultimate, PNSkillSlots.Attack, PNSkillSlots.Dash };
            foreach (var slot in steps)
            {
                bool ult = slot.Equals(PNSkillSlots.Ultimate), skill = slot.Equals(PNSkillSlots.Skill);
                yield return Wait(() => !eve.IsBusy && !eve.IsDashing && (ult ? eve.UltimateRecovery.Current >= 1 : !skill || eve.SkillRecovery.Current >= 1), "Eve idle and charged for the review " + slot); if (failed) yield break;
                try
                {
                    eve.RequestSlot(slot, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); eve.RequestSlot(slot, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap)); eve.RequestSlot(slot, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                } catch (Exception e) { Fail(e); yield break; }
                long sent = remote.LastSentKey;
                yield return Wait(() => remote.AckedKey >= sent && !eve.IsBusy, "the review " + slot + " answered"); if (failed) yield break;
                yield return new WaitForSecondsRealtime(2f);
            }
        }
        // Review mode: client2's own body is kept beside client1's Eve, so the window that follows client2 always shows the observed Eve.
        private IEnumerator FollowSubject(Transform subject, Transform self)
        {
            while (!failed && subject != null && self != null)
            {
                var offset = new Vector3(-1.6f, 0f, 0f); var target = subject.position + offset; target.y = self.position.y;
                self.position = target; Physics.SyncTransforms(); yield return null;
            }
        }
        // Keeps one enemy where client1's machine was told to see it (replication would put it back between frames).
        private IEnumerator PinEnemy(PNStat enemy, Transform around, Func<bool> active)
        {
            while (active() && enemy != null && around != null) { enemy.transform.position = around.position + new Vector3(2.4f, 0, 0); Physics.SyncTransforms(); yield return null; }
        }
        // Review frames (G8): while client2 observes client1's Eve it saves what the screen shows (the observed slash arc, the energy trail, Burst) as PNG next to the markers, for the user's eyes.
        private IEnumerator ObserveShots(EveCharacterSkill seen, Transform localFocus)
        {
            var dir = Path.Combine(folder, "shots"); Directory.CreateDirectory(dir);
            int slash = 0, motion = 0, burst = 0; float nextBurst = 0f;
            while (!failed)
            {
                string label = null;
                if (slash < 14 && (seen.ActionViewsRunning > 0 || seen.GetComponentInChildren<EveSweepVisual>() != null))
                    label = "slash_" + (++slash);
                else if (seen.MotionTrailActive && motion < 10) label = "motion_" + (++motion);
                else if (seen.HasLook(EveCharacterSkill.BurstLook) && burst < 4 && Time.realtimeSinceStartup >= nextBurst) { label = "burst_" + (++burst); nextBurst = Time.realtimeSinceStartup + .6f; }
                if (label != null)
                {
                    yield return new WaitForEndOfFrame();
                    Texture2D texture = null;
                    try { texture = RenderFrame(seen.transform, localFocus); File.WriteAllBytes(Path.Combine(dir, name + "_" + label + ".png"), texture.EncodeToPNG()); }
                    catch (Exception error) { Debug.LogWarning("review frame " + label + " failed: " + error.Message); }
                    finally { if (texture != null) Destroy(texture); }
                }
                yield return null;
            }
        }
        private PNPlayerMove[] Players() => FindObjectsByType<PNPlayerMove>().Where(x => x.GetComponent<EveEntityLoader>()?.IsReady == true).ToArray();
        private static string Diag(PNSkillRemoteInput r, PNPlayerMove e) { var sk = e.GetComponent<PNPlayerSkill>(); return "ack=" + r.AckedKey + " queued=" + r.QueuedCount + " sent=" + r.LastSentKey + " bound=" + r.IsBound + " refusals=" + string.Join(";", r.Refusals.Select(x => x.Key + ":" + x.Reason)) + " canInput=" + sk.CanInput + " ready=" + sk.IsReady + " auth=" + EveRegistrySession.Context.HasAuthority + " dashing=" + sk.IsDashing + " charges=" + sk.DashRecovery.Current + " voluntary=" + sk.Controls.IsAllowed(PNControlIds.VoluntaryMotion) + " activate=" + sk.CanActivateSkill(PNControlIds.Dash, EveId.Parse("base:player_dash")); }
        private PNPlayerMove Of(long owner) => Players().SingleOrDefault(x => x.GetComponent<EveEntityIdentity>().OwnerId == owner);

        private IEnumerator Start()
        {
            folder = Arg("--pn-skill-test="); if (folder == null) yield break;
            bool host = PNGameBootstrap.Config.Role == EveSessionRole.Host; name = host ? "host" : Arg("--pn-skill-client=") ?? "client1";
            if(Environment.GetCommandLineArgs().Contains("--pn-action-delivery-without-service"))PNGameBootstrap.Config.Network.ActionReplication=false;
            try { PNGameBootstrap.OpenRoom(PNGameBootstrap.Config); } catch (Exception e) { Fail(e); yield break; }
            yield return Wait(() => NetworkClient.isConnected && NetworkClient.connection.isAuthenticated, "room connection"); if (failed) yield break;
            var manager = (PNNetworkManager)NetworkManager.singleton; Mark(name + "-lobby");
            if (Environment.GetCommandLineArgs().Contains("--pn-late-join")) { yield return LateJoin(host, manager); yield break; }
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

            yield return Wait(() => PNTimeService.For(EveRegistrySession.Context).Sample().Ready, "session timeline ready"); if (failed) yield break;
            try
            {
                var timeline = PNTimeService.For(EveRegistrySession.Context);
                var sample = timeline.Sample();
                Check(sample.Epoch == (host ? EveRegistrySession.Context.SessionId : EveRegistrySession.Context.Admission.AdmittedHostSession), "timeline epoch matches admitted play");
                Check(Math.Abs(sample.Presentation - (host ? NetworkTime.localTime : Math.Max(0,NetworkTime.unscaledTime-manager.Profile.MoveInterpolationSeconds))) < .01, "one unscaled presentation buffer");
            }
            catch (Exception e) { Fail(e); yield break; }

            if (PNActionDeliveryPlayChecks.Enabled) { yield return PNActionDeliveryPlayChecks.Run(folder, name, e1); yield break; }
            if (PNActionTimePlayChecks.Enabled) { yield return PNActionTimePlayChecks.Run(folder, name, e1); yield break; }
            if (PNBuffViewPlayChecks.Enabled) { yield return PNBuffViewPlayChecks.Run(folder, name, e1); yield break; }
            if (PNObjectSyncPlayChecks.Enabled) { yield return PNObjectSyncPlayChecks.Run(folder, name, e1); yield break; }
            if (PNAbilityTimePlayChecks.Enabled) { yield return PNAbilityTimePlayChecks.Run(folder, name, e1); yield break; }
            if (PNTimeBaselineChecks.Enabled) { yield return PNTimeBaselineChecks.Run(folder, name, e1); yield break; }
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
                // The ack of the last key can overtake the cast result of the press on a lossy link: the guess is confirmed a moment later, so wait for it before judging.
                yield return Wait(() => remote1.PendingPredictions == 0 || remote1.RolledBackCount > 0, "the dash guess is confirmed or rolled back: " + Diag(remote1, e1)); if (failed) yield break;
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
                yield return Wait(() => !ps.IsDashing && ps.DashRecovery.Current >= 1, "dash over, a charge back", () => Diag(remote1, e1) + " predicting=" + ps.IsPredictingDash + " cooldown=" + ps.DashRecovery.CooldownRemaining);
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
            // ---- Eve only: her Attack is guessed by the owner's client and cast by the host ----
            var eveSkill = e1.GetComponent<EveCharacterSkill>();
            if (eveSkill != null && name == "client2") { StartCoroutine(ObserveShots(eveSkill, e2.transform)); if (Arg("--pn-skill-review") != null) StartCoroutine(FollowSubject(e1.transform, e2.transform)); }
            if (eveSkill != null)
            {
                if (name == "client1")
                {
                    yield return Wait(() => !eveSkill.IsBusy && !eveSkill.IsDashing, "Eve idle"); if (failed) yield break;
                    int rolledBefore = remote1.RolledBackCount, guessedBefore = remote1.PredictedCount, refusedBefore = remote1.Refusals.Count;
                    bool a1 = false, b1 = false, c1 = false;
                    try
                    {
                        a1 = eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap));
                        b1 = eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap));
                        Check(a1 && b1 && remote1.PredictedCount == guessedBefore + 1 && eveSkill.IsPredictingCast, "the client starts its Attack slash when the tap performs, as the host does (guesses " + (remote1.PredictedCount - guessedBefore) + ", predicting " + eveSkill.IsPredictingCast + ")");
                        c1 = eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                    } catch (Exception e) { Fail(e); yield break; }
                    long sentNow = remote1.LastSentKey;
                    yield return Wait(() => remote1.AckedKey >= sentNow && !eveSkill.IsBusy, "the host answered the Attack and the guess ended: " + Diag(remote1, e1)); if (failed) yield break;
                    yield return new WaitForSecondsRealtime(1f);
                    try { Check(remote1.RolledBackCount == rolledBefore && remote1.PendingPredictions == 0 && !remote1.Refusals.Any(r => r.Key > sentNow - 3), "the host cast the same Attack: nothing rolled back (rolled back " + (remote1.RolledBackCount - rolledBefore) + ")"); Mark("client1-attacked"); } catch (Exception e) { Fail(e); yield break; }
                    // Skill, then Ultimate when it is charged: each guessed at once, answered by the host, nothing taken back
                    foreach (var slot in new[] { PNSkillSlots.Skill, PNSkillSlots.Ultimate })
                    {
                        bool isUlt = slot.Equals(PNSkillSlots.Ultimate);
                        yield return Wait(() => !eveSkill.IsBusy && !eveSkill.IsDashing && (isUlt ? eveSkill.UltimateRecovery.Current >= 1 : eveSkill.SkillRecovery.Current >= 1), "Eve idle and charged for " + slot); if (failed) yield break;
                        int guessed0 = remote1.PredictedCount, rolled0 = remote1.RolledBackCount;
                        try
                        {
                            eveSkill.RequestSlot(slot, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap));
                            Check(eveSkill.RequestSlot(slot, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap)) && remote1.PredictedCount == guessed0 + 1 && eveSkill.IsPredictingCast, "the client starts " + slot + " when the tap performs (cast " + eveSkill.CurrentCast + ")");
                            eveSkill.RequestSlot(slot, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                        } catch (Exception e) { Fail(e); yield break; }
                        long sent2 = remote1.LastSentKey;
                        yield return Wait(() => remote1.AckedKey >= sent2 && !eveSkill.IsBusy, slot + " answered and finished: " + Diag(remote1, e1)); if (failed) yield break;
                        yield return new WaitForSecondsRealtime(.5f);
                        try { Check(remote1.RolledBackCount == rolled0 && remote1.PendingPredictions == 0, "the host cast the same " + slot + ": nothing rolled back (" + (remote1.RolledBackCount - rolled0) + ")"); } catch (Exception e) { Fail(e); yield break; }
                    }
                    Mark("client1-cast");
                }
                if (host)
                {
                    yield return Wait(() => Has("client1-attacked"), "client1 attacked"); if (failed) yield break;
                    try { Check(eveSkill.ComboIndex >= 1, "the host ran Eve's Attack too (combo step " + eveSkill.ComboIndex + ")"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("client1-cast"), "client1 cast Skill and Ultimate"); if (failed) yield break;
                // ---- a real hit: the host pulls a room enemy next to client1's Eve, the client's sweep reports it, the host damages it ----
                PNStat prey = null; double preyHealth = 0; int hitsBefore = host ? remote1.HitsApplied + remote1.HitsRejected : 0;
                if (host)
                {
                    try
                    {
                        var world = PNLocalWorldContext.Current.World; var at0 = e1.transform.position + new Vector3(.45f, 0, 0);
                        var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "skilltest:enemy", EntityKey = "", Position = at0 };
                        if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused the test enemy");
                    } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => (prey = FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).FirstOrDefault(x => x.IsReady)) != null, "the test enemy to be published"); if (failed) yield break;
                    try { preyHealth = prey.GetCurrent(PNContentIds.Health); var at = e1.transform.position + new Vector3(.45f, 0, 0); prey.transform.position = at; Physics.SyncTransforms(); Mark("prey-ready"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("prey-ready"), "prey placed"); if (failed) yield break;
                if (name == "client1")
                {
                    PNStat seen = null;
                    yield return Wait(() => (seen = FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).FirstOrDefault(x => (x.transform.position - e1.transform.position).sqrMagnitude < 1.5f * 1.5f)) != null, "the enemy shows near the client's Eve on its own machine"); if (failed) yield break;
                    yield return Wait(() => !eveSkill.IsBusy && !eveSkill.IsDashing, "Eve idle before the hit"); if (failed) yield break;
                    long sentHit = 0;
                    try { eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap)); eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap)); sentHit = remote1.LastSentKey; } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.AckedKey >= sentHit && !eveSkill.IsBusy, "the host answered the hitting Attack: " + Diag(remote1, e1)); if (failed) yield break;
                    Mark("client1-hit");
                }
                if (host)
                {
                    yield return Wait(() => Has("client1-hit"), "client1 swung at the enemy"); if (failed) yield break;
                    yield return Wait(() => prey == null || !prey.IsReady || prey.GetCurrent(PNContentIds.Health) < preyHealth, "the enemy lost health from the client's reported hit (reports received " + (remote1.HitsApplied + remote1.HitsRejected - hitsBefore) + ")"); if (failed) yield break;
                    try { Check(remote1.HitsApplied + remote1.HitsRejected > hitsBefore, "the host received the client's hit report (applied " + remote1.HitsApplied + ", dropped " + remote1.HitsRejected + "; the host's own sweep may have hit first)"); Mark("prey-hit"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("prey-hit"), "host verified the hit"); if (failed) yield break;
                // ---- S6: the OTHER client sees client1's slashes drawn on its copy of Eve, and nothing is cast or hit there ----
                if (name == "client2")
                {
                    var seenEve = e1.GetComponent<EveCharacterSkill>();
                    yield return Wait(() => seenEve.ObservedPresentationsStarted >= 1, "client2 draws client1's slashes"); if (failed) yield break;
                    try { Check(!seenEve.IsPredictingCast && !seenEve.IsBusy && seenEve.CurrentInvocation == null, "the observed Eve runs no cast on client2 (busy " + seenEve.IsBusy + ")"); Mark("client2-saw"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("client2-saw"), "client2 saw the slashes"); if (failed) yield break;
                // ---- Burst, then a held Attack: the Repeat chain is guessed by the owner and cast by the host ----
                if (host) { try { eveSkill.BurstGauge.Add(100000); Mark("gauge-full"); } catch (Exception e) { Fail(e); yield break; } }
                yield return Wait(() => Has("gauge-full"), "the host filled the Burst gauge"); if (failed) yield break;
                if (name == "client1")
                {
                    yield return Wait(() => eveSkill.BurstGauge.Current >= eveSkill.Moveset.BurstCost && !eveSkill.IsBusy && !eveSkill.IsDashing, "the owner's replica shows a full gauge"); if (failed) yield break;
                    int rolled1 = remote1.RolledBackCount; long sent3 = 0;
                    try
                    {
                        eveSkill.RequestSlot(PNSkillSlots.Burst, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); eveSkill.RequestSlot(PNSkillSlots.Burst, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap)); eveSkill.RequestSlot(PNSkillSlots.Burst, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                        Check(eveSkill.IsInBurst, "Burst shows on the owner's client at once"); sent3 = remote1.LastSentKey;
                    } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.AckedKey >= sent3 && eveSkill.SlashStacks.Current > 0, "the host answered Burst and the stacks arrived"); if (failed) yield break;
                    try { eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Repeat)); eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Repeat)); } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => eveSkill.IsPredictingCast && eveSkill.CurrentCast == EveCast.Repeat, "the held Attack is guessed as a Repeat stroke on the owner's client"); if (failed) yield break;
                    yield return new WaitForSecondsRealtime(1.5f);
                    long sent4 = 0;
                    try { eveSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Repeat)); sent4 = remote1.LastSentKey; } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.AckedKey >= sent4 && !eveSkill.IsBusy, "the chain ended after the release: " + Diag(remote1, e1)); if (failed) yield break;
                    yield return new WaitForSecondsRealtime(.5f);
                    try { Check(remote1.RolledBackCount == rolled1 && remote1.PendingPredictions == 0, "the host cast the Repeat chain: nothing rolled back (" + (remote1.RolledBackCount - rolled1) + ")"); Mark("client1-repeated"); } catch (Exception e) { Fail(e); yield break; }
                }
                if (host)
                {
                    yield return Wait(() => eveSkill.CurrentCast == EveCast.Repeat || Has("client1-repeated"), "the host ran the Repeat chain (cast " + eveSkill.CurrentCast + ")"); if (failed) yield break;
                    try { Check(eveSkill.IsInBurst || Has("client1-repeated"), "the host's Eve is in Burst"); } catch (Exception e) { Fail(e); yield break; }
                }
                if (name == "client2")
                {
                    yield return Wait(() => eveSkill.HasLook(EveCharacterSkill.BurstLook), "client2 sees client1's Burst look"); if (failed) yield break;
                    try { Check(eveSkill.HasLook(EveCharacterSkill.BurstLook), "client2 holds client1's Burst look"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("client1-repeated"), "client1 repeated"); if (failed) yield break;
                if (Arg("--pn-skill-review") != null)
                {
                    if (name == "client1") { yield return ReviewCasts(eveSkill, remote1); if (failed) yield break; Mark("review-done"); }
                    yield return Wait(() => Has("review-done"), "the slow review casts"); if (failed) yield break;
                }
            }

            // ---- G7, a character that is not Eve (Eve 2): the pet in the Skill slot is guessed by the owner's client and summoned by the host, with no network code in the character ----
            var petSkill = e1.GetComponent<PNEve2Skill>();
            if (eveSkill == null && petSkill != null)
            {
                if (name == "client1")
                {
                    var petAbilities = e1.GetComponent<PNAbilityManager>().Abilities; long petSent = 0;
                    int PetCharges() { foreach (var item in petAbilities.CaptureReplica()) if (item.Id == PNSummonGameplayContent.Eve2PetReady) return item.Amount; return -1; }
                    yield return Wait(() => PetCharges() >= 1, "client1 has its pet ready", () => "charges " + PetCharges()); if (failed) yield break;
                    try
                    {
                        int guessedPet = remote1.PredictedCount; int rolledPet = remote1.RolledBackCount;
                        bool pressed = petSkill.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); petSent = remote1.LastSentKey;
                        Check(pressed && remote1.PredictedCount == guessedPet + 1 && PetCharges() < 1, "client1 guesses the pet and shows its cooldown spent at once (guesses " + (remote1.PredictedCount - guessedPet) + ", charges " + PetCharges() + ")");
                        petSkill.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap)); petSent = remote1.LastSentKey;
                    }
                    catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.AckedKey >= petSent && remote1.PendingPredictions == 0, "the host answered the pet press: " + Diag(remote1, e1)); if (failed) yield break;
                    try { Check(PetCharges() < 1 || e1.GetComponent<PNAbilityManager>().Abilities.CaptureReplica().Any(x => x.Id == PNSummonGameplayContent.Eve2PetReady && x.CooldownRemaining > 0), "the confirmed guess stays spent (the host spent it too)"); Mark("client1-pet"); } catch (Exception e) { Fail(e); yield break; }
                }
                if (host)
                {
                    yield return Wait(() => Has("client1-pet"), "client1 pet"); if (failed) yield break;
                    var summoner = e1.GetComponent<EveEntityIdentity>().Id;
                    yield return Wait(() => PNSummons.Count(summoner, "eve2_pet") >= 1, "the host summoned client1's pet"); if (failed) yield break;
                    try { Check(true, "the host created the pet client1's guess announced"); } catch (Exception e) { Fail(e); yield break; }
                }
            }
            // ---- G7, the trial slash (Attack slot of Eve 2): the owner's client guesses it and proposes its target, the host chooses/accepts it and deals the damage; no network code in the character ----
            if (eveSkill == null && petSkill != null)
            {
                PNStat slashNear = null, slashFar = null; double nearHealth = 0, farHealth = 0;
                if (host)
                {
                    try
                    {
                        var world = PNLocalWorldContext.Current.World;
                        for (int i = 0; i < 2; i++)
                        {
                            var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "skilltest:slash_enemy" + i, EntityKey = "", Position = e1.transform.position + new Vector3(1.0f + .8f * i, 0, 0) };
                            if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused a slash test enemy");
                        }
                    } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).Count(x => x.IsReady) >= 2, "two slash test enemies to be published"); if (failed) yield break;
                    try
                    {
                        // The host's own nearest enemy is `slashNear` (1.0 m); `slashFar` is at 1.8 m. Client1 will see them the other way round (see below), so only client1's proposal can make `slashFar` the one hit.
                        var ready = FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).Where(x => x.IsReady).Take(2).ToArray(); slashNear = ready[0]; slashFar = ready[1];
                        slashNear.transform.position = e1.transform.position + new Vector3(1.0f, 0, 0); slashFar.transform.position = e1.transform.position + new Vector3(1.8f, 0, 0); Physics.SyncTransforms();
                        File.WriteAllText(Path.Combine(folder,"slash-near-id"),slashNear.GetComponent<EveEntityIdentity>().Id.Value.ToString());
                        nearHealth = slashNear.GetCurrent(PNContentIds.Health); farHealth = slashFar.GetCurrent(PNContentIds.Health); Mark("slash-prey-ready");
                    } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("slash-prey-ready"), "slash enemy placed"); if (failed) yield break;
                if (name == "client1")
                {
                    yield return Wait(() => FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).Count(x => (x.transform.position - e1.transform.position).sqrMagnitude < 2.5f * 2.5f) >= 2, "both enemies show near the client's Eve on its own machine"); if (failed) yield break;
                    // On client1's own machine the nearer enemy is moved behind the other one, so ITS nearest enemy is the host's far one: a different choice from the host's own.
                    var nearId = long.Parse(File.ReadAllText(Path.Combine(folder,"slash-near-id")));
                    var seenNear = FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).Single(x => x.GetComponent<EveEntityIdentity>().Id.Value == nearId);
                    bool pinning = true; StartCoroutine(PinEnemy(seenNear, e1.transform, () => pinning));
                    yield return new WaitForSeconds(.4f);
                    seenNear.transform.position = e1.transform.position + new Vector3(2.4f, 0, 0); Physics.SyncTransforms(); yield return new WaitForSeconds(.25f);
                    try { var nowNearest = FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).OrderBy(x => (x.transform.position - e1.transform.position).sqrMagnitude).First(); Check(!ReferenceEquals(nowNearest, seenNear), "on client1's machine the enemy it moved is no longer the nearest (nearest at " + (nowNearest.transform.position - e1.transform.position).magnitude.ToString("F2") + " m, moved one at " + (seenNear.transform.position - e1.transform.position).magnitude.ToString("F2") + " m)"); } catch (Exception e) { Fail(e); yield break; }
                    var slashAbilities = e1.GetComponent<PNAbilityManager>().Abilities; long slashSent = 0;
                    int SlashCharges() { foreach (var item in slashAbilities.CaptureReplica()) if (item.Id == PNSummonGameplayContent.Eve2SlashReady) return item.Amount; return -1; }
                    yield return Wait(() => SlashCharges() >= 1, "client1 has its slash ready", () => "charges " + SlashCharges()); if (failed) yield break;
                    try
                    {
                        seenNear.transform.position = e1.transform.position + new Vector3(2.4f,0,0); Physics.SyncTransforms();
                        Check(!ReferenceEquals(FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).OrderBy(x => (x.transform.position-e1.transform.position).sqrMagnitude).First(),seenNear),"host-identified near enemy is not nearest at the press");
                        int guessedSlash = remote1.PredictedCount; int rolledSlash = remote1.RolledBackCount;
                        bool pressed = petSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); slashSent = remote1.LastSentKey;
                        Check(pressed && remote1.PredictedCount == guessedSlash + 1 && SlashCharges() < 1, "client1 guesses the slash and shows its cooldown spent at once (guesses " + (remote1.PredictedCount - guessedSlash) + ", charges " + SlashCharges() + ")");
                        petSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
                    }
                    catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.AckedKey >= slashSent && remote1.PendingPredictions == 0, "the host answered the slash: " + Diag(remote1, e1)); if (failed) yield break;
                    pinning = false;
                    try { Check(remote1.RolledBackCount == 0 || !remote1.Refusals.Any(r => r.Key >= slashSent), "the host cast the same slash (no refusal for it)"); Mark("client1-slashed"); } catch (Exception e) { Fail(e); yield break; }
                }
                if (host)
                {
                    yield return Wait(() => Has("client1-slashed"), "client1 slashed"); if (failed) yield break;
                    yield return Wait(() => slashFar == null || !slashFar.IsReady || slashFar.GetCurrent(PNContentIds.Health) < farHealth || slashNear.GetCurrent(PNContentIds.Health) < nearHealth, "an enemy lost health from client1's slash"); if (failed) yield break;
                    try { Check(slashFar.GetCurrent(PNContentIds.Health) < farHealth && slashNear.GetCurrent(PNContentIds.Health) == nearHealth, "the host hit the enemy client1 chose (its far one), not the one the host would have chosen (decisions taken " + remote1.DecisionsTaken + " waiting " + remote1.DecisionsWaiting + " expired " + remote1.DecisionsExpired + "; near " + slashNear.GetCurrent(PNContentIds.Health) + "/" + nearHealth + ", far " + slashFar.GetCurrent(PNContentIds.Health) + "/" + farHealth + ")"); Mark("slash-hit"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("slash-hit"), "host verified the slash"); if (failed) yield break;
            }
            // ---- Gura G1: the four-strike Attack. The owner's client guesses each strike (its body steps toward the enemy as a guess), the host casts the same strike and deals the damage; no network code in the character ----
            var guraSkill = e1.GetComponent<PNGuraSkill>();
            if (guraSkill != null)
            {
                PNStat guraPrey = null; double guraHealth = 0; Vector3 guraStart = Vector3.zero;
                if (host)
                {
                    try
                    {
                        var world = PNLocalWorldContext.Current.World;
                        var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = "project_n:dummy_lorica", World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "skilltest:gura_enemy", EntityKey = "", Position = e1.transform.position + new Vector3(2.0f, 0, 0) };
                        if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused the Gura test enemy");
                    } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).Any(x => x.IsReady), "the Gura test enemy is published"); if (failed) yield break;
                    try
                    {
                        guraPrey = FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).First(x => x.IsReady);
                        guraPrey.transform.position = e1.transform.position + new Vector3(2.0f, 0, 0); Physics.SyncTransforms();
                        guraHealth = guraPrey.GetCurrent(PNContentIds.Health); guraStart = e1.transform.position; Mark("gura-prey-ready");
                    } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("gura-prey-ready"), "the Gura enemy placed"); if (failed) yield break;
                if (name == "client1")
                {
                    yield return Wait(() => FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).Any(x => (x.transform.position - e1.transform.position).sqrMagnitude < 3.2f * 3.2f), "the enemy shows near client1's Gura"); if (failed) yield break;
                    long guraSent = 0; var before = e1.transform.position; float guraBest = 0f, guraGap3 = -1f, guraGap4 = -1f; string guraTrace = "";
                    for (int strike = 0; strike < 4; strike++)
                    {
                        yield return Wait(() => guraSkill.CurrentInvocation == null && remote1.PendingPredictions == 0, "Gura idle before strike " + (strike + 1)); if (failed) yield break;
                        try
                        {
                            int guessed = remote1.PredictedCount;
                            bool pressed = guraSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); guraSent = remote1.LastSentKey;
                            Check(pressed && remote1.PredictedCount == guessed + 1, "client1 guesses strike " + (strike + 1) + " (guesses " + (remote1.PredictedCount - guessed) + ")");
                            guraSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap)); guraSent = remote1.LastSentKey;
                        } catch (Exception e) { Fail(e); yield break; }
                        yield return Wait(() => remote1.AckedKey >= guraSent && remote1.PendingPredictions == 0 && guraSkill.CurrentInvocation == null, "the host answered strike " + (strike + 1) + ": " + Diag(remote1, e1)); if (failed) yield break;
                        guraBest = Mathf.Max(guraBest, (e1.transform.position - before).x); var guraEnemy = FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).Where(x => x.IsReady).OrderBy(x => (x.transform.position - e1.transform.position).sqrMagnitude).FirstOrDefault(); if (guraEnemy != null) { var flat = guraEnemy.transform.position - e1.transform.position; flat.y = 0; if (strike == 2) guraGap3 = flat.magnitude; if (strike == 3) guraGap4 = flat.magnitude; } guraTrace += " " + (e1.transform.position - before).x.ToString("F2") + "(rb" + remote1.RolledBackCount + " ref" + remote1.Refusals.Count + " nul" + GuraAttackBehavior.NoTarget + " lost" + GuraAttackBehavior.Cancels + " enemy" + FindObjectsByType<PNEnemyStat>(FindObjectsSortMode.None).Where(x => x.IsReady).Select(x => (x.transform.position - e1.transform.position).x.ToString("F2")).FirstOrDefault() + ")";
                    }
                    try { Check(guraGap3 >= 0f && guraGap3 < .9f && Mathf.Abs(guraGap4 - 1.2f) < .35f, "client1 closed on the enemy to about 0.6 m by strike 3 and stood 1.2 m away after strike 4 (gaps " + guraGap3.ToString("F2") + " and " + guraGap4.ToString("F2") + "; " + guraTrace + ")"); Mark("client1-gura"); } catch (Exception e) { Fail(e); yield break; }
                }
                if (host)
                {
                    yield return Wait(() => Has("client1-gura"), "client1 struck"); if (failed) yield break;
                    yield return Wait(() => guraPrey == null || !guraPrey.IsReady || guraPrey.GetCurrent(PNContentIds.Health) < guraHealth, "the enemy lost health to client1's strikes"); if (failed) yield break;
                    try { Check(GuraAttackBehavior.Casts >= 4, "the host cast the four strikes (" + GuraAttackBehavior.Casts + ") and sees client1's Gura moved " + (e1.transform.position - guraStart).x.ToString("F2") + " m"); Mark("gura-hit"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("gura-hit") || !host && Has("client1-gura"), "the host verified the Gura strikes"); if (failed) yield break;
                // ---- Gura G2: the Skill slot's trident (homing projectile, blast where it ends). The host alone makes the projectile and the blast; client1 guesses the cast ----
                double guraAfter = host && guraPrey != null && guraPrey.IsReady ? guraPrey.GetCurrent(PNContentIds.Health) : 0;
                if (name == "client1")
                {
                    yield return Wait(() => guraSkill.CurrentInvocation == null && remote1.PendingPredictions == 0, "Gura idle before the trident"); if (failed) yield break;
                    try
                    {
                        int guessed = remote1.PredictedCount;
                        bool pressed = guraSkill.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); long sent = remote1.LastSentKey;
                        Check(pressed && remote1.PredictedCount == guessed + 1, "client1 guesses the trident cast");
                        guraSkill.RequestSlot(PNSkillSlots.Skill, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap)); Mark("gura-trident-sent");
                    } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.PendingPredictions == 0 && guraSkill.CurrentInvocation == null, "the host answered the trident: " + Diag(remote1, e1)); if (failed) yield break;
                }
                if (host)
                {
                    yield return Wait(() => Has("gura-trident-sent"), "client1 threw the trident"); if (failed) yield break;
                    yield return Wait(() => GuraCastBehavior.Casts >= 1 && (guraPrey == null || !guraPrey.IsReady || guraPrey.GetCurrent(PNContentIds.Health) < guraAfter), "the trident's blast hurt the enemy (casts " + GuraCastBehavior.Casts + ")"); if (failed) yield break;
                    Mark("gura-trident-hit");
                }
                yield return Wait(() => Has("gura-trident-hit"), "the host verified the trident"); if (failed) yield break;
                // ---- Gura G2: Ultimate tap (shark) and hold (dive, released into the shark) ----
                if (name == "client1")
                {
                    yield return Wait(() => guraSkill.CurrentInvocation == null && remote1.PendingPredictions == 0, "Gura idle before the Ultimate"); if (failed) yield break;
                    try
                    {
                        int guessed = remote1.PredictedCount;
                        guraSkill.RequestSlot(PNSkillSlots.Ultimate, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); bool pressed = guraSkill.RequestSlot(PNSkillSlots.Ultimate, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap));
                        Check(pressed && remote1.PredictedCount == guessed + 1, "client1 guesses the Ultimate shark"); Mark("gura-ult-tap");
                    } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.PendingPredictions == 0 && guraSkill.CurrentInvocation == null, "the host answered the Ultimate: " + Diag(remote1, e1)); if (failed) yield break;
                }
                if (host)
                {
                    yield return Wait(() => Has("gura-ult-tap") && GuraUltimateBehavior.Casts >= 1, "the host cast the Ultimate shark (" + GuraUltimateBehavior.Casts + ")"); if (failed) yield break;
                    yield return Wait(() => guraSkill.Abilities.GetCounter(PNGuraContent.BurstStack).Current >= 15, "the shark's hit gave BurstStack (" + guraSkill.Abilities.GetCounter(PNGuraContent.BurstStack).Current + ")"); if (failed) yield break;
                    try { guraSkill.Abilities.GetCooldown(PNGuraContent.UltimateReady).ResetToFull(); Mark("gura-ult-reset"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("gura-ult-reset"), "the Ultimate cooldown reset"); if (failed) yield break;
                if (name == "client1")
                {
                    yield return Wait(() => guraSkill.CurrentInvocation == null && remote1.PendingPredictions == 0 && !guraSkill.Abilities.GetCooldown(PNGuraContent.UltimateReady).IsRecovering, "Gura ready for the dive"); if (failed) yield break;
                    try { guraSkill.RequestSlot(PNSkillSlots.Ultimate, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Hold)); guraSkill.RequestSlot(PNSkillSlots.Ultimate, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Hold)); Mark("gura-dive-sent"); } catch (Exception e) { Fail(e); yield break; }
                    yield return new WaitForSeconds(.8f);
                    try { guraSkill.RequestSlot(PNSkillSlots.Ultimate, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Hold)); } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.PendingPredictions == 0 && guraSkill.CurrentInvocation == null, "the host answered the dive: " + Diag(remote1, e1)); if (failed) yield break;
                }
                if (host)
                {
                    yield return Wait(() => Has("gura-dive-sent") && e1.GetComponent<PNBuffManager>().Buffs.Snapshot().Any(x => x.Id.Equals(EveId.Parse("project_n:gura_untouchable"))), "Gura is untouchable while she dives"); if (failed) yield break;
                    try
                    {
                        var diver = e1.GetComponent<PNStat>(); double beforeHit = diver.GetCurrent(PNContentIds.Health); guraPrey.SendDamage(diver, 50d, EveId.Parse("base:physical"));
                        Check(diver.GetCurrent(PNContentIds.Health) >= beforeHit - 1e-9, "a real hit on the diving Gura did nothing (" + beforeHit + " -> " + diver.GetCurrent(PNContentIds.Health) + ")");
                    } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => !e1.GetComponent<PNBuffManager>().Buffs.Snapshot().Any(x => x.Id.Equals(EveId.Parse("project_n:gura_untouchable"))), "the dive took the immunity back"); if (failed) yield break;
                    yield return Wait(() => GuraUltimateBehavior.Dives >= 1 && GuraUltimateBehavior.Casts >= 2, "the host dived and called the shark (dives " + GuraUltimateBehavior.Dives + ", casts " + GuraUltimateBehavior.Casts + ")"); if (failed) yield break;
                    // G3: fill the Burst gauge and the Fangs on the authority; the client sees them replicate.
                    try { guraSkill.Abilities.GetCounter(PNGuraContent.BurstStack).SetAmount(150); guraSkill.Abilities.GetCounter(PNGuraContent.FangStack).SetAmount(3); Mark("gura-g3-ready"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("gura-g3-ready"), "the Burst gauge filled"); if (failed) yield break;
                if (name == "client1")
                {
                    yield return Wait(() => guraSkill.CurrentInvocation == null && remote1.PendingPredictions == 0 && guraSkill.Abilities.GetCounter(PNGuraContent.BurstStack).Current >= 125, "client1 sees the gauge full"); if (failed) yield break;
                    try { guraSkill.RequestSlot(PNSkillSlots.Burst, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); Mark("gura-burst-sent"); } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.PendingPredictions == 0 && guraSkill.CurrentInvocation == null, "the host answered the Burst: " + Diag(remote1, e1)); if (failed) yield break;
                }
                if (host)
                {
                    yield return Wait(() => Has("gura-burst-sent") && GuraBurstBehavior.Casts >= 1 && GuraResources.InBurst(guraSkill.Abilities), "the host started the Burst (casts " + GuraBurstBehavior.Casts + ")"); if (failed) yield break;
                    try { Check(guraSkill.Abilities.GetCounter(PNGuraContent.WaveStack).Current >= 10 && guraSkill.Abilities.GetCounter(PNGuraContent.BurstStack).Current <= 25, "the Burst cost 125 and gave 10 Wave (" + guraSkill.Abilities.GetCounter(PNGuraContent.WaveStack).Current + " Wave, " + guraSkill.Abilities.GetCounter(PNGuraContent.BurstStack).Current + " BurstStack)"); } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => Resources.FindObjectsOfTypeAll<PNGuraTideArea>().Any(x => x.IsRunning), "the tide area runs"); if (failed) yield break;
                    yield return Wait(() => e1.GetComponent<PNBuffManager>().Buffs.Snapshot().Any(x => x.Id.Equals(EveId.Parse("project_n:gura_blessing"))), "Gura stands in her own tide and wears the blessing"); if (failed) yield break;
                    Mark("gura-burst-ok");
                }
                yield return Wait(() => Has("gura-burst-ok"), "the host verified the Burst"); if (failed) yield break;
                // ---- Gura G3: inside her own tide a normal attack heals 5% of max health (the blessing's OnHit heal) ----
                double guraLow = 0;
                if (host)
                {
                    try
                    {
                        var self = e1.GetComponent<PNStat>(); guraLow = self.Evaluate(PNContentIds.Health) * .3; self.SetCurrent(PNContentIds.Health, guraLow); Mark("gura-heal-ready");
                    } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("gura-heal-ready"), "Gura's health lowered"); if (failed) yield break;
                if (name == "client1")
                {
                    yield return Wait(() => guraSkill.CurrentInvocation == null && remote1.PendingPredictions == 0, "Gura idle before the healing strike"); if (failed) yield break;
                    try { guraSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); guraSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap)); Mark("gura-heal-sent"); } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.PendingPredictions == 0 && guraSkill.CurrentInvocation == null, "the host answered the healing strike: " + Diag(remote1, e1)); if (failed) yield break;
                }
                if (host)
                {
                    var self = e1.GetComponent<PNStat>();
                    yield return Wait(() => Has("gura-heal-sent") && self.GetCurrent(PNContentIds.Health) > guraLow + 1e-6, "the blessing healed Gura (" + self.GetCurrent(PNContentIds.Health) + " from " + guraLow + ")"); if (failed) yield break;
                    Mark("gura-heal-ok");
                }
                yield return Wait(() => Has("gura-heal-ok"), "the host verified the heal"); if (failed) yield break;
                // ---- Gura G4: the held attack inside Burst spends Wave; the milestones give Fangs (the chain) ----
                if (host)
                {
                    try
                    {
                        // The milestone bonuses reach melee_scale (the size of her strikes): +0.2 at 6, +0.5 in all at 12.
                        var self = e1.GetComponent<PNStat>(); guraSkill.Abilities.GrantState(PNGuraContent.Chain6);
                        double scale6 = PNCombatSource.Capture(self).Snapshot.Calculate(1d, PNContentIds.MeleeScale); guraSkill.Abilities.GrantState(PNGuraContent.Chain12);
                        double scale12 = PNCombatSource.Capture(self).Snapshot.Calculate(1d, PNContentIds.MeleeScale);
                        GuraResources.End(guraSkill.Abilities, PNGuraContent.Chain6); GuraResources.End(guraSkill.Abilities, PNGuraContent.Chain12);
                        Check(Mathf.Abs((float)scale6 - 1.2f) < .01f && Mathf.Abs((float)scale12 - 1.5f) < .01f, "the chain bonus scales her strikes (melee_scale " + scale6.ToString("F2") + " at 6, " + scale12.ToString("F2") + " at 12)");
                    } catch (Exception e) { Fail(e); yield break; }
                    try { guraSkill.Abilities.GetCounter(PNGuraContent.WaveStack).SetAmount(18); guraSkill.Abilities.GetCounter(PNGuraContent.FangStack).SetAmount(0); Mark("gura-chain-ready"); } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("gura-chain-ready"), "Wave filled"); if (failed) yield break;
                if (name == "client1")
                {
                    yield return Wait(() => guraSkill.CurrentInvocation == null && remote1.PendingPredictions == 0 && guraSkill.Abilities.GetCounter(PNGuraContent.WaveStack).Current == 18, "client1 sees the Wave"); if (failed) yield break;
                    try { guraSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Repeat)); } catch (Exception e) { Fail(e); yield break; }
                    for (int hold = 0; hold < 60; hold++)
                    {
                        try { guraSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Repeat)); } catch (Exception e) { Fail(e); yield break; }
                        yield return new WaitForSeconds(.1f);
                    }
                    try { guraSkill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Repeat)); Mark("gura-chain-sent"); } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.PendingPredictions == 0 && guraSkill.CurrentInvocation == null, "the host answered the chain: " + Diag(remote1, e1)); if (failed) yield break;
                }
                if (host)
                {
                    yield return Wait(() => Has("gura-chain-sent"), "client1 held the attack"); if (failed) yield break;
                    yield return new WaitForSeconds(.5f);
                    try
                    {
                        int waveLeft = guraSkill.Abilities.GetCounter(PNGuraContent.WaveStack).Current, fang = guraSkill.Abilities.GetCounter(PNGuraContent.FangStack).Current;
                        Check(waveLeft < 18 && fang >= 6, "the held attack spent Wave through all three milestones, the last one the free Skill Burst (" + waveLeft + " Wave left, " + fang + " Fang)");
                        guraSkill.Abilities.GetCounter(PNGuraContent.FangStack).SetAmount(3); guraSkill.Abilities.GrantState(PNGuraContent.BurstState); Mark("gura-chain-ok");
                    } catch (Exception e) { Fail(e); yield break; }
                }
                yield return Wait(() => Has("gura-chain-ok"), "the host verified the chain"); if (failed) yield break;
                // ---- Gura G4: the Ultimate inside Burst ends it and spends every Fang ----
                if (name == "client1")
                {
                    yield return Wait(() => guraSkill.CurrentInvocation == null && remote1.PendingPredictions == 0 && GuraResources.InBurst(guraSkill.Abilities) && guraSkill.Abilities.GetCounter(PNGuraContent.FangStack).Current == 3, "client1 sees the Burst and the Fangs"); if (failed) yield break;
                    try { guraSkill.RequestSlot(PNSkillSlots.Ultimate, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap)); guraSkill.RequestSlot(PNSkillSlots.Ultimate, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Tap)); Mark("gura-fang-sent"); } catch (Exception e) { Fail(e); yield break; }
                    yield return Wait(() => remote1.PendingPredictions == 0 && guraSkill.CurrentInvocation == null, "the host answered the Burst Ultimate: " + Diag(remote1, e1)); if (failed) yield break;
                }
                if (host)
                {
                    yield return Wait(() => Has("gura-fang-sent") && GuraUltimateBehavior.BurstCasts >= 1, "the host cast the Burst Ultimate (" + GuraUltimateBehavior.BurstCasts + ")"); if (failed) yield break;
                    yield return Wait(() => !GuraResources.InBurst(guraSkill.Abilities) && guraSkill.Abilities.GetCounter(PNGuraContent.FangStack).Current == 0, "the Burst ended and the Fangs were spent (" + guraSkill.Abilities.GetCounter(PNGuraContent.FangStack).Current + " Fang, burst " + GuraResources.InBurst(guraSkill.Abilities) + ")"); if (failed) yield break; Mark("gura-fang-ok");
                }
                yield return Wait(() => Has("gura-fang-ok"), "the host verified the Burst Ultimate"); if (failed) yield break;
                yield return new WaitForSeconds(3f); // the Fang projectiles and the shark blast finish before the next scenario counts hit reports
            }
            int baseApplied = host ? remote1.HitsApplied : 0, baseRejected = host ? remote1.HitsRejected : 0;
            if (host) Mark("host-base");
            // ---- S4-S5 over the wire: a hit report and a decision reach the host, which checks and queues them ----
            if (name == "client1")
            {
                yield return Wait(() => Has("host-base"), "the host took its report baseline"); if (failed) yield break;
            }
            if (name == "client1")
            {
                try
                {
                    long keyNow = PNSkillCastKey.FromInput(remote1.LastSentKey); var other2 = e2.GetComponent<EveEntityIdentity>().Id;
                    Check(remote1.SendDecision(keyNow, "repeat_continue", new Newtonsoft.Json.Linq.JObject { ["v"] = 1 }) && remote1.SendHits(keyNow, 0, new[] { other2 }), "the client sends a decision and a hit report");
                    Mark("client1-reported");
                } catch (Exception e) { Fail(e); yield break; }
            }
            if (host)
            {
                yield return Wait(() => Has("client1-reported"), "client1 reported"); if (failed) yield break;
                yield return Wait(() => remote1.DecisionsTaken + remote1.DecisionsWaiting + remote1.DecisionsExpired >= 1 && remote1.HitsRejected + remote1.HitsApplied >= baseApplied + baseRejected + 1, "the host received the decision and the report (decisions " + remote1.DecisionsTaken + "/" + remote1.DecisionsWaiting + "/" + remote1.DecisionsExpired + ", hits applied " + remote1.HitsApplied + " rejected " + remote1.HitsRejected + ", base " + baseApplied + "/" + baseRejected + ")"); if (failed) yield break;
                try { Check(remote1.HitsApplied == baseApplied && remote1.HitsRejected == baseRejected + 1, "the host refused a hit report while no sweep was running (applied " + (remote1.HitsApplied - baseApplied) + ", rejected " + (remote1.HitsRejected - baseRejected) + ")"); } catch (Exception e) { Fail(e); yield break; }
            }
            Mark(name + "-s45");
            yield return Wait(() => Has("client1-s45") && Has("client2-s45") && Has("host-s45"), "everyone past S4-S5"); if (failed) yield break;
            Mark(name + "-done");
            yield return Wait(() => Has("host-done") || host && Has("client1-done") && Has("client2-done"), "everyone done"); if (failed) yield break;
            if (host) { try { PNGameBootstrap.EndPlay(); } catch (Exception e) { Fail(e); yield break; } yield return new WaitForSecondsRealtime(1f); }
            File.WriteAllText(Path.Combine(folder, name + ".txt"), "PASS " + checks); Application.Quit();
        }

        // PV9: a real late joiner. The host and client1 play; enemies are already casting when client2 connects (`runskill.ps1 -LateClient -Extra --pn-late-join`).
        // Client2 must draw what is under way from where it is (seek) and take no failure; the observers' view counters say how.
        private IEnumerator LateJoin(bool host, PNNetworkManager manager)
        {
            var characters = new[] { "project_n:starkeeper", "project_n:starforger", "project_n:starreaper", "project_n:starweaver", "project_n:starchanter", "project_n:starherald" };
            var shots = Path.Combine(folder, "shots"); Directory.CreateDirectory(shots);
            if (host)
            {
                yield return Wait(() => Has("client1-lobby") && Has("client2-lobby"), "client lobbies"); if (failed) yield break;
                yield return Wait(() => manager.CanBeginPlay, "begin play"); if (failed) yield break;
                try { PNGameBootstrap.BeginPlay("roguelite"); } catch (Exception e) { Fail(e); yield break; }
                yield return Wait(() => manager.IsPlaying && Players().Length >= 3, "three players in play"); if (failed) yield break;
                yield return Wait(() => Has("client1-ready") && Has("client2-ready"), "clients ready"); if (failed) yield break;
                try { var runs = PNGameBootstrap.Runtime.GetComponent<PNGameSession>().Runs; if (!runs.Begin(PNGameFlow.DefaultRun, PNLocalWorldContext.Current.World, 12345UL, out var why)) throw new Exception("run did not begin: " + why); }
                catch (Exception e) { Fail(e); yield break; }
                yield return Wait(() => PNGameBootstrap.Runtime.GetComponent<PNGameSession>().Runs.Phase == PNRunPhase.Playing, "the run to play"); if (failed) yield break;
                var any = Players().First();
                try
                {
                    var world = PNLocalWorldContext.Current.World;
                    for (int i = 0; i < characters.Length; i++)
                    {
                        var request = new PNEntityRequest { Entity = "project_n:enemy", Template = "project_n:enemy", Character = characters[i], World = world, WorldIncarnation = world.Incarnation, ContentSlot = PNGridWorldController.SlotId, SceneKey = "late:" + i, EntityKey = "", Position = any.transform.position + new Vector3(3f + 1.5f * (i % 3), 0, (i / 3) * 3f) };
                        if (PNEntityHosts.For(EveRegistrySession.Context).TrySpawn(request, null, null, error => Debug.LogException(error)) == null) throw new Exception("the host refused " + characters[i]);
                    }
                }
                catch (Exception e) { Fail(e); yield break; }
                yield return new WaitForSecondsRealtime(8f);                 // the enemies are mid-cast when client2 starts to draw
                Mark("late-go");
                yield return Wait(() => Has("client2-in"), "client2 joined"); if (failed) yield break;
                yield return new WaitForSecondsRealtime(20f);
                long published = 0;
                foreach (var s in FindObjectsByType<PNSkill>()) if (s != null && s.gameObject.layer == PNLayers.Enemy) published += s.ActionRunsPublished;
                Mark("host-observed");
                yield return Wait(() => Has("client1-reported") && Has("client2-reported"), "clients reported"); if (failed) yield break;
                try { PNGameBootstrap.EndPlay(); } catch (Exception e) { Fail(e); yield break; }
                yield return new WaitForSecondsRealtime(1f);
                File.WriteAllText(Path.Combine(folder, name + ".txt"), "PASS " + checks + " host published " + published + " enemy runs"); Application.Quit();
                yield break;
            }
            yield return Wait(() => Has("host-lobby"), "host lobby"); if (failed) yield break;
            bool late = name == "client2";
            yield return Wait(() => manager.IsPlaying && manager.ClientInputReady && manager.Worlds.DefaultWorld != null && manager.ClientWorldReady(manager.Worlds.DefaultWorld.InstanceId), "client world ready", () => "playing=" + manager.IsPlaying + " input=" + manager.ClientInputReady + " world=" + (manager.Worlds.DefaultWorld != null) + " connected=" + NetworkClient.isConnected + " auth=" + (NetworkClient.connection != null && NetworkClient.connection.isAuthenticated) + " ready=" + NetworkClient.ready + " players=" + Players().Length); if (failed) yield break;
            Mark(name + "-ready");
            if (late)
            {
                // client2 draws nothing until the host says the enemies are mid-cast: what it then meets is a run already under way (seek), through the real stream.
                while (!Has("late-go")) { foreach (var s in FindObjectsByType<PNSkill>()) if (s != null && s.gameObject.layer == PNLayers.Enemy) s.SetObservedActionPresentation(false); yield return null; }
                foreach (var s in FindObjectsByType<PNSkill>()) if (s != null && s.gameObject.layer == PNLayers.Enemy) s.SetObservedActionPresentation(true);
                Mark("client2-in");
            }
            double joined = Time.realtimeSinceStartupAsDouble, until = joined + 24; double firstDraw = -1; int shotsTaken = 0, failuresMax = 0;
            while (Time.realtimeSinceStartupAsDouble < until && !Has("host-observed"))
            {
                int viewed = 0, failures = 0; PNSkill drawn = null;
                foreach (var s in FindObjectsByType<PNSkill>()) if (s != null && s.gameObject.layer == PNLayers.Enemy) { viewed += s.ViewedBySkill.Values.Sum(); failures += (int)s.ActionViewFailures; if (drawn == null && s.ViewedBySkill.Count > 0) drawn = s; }
                failuresMax = Math.Max(failuresMax, failures);
                if (viewed > 0 && firstDraw < 0) firstDraw = Time.realtimeSinceStartupAsDouble - joined;
                if (name == "client2" && drawn != null && shotsTaken < 2 && Time.realtimeSinceStartupAsDouble - joined > .3 + shotsTaken * 1.5)
                {
                    try { var mine = Players().FirstOrDefault(p => p.GetComponent<EveEntityIdentity>()?.OwnerId == manager.RoomOwner); var texture = RenderFrame(drawn.transform, mine != null ? mine.transform : drawn.transform); File.WriteAllBytes(Path.Combine(shots, "client2_late_" + shotsTaken + ".png"), texture.EncodeToPNG()); Destroy(texture); } catch (Exception e) { Debug.LogException(e); }
                    shotsTaken++;
                }
                yield return null;
            }
            int totalViewed = 0, totalLate = 0; var lateKeys = new System.Collections.Generic.List<string>(); var viewedKeys = new System.Collections.Generic.HashSet<string>();
            foreach (var s in FindObjectsByType<PNSkill>()) if (s != null && s.gameObject.layer == PNLayers.Enemy) { totalViewed += s.ViewedBySkill.Values.Sum(); totalLate += s.ViewedLate; lateKeys.AddRange(s.ViewedLateKeys); foreach (var k in s.ViewedKeys) viewedKeys.Add(k); }
            try
            {
                Check(failuresMax == 0, "no view failed (" + failuresMax + ")");
                Check(totalViewed >= 1, name + " drew the enemies' runs (" + totalViewed + ")");
                if (name == "client2") Check(firstDraw >= 0 && firstDraw < 6, "client2 drew a run " + firstDraw.ToString("0.00") + " s after it was in play");
            }
            catch (Exception e) { Fail(e); yield break; }
            File.WriteAllText(Path.Combine(folder, name + "-late.txt"), name + ": viewed=" + totalViewed + " distinct=" + viewedKeys.Count + " late=" + totalLate + " [" + string.Join(",", lateKeys.Take(12)) + "] firstDraw=" + firstDraw.ToString("0.00") + "s shots=" + shotsTaken + " failures=" + failuresMax);
            Mark(name + "-reported");
            yield return Wait(() => Has("host-observed"), "host done"); if (failed) yield break;
            yield return new WaitForSecondsRealtime(2f);
            File.WriteAllText(Path.Combine(folder, name + ".txt"), "PASS " + checks + " " + File.ReadAllText(Path.Combine(folder, name + "-late.txt"))); Application.Quit();
        }
    }
}
