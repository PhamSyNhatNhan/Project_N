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

// Docs/SkillNetworkingAPlan.md S3-S5 and G6a for Eve, Edit Mode, no Mirror: the owner's CLIENT runs the gesture through the SAME binding path as the authority (`RequestSlot` -> Eve's slot behavior -> `TryAttack` ...)
// as a Predicted cast: the combo step on the replica Abilities, the sweep with its visual, no lock, no activation. It reports the hit its own sweep sees instead of damaging, and takes the whole guess back when
// the authority declines. The cast starts when the gesture performs (a tap on release), exactly when the authority starts it. Also: a predicted Dash ends by itself on Eve (her Update used to return early).
public static class EveChainPredictionChecks
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
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Chain prediction check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5, eveId = 995050; var target = PNSyncTarget.ForEntity(new EveEntityId(eveId), 1);
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject Root(string name, long id, string character, Vector3 position, long ownerId)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, scene); obj.transform.position = position;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), ownerId); return obj;
        }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:chain_prediction"));
            PNAbilityOwnerFixture.Attach();
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            var areaRoot = new GameObject("chain area manager"); SceneManager.MoveGameObjectToScene(areaRoot, scene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var actor = Root("chain client Eve", eveId, "project_n:eve", Vector3.zero, owner); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            var enemyObject = Root("chain enemy", 995051, "project_n:dummy", new Vector3(.45f, .4f, 0), 0); var enemy = enemyObject.AddComponent<PNEnemyStat>(); enemyObject.layer = PNLayers.Enemy;
            enemyObject.AddComponent<SphereCollider>().radius = .15f; enemyObject.AddComponent<EveEntityLoader>().Load(hub); enemyObject.SetActive(true); Physics.SyncTransforms();
            var wire = new Wire(); var service = new PNSyncService(hub, false, owner, EveRegistrySession.Context) { Transport = wire };
            var view = actor.AddComponent<PNSkillOwnerView>(); var remote = actor.AddComponent<PNSkillRemoteInput>();
            service.RegisterObject(target, skill); view.OnBound(service, target); remote.OnBound(service, target);
            var bindings = new JObject(); foreach (var slot in PNSkillSlots.Player) bindings[slot.ToString()] = "project_n:eve_" + slot.ToString().Substring(slot.ToString().IndexOf("slot_", StringComparison.Ordinal) + 5);
            service.ReceiveState(target, PNSkillOwnerView.State, 2, 1, PNAbilityOwnerFixture.Image(view, new JObject { ["a"] = new JArray(), ["b"] = bindings }));
            double hp = enemy.GetCurrent(PNContentIds.Health); int combo = skill.ComboIndex;
            Check(skill.CanInput && !skill.IsBusy, "the client Eve may take input and is idle");

            // Taps the way the input layer delivers them: Started on press, Performed when the tap completes. Returns the key of the press (its Started event), the one the authority answers under.
            long Tap(EveId slot) { skill.Press(slot, PNSkillInputPhase.Started); long pressKey = remote.LastSentKey; skill.Press(slot, PNSkillInputPhase.Performed); return pressKey; }

            // ---- the Attack gesture is run through the binding path as a guess ----
            Check(skill.Press(PNSkillSlots.Attack, PNSkillInputPhase.Started), "the press is accepted for forwarding");
            Check(!skill.IsBusy && remote.PendingPredictions == 0, "the press alone casts nothing: the cast starts when the gesture performs, as it does on the authority");
            long key = remote.LastSentKey;
            Check(skill.Press(PNSkillSlots.Attack, PNSkillInputPhase.Performed), "the performed tap is accepted for forwarding");
            Check(skill.IsPredictingCast && skill.CurrentCast == EveCast.Attack && skill.CurrentPhase == EveCastPhase.Slash, "the client starts its slash at once (cast " + skill.CurrentCast + ", phase " + skill.CurrentPhase + ")");
            Check(skill.ComboIndex == (combo + 1) % 3, "the combo step moves on the replica Abilities (" + combo + " -> " + skill.ComboIndex + ")");
            Check(skill.CurrentInvocation != null && skill.CurrentInvocation.Mode == PNSkillMode.Predicted && skill.CurrentInvocation.PredictionScope != null && skill.ActivationSerial == 0 && remote.PendingPredictions == 1 && wire.Sent.Count == 2, "the guess runs as a Predicted invocation that owns the Ability scope, no activation was committed; one guess waits and both events went out");
            for (int i = 0; i < 300 && skill.IsBusy; i++) skill.Tick(.01f);
            Check(!skill.IsBusy && !skill.IsPredictingCast, "the slash runs to its end on the client and the scope closes");
            Check(enemy.GetCurrent(PNContentIds.Health) == hp, "the client never damages: its sweep only reports");
            var hits = wire.Sent.Where(x => x.Command == PNSkillRemoteInput.HitCommand.ToString()).ToList();
            Check(hits.Count == 1 && (long)hits[0].Payload["k"] == PNSkillCastKey.FromInput(key + 1) && (long)hits[0].Payload["k"] == skill.LastCastKey && ((JArray)hits[0].Payload["h"]).Count == 1 && (long)((JArray)hits[0].Payload["h"])[0] == enemy.EntityId.Value, "the client reported the enemy its sweep hit, under the key of the cast (the input that began it, shifted) (reports " + hits.Count + ")");

            // ---- a refusal takes the whole guess back ----
            skill.Release(PNSkillSlots.Attack);
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 2, new JObject { ["ack"] = 0, ["p"] = 0, ["rej"] = new JArray((object)new JArray(key, "declined")) });
            Check(skill.ComboIndex == combo && remote.RolledBackCount == 1 && remote.PendingPredictions == 0, "the refusal puts the combo step back (" + skill.ComboIndex + ", rolled back " + remote.RolledBackCount + ")");

            // ---- a refusal while the slash is still running stops it ----
            long key2 = Tap(PNSkillSlots.Attack);
            Check(skill.IsPredictingCast, "a second press is guessed"); skill.Tick(.02f);
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 3, new JObject { ["ack"] = 0, ["p"] = 0, ["rej"] = new JArray((object)new JArray(key2, "declined")) });
            Check(!skill.IsBusy && !skill.IsPredictingCast && skill.ComboIndex == combo && remote.RolledBackCount == 2, "a refusal stops a running guess and restores the combo (busy " + skill.IsBusy + ", step " + skill.ComboIndex + ")");

            // ---- a pause stops new guesses ----
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 4, new JObject { ["ack"] = 0, ["p"] = 1, ["rej"] = new JArray() });
            Tap(PNSkillSlots.Attack);
            Check(!skill.IsBusy, "while paused the press goes out but no slash is guessed");
            service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, 5, new JObject { ["ack"] = 0, ["p"] = 0, ["rej"] = new JArray() });
            skill.Release(PNSkillSlots.Attack);

            // ---- Skill, Ultimate and Burst are guessed too; a refusal gives each back (the body only moves with Move's owner role, so the motions are cancelled by the refusal here) ----
            long rev = 10; void Refuse(long refusedKey) { service.ReceiveState(target, PNSkillRemoteInput.ResultState, 1, (ulong)(++rev), new JObject { ["ack"] = 0, ["p"] = 0, ["rej"] = new JArray((object)new JArray(refusedKey, "declined")) }); }
            double skillReady = skill.SkillRecovery.Current;
            var ownAbilities = actor.GetComponent<PNAbilityManager>().Abilities; var staleImage = ownAbilities.CaptureReplica().ToList();
            long skillKey = Tap(PNSkillSlots.Skill);
            Check(skill.IsPredictingCast && skill.CurrentCast == EveCast.Skill, "the Skill press is guessed (cast " + skill.CurrentCast + ")");
            Check(skill.SkillRecovery.Current == skillReady - 1 && skill.Empowerment.IsActive, "the guess spends the Skill charge and arms the next attack (" + skill.SkillRecovery.Current + ", empowered " + skill.Empowerment.IsActive + ")");
            // G6b2: an authoritative image that lands while the guess waits does not make the spent Skill charge blink back
            Action ViewEvent(string name) => (Action)typeof(PNSkillOwnerView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
            ViewEvent("Applying")(); ownAbilities.ApplyReplica(staleImage); ViewEvent("Applied")();
            Check(skill.SkillRecovery.Current == skillReady - 1, "an image under the waiting Skill guess keeps its spent charge shown (" + skill.SkillRecovery.Current + ")");
            Refuse(skillKey);
            Check(!skill.IsBusy && skill.SkillRecovery.Current == skillReady && !skill.Empowerment.IsActive, "the refusal gives the Skill back (charge " + skill.SkillRecovery.Current + ", empowered " + skill.Empowerment.IsActive + ")");

            double ultReady = skill.UltimateRecovery.Current;
            long ultKey = Tap(PNSkillSlots.Ultimate);
            Check(skill.CurrentCast == EveCast.Ultimate && skill.CurrentPhase == EveCastPhase.Retreat, "the Ultimate press is guessed (phase " + skill.CurrentPhase + ")");
            Check(skill.UltimateRecovery.Current == ultReady - 1, "the guess spends the Ultimate charge");
            Refuse(ultKey);
            Check(!skill.IsBusy && skill.UltimateRecovery.Current == ultReady, "the refusal gives the Ultimate back");

            var abilities = actor.GetComponent<PNAbilityManager>().Abilities;
            abilities.ApplyReplica(abilities.CaptureReplica().Select(i => i.Id == EveCharacterSkill.BurstGaugeId ? new PNAbilityReplicaItem(i.Id, i.Index, i.Maximum, i.Maximum, false, 0, false, 0) : i).ToList());
            double gaugeFull = skill.BurstGauge.Current;
            Check(!skill.IsInBurst && gaugeFull >= skill.Moveset.BurstCost, "the replica shows a full Burst gauge (" + gaugeFull + ")");
            long burstKey = Tap(PNSkillSlots.Burst);
            Check(skill.IsInBurst && skill.BurstGauge.Current < gaugeFull && !skill.IsPredictingCast, "the Burst press is guessed: Burst state on, gauge spent (burst " + skill.IsInBurst + ")");
            Refuse(burstKey);
            Check(!skill.IsInBurst && skill.BurstGauge.Current == gaugeFull, "the refusal ends the guessed Burst and gives the gauge back");

            // ---- Repeat: in Burst a tap is a normal attack, a held Attack is the Repeat chain; each is guessed when its gesture performs ----
            abilities.ApplyReplica(abilities.CaptureReplica().Select(i => i.Id == EveCharacterSkill.BurstStateId ? new PNAbilityReplicaItem(i.Id, i.Index, i.Amount, i.Maximum, true, 0, false, 0) : i.Id == EveCharacterSkill.SlashStacksId ? new PNAbilityReplicaItem(i.Id, i.Index, i.Maximum, i.Maximum, false, 0, false, 0) : i).ToList());
            double stacksFull = skill.SlashStacks.Current;
            Check(skill.IsInBurst && stacksFull >= 2, "the replica shows Burst with stacks (" + stacksFull + ")");
            long burstTap = Tap(PNSkillSlots.Attack);
            Check(skill.IsPredictingCast && skill.CurrentCast == EveCast.Attack, "in Burst a tapped Attack is a normal attack, guessed when the tap performs (cast " + skill.CurrentCast + ")");
            Refuse(burstTap); skill.Release(PNSkillSlots.Attack);
            Check(!skill.IsBusy && skill.SlashStacks.Current == stacksFull, "its refusal gives everything back (stacks " + skill.SlashStacks.Current + ")");
            skill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Repeat)); long pressKey = remote.LastSentKey; // the gesture's own Started key
            Check(skill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Performed, PNSkillInteraction.Repeat)) && skill.IsPredictingCast && skill.CurrentCast == EveCast.Repeat, "the held gesture firing is guessed as a Repeat stroke (cast " + skill.CurrentCast + ")");
            Check(skill.SlashStacks.Current == stacksFull - 1, "the guess spends a stack (" + skill.SlashStacks.Current + ")");
            Check(skill.IsRepeatHeld, "the held gesture is seen as held on this machine");
            var firstStroke = skill.CurrentInvocation; int casts0 = remote.PredictedCount;
            for (int i = 0; i < 600 && ReferenceEquals(skill.CurrentInvocation, firstStroke); i++) skill.Tick(.01f);
            Check(skill.IsPredictingCast && !ReferenceEquals(skill.CurrentInvocation, firstStroke) && skill.CurrentCast == EveCast.Repeat && remote.PredictedCount == casts0 + 1 && remote.PendingPredictions == 1,
                "the held gesture chains the next stroke as a guess of its own, kept with the same press (cast " + skill.CurrentCast + ", guesses " + (remote.PredictedCount - casts0) + ", presses " + remote.PendingPredictions + ")");
            skill.RequestSlot(PNSkillSlots.Attack, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Repeat));
            Check(!skill.IsRepeatHeld, "the release is seen: the chain stops at the end of its stroke");
            Refuse(pressKey);
            Check(!skill.IsBusy && skill.SlashStacks.Current == stacksFull && skill.IsInBurst, "the refusal gives every stack back and stops the stroke (stacks " + skill.SlashStacks.Current + ")");
            abilities.ApplyReplica(abilities.CaptureReplica().Select(i => i.Id == EveCharacterSkill.BurstStateId ? new PNAbilityReplicaItem(i.Id, i.Index, i.Amount, i.Maximum, false, 0, false, 0) : i).ToList());

            // ---- a predicted Dash on Eve: her Update advances it (the body only moves with Move's owner role, which a Player run has; here the guess is cancelled by hand) ----
            Check(Tap(PNSkillSlots.Dash) > 0 && skill.IsPredictingDash, "Eve's Dash is guessed through the same binding path");
            skill.Tick(.01f); Check(skill.IsDashing && !skill.IsBusy, "Eve's tick leaves a predicted Dash alone (cast " + skill.CurrentCast + ")");
            skill.TryInterrupt(EveId.Parse("project_n:test")); Check(!skill.IsDashing && !skill.IsPredictingDash, "cancelling the guess ends it");
            // ---- the owner's own client drew its casts itself: the cast block of S6 plays nothing there ----
            var castView = actor.AddComponent<PNSkillCastView>(); castView.OnBound(service, target);
            var castImage = (JObject)castView.Capture();
            service.ReceiveState(target, PNSkillCastView.State, 2, 100, castImage.DeepClone());
            castImage["n"] = 1; castImage["e"] = new JArray((object)new JArray(1, "sweep", "attack_right", 1f, 0f, .3f));
            service.ReceiveState(target, PNSkillCastView.State, 2, 101, castImage);
            Check(skill.IsOwnerClient && skill.ObservedActionCount == 0 && castView.PlayedCount == 0 && castView.LastSerial == 1, "the owner's client sees the serial but plays nothing (it drew the cast itself)");
            castView.OnUnbound();
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
        return "PASS " + n + " chain prediction checks (Attack guess, report instead of damage, rollback, pause, Skill / Ultimate / Burst guesses, Eve Dash guess)";
    }
}
#endif
