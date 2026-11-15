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

// Docs/SkillNetworkingAPlan.md S6, Edit Mode, no Mirror: the authority's Eve raises a picture for each slash it starts, the block `project_n:skill_cast` carries the last few to every client, and an
// observer's Eve plays them as pictures only (a slash arc, a motion trail, Burst on / off): no damage, no report, no Ability written; a replayed block plays nothing twice; the first block only sets the serial.
public static class EveCastViewChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Cast view check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5, eveId = 995060; var target = PNSyncTarget.ForEntity(new EveEntityId(eveId), 1);
        var def = hub.SyncStates().Get(PNSkillCastView.State);
        Check(def != null && !def.OwnerOnly, "the cast block is registered and goes to every client");
        Check(hub.EntityModules().Get(EveId.Parse("project_n:skill_owner_player")) != null, "the player module that carries it is registered");

        GameObject Root(Scene preview, string name, long id, string character, Vector3 position, long ownerId)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, preview); obj.transform.position = position;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), ownerId); return obj;
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
        JObject Input(long key, EveId slot, PNSkillInputPhase phase, EveId profile) => new JObject { ["k"] = key, ["s"] = slot.ToString(), ["p"] = (int)phase, ["i"] = 0, ["f"] = profile.ToString() };

        // ---- the authority ----
        JToken hostBlock = null;
        var hostScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:s6_authority"));
            var areaRoot = new GameObject("S6 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, hostScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var actor = Root(hostScene, "S6 host Eve", eveId, "project_n:eve", Vector3.zero, owner); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            Physics.SyncTransforms(); actor.GetComponent<PNAbilityManager>().Abilities.Advance(0);
            var service = new PNSyncService(hub, true, 0, EveRegistrySession.Context);
            var ownerView = actor.AddComponent<PNSkillOwnerView>(); var remote = actor.AddComponent<PNSkillRemoteInput>(); var view = actor.AddComponent<PNSkillCastView>();
            service.RegisterObject(target, skill); ownerView.OnBound(service, target); remote.OnBound(service, target); view.OnBound(service, target);
            skill.Bindings.TryGet(PNSkillSlots.Attack, out var attackProfile);
            Check(((JArray)view.Capture()["e"]).Count == 0, "before any cast the authority has nothing to show");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(1, PNSkillSlots.Attack, PNSkillInputPhase.Started, attackProfile));
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(2, PNSkillSlots.Attack, PNSkillInputPhase.Performed, attackProfile));
            Check(skill.CurrentCast == EveCast.Attack, "the owner's press starts a slash on the authority");
            Check(service.Flush() >= 1 && service.TryGetLatest(target, PNSkillCastView.State, out _, out hostBlock), "the authority publishes the cast block");
            var events = PNSkillCastView.Parse(hostBlock, out long latest);
            Check(events.Count == 1 && events[0].Event.Kind == PNSkill.ActionKind && events[0].Event.Detail.StartsWith("project_n:eve_attack_") && events[0].Event.X > .9f && latest == events[0].Serial,
                "the block holds one Action start with its skill id and direction (" + string.Join(",", events.Select(e => e.Event.Kind + ":" + e.Event.Detail)) + ")");
            Check(hostBlock.ToString(Newtonsoft.Json.Formatting.None).Length < PNSkillCastView.StateBytes, "the block fits its byte bound");
            for (int i = 0; i < 300 && skill.IsBusy; i++) skill.Tick(.01f);
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(3, PNSkillSlots.Attack, PNSkillInputPhase.Canceled, attackProfile));
            Check(service.TryGetLatest(target, PNSkillCastView.State, out _, out hostBlock) && view.PlayedCount == 0, "the authority never plays its own pictures");
            ownerView.OnUnbound(); remote.OnUnbound(); view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(hostScene); }

        // ---- an observer: another player's Eve on this machine ----
        var clientScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:s6_observer"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            var areaRoot = new GameObject("S6 observer area manager"); SceneManager.MoveGameObjectToScene(areaRoot, clientScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var actor = Root(clientScene, "S6 observed Eve", eveId, "project_n:eve", Vector3.zero, owner + 1); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            var enemyObject = Root(clientScene, "S6 enemy", 995061, "project_n:dummy", new Vector3(.45f, .4f, 0), 0); var enemy = enemyObject.AddComponent<PNEnemyStat>(); enemyObject.layer = PNLayers.Enemy;
            enemyObject.AddComponent<SphereCollider>().radius = .15f; enemyObject.AddComponent<EveEntityLoader>().Load(hub); enemyObject.SetActive(true); Physics.SyncTransforms();
            var abilities = actor.GetComponent<PNAbilityManager>().Abilities;
            var service = new PNSyncService(hub, false, owner, EveRegistrySession.Context);
            var view = actor.AddComponent<PNSkillCastView>(); service.RegisterObject(target, skill); view.OnBound(service, target);
            double hp = enemy.GetCurrent(PNContentIds.Health); string before = string.Join(";", abilities.CaptureReplica().Select(i => i.Id + ":" + i.Amount + ":" + i.Active));
            ulong rev = 1; bool Send(JToken block) => service.ReceiveState(target, PNSkillCastView.State, 1, rev++, block.DeepClone());
            JObject Block(long latest, params object[][] rows) => new JObject { ["n"] = latest, ["e"] = new JArray(rows.Select(r => (object)new JArray(r))) };
            Check(!skill.IsOwnerClient, "an observed Eve is not on its owner's machine");

            Check(Send(Block(7)) && view.PlayedCount == 0 && view.LastSerial == 7, "the first block only sets the serial: a late joiner replays nothing");
            Check(Send(Block(7, new object[] { 7L, "action", "base:eve_attack_right", 1f, 0f, 0f })) && view.PlayedCount == 0, "an event the first block already counted is not played");
            Check(Send(hostBlock) && view.PlayedCount == 0, "the authority's own block, with a serial this client already passed, plays nothing");

            Check(Send(Block(9, new object[] { 8L, "action", "base:eve_attack_right", 1f, 0f, 0f }, new object[] { 9L, "action", "base:eve_attack_left", 0f, 1f, 0f })) && view.PlayedCount == 2, "two new events are played in order (" + view.PlayedCount + ")");
            Check(skill.ObservedActionCount == 2 && skill.ObservedActionsPlayed == 2, "both slashes run as the same Action on the observed Eve (running " + skill.ObservedActionCount + ", played " + skill.ObservedActionsPlayed + ")");
            Check(Send(Block(9, new object[] { 8L, "action", "base:eve_attack_right", 1f, 0f, 0f }, new object[] { 9L, "action", "base:eve_attack_left", 0f, 1f, 0f })) && view.PlayedCount == 2, "the same block again plays nothing twice");
            for (int i = 0; i < 300 && skill.ObservedActionCount > 0; i++) skill.AdvanceObservedActions(.01f);
            Check(skill.ObservedActionCount == 0, "both end by themselves when their time is up");
            Check(enemy.GetCurrent(PNContentIds.Health) == hp, "the observed slash hits nothing");
            Check(!skill.IsBusy && skill.CurrentInvocation == null, "no cast runs on the observer");

            Check(skill.Presentation.Apply(new System.Collections.Generic.Dictionary<string, string> { [EveCharacterSkill.BurstPresentation] = "1" }) && skill.Presentation.Get(EveCharacterSkill.BurstPresentation) == "1", "Burst on is drawn from the presentation picture");
            Check(skill.Presentation.Apply(new System.Collections.Generic.Dictionary<string, string>()) && skill.Presentation.Get(EveCharacterSkill.BurstPresentation) == "", "Burst off is drawn from the next picture");
            Check(Send(Block(12, new object[] { 12L, "action", "base:eve_no_such_profile", 1f, 0f, 0f })) && skill.ObservedActionCount == 0 && skill.ObservedActionsPlayed == 2, "an unknown Action is dropped without a sound");
            Check(Send(Block(13, new object[] { 13L, "action", "base:eve_attack_right", 0f, 0f, 0f })) && skill.ObservedActionCount == 0 && skill.ObservedActionsPlayed == 2, "a slash without a direction draws nothing");
            Check(Send(Block(14, new object[] { 14L, "action", "base:eve_attack_left", 0f, 1f, 0f })) && skill.ObservedActionCount == 1, "a new slash starts");
            Check(Send(Block(15, new object[] { 15L, "action", "base:eve_attack_right", 1f, 0f, 0f })) && skill.ObservedActionCount == 2 && skill.ObservedActionsPlayed == 4, "a second slash runs beside the first");
            Check(Send(Block(16, new object[] { 16L, "end", "base:eve_attack_right", 0f, 0f, 0f })) && skill.ObservedActionCount == 1, "the authority's end of an Action stops that one on the observer");
            skill.AdvanceObservedActions(.01f);
            Check(string.Join(";", abilities.CaptureReplica().Select(i => i.Id + ":" + i.Amount + ":" + i.Active)) == before, "no Ability was written by anything the observer drew");
            Check(wireIsEmpty(skill), "nothing was sent or reported (no sink on an observer)");

            foreach (var bad in new JToken[] {
                new JObject { ["n"] = 1 }, new JObject { ["e"] = new JArray() }, Block(1, new object[] { 1L, "sweep", "x", 0f }), Block(2, new object[] { 2L, "sweep", "x", 1f, 0f, 1f }, new object[] { 1L, "sweep", "x", 1f, 0f, 1f }),
                Block(1, new object[] { 1L, new string('a', 60), "x", 1f, 0f, 1f }), Block(1, new object[] { 1L, "sweep", "x", "a", 0f, 1f }),
                new JObject { ["n"] = 1, ["e"] = new JArray(Enumerable.Range(1, 13).Select(i => (object)new JArray((long)i, "k", "d", 0f, 0f, 0f))) } })
            {
                bool refused = false; try { PNSkillCastView.Parse(bad, out _); } catch (FormatException) { refused = true; }
                Check(refused, "a malformed block is refused: " + bad.ToString(Newtonsoft.Json.Formatting.None).Substring(0, Math.Min(60, bad.ToString(Newtonsoft.Json.Formatting.None).Length)));
            }
            view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(clientScene); }
        return "PASS " + n + " cast view checks (authority pictures, block, observer replay, no replay twice, no damage / Ability, malformed refused)";
    }

    private static bool wireIsEmpty(EveCharacterSkill skill) => skill.ReportSink == null && skill.InputForwarder == null;
}
#endif
