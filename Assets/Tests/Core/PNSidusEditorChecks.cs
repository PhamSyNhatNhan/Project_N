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

// Sidus on real components (Docs/SidusPlan.md, S4): a native PNEnemyStat and PNBuffManager, loaded through EveEntityLoader in a disposable preview scene; the real BaseContent plus a test pack.
// The clock check uses the real EveTimeScale component: a Sidus package on the Game clock runs at game speed whatever the entity's own time scale is.
public static class PNSidusEditorChecks
{
    private const string Pack = @"{schemaVersion:1,
      characters:[
        {id:'test:sidus_normal',data:{enemyTypeId:'base:normal'},stats:[{statId:'base:health',base:1000,initialCurrent:1000},{statId:'base:attack_power',base:10},{statId:'base:move_speed',base:1}]},
        {id:'test:sidus_boss',data:{enemyTypeId:'base:boss'},stats:[{statId:'base:health',base:1000,initialCurrent:1000},{statId:'base:attack_power',base:10},{statId:'base:move_speed',base:1}]}],
      extensions:{project_n:{buffs:[
        {id:'test:sidus_atk',factoryId:'base:buff_sidus_template',data:{duration:10,clock:'Game',modifiers:[{statId:'base:attack_power',value:5,group:0},{statId:'base:move_speed',value:0.5,group:0}]},tags:['project_n:buff','project_n:sidus'],
         multipleInstances:false,maximumInstances:1,scope:'Target',canDispel:true,showInUI:true,iconPath:'',glow:''},
        {id:'test:sidus_entity',factoryId:'base:buff_sidus_template',data:{duration:10,clock:'Entity',modifiers:[]},tags:['project_n:buff','project_n:sidus'],
         multipleInstances:false,maximumInstances:1,scope:'Target',canDispel:true,showInUI:true,iconPath:'',glow:''}]}}}";

    [MenuItem("Tools/PN/Run Sidus Checks")]
    private static void Menu()
    {
        try { Debug.Log(Run()); } catch (Exception error) { Debug.LogError("Sidus native checks failed: " + error); }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Sidus checks require idle Edit Mode and a stopped session.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var evidence = new List<string>(); int checks = 0; bool started = false;
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); evidence.Add("PASS " + text); checks++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments()
            .Concat(new[] { new EveContentDocument("native_sidus.json", Pack, "test", dependencies: new[] { "project_n" }) }).ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:native_sidus"));
            long next = 989100;
            (GameObject root, PNEnemyStat stat, PNBuffCollection buffs, EveEntityLoader loader) Enemy(string character, bool timeScale = false)
            {
                var root = new GameObject("Sidus native " + character); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
                root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(++next), EveId.Parse(character), 0);
                var stat = root.AddComponent<PNEnemyStat>(); var loader = root.AddComponent<EveEntityLoader>();
                if (timeScale) { var clock = root.AddComponent<EveTimeScale>(); clock.MinScale = 0; }
                loader.Load(hub); root.SetActive(true);
                var manager = root.GetComponent<PNBuffManager>(); manager.Advance(0);
                return (root, stat, manager.Buffs, loader);
            }
            PNBuffReceipt Apply(PNEnemyStat stat, PNBuffCollection buffs, string id) => buffs.Apply(new PNBuffApplyRequest(EveId.Parse(id), PNCombatSource.Capture(stat)));
            var attack = hub.Stats.ResolveIdentity(EveId.Parse("base:attack_power")); var speed = hub.Stats.ResolveIdentity(EveId.Parse("base:move_speed"));

            var n = Enemy("test:sidus_normal"); var b = Enemy("test:sidus_boss");
            Check(n.stat.EnemyTypeId == PNContentIds.NormalEnemy && b.stat.EnemyTypeId == PNContentIds.BossEnemy && n.stat.CanBeSidus && !b.stat.CanBeSidus, "native classification: normal can be Sidus, boss cannot");
            Check(!hub.EnemyTypes().Snapshot().Values.Any(x => x.Id.Path == "elite") && !hub.EnemyTypes().BindingSnapshot().Any(x => x.Id.Path == "elite"), "the elite enemy type is gone from the registry");

            Apply(n.stat, n.buffs, "test:sidus_atk");
            Check(n.stat.IsSidus && Math.Abs(n.stat.Evaluate(attack) - 15) < 1e-9 && Math.Abs(n.stat.Evaluate(speed) - 1.5) < 1e-9, "native normal enemy: the Sidus package grants the state and its modifiers");
            Apply(b.stat, b.buffs, "test:sidus_atk");
            Check(b.buffs.Snapshot().Count == 1 && !b.stat.IsSidus && Math.Abs(b.stat.Evaluate(attack) - 10) < 1e-9 && Math.Abs(b.stat.Evaluate(speed) - 1) < 1e-9, "native boss: the Buff is present but inert (no state, no modifier)");
            Check(n.stat.IsSidus && b.buffs.Snapshot().Count == 1, "entities are isolated from each other");

            // The Game clock ignores the entity's own time scale; the same package on the Entity clock follows it.
            n.buffs.Advance(2, .25, .25);
            Check(Math.Abs(n.buffs.Snapshot()[0].Remaining.Value - 8) < 1e-6, "Game clock: 2 s pass at game speed even with the entity slowed to 0.25 (remaining " + n.buffs.Snapshot()[0].Remaining + ")");
            Apply(n.stat, n.buffs, "test:sidus_entity");
            Check(n.buffs.Snapshot().Count == 1 && n.buffs.Snapshot()[0].Id == EveId.Parse("test:sidus_entity") && n.stat.IsSidus && Math.Abs(n.stat.Evaluate(attack) - 10) < 1e-9, "native replacement: the Entity-clock package replaces the Game-clock one and the old modifiers are returned");
            n.buffs.Advance(2, .25, .25);
            Check(Math.Abs(n.buffs.Snapshot()[0].Remaining.Value - 9.5) < 1e-6, "Entity clock: 2 s at entity speed 0.25 passes 0.5 s (remaining " + n.buffs.Snapshot()[0].Remaining + ")");
            n.buffs.Advance(40, 1, 1);
            Check(n.buffs.Snapshot().Count == 0 && !n.stat.IsSidus, "expiry returns the state");

            // Unload and destruction return everything.
            Apply(n.stat, n.buffs, "test:sidus_atk"); Check(n.stat.IsSidus, "applied again");
            n.loader.Unload();
            Check(!n.stat.IsSidus, "unloading the entity clears the Sidus state");
            n.loader.Load(hub);
            Check(!n.stat.IsSidus && Math.Abs(n.stat.Evaluate(attack) - 10) < 1e-9, "reloading starts without Sidus and without leftover modifiers");
            b.loader.Unload(); n.loader.Unload();
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            if (started) EveRegistrySession.End();
            if (!dirty && original.isDirty) throw new Exception("Sidus checks changed the active scene.");
        }
        return "Sidus native checks: PASS " + checks + "\n" + string.Join("\n", evidence);
    }
}
#endif
