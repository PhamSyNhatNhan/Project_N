#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/SkillNetworkingAPlan.md S3 for Dash and cooldown slots, Edit Mode, no Mirror: (1) the owner's CLIENT runs its Dash at once and spends the charge on its replica; a refusal takes both back, a
// release the authority acknowledged confirms the guess, a pause from the authority stops new guesses; (2) the AUTHORITY tells a press that cast from one that was declined and publishes the pause.
public static class EveSkillPredictionChecks
{
    private sealed class Wire : IPNSyncTransport
    {
        public readonly List<JObject> Sent = new List<JObject>();
        public void SendState(PNSyncTarget target, PNSyncStateDefinition state, ulong revision, JToken payload) { }
        public void SendCommand(PNSyncTarget target, PNSyncCommandDefinition command, JToken payload) { Sent.Add((JObject)payload); }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Skill prediction check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5; var target = PNSyncTarget.ForEntity(new EveEntityId(995030), 1);

        (GameObject root, EveClientRoleProbeSkill skill) Eve(Scene preview, string name)
        {
            var area = new GameObject("S3 area manager"); SceneManager.MoveGameObjectToScene(area, preview); area.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, preview); obj.layer = PNLayers.Player;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(995030), EveId.Parse("project_n:eve"), owner); obj.AddComponent<EveCharacterStat>(); obj.AddComponent<PNAbilityManager>();
            var body = obj.AddComponent<Rigidbody>(); body.useGravity = false; var cap = obj.AddComponent<CapsuleCollider>(); cap.radius = .12f; cap.height = .6f;
            obj.AddComponent<PNPlayerMove>(); var s = obj.AddComponent<EveClientRoleProbeSkill>(); s.Prepare(); var l = obj.AddComponent<EveEntityLoader>(); l.Load(hub); obj.SetActive(true); return (obj, s);
        }
        void Cleanup(Scene preview)
        {
            try
            {
                foreach (var r in preview.GetRootGameObjects()) if (r != null) foreach (var l in r.GetComponentsInChildren<EveEntityLoader>(true)) { try { l.Unload(); } catch (Exception) { } }
                foreach (var r in preview.GetRootGameObjects()) if (r != null) r.SetActive(false);
            }
            finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(preview); } }
        }
        JObject Input(long key, EveId slot, PNSkillInputPhase phase, EveId profile)
            => new JObject { ["k"] = key, ["s"] = slot.ToString(), ["p"] = (int)phase, ["i"] = 0, ["f"] = profile.ToString() };

        // ---- 1) the authority: declined presses and the pause ----
        var hostScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:s3_authority"));
            var (root, skill) = Eve(hostScene, "S3 host Eve"); root.GetComponent<PNAbilityManager>().enabled = false;
            var service = new PNSyncService(hub, true, 0, EveRegistrySession.Context);
            var view = root.AddComponent<PNSkillOwnerView>(); var remote = root.AddComponent<PNSkillRemoteInput>();
            service.RegisterObject(target, skill); view.OnBound(service, target); remote.OnBound(service, target);
            Check(skill.AcceptsRemoteInput, "the authority accepts a remote owner's input once bound");
            skill.Bindings.TryGet(PNSkillSlots.Dash, out var dashProfile);
            void Tap(long start) { foreach (var phase in new[] { PNSkillInputPhase.Started, PNSkillInputPhase.Performed, PNSkillInputPhase.Canceled }) service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(start++, PNSkillSlots.Dash, phase, dashProfile)); }

            long serial = skill.ActivationSerial; Tap(1);
            Check(skill.ActivationSerial == serial + 1 && !remote.Refusals.Any() && remote.AckedKey == 3, "a Dash press with charges casts: one activation, acknowledged, nothing refused (serial " + serial + " -> " + skill.ActivationSerial + ")");
            skill.TryInterrupt(EveId.Parse("test:end_dash"));
            while (skill.DashRecovery.TryConsume()) { }
            Check(skill.DashRecovery.Current == 0, "the Dash charges are spent");
            serial = skill.ActivationSerial; Tap(4);
            Check(skill.ActivationSerial == serial && remote.AckedKey == 6 && remote.Refusals.Any(r => r.Key == 4 && r.Reason == "declined"), "a Dash press with no charge is delivered but declined, and the refusal names its Started key (refusals " + string.Join(";", remote.Refusals.Select(r => r.Key + ":" + r.Reason)) + ")");

            remote.RefreshPause(); Check(!remote.IsPausing, "a healthy owner is not asked to pause");
            skill.GetComponent<PNStat>().SetCurrent(PNContentIds.Health, 0); remote.RefreshPause();
            Check(remote.IsPausing, "a dead owner is asked to pause predictions");
            service.Flush(); service.TryGetLatest(target, PNSkillRemoteInput.ResultState, out _, out var result);
            Check(result != null && (int)result["p"] == 1, "the pause is published in the result block");
            remote.OnUnbound(); view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(hostScene); }

        // ---- 2) the owner's client guesses ----
        var clientScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:s3_client"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            var (root, skill) = Eve(clientScene, "S3 client Eve");
            var wire = new Wire(); var service = new PNSyncService(hub, false, owner, EveRegistrySession.Context) { Transport = wire };
            var view = root.AddComponent<PNSkillOwnerView>(); var remote = root.AddComponent<PNSkillRemoteInput>();
            service.RegisterObject(target, skill); view.OnBound(service, target); remote.OnBound(service, target);
            var bindings = new JObject(); foreach (var slot in PNSkillSlots.Player) bindings[slot.ToString()] = "project_n:eve_" + slot.ToString().Substring(slot.ToString().IndexOf("slot_", StringComparison.Ordinal) + 5);
            service.ReceiveState(target, PNSkillOwnerView.State, 1, 1, new JObject { ["a"] = new JArray(), ["b"] = bindings });
            double charges = skill.DashRecovery.Current;
            Check(charges >= 2 && skill.CanInput, "the client Eve may take input and holds Dash charges (" + charges + ")");

            // Dash is a press-only slot: the input layer delivers Started and Performed together. Returns the key of the press (its Started event).
            long DashTap() { skill.Press(PNSkillSlots.Dash, PNSkillInputPhase.Started); long pressKey = remote.LastSentKey; skill.Press(PNSkillSlots.Dash, PNSkillInputPhase.Performed); return pressKey; }
            // the gesture is guessed at once, through the same binding path as the authority's
            Check(DashTap() == 1, "the press is accepted for forwarding");
            Check(skill.IsDashing && skill.IsPredictingDash && skill.DashRecovery.Current == charges - 1, "the client dashes at once and spends one charge on its replica (charges " + skill.DashRecovery.Current + ")");
            Check(remote.PendingPredictions == 1 && remote.PredictedCount == 1 && wire.Sent.Count == 2, "one guess is waiting for the answer and both events went out");
            Check(skill.CurrentInvocation != null && skill.CurrentInvocation.Mode == PNSkillMode.Predicted && skill.ActivationSerial == 0, "the guess is a Predicted invocation and committed no activation (the authority's cast is the real one)");

            // a refusal takes the guess back
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 2, new JObject { ["ack"] = 1, ["p"] = 0, ["rej"] = new JArray((object)new JArray(1, "declined")) });
            Check(!skill.IsDashing && skill.DashRecovery.Current == charges && remote.PendingPredictions == 0 && remote.RolledBackCount == 1, "the refusal stops the dash and gives the charge back (dashing " + skill.IsDashing + ", charges " + skill.DashRecovery.Current + ")");
            skill.Release(PNSkillSlots.Dash);

            // a pause stops new guesses
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 3, new JObject { ["ack"] = 2, ["p"] = 1, ["rej"] = new JArray() });
            Check(remote.PredictionPaused, "the client reads the pause");
            DashTap();
            Check(!skill.IsDashing && skill.DashRecovery.Current == charges && remote.PendingPredictions == 0 && wire.Sent.Count == 5, "while paused the press still goes out but is not guessed");
            skill.Release(PNSkillSlots.Dash);
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 4, new JObject { ["ack"] = 4, ["p"] = 0, ["rej"] = new JArray() });
            Check(!remote.PredictionPaused, "the pause ends");

            // an acknowledged release confirms the guess
            long before = remote.LastSentKey; long startKey = DashTap(); skill.Release(PNSkillSlots.Dash);
            Check(skill.IsDashing && remote.PendingPredictions == 1, "a new press is guessed again (pending " + remote.PendingPredictions + ")");
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 5, new JObject { ["ack"] = startKey + 2, ["p"] = 0, ["rej"] = new JArray() });
            Check(remote.PendingPredictions == 0 && remote.RolledBackCount == 1 && skill.IsDashing && skill.DashRecovery.Current == charges - 1, "the acknowledged release confirms the guess: nothing rolled back, the charge stays spent");
            skill.TryInterrupt(EveId.Parse("project_n:test"));

            // G6b2: a newer image under a waiting guess does not undo what it shows; the refusal then gives back what the IMAGE held, and an image under an ANSWERED guess is simply the truth
            Action ViewEvent(string name) => (Action)typeof(PNSkillOwnerView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
            var abilities = root.GetComponent<PNAbilityManager>().Abilities; var image = abilities.CaptureReplica().ToList(); double c0 = skill.DashRecovery.Current;
            long key2 = DashTap();
            Check(skill.DashRecovery.Current == c0 - 1, "a fresh guess spends a charge (" + skill.DashRecovery.Current + " of " + c0 + ")");
            ViewEvent("Applying")(); abilities.ApplyReplica(image); Check(skill.DashRecovery.Current == c0, "the stale image puts the charge back");
            ViewEvent("Applied")();
            Check(skill.DashRecovery.Current == c0 - 1, "the waiting guess is shown on top of the image again (" + skill.DashRecovery.Current + ")");
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 6, new JObject { ["ack"] = key2, ["p"] = 0, ["rej"] = new JArray((object)new JArray(key2, "declined")) });
            Check(skill.DashRecovery.Current == c0 && !skill.IsDashing, "the refusal then restores the charge the image held");
            // a guess the authority already took is not laid over an image: the image is the truth
            long key3 = DashTap(); skill.Release(PNSkillSlots.Dash);
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 7, new JObject { ["ack"] = key3 + 1, ["p"] = 0, ["rej"] = new JArray() });
            ViewEvent("Applying")(); abilities.ApplyReplica(image); ViewEvent("Applied")();
            Check(skill.DashRecovery.Current == c0, "an image after the authority took the press stands as it is (" + skill.DashRecovery.Current + ")");
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 8, new JObject { ["ack"] = key3 + 2, ["p"] = 0, ["rej"] = new JArray() });
            skill.TryInterrupt(EveId.Parse("project_n:test"));
            Check(remote.PendingPredictions == 0, "the release is answered and the press is settled");
            // a guess nobody answers is taken back
            skill.Release(PNSkillSlots.Dash); DashTap(); skill.Release(PNSkillSlots.Dash);
            int rolled = remote.RolledBackCount; remote.ExpireGuesses(UnityEngine.Time.unscaledTime + 1f);
            Check(remote.RolledBackCount == rolled && remote.PendingPredictions == 1, "a young guess is kept");
            remote.ExpireGuesses(UnityEngine.Time.unscaledTime + PNSkillRemoteInput.GuessTimeoutReleased + 1f);
            Check(remote.RolledBackCount == rolled + 1 && remote.PendingPredictions == 0 && skill.DashRecovery.Current == c0 && !skill.IsDashing, "an unanswered guess times out and is taken back");

            remote.OnUnbound(); view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(clientScene); }
        return "PASS " + n + " skill prediction checks (declined press, pause, client dash guess, rollback, pause, confirm)";
    }
}
#endif
