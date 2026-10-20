#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class EveBurstBuffProbe : EveCharacterSkill
{
    public void Tick(float delta) => AdvanceAttack(delta);
    public void Hold() => RepeatAttack();
    public void Release() => CanceledRepeatAttack();
    public void PrepareInput() { if (pc == null) Awake(); OnEnable(); }
    public void DisableReview() { enabled = false; OnDisable(); }
}
public static class EveBurstBuffEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); int count = 0; var evidence = new List<string>(); EveBurstBuffProbe skill = null;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        void AdvanceUntil(Func<bool> ready) { for (int i = 0; i < 1000 && !ready(); i++) skill.Tick(.005f); if (!ready()) throw new Exception("Fixture timed out"); }
        GameObject Root(string name, long id, string character, Vector3 position)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, preview); obj.transform.position = position;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), 0); return obj;
        }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:eve_burst_buffs"));
            var actor = Root("Eve Burst Buff source", 989201, "project_n:eve", Vector3.zero); var stat = actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); skill = actor.AddComponent<EveBurstBuffProbe>(); skill.PrepareInput(); var loader = actor.AddComponent<EveEntityLoader>(); loader.Load(hub); actor.SetActive(true); actor.GetComponent<PNBuffManager>().Advance(0);
            var enemies = new List<PNEnemyStat>();
            for (int i = 0; i < 2; i++)
            {
                var obj = Root("Burst Buff target " + i, 989202 + i, "project_n:dummy", new Vector3(.45f + i * .12f, .4f, 0));
                var enemy = obj.AddComponent<PNEnemyStat>(); obj.AddComponent<SphereCollider>().radius = .15f; obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); obj.GetComponent<PNBuffManager>().Advance(0); enemies.Add(enemy);
            }
            Physics.SyncTransforms(); var burnId = hub.Buffs().ResolveIdentity(EveId.Parse("base:burn"));
            int Burn(PNEnemyStat enemy) => (int)(enemy.GetComponent<PNBuffManager>().Buffs.Snapshot().FirstOrDefault(x => x.Id == burnId)?.Stacks ?? 0);
            Check(skill.TryAttack(Vector3.right), "Normal Attack starts outside Burst."); AdvanceUntil(() => !skill.IsBusy);
            Check(enemies.All(x => Burn(x) == 0), "Outside Burst normal hit does not apply Burn.");
            skill.BurstGauge.Add(150); Check(skill.TryBurst() && skill.BlazingAssault != null, "Burst grants registered Blazing Assault.");
            Check(skill.TryAttack(Vector3.right), "Normal Attack starts inside Burst."); AdvanceUntil(() => !skill.IsBusy);
            Check(enemies.All(x => Burn(x) == 1), "Each valid Burst normal hit applies one Burn stack per target.");
            Check(skill.RisingMomentum == null, "Normal Attack does not grant Rising Momentum.");
            var external = EveId.Parse("test:external_momentum_owner"); stat.SetSource(external, new[] { new EveStatModifier(PNContentIds.AttackPower, .2, 2), new EveStatModifier(PNContentIds.AttackSpeed, 7, 0) });
            skill.Hold(); AdvanceUntil(() => skill.RisingMomentum != null && enemies.All(x => Burn(x) >= 2));
            Check(skill.RisingMomentum.Stacks == 1 && enemies.All(x => Burn(x) == 2), "Repeat hitting two targets adds one Momentum tier and Burn to each: momentum=" + skill.RisingMomentum.Stacks + "; burn=" + string.Join(",", enemies.Select(Burn)));
            Check(Math.Abs(stat.Evaluate(PNContentIds.AttackPower) - 113) < 1e-8 && stat.Evaluate(PNContentIds.AttackSpeed) == 167, "Momentum ATK is additive in group2 (Burst flat +100 routes after the groups: 10*(1+.2+.1)+100); speed is ten percentage points.");
            AdvanceUntil(() => skill.RisingMomentum?.Stacks == 2 && enemies.All(x => Burn(x) >= 3));
            Check(enemies.All(x => Burn(x) == 3) && stat.Evaluate(PNContentIds.AttackSpeed) == 177, "Next hit in same held chain accumulates second tier.");
            skill.Release(); Check(skill.RisingMomentum?.Stacks == 2, "Release retains bonus until current stroke finishes."); AdvanceUntil(() => !skill.IsBusy);
            Check(skill.RisingMomentum == null && stat.Evaluate(PNContentIds.AttackPower) == 112 && stat.Evaluate(PNContentIds.AttackSpeed) == 157, "End of held chain removes only its modifiers, preserving Burst/equipment.");
            Check(enemies.All(x => Burn(x) == 3), "Ending chain leaves target Burn alive.");
            foreach (var enemy in enemies) enemy.gameObject.SetActive(false); skill.SlashStacks.Clear(); skill.SlashStacks.Add(1); skill.Release(); skill.Hold(); AdvanceUntil(() => !skill.IsBusy);
            Check(skill.RisingMomentum == null && skill.SlashStacks.Current == 0, "Miss consumes slash resource but grants no Momentum; exhausted chain resets."); skill.Release();
            foreach (var enemy in enemies) enemy.gameObject.SetActive(true); body.position = actor.transform.position = Vector3.zero; Physics.SyncTransforms(); skill.SlashStacks.Add(4);
            skill.Hold(); AdvanceUntil(() => skill.RisingMomentum != null); Check(skill.TryDash(), "Dash interrupts repeat chain.");
            Check(skill.RisingMomentum == null && skill.BlazingAssault != null && stat.Evaluate(PNContentIds.AttackSpeed) == 157, "Interrupt resets Momentum without ending Burst Buff."); AdvanceUntil(() => !skill.IsBusy);
            body.position = actor.transform.position = Vector3.zero; Physics.SyncTransforms(); skill.Release(); skill.Hold(); AdvanceUntil(() => skill.RisingMomentum != null);
            skill.BurstState.End(); Check(!skill.IsInBurst && !skill.IsBusy && skill.RisingMomentum == null && skill.BlazingAssault == null, "Burst end cancels chain and removes both exact owned Buffs.");
            Check(Math.Abs(stat.Evaluate(PNContentIds.AttackPower) - 12) < 1e-8 && stat.Evaluate(PNContentIds.AttackSpeed) == 107, "Burst cleanup preserves independent stat owner.");
            var buffs = actor.GetComponent<PNBuffManager>().Buffs;
            buffs.Coordinator.Submit(() => { skill.BurstGauge.Add(150); if (!skill.TryBurst()) throw new Exception("Pending Burst failed"); skill.BurstState.End(); });
            Check(!skill.IsInBurst && !buffs.Snapshot().Any(x => x.Id == EveBurstBuffContent.BlazingAssault), "Pending Apply followed by Burst end cannot strand contribution Buff.");
            skill.BurstGauge.Add(150); skill.TryBurst(); body.position = actor.transform.position = Vector3.zero; Physics.SyncTransforms(); skill.Release(); skill.Hold(); AdvanceUntil(() => skill.RisingMomentum != null);
            skill.DisableReview(); Check(skill.RisingMomentum == null && skill.BlazingAssault != null, "Explicit native disable path ends chain but retains entity-owned Burst Buff."); skill.enabled = true;
            stat.SetCurrent(PNContentIds.Health, 0); skill.Tick(.01f);
            Check(!skill.IsInBurst && !buffs.Snapshot().Any(x => x.Id == EveBurstBuffContent.BlazingAssault || x.Id == EveBurstBuffContent.RisingMomentum), "Source death clears Eve-owned Buffs.");
            loader.Unload(); loader.Load(hub); Check(skill.BlazingAssault == null && skill.RisingMomentum == null, "Reload starts without previous Burst/chain handles.");
        }
        finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(preview); } }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + count + " native Eve Burst Buff checks"; Directory.CreateDirectory(".utmp/EveBurstBuffReview"); File.WriteAllLines(".utmp/EveBurstBuffReview/NativeResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}
#endif
