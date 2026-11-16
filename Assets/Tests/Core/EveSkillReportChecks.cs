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

// Docs/SkillNetworkingAPlan.md S4-S5 on a real Eve, Edit Mode, no Mirror: (1) the AUTHORITY takes the hits its owner's client reported only through the sweep that is running, within its reach and once per cast
// (a stranger's report, a stale key, a dead / far / unknown / repeated target, a report after the sweep ended all fail and change nothing); (2) the owner's decisions at the points inside a cast are queued
// per point, expire, and the ultimate takes the target the owner named instead of its own; (3) the owner's CLIENT sends both as commands. The wire itself is checked in the Player run.
public static class EveSkillReportChecks
{
    private sealed class Wire : IPNSyncTransport
    {
        public readonly List<(string Command, JObject Payload)> Sent = new List<(string, JObject)>();
        public void SendState(PNSyncTarget target, PNSyncStateDefinition state, ulong revision, JToken payload) { }
        public void SendCommand(PNSyncTarget target, PNSyncCommandDefinition command, JToken payload) { Sent.Add((command.Id.ToString(), (JObject)payload)); }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Skill report check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5, eveId = 995040; var target = PNSyncTarget.ForEntity(new EveEntityId(eveId), 1);
        Check(hub.SyncCommands().Get(PNSkillRemoteInput.HitCommand) != null && hub.SyncCommands().Get(PNSkillRemoteInput.DecisionCommand) != null, "the hit and decision commands are registered");

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
        JObject Input(long key, EveId slot, PNSkillInputPhase phase, EveId profile)
            => new JObject { ["k"] = key, ["s"] = slot.ToString(), ["p"] = (int)phase, ["i"] = 0, ["f"] = profile.ToString() };
        JObject Hits(long key, int step, params long[] ids) => new JObject { ["k"] = key, ["st"] = step, ["h"] = new JArray(ids.Select(x => (object)x)) };
        JObject Decision(long key, string point, JToken value) => new JObject { ["k"] = key, ["n"] = point, ["v"] = value };

        var hostScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:s45_authority"));
            PNAbilityOwnerFixture.Attach();
            var areaRoot = new GameObject("S45 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, hostScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var actor = Root(hostScene, "S45 host Eve", eveId, "project_n:eve", Vector3.zero, owner); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            PNEnemyStat Enemy(string name, long id, Vector3 at)
            {
                var obj = Root(hostScene, name, id, "project_n:dummy", at, 0); var enemy = obj.AddComponent<PNEnemyStat>(); obj.layer = PNLayers.Enemy; obj.AddComponent<SphereCollider>().radius = .15f;
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); return enemy;
            }
            var front = Enemy("S45 front", 995041, new Vector3(.45f, .4f, 0)); var behind = Enemy("S45 behind", 995042, new Vector3(-.4f, .4f, 0));
            var far = Enemy("S45 far", 995043, new Vector3(40f, .4f, 0)); var dead = Enemy("S45 dead", 995044, new Vector3(0f, .4f, .5f)); dead.SetCurrent(PNContentIds.Health, 0);
            Physics.SyncTransforms();
            var abilities = actor.GetComponent<PNAbilityManager>().Abilities; abilities.Advance(0);
            var service = new PNSyncService(hub, true, 0, EveRegistrySession.Context);
            var view = actor.AddComponent<PNSkillOwnerView>(); var remote = actor.AddComponent<PNSkillRemoteInput>();
            service.RegisterObject(target, skill); view.OnBound(service, target); remote.OnBound(service, target);
            skill.Bindings.TryGet(PNSkillSlots.Attack, out var attackProfile);
            double Hp(PNEnemyStat e) => e.GetCurrent(PNContentIds.Health);
            double behindHp = Hp(behind), frontHp = Hp(front);

