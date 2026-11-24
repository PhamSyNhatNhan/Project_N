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

// A generic skill (no character logic) that records how its casts end: the framework alone, not a character's own death handler, must end a cast when its owner dies.
public sealed class PNOwnerDeathProbeSkill : PNSkillEffectsNativeProbe
{
    public int Ended; public string LastReason; public bool LastCancelled;
    protected override void OnCastEnded(PNSkillInvocation invocation, bool cancelled, string reason) { Ended++; LastReason = reason; LastCancelled = cancelled; }
}

// Docs/SkillNetworkFrameworkPlan.md G9b, Edit Mode: the owner dies in the middle of a cast and the cast ends once, as cancelled, with the reason `died` (PNCastEnd.Died), for an enemy (DummyStat) and for a summon-like
// player-side entity (PNEve2Stat, the pet); the execution list of the skill is empty afterwards. Eve's own version of this check lives in EveCastCancelChecks (death at every cast).
public static class PNOwnerDeathCastChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Owner death check " + n + ": " + what); n++; }
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNSkillEffectsNativeModule() }, docs);
        long id = 987600;
        var created = new List<GameObject>();
        T Entity<T>(string name, string character, out PNOwnerDeathProbeSkill probe, bool body = false) where T : PNStat
        {
            var root = new GameObject(name); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview); created.Add(root);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(++id), EveId.Parse(character), 1);
            var stat = root.AddComponent<T>().Declared(); root.AddComponent<PNAbilityManager>(); probe = root.AddComponent<PNOwnerDeathProbeSkill>();
            if (body) { var rb = root.AddComponent<Rigidbody>(); rb.useGravity = false; var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f; root.AddComponent<PNPlayerMove>(); }
            else root.AddComponent<SphereCollider>().radius = .1f;
            root.AddComponent<EveEntityLoader>().Load(hub); root.SetActive(true); return stat;
        }
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:owner_death"));
        try
        {
            var killerRoot = new GameObject("Killer"); killerRoot.SetActive(false); SceneManager.MoveGameObjectToScene(killerRoot, preview); created.Add(killerRoot);
            killerRoot.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(++id), EveId.Parse("project_n:eve"), 0);
            var killer = killerRoot.AddComponent<PNStat>(); killerRoot.AddComponent<EveEntityLoader>().Load(hub); killerRoot.SetActive(true);
            PNOwnerDeathProbeSkill probe;
            var victims = new List<(string Name, PNStat Stat, PNOwnerDeathProbeSkill Skill)>();
            var monster = Entity<DummyStat>("Monster", "project_n:dummy", out probe); victims.Add(("monster", monster, probe));
            var pet = Entity<PNEve2Stat>("Pet", "project_n:eve_2", out probe, true); victims.Add(("summon", pet, probe));
            foreach (var victim in victims)
            {
                var skill = victim.Skill; var stat = victim.Stat;
                Check(stat.IsReady && skill.IsReady, victim.Name + ": the entity loaded");
                Check(skill.Begin(PNSkillEffectsNativeProbe.Attack) && skill.CurrentInvocation != null, victim.Name + ": a cast began");
                var invocation = skill.CurrentInvocation; int before = skill.Ended;
                stat.SetCurrent(PNContentModule.Health, 1f);
                var killed = killer.SendDamage(stat, 1000000f, PNContentModule.True);
                Check(stat.IsDead && killed.Killed, victim.Name + ": the owner died");
                Check(skill.CurrentInvocation == null && invocation.IsEnded, victim.Name + ": the cast is over");
                Check(skill.Ended == before + 1 && skill.LastCancelled && skill.LastReason == PNCastEnd.Died, victim.Name + ": the end hook ran once, cancelled, reason died (ended " + (skill.Ended - before) + ", reason " + skill.LastReason + ")");
                Check(skill.Effects.ActiveExecutionCount == 0, victim.Name + ": no execution is left (" + skill.Effects.ActiveExecutionCount + ")");
            }
        }
        finally
        {
            foreach (var o in created) { try { var l = o.GetComponent<EveEntityLoader>(); if (l != null) l.Unload(); } catch (Exception) { } if (o != null) o.SetActive(false); }
            try { EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(original.isDirty == dirty, "the active scene stayed as it was");
        return "PASS " + n + " owner death cast checks (an enemy and a summon-like owner die mid-cast: one end, cancelled, reason died, nothing left running)";
    }
}
#endif
