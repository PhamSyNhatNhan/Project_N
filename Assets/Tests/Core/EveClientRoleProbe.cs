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

// Docs/SkillNetworkingAPlan.md S0: what a CLIENT-role session does to Eve today (no authority). Report only, nothing is asserted: each probe records what happened
// (value, refusal or exception text) in .utmp/EveClientProbe/result.txt. Edit Mode, preview scene, nothing saved.
public sealed class EveClientRoleProbeSkill : EveCharacterSkill
{
    public void Prepare() { if (pc == null) Awake(); OnEnable(); }
    public string Gates() => "IsReady=" + IsReady + " CanInput=" + CanInput + " InputArmed=" + InputArmed + " HasGameplayAuthority=" + HasGameplayAuthority + " sessionHasAuthority=" + EveRegistrySession.Context.HasAuthority;
    public bool Slot(EveId slot) => RequestSlot(slot, new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap));
    public void Tick(float delta) => AdvanceAttack(delta);
    public bool Release(EveId slot) => RequestSlot(slot, new PNSkillInputEvent(PNSkillInputPhase.Canceled, PNSkillInteraction.Tap));
    public bool Press(EveId slot, PNSkillInputPhase phase) => RequestSlot(slot, new PNSkillInputEvent(phase, PNSkillInteraction.Tap));
}

public static class EveClientRoleProbe
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var lines = new List<string>(); void Note(string label, Func<object> probe)
        {
            string text; try { text = Convert.ToString(probe(), System.Globalization.CultureInfo.InvariantCulture); } catch (Exception e) { text = "THROWS " + e.GetType().Name + ": " + e.Message; }
            lines.Add(label + " => " + text);
        }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject Root(string name, long id, string character, Vector3 position)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, preview); obj.transform.position = position;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), 0); return obj;
        }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:s0_client_probe"));
            lines.Add("session role=" + EveRegistrySession.Context.Role + " HasAuthority=" + EveRegistrySession.Context.HasAuthority);
            var areaRoot = new GameObject("S0 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, preview); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var actor = Root("S0 Eve (client)", 995001, "project_n:eve", Vector3.zero); actor.layer = PNLayers.Player; var stat = actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>();
            Note("Eve skill Prepare (Awake/OnEnable)", () => { skill.Prepare(); return "ok"; });
            var loader = actor.AddComponent<EveEntityLoader>();
            Note("Eve loader.Load as client", () => { loader.Load(hub); actor.SetActive(true); return "loaded IsReady=" + loader.IsReady; });
            var dummyObject = Root("S0 dummy", 995002, "project_n:dummy", new Vector3(.45f, .4f, 0)); dummyObject.layer = PNLayers.Enemy;
            var dummy = dummyObject.AddComponent<PNEnemyStat>(); dummyObject.AddComponent<SphereCollider>().radius = .15f; dummyObject.AddComponent<EveEntityLoader>().Load(hub); dummyObject.SetActive(true);
            Physics.SyncTransforms();

            Note("1 gates", () => skill.Gates());
            Note("1 CanActivateSkill(attack entry)", () => skill.CanActivateSkill(PNControlIds.Attack, PNGameplayContentModule.EveAttackEntry));
            Note("1 RequestSlot(Attack, Tap Started)", () => skill.Slot(PNSkillSlots.Attack));
            Note("1 TryAttack(right)", () => skill.TryAttack(Vector3.right));
            Note("1 TrySkill", () => skill.TrySkill());
            Note("1 TryDash", () => skill.TryDash());
            Note("1 CurrentCast after attempts", () => skill.CurrentCast);
            Note("1 dummy HP after attempts (hits would lower it)", () => dummy.GetCurrent(PNContentIds.Health));

            Note("2 Ability values on the client (defaults, not synced): burst gauge", () => skill.BurstGauge.Current);
            Note("2 dash charges", () => skill.DashRecovery.Current);
            Note("2 skill ready", () => skill.SkillRecovery.Current);
            Note("2 ability write BurstGauge.Add(10)", () => { skill.BurstGauge.Add(10); return skill.BurstGauge.Current; });
            Note("2 ability write SlashStacks.Add(1)", () => { skill.SlashStacks.Add(1); return skill.SlashStacks.Current; });

            var buffs = dummy.GetComponent<PNBuffManager>().Buffs; PNCombatSource capture = default; Note("3 PNCombatSource.Capture(Eve) on a client", () => { capture = PNCombatSource.Capture(stat); return "ok"; });
            Note("3 Buff apply on dummy (base:burn) from the client", () => { var r = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:burn"), capture, JObject.Parse("{'stacks':1}"))); return "handleAlive=" + r.Handle.IsAlive + " snapshots=" + buffs.Snapshot().Count(); });
            var eveBuffs = actor.GetComponent<PNBuffManager>().Buffs;
            Note("3 Buff apply on Eve (base:burn) from the client", () => { var r = eveBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:burn"), PNCombatSource.Capture(dummy), JObject.Parse("{'stacks':1}"))); return "handleAlive=" + r.Handle.IsAlive + " snapshots=" + eveBuffs.Snapshot().Count(); });
            Note("3 Buff coordinator Submit on Eve", () => { bool ran = false; eveBuffs.Coordinator.Submit(() => ran = true); return "ran=" + ran; });

            Note("4 damage ReceiveDamage on dummy", () => { dummy.ReceiveDamage(stat.CreateDamage(100, false, 0d, PNContentIds.True)); return "hp=" + dummy.GetCurrent(PNContentIds.Health); });
            Note("4 stat SetCurrent on dummy", () => { dummy.SetCurrent(PNContentIds.Health, 5); return "hp=" + dummy.GetCurrent(PNContentIds.Health); });
            Note("4 stat SetSource on Eve", () => { stat.SetSource(EveId.Parse("test:s0"), new[] { new EveStatModifier(PNContentIds.AttackPower, 1, 0) }); return "atk=" + stat.Evaluate(PNContentIds.AttackPower); });

            Note("5 Area create from a client", () => PNAreaManager.Resolve(EveRegistrySession.Context, preview).Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), JObject.Parse("{'radius':1,'duration':1}"), capture, preview, Vector3.zero)).Status);
        }
        finally
        {
            try
            {
                foreach (var root in preview.GetRootGameObjects()) if (root != null) foreach (var loader in root.GetComponentsInChildren<EveEntityLoader>(true)) { try { loader.Unload(); } catch (Exception) { } }
                foreach (var root in preview.GetRootGameObjects()) if (root != null) root.SetActive(false);
            }
            finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(preview); } }
        }
        Directory.CreateDirectory(".utmp/EveClientProbe"); File.WriteAllLines(".utmp/EveClientProbe/result.txt", lines);
        return string.Join("\n", lines);
    }
}
#endif
