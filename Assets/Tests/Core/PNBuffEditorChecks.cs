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

public sealed class PNBuffDeclineSkill : PNSkill
{
    public int Attempts;
    public override bool TryInterrupt(EveId reason) { Attempts++; return false; }
}
public sealed class PNBuffNativeClockBehavior : PNBuffBehavior
{
    private readonly PNBuffClock clock;
    public int Ticks;
    public PNBuffNativeClockBehavior(PNBuffClock clock) { this.clock = clock; }
    public override void OnApply(PNBuffContext context, JToken input) { context.SetClock(clock); context.SetDuration(4); context.Schedule("tick", 1, 1); }
    public override void OnScheduled(PNBuffContext context, string key) { Ticks++; }
}
public sealed class PNBuffNativeModule : EveIContentModule
{
    public readonly List<PNBuffNativeClockBehavior> Clocks = new List<PNBuffNativeClockBehavior>();
    public void Register(EveRegistryHub hub)
    {
        foreach (PNBuffClock clock in Enum.GetValues(typeof(PNBuffClock)))
        {
            var selected = clock;
            hub.Buffs().Register(new PNBuffDefinition(EveId.Parse("test:clock_" + clock.ToString().ToLowerInvariant()), EveId.Parse("test:pnbuffeditorchecks_factory_1"), _ => { var value = new PNBuffNativeClockBehavior(selected); Clocks.Add(value); return value; }, null));
        }
        var control = hub.Buffs().Get(EveId.Parse("project_n:buff_control_template"));
        var health = hub.Buffs().Get(EveId.Parse("project_n:buff_stat_template")); var healthData = (JObject)health.Payload;
        healthData["statId"] = PNContentIds.Health.ToString(); healthData["group"] = 1; healthData["value"] = 1; healthData["maximumStacks"] = 1;
        hub.Buffs().Register(health.Derive(EveId.Parse("test:health_refresh"), healthData, health.Tags, false, 1, PNBuffInstanceScope.Target, true));
        var data = (JObject)control.Payload; data["interrupt"] = false;
        hub.Buffs().Register(control.Derive(EveId.Parse("test:no_interrupt"), data, new[] { EveId.Parse("base:stun") }, true, 3, PNBuffInstanceScope.Target, false));
        data["duration"] = 2; data["clock"] = "EntityWithoutMinimum";
        hub.Buffs().Register(control.Derive(EveId.Parse("test:raw_control"), data, new[] { EveId.Parse("base:stun") }, false, 1, PNBuffInstanceScope.Target, true));
        var dot = hub.Buffs().Get(EveId.Parse("project_n:buff_dot_template")); var dotData = (JObject)dot.Payload;
        hub.Buffs().Register(dot.Derive(EveId.Parse("test:live_dot"), dotData, dot.Tags, false, 1, PNBuffInstanceScope.Source, true));
    }
}

