#if UNITY_EDITOR
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Opt-in native component checks in a disposable preview scene; never save gameplay scenes.
// VI: Kiểm tra component thật khi gọi tường minh trong preview scene dùng tạm; không lưu scene gameplay.
public static class PNDamageFlowEditorChecks
{
    public static string RunFlatScale()
    {
        if (UnityEditor.EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Idle Edit Mode is required for flat Scale checks.");
        var active = SceneManager.GetActiveScene();
        bool dirtyBefore = active.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        bool started = false;
        int checks = 0;
        Action<bool, string> check = (ok, name) => { if (!ok) throw new Exception(name); checks++; };
        Func<double, double, bool> near = (a, b) => Math.Abs(a - b) < .00001d;
        try
        {
            var hub = new EveRegistryHub();
            new PNContentModule().Register(hub);
            var hero = EveId.Parse("test:native_flat_hero");
            hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(hero, new[] {
                new EveInitialStat(PNContentModule.Health, 10000d, 10000d),
                new EveInitialStat(PNContentModule.AttackPower, 100d),
                new EveInitialStat(PNContentModule.Defense, 500d)
            }));
            hub.Seal();
            EveRegistrySession.Begin(); started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline,
                EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:native_flat_scale"));
            var sender = Make(preview, hub, hero, 913001);
            var receiver = Make(preview, hub, hero, 913002);
            var bonus = EveId.Parse("test:flat_bonus");
            sender.SetSource(bonus, new[] {
                new EveStatModifier(PNContentModule.AttackPower, 50d, 0),
                new EveStatModifier(PNContentModule.AttackPower, .2d, 1),
                new EveStatModifier(PNContentModule.Fire, .2d, 1),
                new EveStatModifier(PNContentModule.Physical, .4d, 1),
                new EveStatModifier(PNContentModule.Fire, 100d, 0)
            });
            check(near(sender.Evaluate(PNContentModule.AttackPower), 170d), "Base flat stat regression");
            check(near(sender.Evaluate(PNContentModule.AttackPower, PNContentModule.Fire), 190d), "Combined Evaluate excludes flat");
            check(sender.Evaluate(PNContentModule.Fire) == 0d, "Tag Evaluate excludes flat");
            double value = new PNSkillValue(500d, PNContentModule.AttackPower).Evaluate(sender);
            check(near(value, 850d), "Skill multiplier before outgoing flat");
            var normal = sender.CreateDamage(value, false, PNContentModule.Physical, PNContentModule.Fire, PNContentModule.Fire);
            check(near(normal.Amount, 1460d), "Late flat and duplicate tags");
            receiver.SetSource(EveId.Parse("test:flat_resistance"), new[] {
                new EveStatModifier(PNContentModule.Fire, -.2d, 1, PNContentModule.Mitigation)
            });
            var first = receiver.ReceiveDamage(normal);
            check(near(first.AppliedDamage, 584d) && near(first.HealthAfter, 9416d), "Normal DEF and mitigation HP commit");
            sender.SetSource(EveId.Parse("test:flat_crit"), new[] {
                new EveStatModifier(PNContentIds.CritRate, 100d, 0),
                new EveStatModifier(PNContentIds.CritDamage, 50d, 0)
            });
            var critical = sender.CreateDamage(value, PNContentModule.Physical, PNContentModule.Fire);
            check(critical.IsCritical && near(critical.Amount, 2920d), "Crit multiplies late flat");
            receiver.AdvanceCombatTime(1d);
            var hit = receiver.ReceiveDamage(critical);
            check(near(hit.AppliedDamage, 1168d), "Crit DEF mitigation");
            var protectedHit = receiver.ReceiveDamage(critical);
            check(near(protectedHit.AppliedDamage, 350.4d), "Iframe multiplies final received damage");
            sender.RemoveSource(bonus);
            check(near(sender.Evaluate(PNContentModule.AttackPower, PNContentModule.Fire), 100d), "Removal invalidates Evaluate cache");
            sender.GetComponent<EveEntityLoader>().Unload();
            receiver.AdvanceCombatTime(1d);
            check(near(receiver.ReceiveDamage(critical).AppliedDamage, 1168d), "Flat packet survives source removal and unload");
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        check(active.isDirty == dirtyBefore, "Authored scene dirty state changed");
        return "PASS flat Scale native " + checks + ": stat170 -> skill850 -> Scale1460 -> crit2920 -> received1168 -> iframe350.4; preview cleaned; dirty=" + active.isDirty;
    }

    public static string Run()
    {
        if (EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Stop the current session before native damage checks.");
        var active = SceneManager.GetActiveScene();
        bool dirtyBefore = active.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        bool started = false;
        try
        {
            var hub = new EveRegistryHub();
            new PNContentModule().Register(hub);
            var hero = EveId.Parse("test:native_damage_hero");
            hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(hero, new[] {
                new EveInitialStat(PNContentModule.Health, 100f, 100f),
                new EveInitialStat(PNContentModule.AttackPower, 100f),
                new EveInitialStat(PNContentModule.Defense, 500f)
            }));
            hub.Seal();
            EveRegistrySession.Begin();
            started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline,
                EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:native_damage_flow"));
            var sender = Make(preview, hub, hero, 911001);
            var receiver = Make(preview, hub, hero, 911002);
            sender.SetSource(EveId.Parse("test:bonus"), new[] {
                new EveStatModifier(PNContentModule.Fire, .2f, 1, PNContentModule.Scale)
            });
            receiver.SetSource(EveId.Parse("test:resistance"), new[] {
                new EveStatModifier(PNContentModule.Fire, -.2f, 1, PNContentModule.Mitigation)
            });
            int hits = 0, deaths = 0;
            receiver.DamageReceived += value => hits++;
            receiver.Died += value => deaths++;
            var first = sender.SendDamage(receiver, 100f, PNContentModule.Physical, PNContentModule.Fire);
            if (Math.Abs(first.Damage.Amount - 120f) > .001f || Math.Abs(first.AppliedDamage - 48f) > .001f ||
                Math.Abs(first.HealthAfter - 52f) > .001f || first.Damage.SourceId != sender.EntityId || first.TargetId != receiver.EntityId)
                throw new Exception("Native send/receive or identity check failed.");
            receiver.SetCurrent(PNContentModule.Health, 10f);
            receiver.AdvanceCombatTime(1d);
            var kill = sender.SendDamage(receiver, 25f, PNContentModule.True);
            var repeated = receiver.ReceiveDamage(kill.Damage);
            if (!kill.Killed || kill.AppliedDamage != 10f || repeated.AppliedDamage != 0f || deaths != 1 || hits != 2)
                throw new Exception("Native overkill/death transition failed.");
            var delayed = sender.CreateDamage(100f, PNContentModule.Physical, PNContentModule.Fire);
            sender.GetComponent<EveEntityLoader>().Unload();
            receiver.SetCurrent(PNContentModule.Health, 100f);
            receiver.AdvanceCombatTime(1d);
            if (Math.Abs(receiver.ReceiveDamage(delayed).AppliedDamage - 48f) > .001f)
                throw new Exception("Native delayed payload failed.");
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        if (active.isDirty != dirtyBefore) throw new Exception("Native checks changed gameplay scene dirty state.");
        return "PASS native combat: raw100 -> outgoing120 -> received48 -> HP52; overkill loses10HP; death once; " +
            "delayed payload survives source unload; preview cleaned; active scene dirty=" + active.isDirty;
    }

    public static string RunValueAction()
    {
        if (EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Stop the current session before native value checks.");
        var active = SceneManager.GetActiveScene();
        bool dirtyBefore = active.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        bool started = false;
        try
        {
            var hub = new EveRegistryHub();
            new PNContentModule().Register(hub);
            new PNGameplayContentModule().Register(hub);
            var hero = EveId.Parse("test:native_value_hero");
            var scaled = EveId.Parse("test:native_scaled");
            var raw = EveId.Parse("test:native_raw");
            var skills = hub.GetOrCreate<EveSkillDefinition>();
            skills.Register(new EveSkillDefinition(scaled, JObject.Parse("{\"action\":{\"id\":\"project_n:damage_action\",\"perform\":{\"value\":{\"amount\":500,\"statId\":\"project_n:attack_power\"},\"tags\":[\"project_n:physical\",\"project_n:fire\"]}}}")));
            skills.Register(new EveSkillDefinition(raw, JObject.Parse("{\"action\":{\"id\":\"project_n:damage_action\",\"perform\":{\"value\":{\"amount\":42},\"tags\":[\"project_n:true\"]}}}")));
            hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(hero, new[] {
                new EveInitialStat(PNContentModule.Health, 2000f, 2000f),
                new EveInitialStat(PNContentModule.AttackPower, 100f),
                new EveInitialStat(PNContentModule.Defense, 500f)
            }, new[] { scaled, raw }));
            hub.Seal();
            EveRegistrySession.Begin();
            started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline,
                EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:native_value"));
            var sender = Make(preview, hub, hero, 912001, true);
            var receiver = Make(preview, hub, hero, 912002, true);
            var skill = sender.GetComponent<PNSkill>();
            using (var action = (PNDamageAction)skill.CreateAction(scaled, out var context, new[] { receiver, receiver }))
            {
                action.Start(context);
                sender.SetSource(EveId.Parse("test:buff"), new[] {
                    new EveStatModifier(PNContentModule.AttackPower, 1f, 1),
                    new EveStatModifier(PNContentModule.Fire, .2f, 1, PNContentModule.Scale)
                });
                receiver.SetSource(EveId.Parse("test:resistance"), new[] {
                    new EveStatModifier(PNContentModule.Fire, -.2f, 1, PNContentModule.Mitigation)
                });
                action.Perform(context);
                if (Math.Abs(action.Damage.Amount - 1200d) > .001d || action.Results.Count != 1 ||
                    Math.Abs(action.Results[0].AppliedDamage - 480f) > .001f ||
                    Math.Abs(receiver.GetCurrent(PNContentModule.Health) - 1520f) > .001f)
                    throw new Exception("Native scaled action check failed: outgoing="+action.Damage.Amount+
                        " results="+action.Results.Count+" applied="+action.Results[0].AppliedDamage+
                        " HP="+receiver.GetCurrent(PNContentModule.Health)+" iframe="+action.Results[0].IFrameMultiplier);
                action.Exit(context);
            }
            using (var action = (PNDamageAction)skill.CreateAction(raw, out var context, new[] { receiver }))
            {
                receiver.AdvanceCombatTime(1d); // Isolate raw-value math from prior hit protection.
                action.Start(context);
                action.Perform(context);
                if (action.Damage.Amount != 42f || action.Results[0].AppliedDamage != 42f)
                    throw new Exception("Native raw action check failed.");
                action.Exit(context);
            }
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        if (active.isDirty != dirtyBefore) throw new Exception("Native value checks changed gameplay scene dirty state.");
        return "PASS native value/action: buffed ATK200 x 500% -> value1000 -> bonus1200 -> HP loss480; " +
            "duplicate target hits once; raw42 applies42; preview cleaned; active scene dirty=" + active.isDirty;
    }

    private static PNStat Make(Scene scene, EveRegistryHub hub, EveId hero, long id, bool withSkill = false)
    {
        var obj = new GameObject("Native combat probe " + id) { hideFlags = HideFlags.HideAndDontSave };
        obj.SetActive(false);
        SceneManager.MoveGameObjectToScene(obj, scene);
        obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), hero, 0);
        var stat = obj.AddComponent<PNStat>();
        if (withSkill) obj.AddComponent<PNSkill>();
        obj.AddComponent<EveEntityLoader>().Load(hub);
        return stat;
    }
}
#endif
