#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/SkillNetworkingAPlan.md S2 on a real Eve, Edit Mode, no Mirror: (1) the AUTHORITY runs the owner's forwarded input through the command handler (owner check, binding match, queue of one with a
// 0.25 s limit, ordered release, answer block); (2) the owner's CLIENT forwards its slot presses instead of running them. The two ends are joined by hand here; the wire is checked in the Player run.
public static class EveSkillRemoteInputChecks
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
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Skill remote input check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5; var target = PNSyncTarget.ForEntity(new EveEntityId(995020), 1);
        Check(hub.SyncCommands().Get(PNSkillRemoteInput.Command) != null && hub.SyncStates().Get(PNSkillRemoteInput.ResultState).OwnerOnly, "the command and the owner-only result block are registered");

        (GameObject root, EveClientRoleProbeSkill skill) Eve(Scene preview, string name)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, preview); obj.layer = PNLayers.Player;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(995020), EveId.Parse("project_n:eve"), owner); obj.AddComponent<EveCharacterStat>(); obj.AddComponent<PNAbilityManager>();
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
        JObject Input(long key, EveId slot, PNSkillInputPhase phase, PNSkillInteraction interaction, EveId profile)
            => new JObject { ["k"] = key, ["s"] = slot.ToString(), ["p"] = (int)phase, ["i"] = (int)interaction, ["f"] = profile.ToString() };

        // ---- 1) authority ----
        var hostScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:s2_authority"));
            PNAbilityOwnerFixture.Attach();
            var areaRoot = new GameObject("S2 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, hostScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var (root, skill) = Eve(hostScene, "S2 host Eve"); root.GetComponent<PNAbilityManager>().enabled = false; var abilities = root.GetComponent<PNAbilityManager>().Abilities;
            var service = new PNSyncService(hub, true, 0, EveRegistrySession.Context);
            var view = root.AddComponent<PNSkillOwnerView>(); var remote = root.AddComponent<PNSkillRemoteInput>();
            service.RegisterObject(target, skill); view.OnBound(service, target); remote.OnBound(service, target);
            skill.Bindings.TryGet(PNSkillSlots.Attack, out var attackProfile); skill.Bindings.TryGet(PNSkillSlots.Skill, out var skillProfile);
            void Frame(float dt) { skill.Tick(dt); abilities.Advance(dt); remote.Pump(dt); }

            service.ReceiveCommand(owner + 1, target, PNSkillRemoteInput.Command, Input(1, PNSkillSlots.Attack, PNSkillInputPhase.Started, PNSkillInteraction.Tap, attackProfile));
            Check(skill.CurrentCast == EveCast.None && remote.AckedKey == 0 && remote.QueuedCount == 0, "a command from somebody who is not the owner is dropped as stale");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(2, PNSkillSlots.Attack, PNSkillInputPhase.Started, PNSkillInteraction.Tap, EveId.Parse("test:not_the_profile")));
            Check(skill.CurrentCast == EveCast.None && remote.Refusals.Any(r => r.Key == 2 && r.Reason == "binding"), "a press for a profile the slot no longer holds is refused with 'binding' (N4)");

            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(3, PNSkillSlots.Attack, PNSkillInputPhase.Started, PNSkillInteraction.Tap, attackProfile));
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(4, PNSkillSlots.Attack, PNSkillInputPhase.Performed, PNSkillInteraction.Tap, attackProfile));
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(5, PNSkillSlots.Attack, PNSkillInputPhase.Canceled, PNSkillInteraction.Tap, attackProfile));
            Check(skill.CurrentCast == EveCast.Attack && remote.AckedKey == 5 && remote.QueuedCount == 0, "the owner's attack press runs as an attack on the authority and is acknowledged (cast " + skill.CurrentCast + ", ack " + remote.AckedKey + ")");

            // Eve accepts a normal attack press while the first attack is still running (it chains), so it is acknowledged at once
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(6, PNSkillSlots.Attack, PNSkillInputPhase.Started, PNSkillInteraction.Tap, attackProfile));
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(7, PNSkillSlots.Attack, PNSkillInputPhase.Canceled, PNSkillInteraction.Tap, attackProfile));
            Check(remote.QueuedCount == 0 && remote.AckedKey == 7, "a normal attack press during an attack is accepted at once and acknowledged (ack " + remote.AckedKey + ")");
            int frames = 0; while (skill.IsBusy && frames++ < 1000) Frame(.005f);

            // a press that stays impossible is refused after 0.25 s: Skill goes on cooldown, a second Skill press cannot start
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(8, PNSkillSlots.Skill, PNSkillInputPhase.Started, PNSkillInteraction.Tap, skillProfile));
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(9, PNSkillSlots.Skill, PNSkillInputPhase.Canceled, PNSkillInteraction.Tap, skillProfile));
            frames = 0; while (skill.IsBusy && frames++ < 1000) Frame(.005f);
            skill.CanInput = false; // input is gated (RequestSlot returns false): the press cannot start yet
            long ackBefore = remote.AckedKey;
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(10, PNSkillSlots.Skill, PNSkillInputPhase.Started, PNSkillInteraction.Tap, skillProfile));
            Check(remote.QueuedCount == 1, "a press that cannot start yet (input gated) waits: queued=" + remote.QueuedCount + " ack=" + remote.AckedKey + " before=" + ackBefore + " skillCharges=" + skill.SkillRecovery.Current + " cd=" + skill.SkillRecovery.CooldownRemaining + " cast=" + skill.CurrentCast + " busy=" + skill.IsBusy);
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(11, PNSkillSlots.Skill, PNSkillInputPhase.Canceled, PNSkillInteraction.Tap, skillProfile));
            Check(remote.QueuedCount == 2, "its release queues behind it (order is kept)");
            for (int i = 0; i < 40; i++) remote.Pump(.005);
            Check(remote.QueuedCount == 2 && !remote.Refusals.Any(r => r.Key == 10), "press and release are still waiting at 0.2 s");
            for (int i = 0; i < 20; i++) remote.Pump(.005);
            Check(remote.QueuedCount == 0 && remote.Refusals.Any(r => r.Key == 10 && r.Reason == "refused") && remote.AckedKey == ackBefore, "past 0.25 s the press is refused with 'refused' and its release is dropped with it");
            skill.CanInput = true;
            Check(service.TryGetLatest(target, PNSkillRemoteInput.ResultState, out _, out var result) || service.Flush() >= 0, "the result block is published");
            service.Flush(); service.TryGetLatest(target, PNSkillRemoteInput.ResultState, out _, out result);
            Check(result != null && (long)result["ack"] == remote.AckedKey && ((JArray)result["rej"]).Count == remote.Refusals.Count, "the published block carries the acknowledged key and the refusals");

            bool malformed = false; try { service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, new JObject { ["k"] = 30 }); } catch (FormatException) { malformed = true; }
            Check(malformed, "a malformed input is a protocol error");
            skill.GetComponent<PNStat>().SetCurrent(PNContentIds.Health, 0);
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(22, PNSkillSlots.Attack, PNSkillInputPhase.Started, PNSkillInteraction.Tap, attackProfile));
            Check(remote.Refusals.Any(r => r.Key == 22 && r.Reason == "dead"), "a dead owner's input is refused with 'dead'");
            remote.OnUnbound(); view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(hostScene); }

        // ---- 2) the owner's client forwards ----
        var clientScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:s2_client"));
            PNAbilityOwnerFixture.Attach();
            var (root, skill) = Eve(clientScene, "S2 client Eve");
            var wire = new Wire(); var service = new PNSyncService(hub, false, owner, EveRegistrySession.Context) { Transport = wire };
            var view = root.AddComponent<PNSkillOwnerView>(); var remote = root.AddComponent<PNSkillRemoteInput>();
            service.RegisterObject(target, skill); view.OnBound(service, target); remote.OnBound(service, target);
            Check(!remote.IsActive && !skill.Slot(PNSkillSlots.Attack) && wire.Sent.Count == 0, "before the first skill_owner image the client forwards nothing (it does not know the bindings yet)");
            var bindings = new JObject(); foreach (var slot in PNSkillSlots.Player) bindings[slot.ToString()] = "project_n:eve_" + slot.ToString().Substring(slot.ToString().IndexOf("slot_", StringComparison.Ordinal) + 5);
            service.ReceiveState(target, PNSkillOwnerView.State, 2, 1, PNAbilityOwnerFixture.Image(view, new JObject { ["a"] = new JArray(), ["b"] = bindings }));
            Check(remote.IsActive && ReferenceEquals(skill.InputForwarder, remote), "after the image the client's skill forwards through the component");
            Check(skill.Slot(PNSkillSlots.Attack) && wire.Sent.Count == 1, "a slot press on the client is sent, not run");
            var sent = wire.Sent[0];
            Check((long)sent["k"] == 1 && (string)sent["s"] == PNSkillSlots.Attack.ToString() && (int)sent["p"] == 0 && (int)sent["i"] == 0 && (string)sent["f"] == (string)bindings[PNSkillSlots.Attack.ToString()], "the command carries key, slot, phase, interaction and the profile the client believes");
            Check(skill.CurrentCast == EveCast.None, "the client itself cast nothing");
            skill.Slot(PNSkillSlots.Skill); Check(wire.Sent.Count == 2 && (long)wire.Sent[1]["k"] == 2, "keys count up per press");
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 1, new JObject { ["ack"] = 2, ["rej"] = new JArray((object)new JArray(1, "refused")) });
            Check(remote.AckedKey == 2 && remote.Refusals.Count == 1 && remote.Refusals[0].Reason == "refused", "the client reads the authority's answer");
            remote.OnUnbound(); view.OnUnbound(); service.Dispose();
            Check(skill.InputForwarder == null, "unbinding releases the forwarder");
        }
        finally { Cleanup(clientScene); }
        return "PASS " + n + " skill remote input checks (owner check, binding match, queue of one with 0.25 s limit, ordered release, answers, dead owner, client forwards instead of casting)";
    }
}
#endif