public static class PNBuffEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit Mode and stopped session required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var evidence = new List<string>(); int checks = 0;
        PNControlMoveEditorProbe move = null;
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); evidence.Add("PASS " + text); checks++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var module = new PNBuffNativeModule();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), module }, docs);
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject Entity(string name, long id, string character)
        {
            var root = new GameObject(name); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), 0); return root;
        }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:buff_native"));
            var root = Entity("Buff Eve native fixture", 988001, "project_n:eve");
            var stat = root.AddComponent<EveCharacterStat>(); root.AddComponent<PNAbilityManager>();
            var body = root.AddComponent<Rigidbody>(); body.useGravity = false;
            root.AddComponent<CapsuleCollider>(); move = root.AddComponent<PNControlMoveEditorProbe>(); move.InitializeInput();
            var skill = root.AddComponent<PNEveAttackSpeedProbe>(); var loader = root.AddComponent<EveEntityLoader>(); loader.Load(hub); root.SetActive(true);
            root.GetComponent<PNBuffManager>().Advance(0);
            var buffs = root.GetComponent<PNBuffManager>().Buffs; var controls = root.GetComponent<PNControlManager>().State;
            Check(root.GetComponents<PNBuffManager>().Length == 1 && buffs.Snapshot().Count == 0, "One prepared Buff manager; no template auto-applied.");
            var enemyRoot = Entity("Buff enemy native fixture", 988002, "project_n:dummy");
            var enemy = enemyRoot.AddComponent<PNEnemyStat>(); var enemyLoader = enemyRoot.AddComponent<EveEntityLoader>(); enemyLoader.Load(hub); enemyRoot.SetActive(true);
            enemyRoot.GetComponent<PNBuffManager>().Advance(0);
            var enemyBuffs = enemyRoot.GetComponent<PNBuffManager>().Buffs;
            var burnRoot = Entity("Burn isolated native source", 988004, "project_n:eve");
            var burnStat = burnRoot.AddComponent<EveCharacterStat>(); var burnLoader = burnRoot.AddComponent<EveEntityLoader>(); burnLoader.Load(hub);
            var burnSource = PNCombatSource.Capture(burnStat); var burnId = EveId.Parse("base:burn");
            double sourceAttack = burnStat.Evaluate(PNContentIds.AttackPower);
            PNDamageResult lastBurn = null; int burnTicks = 0;
            Action<PNDamageResult> watchBurn = result => { lastBurn = result; burnTicks++; }; enemy.DamageReceived += watchBurn;
            try
            {
                var burn = enemyBuffs.Apply(new PNBuffApplyRequest(burnId, burnSource, JObject.Parse("{'stacks':35}")));
                Check(burn.Status == PNBuffStatus.Applied && enemyBuffs.Get(burn.Handle).Stacks == 35, "Native Burn accepts explicit incoming stack count.");
                enemyBuffs.Advance(.4); enemyBuffs.Apply(new PNBuffApplyRequest(burnId, burnSource)); enemyBuffs.Advance(.4);
                Check(burnTicks == 1 && Math.Abs(lastBurn.Damage.Amount - sourceAttack * 1.75) < 1e-8 && !lastBurn.Damage.CanCrit && !lastBurn.Damage.IsCritical, "Native full-stack refresh preserves tick cadence, coefficient and noncrit packet.");
                var attackOwner = EveId.Parse("test:burn_attack"); burnStat.SetSource(attackOwner, new[] { new EveStatModifier(PNContentIds.AttackPower, 100, 0) });
                double time = enemyBuffs.Get(burn.Handle).Remaining.Value;
                Check(enemyBuffs.Trigger(burn.Handle).Status == PNBuffStatus.Triggered && Math.Abs(lastBurn.Damage.Amount - (sourceAttack + 100) * 1.75) < 1e-8 && enemyBuffs.Get(burn.Handle).Remaining == time, "Native manual trigger reads Live ATK without resetting time.");
                using (var valueLease = enemyBuffs.Contribute("native dot prepared value", new PNBuffNumberContribution(typeof(PNBuffValueContract), "amount", 2), selection: PNBuffSelection.Current, exact: burn.Handle))
                {
                    enemyBuffs.Trigger(burn.Handle);
                    Check(Math.Abs(lastBurn.Damage.Amount - ((sourceAttack + 100) * 1.75 + 70)) < 1e-8, "Native contribution updates prepared DOT amount.");
                    enemyBuffs.ChangeStacks(burn.Handle, -1); enemyBuffs.Trigger(burn.Handle);
                    Check(Math.Abs(lastBurn.Damage.Amount - ((sourceAttack + 100) * 1.7 + 68)) < 1e-8, "Native stack change uses current stack with prepared values.");
                    enemyBuffs.ChangeStacks(burn.Handle, 1);
                }
                enemyBuffs.Trigger(burn.Handle);
                Check(Math.Abs(lastBurn.Damage.Amount - (sourceAttack + 100) * 1.75) < 1e-8, "Native contribution revoke invalidates prepared DOT value.");
                burnStat.SetCurrent(PNContentIds.Health, 0); burnStat.SetSource(attackOwner, new[] { new EveStatModifier(PNContentIds.AttackPower, 200, 0) }); enemyBuffs.Trigger(burn.Handle);
                Check(burn.Handle.IsAlive && Math.Abs(lastBurn.Damage.Amount - (sourceAttack + 200) * 1.75) < 1e-8, "Native loaded dead source still supplies current ATK.");
                burnLoader.Unload(); UnityEngine.Object.DestroyImmediate(burnRoot); double burnPacketAmount = lastBurn.Damage.Amount; enemyBuffs.Trigger(burn.Handle);
                Check(lastBurn.Damage.Amount == burnPacketAmount && lastBurn.Damage.SourceId == new EveEntityId(988004), "Native destroyed-source fallback preserves last valid ATK and attribution.");
                enemyBuffs.Suspend(true); Check(enemyBuffs.Trigger(burn.Handle).Status == PNBuffStatus.Rejected, "Native suspended Burn rejects trigger."); enemyBuffs.Suspend(false);
                enemyBuffs.ChangeStacks(burn.Handle, -34); Check(enemyBuffs.Get(burn.Handle).Stacks == 1 && enemyBuffs.Get(burn.Handle).Remaining == time, "Native direct stack removal preserves lifetime.");
                enemyBuffs.ChangeStacks(burn.Handle, -1); Check(!burn.Handle.IsAlive, "Native depleted Burn cleans selected instance.");
            }
            finally
            {
                enemy.DamageReceived -= watchBurn;
                foreach (var item in enemyBuffs.Snapshot()) enemyBuffs.Remove(item.Handle, PNBuffEndReason.Bypass, true);
                if (burnRoot != null) UnityEngine.Object.DestroyImmediate(burnRoot);
            }
            var source = PNCombatSource.Capture(stat); double attack = stat.Evaluate(PNContentIds.AttackPower);
            var value = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stat_template"), source));
            Check(value.Status == PNBuffStatus.Applied && stat.Evaluate(PNContentIds.AttackPower) == attack + 10, "Native Buff stat modifier.");
            buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stat_template"), source)); Check(buffs.Get(value.Handle).Stacks == 2 && stat.Evaluate(PNContentIds.AttackPower) == attack + 20, "Native stat reapply/stack.");
            var grant = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stack_limit_template"), source));
            for (int i = 0; i < 6; i++) buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stat_template"), source));
            Check(buffs.Get(value.Handle).Stacks == 5, "Buff-owned contribution raises max stacks.");
            buffs.Remove(grant.Handle); Check(buffs.Get(value.Handle).Stacks == 3 && stat.Evaluate(PNContentIds.AttackPower) == attack + 30, "Source end revokes contribution and discards excess.");
            var floor = stat.EndFloorAndCaptureSnapshot(); Check(stat.Evaluate(PNContentIds.AttackPower) == attack + 30 && floor.PersistentSources.Count == 0, "Floor-local cleanup preserves Buff-owned source, excludes it from transfer snapshot.");
            var replace = buffs.Contribute("native override", new PNBuffOverrideContribution(EveId.Parse("test:no_interrupt"), 1), selection: PNBuffSelection.Current, exact: value.Handle);
            Check(value.Handle.IsAlive && buffs.Get(value.Handle).ProfileId == EveId.Parse("test:no_interrupt") && stat.Evaluate(PNContentIds.AttackPower) == attack && !move.CanMove, "Full override returns stat scope and acquires control scope.");
            replace.Dispose(); Check(value.Handle.IsAlive && stat.Evaluate(PNContentIds.AttackPower) == attack + 10 && move.CanMove, "Original behavior restored with default reset and no stale control lease.");
            buffs.Remove(value.Handle); Check(stat.Evaluate(PNContentIds.AttackPower) == attack && stat.Set.SourceSnapshot().Count == 0, "All Buff-owned stat sources released.");
            double oldCurrent = stat.GetCurrent(PNContentIds.Health), oldMaximum = stat.GetMax(PNContentIds.Health);
            var healthRefresh = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("test:health_refresh"), source)); stat.SetCurrent(PNContentIds.Health, oldMaximum * 1.5);
            var healthSourceKey = stat.Set.SourceSnapshot().Keys.Single();
            buffs.Apply(new PNBuffApplyRequest(EveId.Parse("test:health_refresh"), source));
            Check(stat.Set.SourceSnapshot().ContainsKey(healthSourceKey), "Identical stat refresh retains the existing modifier source key.");
            Check(stat.GetCurrent(PNContentIds.Health) == oldMaximum * 1.5, "Identical health modifier refresh retains lease without temporary maximum/current loss.");
            buffs.Suspend(true); buffs.Suspend(false);
            Check(stat.GetMax(PNContentIds.Health) == oldMaximum * 2, "Stat Buff resumes by reacquiring modifier in its new resource scope.");
            buffs.Remove(healthRefresh.Handle); stat.SetCurrent(PNContentIds.Health, oldCurrent);
            skill.BurstGauge.Add(150);
            Check(skill.TryDash(), "Native Action running before Buff interruption.");
            var control = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source));
            Check(!skill.IsBusy && !move.CanMove && !move.CanJump, "Buff explicitly interrupts Action and retains its own movement/jump locks.");
            double charges = skill.DashRecovery.Current, gauge = skill.BurstGauge.Current;
            Check(!skill.TryAttack() && !skill.TrySkill() && !skill.TryDash() && !skill.TryUltimate() && !skill.TryBurst(), "All five direct Try gates blocked by Buff.");
            Check(skill.DashRecovery.Current == charges && skill.BurstGauge.Current == gauge, "Denied Buff-controlled activation consumes no resources.");
            buffs.Suspend(true); Check(move.CanMove && controls.Snapshot().Count == 0, "Suspend returns behavior control lease."); buffs.Suspend(false);
            Check(!move.CanMove && controls.Snapshot().Count == 1, "Resume reacquires one lease.");
            var external = controls.Acquire("other owner", new[] { PNControlIds.Move }); buffs.Remove(control.Handle);
            Check(!move.CanMove && controls.IsAllowed(PNControlIds.SkillAll) && controls.Snapshot().Count == 1, "Buff end preserves external owner."); external.Dispose();
            move.SetMoveDirection(Vector2.right); move.TickPhysics(); float start = body.position.x; preview.GetPhysicsScene().Simulate(.02f);
            Check(body.position.x > start, "PhysX walking restored after Buff.");
            var p1 = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("test:no_interrupt"), source)); var p2 = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("test:no_interrupt"), source));
            Check(buffs.Snapshot().Count == 2 && controls.Snapshot().Count == 2, "Native independent instances own separate leases and UI snapshots.");
            Check(buffs.Remove(p1.Handle).Status == PNBuffStatus.Rejected && p1.Handle.IsAlive, "Protected native Buff refuses ordinary dispel.");
            buffs.Remove(p1.Handle, PNBuffEndReason.Bypass, true); Check(!move.CanMove && controls.Snapshot().Count == 1, "Bypass removes only one protected instance.");
            using (buffs.Immune(tags: new[] { EveId.Parse("base:stun") }, purge: true))
            { Check(!p2.Handle.IsAlive && move.CanMove, "Tag immunity purges protected Buff through bypass."); Check(buffs.Apply(new PNBuffApplyRequest(EveId.Parse("test:no_interrupt"), source)).Status == PNBuffStatus.Rejected, "Immunity checked before locks."); }
            var manager = root.GetComponent<PNBuffManager>();
            control = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source)); double remaining = buffs.Get(control.Handle).Remaining.Value;
            manager.enabled = false; manager.Advance(5); Check(control.Handle.IsAlive && move.CanMove && buffs.Get(control.Handle).Remaining == remaining, "Manager disable preserves timer and returns effects.");
            manager.enabled = true; manager.Advance(0); Check(!move.CanMove && buffs.Get(control.Handle).Remaining == remaining, "Manager enable resumes without catch-up."); buffs.Remove(control.Handle);
            var clock = root.AddComponent<EveTimeScale>(); clock.MinScale = 1; var scaleLease = clock.AddOwnedModifier(EveId.Parse("test:clock"), EveTimeModifierKind.Scale, -1, root);
            foreach (PNBuffClock mode in Enum.GetValues(typeof(PNBuffClock))) buffs.Apply(new PNBuffApplyRequest(EveId.Parse("test:clock_" + mode.ToString().ToLowerInvariant()), source));
            buffs.Advance(1, clock.Scale, Math.Max(0, clock.RawScale));
            Check(module.Clocks[0].Ticks == 1 && module.Clocks[1].Ticks == 1 && module.Clocks[2].Ticks == 0, "Native Game/Entity/raw clocks honor minimum versus raw zero.");
            clock.RemoveModifier(scaleLease);
            foreach (var item in buffs.Snapshot()) buffs.Remove(item.Handle, PNBuffEndReason.Bypass, true);
            double hp = enemy.GetCurrent(PNContentIds.Health);
            var dot = enemyBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_dot_template"), source)); stat.SetCurrent(PNContentIds.Health, 0);
            stat.SetCurrent(PNContentIds.Health, stat.GetMax(PNContentIds.Health));
            var live = enemyBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("test:live_dot"), source)); stat.SetCurrent(PNContentIds.Health, 0);
            enemyBuffs.Advance(2); Check(enemy.GetCurrent(PNContentIds.Health) < hp && dot.Handle.IsAlive, "Real enemy receives DOT damage after player source death.");
            Check(live.Handle.IsAlive, "DOT keeps a loaded dead source and continues ticking.");
            loader.Unload(); hp = enemy.GetCurrent(PNContentIds.Health); enemyBuffs.Advance(1);
            Check(enemy.GetCurrent(PNContentIds.Health) < hp && source.LoadedSource == null, "DOT fallback remains valid after native source unload.");
            loader.Load(hub); source = PNCombatSource.Capture(stat);
            foreach (var item in enemyBuffs.Snapshot()) enemyBuffs.Remove(item.Handle, PNBuffEndReason.Bypass, true);
            var bomb = enemyBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_bomb_template"), source)); hp = enemy.GetCurrent(PNContentIds.Health); enemyBuffs.Remove(bomb.Handle);
            Check(enemy.GetCurrent(PNContentIds.Health) == hp, "Native bomb dispel skips expiration effect.");
            bomb = enemyBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_bomb_template"), source)); enemyBuffs.Advance(3);
            Check(!bomb.Handle.IsAlive && enemy.GetCurrent(PNContentIds.Health) < hp, "Native bomb expires once through receive pipeline.");
            var packet = source.Snapshot.CreateHeal(5); hp = enemy.GetCurrent(PNContentIds.Health); loader.Unload(); enemy.ReceiveHeal(packet);
            Check(enemy.GetCurrent(PNContentIds.Health) > hp && packet.SourceId == new EveEntityId(988001), "Snapshot Heal uses original source attribution and actual receive calculation.");
            loader.Load(hub); source = PNCombatSource.Capture(stat);
            var enemyControl = enemyBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source));
            enemy.ReceiveDamage(source.Snapshot.CreateDamage(1e12, false, 0, PNContentIds.True));
            Check(!enemyControl.Handle.IsAlive && enemyBuffs.Snapshot().Count == 0, "Target death cleans default Buff behavior.");
            var declineRoot = Entity("Declined interruption fixture", 988003, "project_n:dummy"); declineRoot.AddComponent<PNEnemyStat>();
            var decline = declineRoot.AddComponent<PNBuffDeclineSkill>(); var declineLoader = declineRoot.AddComponent<EveEntityLoader>(); declineLoader.Load(hub); declineRoot.SetActive(true);
            declineRoot.GetComponent<PNBuffManager>().Advance(0);
            var declined = declineRoot.GetComponent<PNBuffManager>().Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source));
            Check(decline.Attempts == 1 && declined.Handle.IsAlive && !decline.Controls.IsAllowed(PNControlIds.SkillAll), "Declined interruption keeps Buff and gates new activations.");
            var declinedBuffs = declineRoot.GetComponent<PNBuffManager>().Buffs; declinedBuffs.Suspend(true); declinedBuffs.Suspend(false);
            Check(decline.Attempts == 1, "Resume never repeats initial interruption request.");
            var old = root.GetComponent<PNBuffManager>().Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source)).Handle;
            loader.Unload(); loader.Load(hub); Check(!old.IsAlive && root.GetComponent<PNBuffManager>().Buffs.Snapshot().Count == 0 && move.CanMove, "Native unload/reload invalidates old Buff state.");
            var sessionPacket = source.Snapshot; EveRegistrySession.End();
            Check(!declined.Handle.IsAlive && !manager.IsReady, "Session end cleans every registered entity.");
            bool rejected = false; try { sessionPacket.CreateHeal(1); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "Ended-session snapshot cannot create new combat packets.");
        }
        finally
        {
            if (move != null) move.RetireEditorInput();
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        string summary = "PASS " + checks + " native Buff checks"; Directory.CreateDirectory(".utmp/BuffReview"); File.WriteAllLines(".utmp/BuffReview/NativeResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}
#endif
