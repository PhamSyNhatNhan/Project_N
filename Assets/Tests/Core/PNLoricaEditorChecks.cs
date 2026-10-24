#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNLoricaSkillProbe : PNEnemySkill
{
    public int Interrupts; public EveId LastReason;
    public override bool TryInterrupt(EveId reason) { Interrupts++; LastReason = reason; return true; }
}

// EN: Enemy lorica with real components: the break Buff denies movement and skills on the real Control state and interrupts the running cast,
//     the Entity clock (TimeScale) stretches the break and the recovery lock, and the shipped Eve JSON content still loads with the new stat.
// VI: Lorica của enemy với component thật: Buff vỡ khoá Control thật và ngắt chiêu đang chạy, đồng hồ Entity (TimeScale) kéo dài thời gian.
public static class PNLoricaEditorChecks
{
    private const string Characters = "{\"schemaVersion\":1,\"characters\":[{\"id\":\"test:lorica_native\",\"data\":{\"lorica\":{\"share\":0.6,\"breakSeconds\":7,\"recoverySeconds\":5}},\"stats\":[{\"statId\":\"base:health\",\"base\":100000},{\"statId\":\"base:lorica\",\"base\":100}],\"skill\":[]}]}";
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene(); int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var baseDocs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var docs = baseDocs.Concat(new[] { new EveContentDocument("lorica native", Characters, "test", dependencies: new[] { "project_n" }) }).ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        Check(hub.Characters.Get(EveId.Parse("project_n:eve")) != null && hub.Characters.Get(EveId.Parse("project_n:dummy")) != null, "the shipped content (Eve, Dummy) loads with the lorica stat registered");
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:lorica_native"));
        GameObject enemyRoot = null;
        try
        {
            enemyRoot = new GameObject("Lorica enemy"); enemyRoot.SetActive(false); SceneManager.MoveGameObjectToScene(enemyRoot, preview);
            enemyRoot.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(987500), EveId.Parse("test:lorica_native"), 0);
            var stat = enemyRoot.AddComponent<PNEnemyStat>(); var skill = enemyRoot.AddComponent<PNLoricaSkillProbe>(); var clock = enemyRoot.AddComponent<EveTimeScale>();
            enemyRoot.AddComponent<SphereCollider>().radius = .3f; enemyRoot.AddComponent<EveEntityLoader>().Load(hub); enemyRoot.SetActive(true);
            enemyRoot.GetComponent<PNBuffManager>().Advance(0); // the first Update resumes the Buff collection (it is prepared suspended)
            var controls = enemyRoot.GetComponent<PNControlManager>().State; var manager = enemyRoot.GetComponent<PNBuffManager>(); var buffs = manager.Buffs;
            PNDamageResult Hit(double amount) => stat.ReceiveDamage(new PNDamage(stat.SessionId, new EveEntityId(1), amount, new[] { PNContentIds.Physical }).WithIFrameDuration(0d));
            Check(stat.HasLorica && controls.IsAllowed(PNControlIds.Move) && controls.IsAllowed(PNControlIds.SkillAll) && controls.IsAllowed(PNControlIds.VoluntaryMotion), "a fresh enemy with lorica may move and use skills");
            Hit(100); Check(stat.LoricaCurrent == 40 && controls.IsAllowed(PNControlIds.Move) && skill.Interrupts == 0, "sharing a hit stuns nothing");
            Hit(100);
            if (!(stat.LoricaCurrent == 0 && buffs.Snapshot().Any(x => x.Id == PNContentIds.LoricaBreak)))
            {
                var probe = buffs.Apply(new PNBuffApplyRequest(PNContentIds.LoricaBreak, PNCombatSource.Capture(stat), Newtonsoft.Json.Linq.JObject.Parse("{\"duration\":7}")));
                throw new Exception("breaking did not apply the Buff: lorica=" + stat.LoricaCurrent + " buffs=" + string.Join(",", buffs.Snapshot().Select(x => x.Id.ToString())) + " manual apply=" + probe.Status + " " + probe.Message + " " + probe.Error);
            }
            checks++;
            Check(!controls.IsAllowed(PNControlIds.Move) && !controls.IsAllowed(PNControlIds.Jump) && !controls.IsAllowed(PNControlIds.VoluntaryMotion) && !controls.IsAllowed(PNControlIds.SkillAll), "the break denies moving, jumping, voluntary motion and every skill (skill_all) on the real Control state");
            Check(skill.Interrupts == 1, "the break interrupts the running cast exactly once");

            // The break lasts 7 entity seconds: at half speed it needs 14 game seconds.
            clock.AddTimedModifier(EveId.Parse("test:slow"), EveTimeModifierKind.Scale, -.5f, 600f);
            Check(Math.Abs(clock.Scale - .5f) < 1e-4f, "the enemy clock runs at half speed");
            manager.Advance(7.1); Check(buffs.Snapshot().Any(x => x.Id == PNContentIds.LoricaBreak) && stat.LoricaCurrent == 0, "a slowed enemy stays broken beyond 7 game seconds (entity clock)");
            manager.Advance(7.0);
            Check(!buffs.Snapshot().Any(x => x.Id == PNContentIds.LoricaBreak) && stat.LoricaCurrent == 100 && controls.IsAllowed(PNControlIds.Move) && controls.IsAllowed(PNControlIds.SkillAll), "after 14 game seconds the break ends: lorica refilled, movement and skills allowed again");
            Check(buffs.Snapshot().Any(x => x.Id == PNContentIds.LoricaRecovery) && stat.IsLoricaLocked, "the recovery lock starts");
            var held = Hit(100); Check(held.LoricaBroke == false && stat.LoricaCurrent == 100 && Math.Abs(held.AppliedDamage - 40) < 1e-6, "during recovery a hit shares but lorica holds");
            manager.Advance(9.9); Check(stat.IsLoricaLocked, "the recovery also follows the entity clock (5 entity seconds at half speed)");
            manager.Advance(.2); Check(!stat.IsLoricaLocked && !buffs.Snapshot().Any(x => x.Id == PNContentIds.LoricaRecovery), "the recovery lock is returned when it ends");
            clock.ClearModifiers();
            Hit(100); Check(stat.LoricaCurrent == 40, "after recovery lorica goes down again");

            // A time-stopped enemy keeps its break.
            Hit(100); Check(stat.LoricaCurrent == 0, "second break");
            clock.AddTimedModifier(EveId.Parse("test:stop"), EveTimeModifierKind.Scale, -1f, 600f);
            manager.Advance(30); Check(buffs.Snapshot().Any(x => x.Id == PNContentIds.LoricaBreak), "a time-stopped enemy stays broken (the area timestop matters)");
            clock.ClearModifiers();
        }
        finally
        {
            try { if (enemyRoot != null) enemyRoot.GetComponent<EveEntityLoader>()?.Unload(); EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(original.isDirty == dirty, "the test left the active scene dirty state alone");
        return "PASS " + checks + " native lorica checks; real Control state, interrupt, entity clock";
    }
}
#endif
