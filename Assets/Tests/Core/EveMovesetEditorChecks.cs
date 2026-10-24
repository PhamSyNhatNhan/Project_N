#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EveMovesetEditorChecks
{
    public static void CleanupTestSession()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End();
    }
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Eve checks require idle Edit Mode.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene(); int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
            Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        long id = 986000;
        T Entity<T>(string name, Vector3 position, string character) where T : PNStat
        {
            var root = new GameObject(name); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            root.transform.position = position;
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(++id), EveId.Parse(character), 1);
            var stat = root.AddComponent<T>(); stat.gameObject.layer = stat is PNEnemyStat ? PNLayers.Enemy : stat is PNPlayerStat ? PNLayers.Player : stat.gameObject.layer;
            if (character == "project_n:eve")
            {
                root.AddComponent<Rigidbody>().useGravity = false;
                var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f; capsule.center = Vector3.up * .3f;
                root.AddComponent<PNPlayerMove>(); root.AddComponent<PNEveAttackSpeedProbe>();
            }
            else { var sphere = root.AddComponent<SphereCollider>(); sphere.radius = .08f; }
            root.AddComponent<EveEntityLoader>().Load(hub); root.SetActive(true); return stat;
        }
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:eve_moveset"));
        var areaRoot = new GameObject("Eve moveset area fixture"); SceneManager.MoveGameObjectToScene(areaRoot, preview);
        var areaManager = areaRoot.AddComponent<PNAreaManager>(); areaManager.Initialize(EveRegistrySession.Context);
        var projectileManager = areaRoot.AddComponent<PNProjectileManager>(); projectileManager.Initialize(EveRegistrySession.Context, new PNProjectileProfile { MaxActive = 20 }); projectileManager.ManualAdvance = true;
        try
        {
            var caster = Entity<EveCharacterStat>("Eve moveset probe", Vector3.zero, "project_n:eve");
            var skill = caster.GetComponent<PNEveAttackSpeedProbe>(); var manager = caster.GetComponent<PNAbilityManager>();
            var move = caster.GetComponent<PNPlayerMove>(); var loader = caster.GetComponent<EveEntityLoader>();
            caster.SetSource(EveId.Parse("test:crit_off"), new[] { new EveStatModifier(PNContentIds.CritRate, -20d, 0) });
            var near = Entity<DummyStat>("Nearest target", new Vector3(.45f, .4f, 0), "project_n:dummy");
            var low = Entity<DummyStat>("Lowest absolute HP", new Vector3(0, .4f, 3), "project_n:dummy"); low.SetCurrent(PNContentIds.Health, 10000);
            Physics.SyncTransforms();
            Check(skill.TryAttack(), "Normal attack failed.");
            Check(Vector3.Dot(skill.AttackVisual.AttackRoot.right, Vector3.right) > .99f, "Attack did not select nearest target.");
            skill.TickAttack(.22f);
            Check(skill.BurstGauge.Current == 2 && near.GetCurrent(PNContentIds.Health) < 200000000,
                "Normal hit did not grant two points exactly once.");
            skill.TickAttack(.3f); Check(skill.BurstGauge.Current == 2, "Same slash awarded points twice.");
            skill.TryAttack(); skill.TickAttack(.22f);
            Check(skill.BurstGauge.Current == 4, "Left attack did not award its separate hit."); skill.TickAttack(.3f);
            skill.TryAttack();
            Check(skill.AttackVisual.Radius > .6f, "Third horizontal attack did not grow its geometry.");
            skill.TickAttack(.4f);
            skill.UltimateRecovery.TryConsume(); double remaining = skill.UltimateRecovery.CooldownRemaining;
            Check(skill.TrySkill() && skill.BurstGauge.Current == 11, "Skill activation did not grant five points without damage.");
            Check(skill.UltimateRecovery.CooldownRemaining == remaining - 2 && skill.Empowerment.DurationRemaining == 10,
                "Skill did not reduce current ultimate cooldown or create ten-second empowerment.");
            Check(!skill.TryUltimate() && skill.TryDash(), "Dash failed to interrupt active skill."); skill.TickAttack(.2f);
            caster.transform.position = Vector3.zero; caster.GetComponent<Rigidbody>().position = Vector3.zero; Physics.SyncTransforms();
            Check(skill.TryAttack(), "Empowered Attack failed.");
            Check(skill.CurrentPhase == EveCastPhase.Rush && skill.AttackVisual == null, "Empowered attack skipped rush.");
            skill.TickAttack(.17f);
            Check(!skill.Empowerment.IsActive && skill.AttackVisual.Radius >= 1.9f,
                "Attack failed to consume empowerment or enlarge its hitbox."); skill.TickAttack(.4f);
            skill.BurstGauge.Add(150); Check(skill.TryBurst() && skill.SlashStacks.Current == 6, "Burst did not grant initial stacks.");
            int gaugeBefore = skill.BurstGauge.Current;
            caster.transform.position = Vector3.zero; caster.GetComponent<Rigidbody>().position = Vector3.zero; Physics.SyncTransforms();
            skill.TryAttack(); skill.TickAttack(.4f);
            Check(skill.SlashStacks.Current == 8 && skill.BurstGauge.Current == gaugeBefore,
                "Burst normal hit failed to add stacks without gauge.");
            skill.SkillRecovery.ResetToFull(); skill.TrySkill(); skill.TickAttack(.2f);
            Check(skill.SlashStacks.Current == 12 && skill.BurstGauge.Current == gaugeBefore, "Burst skill failed capped stack grant.");
            Check(skill.TryRepeatAttack() && skill.SlashStacks.Current == 11, "Repeat failed to spend stack.");
            skill.TickAttack(.2f); Check(skill.IsAttacking, "Repeat rush did not produce a slash.");
            manager.Advance(15);
            Check(!skill.IsBusy && !skill.IsInBurst && skill.SlashStacks.Current == 0 && move.CanMove,
                "Burst expiry failed immediate Repeat cancellation and cleanup.");
            Check(skill.BurstGauge.Current == gaugeBefore + 30, "Burst expiry refund was incorrect.");
            skill.BurstGauge.Add(150); skill.TryBurst(); skill.UltimateRecovery.ResetToFull();
            caster.transform.position = Vector3.zero; caster.GetComponent<Rigidbody>().position = Vector3.zero; Physics.SyncTransforms();
            Check(skill.TryUltimate() && !skill.TryDash(), "Ultimate should reject dash interruption.");
            skill.TickAttack(.18f); Check(skill.CurrentPhase == EveCastPhase.Charge, "Ultimate did not enter charge after retreat.");
            skill.TickAttack(.99f); Check(skill.CurrentPhase == EveCastPhase.Charge, "Ultimate charge finished before one second.");
            skill.TickAttack(.011f); Check(skill.CurrentPhase == EveCastPhase.Rush, "Ultimate did not rush after charge.");
            skill.TickAttack(.16f);
            Check(skill.CurrentTarget == low && skill.AttackVisual != null,
                "Ultimate did not select lowest absolute HP target after charging: phase=" + skill.CurrentPhase +
                "; target=" + (skill.CurrentTarget == null ? "none" : skill.CurrentTarget.name));
            manager.Advance(15);
            Check(skill.IsInBurst && caster.Evaluate(PNContentIds.AttackPower) == 110, "Burst finisher lost bonuses at expiry.");
            skill.TickAttack(.43f); Check(skill.IsAttacking && skill.IsInBurst, "Burst ultimate did not continue to second slash.");
            skill.TickAttack(.43f); Check(skill.IsAttacking && skill.IsInBurst, "Burst ultimate did not continue to third slash.");
            skill.TickAttack(.43f);
            Check(!skill.IsBusy && !skill.IsInBurst && skill.SlashStacks.Current == 0 && move.CanMove,
                "Third ultimate slash did not finish Burst and restore movement.");
            Check(caster.Evaluate(PNContentIds.AttackPower) == 10 && caster.Evaluate(PNContentIds.AttackSpeed) == 100,
                "Finisher leaked Burst modifiers.");
            near.gameObject.SetActive(false); low.gameObject.SetActive(false);
            move.SetMoveDirection(Vector2.left); skill.SkillRecovery.ResetToFull();
            Vector3 before = caster.transform.position; Check(skill.TrySkill(), "No-target skill could not activate."); skill.TickAttack(.2f);
            Check(caster.transform.position.x < before.x && move.FacingDirection < 0,
                "No-target skill did not use movement fallback and flip: before=" + before + "; after=" + caster.transform.position + "; facing=" + move.FacingDirection);
            skill.BurstGauge.Add(150); skill.TryBurst(); caster.SetCurrent(PNContentIds.Health, 0); skill.TickAttack(.1f);
            // Damage-based death event is covered separately; next Update must also clear effects on direct HP edits.
            Check(!skill.IsBusy && !skill.IsInBurst && skill.BurstGauge.Current == 0 && skill.SlashStacks.Current == 0,
                "Dead caster retained cast, Burst effects or resources.");
            loader.Unload();
        }
        finally
        {
            try { projectileManager.Shutdown(); areaManager.Shutdown(); EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(original.isDirty == dirty, "Eve test changed active scene dirty state.");
        return "PASS " + checks + " native Eve moveset checks; real target selection, hits, rewards, burst expiry, finisher, movement; preview cleaned.";
    }
}
#endif
