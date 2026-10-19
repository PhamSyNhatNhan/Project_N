#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Native lifecycle/clock checks in a disposable preview; never modify/save the gameplay scene.
// VI: Kiểm tra vòng đời/đồng hồ thật trong preview dùng tạm; không sửa/lưu scene gameplay.
public sealed class PNAbilitySkillProbe : PNSkill
{
    public void ApplyReduction(PNAbility ability) => ApplyCooldownReduction(ability);
}

public static class PNAbilityEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Stop Play Mode and the session before running native ability checks.");
        var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty;
        var preview = EditorSceneManager.NewPreviewScene(); bool started = false; int checks = 0;
        Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); checks++; };
        Func<double, double, bool> near = (a, b) => Math.Abs(a - b) < .00001;
        var gameId = EveId.Parse("test:native_counter_game");
        var entityId = EveId.Parse("test:native_counter_entity");
        var rawId = EveId.Parse("test:native_counter_raw");
        var stateId = EveId.Parse("test:native_state");
        var chargeId = EveId.Parse("test:native_charge");
        try
        {
            var hub = new EveRegistryHub(); new PNContentModule().Register(hub);
            hub.GetOrCreate<EveSkillDefinition>();
            var hero = EveId.Parse("test:native_ability_entity");
            // PNStat auto-adds PNStatReplication, which reads Health at commit; real entities always have it.
            hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(hero, new[] { new EveInitialStat(PNContentModule.Health, 100d, 100d) }));
            hub.Seal(); EveRegistrySession.Begin(); started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline,
                EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:native_abilities"));
            var root = new GameObject("Native passive Ability fixture"); root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, preview);
            var identity = root.AddComponent<EveEntityIdentity>(); identity.Assign(new EveEntityId(981001), hero, 0);
            var clock = root.AddComponent<EveTimeScale>(); clock.MinScale = 1;
            var manager = root.AddComponent<PNAbilityManager>();
            manager.Configure(new[]
            {
                new PNAbilitySettings { Id = gameId.ToString(), Kind = PNAbilityKind.Counter, Maximum = 3, InitialAmount = 3,
                    HasDuration = true, Duration = 10 }.Create(),
                new PNAbilitySettings { Id = entityId.ToString(), Kind = PNAbilityKind.Counter, Maximum = 3, InitialAmount = 3,
                    HasDuration = true, Duration = 10, Clock = PNAbilityClock.Entity }.Create(),
                new PNAbilitySettings { Id = rawId.ToString(), Kind = PNAbilityKind.Counter, Maximum = 3, InitialAmount = 3,
                    HasDuration = true, Duration = 10, Clock = PNAbilityClock.EntityWithoutMinimum }.Create(),
                new PNAbilitySettings { Id = stateId.ToString(), Kind = PNAbilityKind.State, HasDuration = true, Duration = 5,
                    Cooldown = 2, CooldownStart = PNAbilityCooldownStart.OnEnd, Reapply = PNAbilityReapply.Independent }.Create(),
                new PNAbilitySettings { Id = chargeId.ToString(), Kind = PNAbilityKind.Cooldown, Maximum = 3, InitialAmount = 3,
                    Cooldown = 1 }.Create()
            });
            var stat = root.AddComponent<PNStat>();
            var skill = root.AddComponent<PNAbilitySkillProbe>(); var loader = root.AddComponent<EveEntityLoader>();
            root.SetActive(true); loader.Load(hub);
            check(manager.IsReady && skill.IsReady, "Manager and data-less skill load independently.");
            check(root.GetComponents<PNAbilityManager>().Length == 1, "Required component does not duplicate manager.");
            check(ReferenceEquals(manager.Abilities, skill.Abilities), "Skill reads entity-owned state.");
            var game = manager.Abilities.GetCounter(gameId); var entity = manager.Abilities.GetCounter(entityId);
            var raw = manager.Abilities.GetCounter(rawId); var charges = manager.Abilities.GetCooldown(chargeId);
            long zone = clock.AddTimedModifier(EveId.Parse("test:native_zone"), EveTimeModifierKind.Scale, -1, 20);
            manager.Advance(2);
            check(game.DurationRemaining == 8, "Game clock ignores entity time scale.");
            check(entity.DurationRemaining == 8, "Entity minimum one protects effective clock.");
            check(raw.DurationRemaining == 10, "Raw clock freezes despite entity minimum one.");
            clock.RemoveModifier(zone); clock.AddTimedModifier(EveId.Parse("test:native_zone"), EveTimeModifierKind.Scale, -.5f, 20);
            manager.Advance(2);
            check(raw.DurationRemaining == 9 && entity.DurationRemaining == 6, "Raw half speed and effective minimum use distinct sources.");
            skill.enabled = false; manager.Advance(1);
            check(game.DurationRemaining == 5 && near(raw.DurationRemaining.Value, 8.5), "Disabled skill does not freeze abilities.");
            var beforeDisable = game.DurationRemaining; root.SetActive(false); manager.Advance(3);
            check(game.DurationRemaining == beforeDisable && manager.IsReady, "Whole entity disable pauses and retains runtime.");
            root.SetActive(true); manager.Advance(1);
            check(game.DurationRemaining == 4, "Reenable resumes retained timer without catchup.");
            charges.TryConsume(); charges.TryConsume(); charges.TryConsume(); manager.Advance(2.5);
            check(charges.Current == 2 && near(charges.CooldownRemaining, .5), "Native large frame recovers multiple charges with surplus.");
            var first = manager.Abilities.GrantState(stateId); manager.Advance(2);
            var second = manager.Abilities.GrantState(stateId); manager.Advance(3.25);
            check(!first.IsActive && near(first.CooldownRemaining, 1.75), "State expiration starts cooldown inside same update.");
            check(second.IsActive && near(second.DurationRemaining.Value, 1.75), "Independent state retains its own lifetime.");
            manager.Advance(3.75);
            check(!manager.Abilities.TryGet(second.Handle, out _), "Completed transient state releases its handle.");
            check(!manager.Abilities.GetStates(stateId)[0].IsActive, "Named flag remains inactive instead of auto-activating.");
            manager.Abilities.ResetAll(); check(game.Current == 3 && game.DurationRemaining == 10, "Reset is a passive explicit state operation.");
            var cdrSource = EveId.Parse("test:native_cdr");
            stat.SetSource(cdrSource, new[] { new EveStatModifier(PNContentIds.CooldownReduction, 20d, 0) });
            skill.ApplyReduction(charges); charges.TryConsume(); manager.Advance(.2);
            check(near(charges.CooldownRemaining, .6), "Skill explicitly applies entity CDR to opted-in charges.");
            stat.SetSource(cdrSource, new[] { new EveStatModifier(PNContentIds.CooldownReduction, 50d, 0) });
            skill.ApplyReduction(charges);
            check(near(charges.CooldownRemaining, .6) && charges.ActualCooldown == .5, "Stat refresh preserves running period and changes next cooldown.");
            manager.Advance(charges.CooldownRemaining); charges.TryConsume();
            check(near(charges.CooldownRemaining, .5), "Next charge cycle uses updated stat reduction.");
            var optedState = manager.Abilities.GetStates(stateId)[0]; skill.ApplyReduction(optedState); optedState.TryActivate();
            check(optedState.DurationRemaining == 5, "Skill CDR leaves state duration unchanged.");
            optedState.End(); check(optedState.CooldownRemaining == 1, "Skill CDR applies to state OnEnd cooldown.");
            bool counterRejected = false;
            try { skill.ApplyReduction(game); } catch (ArgumentException) { counterRejected = true; }
            check(counterRejected, "Skill cannot apply cooldown reduction to counter lifetimes.");
            var old = game.Handle; loader.Unload();
            check(!manager.IsReady && !game.IsAttached, "Native unload disposes runtime.");
            loader.Load(hub);
            check(manager.IsReady && !manager.Abilities.TryGet(old, out _), "Reload cannot reuse old handles.");
            check(manager.Abilities.GetCounter(gameId).Current == 3, "Reload restores initial configuration.");
            loader.Unload();
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        if (scene.isDirty != dirty) throw new Exception("Ability checks changed active scene dirty state.");
        return "PASS " + checks + " native Ability checks; entity lifecycle, all three clocks, states, charges; preview cleaned; active scene dirty=" + dirty;
    }
}
#endif
