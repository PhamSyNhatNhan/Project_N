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

// Docs/SkillNetworkFrameworkPlan.md G7, Edit Mode: a character that is NOT Eve (Eve 2, the pet in the Skill slot and the shared Dash) is guessed on the owner's client with no network code of its own.
// The pet behavior (`PNSummonCastBehavior`) is one path for every execution mode: in a guess the summon Operation creates nothing (status Predicted) and the cooldown spend lands in the guess's scope; the
// authority's refusal puts it back. The same behavior on the authority creates the summon (PNEntitySpawnEditorChecks).
public sealed class PNEve2ClientProbeSkill : PNEve2Skill
{
    public void Prepare() { if (pc == null) Awake(); OnEnable(); }
    public bool Press(EveId slot, PNSkillInputPhase phase) => RequestSlot(slot, new PNSkillInputEvent(phase, PNSkillInteraction.Tap));
}

public static class PNPetPredictionChecks
{
    private sealed class Wire : IPNSyncTransport
    {
        public readonly List<(string Command, JObject Payload)> Sent = new List<(string, JObject)>();
        public void SendState(PNSyncTarget target, PNSyncStateDefinition state, ulong revision, JToken payload) { }
        public void SendCommand(PNSyncTarget target, PNSyncCommandDefinition command, JToken payload) { Sent.Add((command.Id.ToString(), (JObject)payload)); }
    }