            // ---- S4: hits ----
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.HitCommand, Hits(1, 0, behind.EntityId.Value));
            Check(remote.HitsApplied == 0 && remote.HitsRejected == 1 && Hp(behind) == behindHp, "a hit report before any cast (key beyond what the authority received) changes nothing");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(1, PNSkillSlots.Attack, PNSkillInputPhase.Started, attackProfile));
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(2, PNSkillSlots.Attack, PNSkillInputPhase.Performed, attackProfile));
            Check(skill.CurrentCast == EveCast.Attack && skill.HitReceiverCount == 1, "the owner's Attack press starts a sweep that listens for reported hits (cast " + skill.CurrentCast + ", receivers " + skill.HitReceiverCount + ")");
            // G6c: a hit travels under the cast's key (the input that began the cast, shifted), which the framework gave both the guess and this real cast
            long ck = skill.CurrentInvocation.CastKey; Check(ck == PNSkillCastKey.FromInput(2), "the host's cast carries the key of the input that began it (" + ck + ")");
            service.ReceiveCommand(owner + 1, target, PNSkillRemoteInput.HitCommand, Hits(ck, 0, behind.EntityId.Value));
            Check(remote.HitsApplied == 0 && Hp(behind) == behindHp, "a report from somebody who is not the owner is dropped as stale");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.HitCommand, Hits(ck, 0, behind.EntityId.Value));
            Check(remote.HitsApplied == 1 && Hp(behind) < behindHp, "a legal reported hit is applied with the sweep's own damage (hp " + behindHp + " -> " + Hp(behind) + ")");
            double afterFirst = Hp(behind);
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.HitCommand, Hits(ck, 0, behind.EntityId.Value));
            Check(remote.HitsApplied == 1 && Hp(behind) == afterFirst, "the same target at the same step is not hit twice");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.HitCommand, Hits(ck, 1, behind.EntityId.Value));
            Check(remote.HitsApplied == 1 && Hp(behind) == afterFirst, "another step does not hit it again either: the sweep hits each target once per cast");
            int rejected = remote.HitsRejected;
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.HitCommand, Hits(ck, 0, far.EntityId.Value, dead.EntityId.Value, 777000, 0, eveId));
            Check(remote.HitsApplied == 1 && remote.HitsRejected == rejected + 5, "far, dead, unknown, zero and self targets are all refused (" + (remote.HitsRejected - rejected) + " of 5)");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.HitCommand, Hits(PNSkillCastKey.FromInput(99), 0, front.EntityId.Value));
            Check(remote.HitsApplied == 1 && Hp(front) == frontHp, "a key from the future is refused");
            bool malformed = false; try { service.ReceiveCommand(owner, target, PNSkillRemoteInput.HitCommand, new JObject { ["k"] = 1, ["st"] = 0 }); } catch (FormatException) { malformed = true; }
            Check(malformed, "a report without targets is a protocol error");
            malformed = false; try { service.ReceiveCommand(owner, target, PNSkillRemoteInput.HitCommand, Hits(1, 0, Enumerable.Range(1, 17).Select(x => (long)x).ToArray())); } catch (FormatException) { malformed = true; }
            Check(malformed, "more than 16 targets in one report is a protocol error");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.Command, Input(3, PNSkillSlots.Attack, PNSkillInputPhase.Canceled, attackProfile));
            for (int i = 0; i < 400 && skill.IsBusy; i++) { skill.Tick(.01f); remote.Pump(.01); }
            Check(!skill.IsBusy && skill.HitReceiverCount == 0, "the sweep ended and stopped listening");
            double settled = Hp(behind); int applied = remote.HitsApplied;
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.HitCommand, Hits(ck, 2, behind.EntityId.Value));
            Check(remote.HitsApplied == applied && Hp(behind) == settled, "a report that arrives after the sweep ended changes nothing");

            // ---- S5: decisions ----
            service.ReceiveCommand(owner + 1, target, PNSkillRemoteInput.DecisionCommand, Decision(2, "ult_retarget", new JObject { ["t"] = front.EntityId.Value }));
            Check(remote.DecisionsWaiting == 0, "a decision from a stranger is dropped");
            bool bad = false; try { service.ReceiveCommand(owner, target, PNSkillRemoteInput.DecisionCommand, Decision(2, "Not A Point", new JObject())); } catch (FormatException) { bad = true; }
            Check(bad, "a decision point named with anything but lowercase letters, digits and underscores is a protocol error");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.DecisionCommand, Decision(2, "nobody_asks", new JObject { ["v"] = 1 }));
            Check(remote.DecisionsWaiting == 1, "a well named point nobody has declared is kept (a skill names its points at the Choose call)");
            remote.ExpireDecisions(PNSkillRemoteInput.DecisionSeconds + 1); Check(remote.DecisionsWaiting == 0, "and expires when nobody asks");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.DecisionCommand, Decision(2, "repeat_continue", new JObject { ["v"] = 0 }));
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.DecisionCommand, Decision(2, "repeat_continue", new JObject { ["v"] = 1 }));
            Check(remote.DecisionsWaiting == 2 && !remote.TryTake(3, "repeat_continue", out _) && remote.TryTake(2, "repeat_continue", out var first) && (int)first["v"] == 0 && remote.TryTake(2, "repeat_continue", out var second) && (int)second["v"] == 1 && !remote.TryTake(2, "repeat_continue", out _), "decisions are answered only to the cast whose key they name, and in the order they arrived");
            for (int i = 0; i < 6; i++) service.ReceiveCommand(owner, target, PNSkillRemoteInput.DecisionCommand, Decision(2, "empowered_strike", new JObject { ["v"] = "empowered_left", ["i"] = i }));
            Check(remote.DecisionsWaiting == PNSkillRemoteInput.MaxDecisionsPerPoint && remote.DecisionsExpired >= 2, "a point keeps at most " + PNSkillRemoteInput.MaxDecisionsPerPoint + " waiting answers (" + remote.DecisionsWaiting + ")");
            remote.ExpireDecisions(PNSkillRemoteInput.DecisionSeconds + 1);
            Check(remote.DecisionsWaiting == 0, "answers nobody asked for expire");
            for (int i = 0; i < PNSkillRemoteInput.MaxDecisionsWaiting + 4; i++) service.ReceiveCommand(owner, target, PNSkillRemoteInput.DecisionCommand, Decision(2, "flood_" + i, new JObject { ["v"] = i }));
            Check(remote.DecisionsWaiting == PNSkillRemoteInput.MaxDecisionsWaiting, "the host keeps at most " + PNSkillRemoteInput.MaxDecisionsWaiting + " waiting answers in all, whatever names arrive (" + remote.DecisionsWaiting + ")");
            remote.ExpireDecisions(PNSkillRemoteInput.DecisionSeconds + 1); Check(remote.DecisionsWaiting == 0, "and they expire");

            // the ultimate: the owner names the front enemy while the authority's own rule (lowest health) would pick the other one
            int took = remote.DecisionsTaken; behind.SetCurrent(PNContentIds.Health, 1); skill.UltimateRecovery.Reset();
            Check(skill.TryUltimate(), "the ultimate starts");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.DecisionCommand, Decision(skill.CurrentInvocation.CastKey + 1, "ult_retarget", new JObject { ["t"] = far.EntityId.Value }));
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.DecisionCommand, Decision(skill.CurrentInvocation.CastKey, "ult_retarget", new JObject { ["t"] = front.EntityId.Value }));
            for (int i = 0; i < 2000 && skill.CurrentPhase != EveCastPhase.Rush && skill.IsBusy; i++) { skill.Tick(.01f); remote.Pump(.01); }
            Check(remote.DecisionsTaken == took + 1 && ReferenceEquals(skill.CastTarget, front), "the ultimate retargets to the enemy its owner named (taken " + (remote.DecisionsTaken - took) + ", target " + (skill.CastTarget != null ? skill.CastTarget.name : "none") + ")");
            for (int i = 0; i < 4000 && skill.IsBusy; i++) { skill.Tick(.01f); remote.Pump(.01); }

            // a named target that is not legal is ignored and the authority chooses itself
            skill.UltimateRecovery.Reset(); body.position = actor.transform.position = Vector3.zero; Physics.SyncTransforms();
            remote.ExpireDecisions(PNSkillRemoteInput.DecisionSeconds + 1);
            Check(skill.TryUltimate(), "the ultimate starts again");
            service.ReceiveCommand(owner, target, PNSkillRemoteInput.DecisionCommand, Decision(skill.CurrentInvocation.CastKey, "ult_retarget", new JObject { ["t"] = far.EntityId.Value }));
            for (int i = 0; i < 2000 && skill.CurrentPhase != EveCastPhase.Rush && skill.IsBusy; i++) { skill.Tick(.01f); remote.Pump(.01); }
            Check(remote.DecisionsTaken == took + 2 && !ReferenceEquals(skill.CastTarget, far), "a target out of reach is ignored (the authority picked " + (skill.CastTarget != null ? skill.CastTarget.name : "none") + ")");
            remote.OnUnbound(); view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(hostScene); }

        // ---- the owner's client ----
        var clientScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:s45_client"));
            PNAbilityOwnerFixture.Attach();
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            var actor = Root(clientScene, "S45 client Eve", eveId, "project_n:eve", Vector3.zero, owner); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            var wire = new Wire(); var service = new PNSyncService(hub, false, owner, EveRegistrySession.Context) { Transport = wire };
            var view = actor.AddComponent<PNSkillOwnerView>(); var remote = actor.AddComponent<PNSkillRemoteInput>();
            service.RegisterObject(target, skill); view.OnBound(service, target); remote.OnBound(service, target);
            var bindings = new JObject(); foreach (var slot in PNSkillSlots.Player) bindings[slot.ToString()] = "project_n:eve_" + slot.ToString().Substring(slot.ToString().IndexOf("slot_", StringComparison.Ordinal) + 5);
            service.ReceiveState(target, PNSkillOwnerView.State, 2, 1, PNAbilityOwnerFixture.Image(view, new JObject { ["a"] = new JArray(), ["b"] = bindings }));
            Check(remote.SendHits(7, 0, new[] { new EveEntityId(995041), new EveEntityId(995042) }) && wire.Sent.Count == 1 && wire.Sent[0].Command == PNSkillRemoteInput.HitCommand.ToString() && (long)wire.Sent[0].Payload["k"] == 7 && ((JArray)wire.Sent[0].Payload["h"]).Count == 2, "the client sends the hits it saw");
            Check(!remote.SendHits(7, 0, new EveEntityId[0]) && !remote.SendHits(0, 0, new[] { new EveEntityId(995041) }) && !remote.SendHits(7, 0, Enumerable.Range(1, 17).Select(x => new EveEntityId(x)).ToArray()), "an empty, keyless or oversized report is not sent");
            Check(remote.SendDecision(7, "ult_retarget", new JObject { ["t"] = 995041 }) && wire.Sent.Count == 2 && wire.Sent[1].Command == PNSkillRemoteInput.DecisionCommand.ToString(), "the client sends its decision");
            Check(!remote.SendDecision(7, "Not A Point", new JObject()), "a badly named decision point is not sent");
            remote.OnUnbound(); view.OnUnbound(); service.Dispose();
        }
        finally { Cleanup(clientScene); }
        return "PASS " + n + " skill report checks (reported hits, decisions, ultimate retarget, client senders)";
    }
}
#endif