    // The same slash on the authority (offline): the same behavior deals the damage, spends the cooldown and heard no network code either.
    public static string RunAuthority()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Slash authority check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:g7_slash"));
            var areaRoot = new GameObject("g7 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, scene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var actor = new GameObject("g7 Eve 2"); actor.SetActive(false); SceneManager.MoveGameObjectToScene(actor, scene); actor.layer = PNLayers.Player;
            actor.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(995080), EveId.Parse("project_n:eve_2"), 1); actor.AddComponent<PNEve2Stat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<PNEve2ClientProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            var enemyGo = new GameObject("g7 enemy"); enemyGo.SetActive(false); SceneManager.MoveGameObjectToScene(enemyGo, scene); enemyGo.transform.position = new Vector3(1.2f, .4f, 0);
            enemyGo.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(995081), EveId.Parse("project_n:dummy"), 1);
            var enemy = enemyGo.AddComponent<DummyStat>().Declared(); enemyGo.layer = PNLayers.Enemy; enemyGo.AddComponent<SphereCollider>().radius = .08f; enemyGo.AddComponent<EveEntityLoader>().Load(hub); enemyGo.SetActive(true);
            Physics.SyncTransforms();
            double health = enemy.GetCurrent(PNContentIds.Health); int strikes = PNTargetStrikeBehavior.Strikes;
            Check(skill.Press(PNSkillSlots.Attack, PNSkillInputPhase.Started), "the slash press is accepted");
            skill.Press(PNSkillSlots.Attack, PNSkillInputPhase.Canceled);
            Check(PNTargetStrikeBehavior.Strikes == strikes + 1, "the authority strikes once");
            Check(enemy.GetCurrent(PNContentIds.Health) < health, "the authority's slash damaged the enemy");
            bool spent = false; foreach (var item in actor.GetComponent<PNAbilityManager>().Abilities.CaptureReplica()) if (item.Id == PNSummonGameplayContent.Eve2SlashReady) spent = item.Amount < 1;
            Check(spent, "the slash cooldown is spent");
        }
        finally
        {
            try
            {
                foreach (var r in scene.GetRootGameObjects()) if (r != null) foreach (var l in r.GetComponentsInChildren<EveEntityLoader>(true)) { try { l.Unload(); } catch (Exception) { } }
                foreach (var r in scene.GetRootGameObjects()) if (r != null) r.SetActive(false);
            }
            finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(scene); } }
        }
        return "PASS " + n + " slash authority checks (the same Eve 2 slash deals the damage and spends the cooldown on the authority)";
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Pet prediction check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5, petId = 995070; var target = PNSyncTarget.ForEntity(new EveEntityId(petId), 1);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:g7_pet"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            var areaRoot = new GameObject("g7 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, scene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var actor = new GameObject("g7 client Eve 2"); actor.SetActive(false); SceneManager.MoveGameObjectToScene(actor, scene); actor.layer = PNLayers.Player;
            actor.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(petId), EveId.Parse("project_n:eve_2"), owner); actor.AddComponent<PNEve2Stat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<PNEve2ClientProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            var wire = new Wire(); var service = new PNSyncService(hub, false, owner, EveRegistrySession.Context) { Transport = wire };
            var view = actor.AddComponent<PNSkillOwnerView>(); var remote = actor.AddComponent<PNSkillRemoteInput>();
            service.RegisterObject(target, skill); view.OnBound(service, target); remote.OnBound(service, target);
            var bindings = new JObject { [PNSkillSlots.Attack.ToString()] = PNSummonGameplayContent.Eve2SlashProfile.ToString(), [PNSkillSlots.Skill.ToString()] = PNSummonGameplayContent.Eve2PetProfile.ToString(),[PNSkillSlots.Dash.ToString()] = "project_n:binding_player_dash" };
            service.ReceiveState(target, PNSkillOwnerView.State, 1, 1, new JObject { ["a"] = new JArray(), ["b"] = bindings });
            long rev = 10; void Refuse(long refusedKey) { service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, (ulong)(++rev), new JObject { ["ack"] = 0, ["p"] = 0, ["rej"] = new JArray((object)new JArray(refusedKey, "declined")) }); }
            var replicaAbilities = actor.GetComponent<PNAbilityManager>().Abilities; int Charges() { var items = replicaAbilities.CaptureReplica(); foreach (var item in items) if (item.Id == PNSummonGameplayContent.Eve2PetReady) return item.Amount; throw new Exception("the replica has no pet cooldown; it holds: " + string.Join(", ", items.Select(x => x.Id.ToString()))); }
            Check(remote.IsActive, "the client Eve 2 is bound");

            int casts = PNSummonCastBehavior.Casts, refusals = PNSummonCastBehavior.Refusals;
            Check(skill.Press(PNSkillSlots.Skill, PNSkillInputPhase.Started), "the pet press is accepted for forwarding");
            long petKey = remote.LastSentKey;
            Check(remote.PredictedCount == 1 && remote.PendingPredictions == 1 && wire.Sent.Count == 1, "it is guessed as a Predicted cast and sent once");
            Check(Charges() < 1, "the guess shows the pet cooldown spent at once (" + Charges() + ")");
            Check(PNSummonCastBehavior.Casts == casts && PNSummonCastBehavior.Refusals == refusals, "a guess is neither a summon nor a refusal in the behavior's own counters");
            Check(skill.CurrentInvocation == null || skill.CurrentInvocation.IsEnded, "the cast has ended in the same call, as on the authority");
            skill.Press(PNSkillSlots.Skill, PNSkillInputPhase.Canceled);

            Refuse(petKey);
            Check(Charges() >= 1 && remote.PendingPredictions == 0 && remote.RolledBackCount == 1, "the authority's refusal gives the pet cooldown back (" + Charges() + ")");

            // the shared Dash of this character is guessed too (no code of its own)
            long dashKey = 0; skill.Press(PNSkillSlots.Dash, PNSkillInputPhase.Started); dashKey = remote.LastSentKey; skill.Press(PNSkillSlots.Dash, PNSkillInputPhase.Performed);
            Check(skill.IsPredictingDash && remote.PendingPredictions == 1, "Eve 2's shared Dash is guessed through the same path");
            Refuse(dashKey);
            Check(!skill.IsDashing && remote.PendingPredictions == 0, "and taken back when the authority refuses");
            skill.Press(PNSkillSlots.Dash, PNSkillInputPhase.Canceled);

            // the Attack-slot slash (trial character): one decision point, a hit on that target, a cooldown, no network code in the behavior
            var enemyGo = new GameObject("g7 enemy"); enemyGo.SetActive(false); SceneManager.MoveGameObjectToScene(enemyGo, scene); enemyGo.transform.position = new Vector3(1.2f, .4f, 0);
            enemyGo.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(petId + 1), EveId.Parse("project_n:dummy"), 1);
            var enemy = enemyGo.AddComponent<DummyStat>().Declared(); enemyGo.layer = PNLayers.Enemy; enemyGo.AddComponent<SphereCollider>().radius = .08f; enemyGo.AddComponent<EveEntityLoader>().Load(hub); enemyGo.SetActive(true);
            Physics.SyncTransforms();
            int SlashCharges() { foreach (var item in replicaAbilities.CaptureReplica()) if (item.Id == PNSummonGameplayContent.Eve2SlashReady) return item.Amount; throw new Exception("the replica has no slash cooldown"); }
            double health = enemy.GetCurrent(PNContentIds.Health);
            Check(SlashCharges() == 1, "the slash cooldown starts ready");
            int sentBefore = wire.Sent.Count;
            Check(skill.Press(PNSkillSlots.Attack, PNSkillInputPhase.Started), "the slash press is accepted for forwarding");
            long slashKey = remote.LastSentKey; skill.Press(PNSkillSlots.Attack, PNSkillInputPhase.Canceled);
            Check(remote.PredictedCount >= 1 && remote.PendingPredictions == 1, "the slash is guessed as a Predicted cast");
            Check(SlashCharges() < 1, "the guess shows the slash cooldown spent at once");
            Check(enemy.GetCurrent(PNContentIds.Health) == health, "a guess deals no damage");
            var sent = wire.Sent.Skip(sentBefore).ToArray();
            Check(sent.Length >= 3, "the cast, its decision and its hit were sent (" + sent.Length + ")");
            Check(sent.Any(s => s.Payload.ToString(Newtonsoft.Json.Formatting.None).Contains((petId + 1).ToString())), "the chosen target travels under the cast's key");
            Refuse(slashKey);
            Check(SlashCharges() == 1 && remote.PendingPredictions == 0, "the authority's refusal gives the slash cooldown back");

            // no enemy in reach: nothing happens and nothing is spent
            enemyGo.transform.position = new Vector3(20f, .4f, 0); Physics.SyncTransforms();
            int noTarget = PNTargetStrikeBehavior.NoTarget; sentBefore = wire.Sent.Count;
            skill.Press(PNSkillSlots.Attack, PNSkillInputPhase.Started); skill.Press(PNSkillSlots.Attack, PNSkillInputPhase.Canceled);
            Check(SlashCharges() == 1 && PNTargetStrikeBehavior.NoTarget == noTarget + 1, "with no enemy in reach the slash spends nothing");
            remote.OnUnbound(); view.OnUnbound(); service.Dispose();
        }
        finally
        {
            try
            {
                foreach (var r in scene.GetRootGameObjects()) if (r != null) foreach (var l in r.GetComponentsInChildren<EveEntityLoader>(true)) { try { l.Unload(); } catch (Exception) { } }
                foreach (var r in scene.GetRootGameObjects()) if (r != null) r.SetActive(false);
            }
            finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(scene); } }
        }
        return "PASS " + n + " pet prediction checks (Eve 2 pet guessed with no network code, cooldown shown spent, refusal gives it back, shared Dash guessed)";
    }
}
#endif
