#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public sealed class PNBuffPlayReview : MonoBehaviour
{
    public static string Status = "Idle";
    private readonly List<string> evidence = new List<string>();
    private readonly List<Action> restores = new List<Action>();
    private Keyboard keyboard;
    private GameObject fixture, enemyFixture;
    private InputSettings previousSettings, settings;
    private HideFlags previousFlags;
    private bool previousBackground, cleaned;
    private int checks;
    public static string Run()
    {
        if (!Application.isPlaying || !EveRegistrySession.IsReady || EditorApplication.isPaused) throw new InvalidOperationException("Ready unpaused Play Mode required.");
        Status = "Running"; var review = new GameObject("Disposable Buff Play review").AddComponent<PNBuffPlayReview>(); EditorApplication.update += review.Pump; return Status;
    }
    private void Pump() => EditorApplication.QueuePlayerLoopUpdate();
    private void Start() => StartCoroutine(Guarded());
    private void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; evidence.Add("PASS " + text); }
    private IEnumerator Guarded()
    {
        previousBackground = Application.runInBackground; Application.runInBackground = true;
        previousSettings = InputSystem.settings; previousFlags = previousSettings.hideFlags;
        if (previousFlags == HideFlags.HideAndDontSave) previousSettings.hideFlags = HideFlags.DontSave;
        settings = Instantiate(previousSettings); settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView; InputSystem.settings = settings;
        var sequence = Sequence();
        while (true)
        {
            object next;
            try { if (!sequence.MoveNext()) { Status = "PASS " + checks + " Buff Play checks"; break; } next = sequence.Current; }
            catch (Exception error) { Status = "FAIL " + error; break; }
            yield return next;
        }
        (sequence as IDisposable)?.Dispose(); Cleanup(); Directory.CreateDirectory(".utmp/BuffReview"); File.WriteAllLines(".utmp/BuffReview/PlayResults.txt", evidence.Concat(new[] { Status })); Destroy(gameObject);
    }
    private IEnumerator Sequence()
    {
        var existing = InputSystem.devices.ToArray();
        foreach (var other in FindObjectsByType<PNPlayerMove>(FindObjectsSortMode.None))
        { var pc = other.PC; if (pc == null) continue; var prior = pc.devices; pc.devices = existing; restores.Add(() => { if (other != null) pc.devices = prior; }); }
        keyboard = InputSystem.AddDevice<Keyboard>("PNBuffReviewKeyboard");
        fixture = new GameObject("Buff Play Eve fixture"); fixture.SetActive(false); fixture.transform.position = new Vector3(500, 5, 500);
        fixture.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(988101), EveId.Parse("project_n:eve"), 0);
        var stat = fixture.AddComponent<EveCharacterStat>(); fixture.AddComponent<PNAbilityManager>();
        var body = fixture.AddComponent<Rigidbody>(); body.useGravity = false;
        var capsule = fixture.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
        var move = fixture.AddComponent<PNPlayerMove>(); var skill = fixture.AddComponent<EveCharacterSkill>();
        var loader = fixture.AddComponent<EveEntityLoader>(); loader.Load(EveRegistrySession.Current); fixture.SetActive(true); yield return null;
        move.PC.devices = new InputDevice[] { keyboard };
        var manager = fixture.GetComponent<PNBuffManager>(); var buffs = manager.Buffs; var source = PNCombatSource.Capture(stat); var controls = skill.Controls;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D)); var start = body.position; yield return new WaitForSecondsRealtime(.15f);
        Check(body.position.x > start.x + .1f, "Actual held input walks before Buff.");
        var control = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source));
        yield return new WaitForSecondsRealtime(.05f); start = body.position; yield return new WaitForSecondsRealtime(.15f);
        Check(control.Handle.IsAlive && Vector3.Distance(body.position, start) < .01f && !move.TryJump(), "Buff stops held input and blocks jump in real player loop.");
        skill.BurstGauge.Add(150); double charges = skill.DashRecovery.Current, gauge = skill.BurstGauge.Current;
        Check(!skill.TryAttack() && !skill.TrySkill() && !skill.TryDash() && !skill.TryUltimate() && !skill.TryBurst(), "Buff blocks all five direct Try requests.");
        Check(charges == skill.DashRecovery.Current && gauge == skill.BurstGauge.Current, "Blocked requests consume no charges or gauge.");
        var outer = controls.Acquire("external", new[] { PNControlIds.Move }); buffs.Remove(control.Handle);
        Check(!move.CanMove && controls.Snapshot().Count == 1, "Buff removal preserves another control owner."); outer.Dispose(); start = body.position;
        yield return new WaitForSecondsRealtime(.15f); Check(body.position.x > start.x + .1f, "Held movement resumes after last owner ends.");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return new WaitForSecondsRealtime(.05f);
        Check(skill.TryDash(), "Dash starts before interruption."); control = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source));
        Check(!skill.IsDashing && !skill.IsBusy && controls.Snapshot().Count == 1, "Buff interruption cleans running Dash Action leases."); buffs.Remove(control.Handle);
        double attack = stat.Evaluate(PNContentIds.AttackPower);
        var statBuff = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stat_template"), source));
        Check(stat.Evaluate(PNContentIds.AttackPower) == attack + 10, "Play stat modifier active."); manager.enabled = false; double remaining = buffs.Get(statBuff.Handle).Remaining.Value;
        yield return new WaitForSecondsRealtime(.2f);
        Check(stat.Evaluate(PNContentIds.AttackPower) == attack && buffs.Get(statBuff.Handle).Remaining == remaining, "Real OnDisable returns effects and freezes remaining time.");
        manager.enabled = true; yield return null;
        Check(stat.Evaluate(PNContentIds.AttackPower) == attack + 10 && buffs.Get(statBuff.Handle).Stacks == 1, "Real OnEnable restores effects without replaying Apply."); buffs.Remove(statBuff.Handle);
        using (buffs.Immune(tags: new[] { EveId.Parse("base:neutral") }))
        { Check(buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source)).Status == PNBuffStatus.Rejected && controls.Snapshot().Count == 0, "Immunity rejects control before locks or interruption."); }
        control = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source)); yield return new WaitForSecondsRealtime(1.2f);
        Check(!control.Handle.IsAlive && controls.Snapshot().Count == 0, "Normal Update expires Buff and returns control lease.");
        enemyFixture = new GameObject("Buff Play enemy fixture"); enemyFixture.SetActive(false); enemyFixture.transform.position = new Vector3(510, 5, 500);
        enemyFixture.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(988102), EveId.Parse("project_n:dummy"), 0);
        var enemy = enemyFixture.AddComponent<PNEnemyStat>(); var enemyLoader = enemyFixture.AddComponent<EveEntityLoader>(); enemyLoader.Load(EveRegistrySession.Current); enemyFixture.SetActive(true); yield return null;
        var enemyBuffs = enemyFixture.GetComponent<PNBuffManager>().Buffs; double hp = enemy.GetCurrent(PNContentIds.Health);
        var dot = enemyBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_dot_template"), source));
        stat.SetCurrent(PNContentIds.Health, 0); yield return new WaitForSecondsRealtime(1.2f);
        Check(enemy.GetCurrent(PNContentIds.Health) < hp && dot.Handle.IsAlive, "Normal Update ticks DOT damage after source death.");
        enemyBuffs.Remove(dot.Handle); stat.SetCurrent(PNContentIds.Health, stat.GetMax(PNContentIds.Health));
        var burnId = EveId.Parse("base:burn");
        var burn = enemyBuffs.Apply(new PNBuffApplyRequest(burnId, PNCombatSource.Capture(stat), Newtonsoft.Json.Linq.JObject.Parse("{'stacks':35}")));
        int burnTicks = 0; PNDamageResult lastBurn = null;
        Action<PNDamageResult> watchBurn = result => { burnTicks++; lastBurn = result; }; enemy.DamageReceived += watchBurn;
        restores.Add(() => { if (enemy != null) enemy.DamageReceived -= watchBurn; });
        yield return new WaitForSecondsRealtime(.3f);
        enemyBuffs.Apply(new PNBuffApplyRequest(burnId, PNCombatSource.Capture(stat)));
        Check(enemyBuffs.Get(burn.Handle).Stacks == 35 && enemyBuffs.Get(burn.Handle).Remaining > 14.9, "Burn full-stack reapply refreshes lifetime in Play.");
        yield return new WaitForSecondsRealtime(.55f);
        Check(burnTicks == 1 && Math.Abs(lastBurn.Damage.Amount - stat.Evaluate(PNContentIds.AttackPower) * 1.75) < 1e-8 && !lastBurn.IsCritical, "Burn Update ticks at original cadence with live coefficient/noncrit.");
        var burnAttack = EveId.Parse("test:burn_play_attack"); stat.SetSource(burnAttack, new[] { new EveStatModifier(PNContentIds.AttackPower, 100, 0) });
        enemyBuffs.Trigger(burn.Handle);
        Check(burnTicks == 2 && Math.Abs(lastBurn.Damage.Amount - stat.Evaluate(PNContentIds.AttackPower) * 1.75) < 1e-8, "Manual Burn trigger reads changed Live ATK in actual Play.");
        stat.SetCurrent(PNContentIds.Health, 0); yield return new WaitForSecondsRealtime(.85f);
        Check(burnTicks >= 3 && burn.Handle.IsAlive, "Burn Update continues after source death.");
        loader.Unload(); double cachedBurn = lastBurn.Damage.Amount; int ticksBefore = burnTicks; yield return new WaitForSecondsRealtime(.85f);
        Check(burnTicks > ticksBefore && lastBurn.Damage.Amount == cachedBurn, "Burn Update uses last valid source after unload.");
        var enemyManager = enemyFixture.GetComponent<PNBuffManager>(); enemyManager.enabled = false;
        double burnTime = enemyBuffs.Get(burn.Handle).Remaining.Value; ticksBefore = burnTicks; yield return new WaitForSecondsRealtime(.2f);
        Check(burnTicks == ticksBefore && enemyBuffs.Get(burn.Handle).Remaining == burnTime, "Burn manager disable pauses ticks and remaining duration.");
        enemyManager.enabled = true; yield return null;
        enemyBuffs.Remove(burn.Handle); Check(!burn.Handle.IsAlive, "Burn ordinary dispel cleans instance in Play.");
        loader.Load(EveRegistrySession.Current); source = PNCombatSource.Capture(stat); buffs = manager.Buffs; controls = skill.Controls;
        control = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), source)); double time = buffs.Get(control.Handle).Remaining.Value;
        fixture.SetActive(false); yield return new WaitForSecondsRealtime(.2f);
        Check(control.Handle.IsAlive && controls.Snapshot().Count == 0 && buffs.Get(control.Handle).Remaining == time, "Whole entity disable preserves Buff state and returns effects.");
        fixture.SetActive(true); yield return null;
        Check(control.Handle.IsAlive && !move.CanMove, "Whole entity resume reacquires effect.");
        loader.Unload(); Check(!control.Handle.IsAlive && controls.IsDisposed, "Unload invalidates Buff and control ownership.");
        loader.Load(EveRegistrySession.Current); Check(manager.Buffs.Snapshot().Count == 0, "Reload creates fresh empty Buff collection.");
        buffs = manager.Buffs;
        var sphere = enemyFixture.AddComponent<SphereCollider>(); sphere.radius = .5f; sphere.center = Vector3.up * .5f;
        enemyFixture.transform.position = fixture.transform.position + Vector3.right * 1.2f; Physics.SyncTransforms();
        var attackGrant = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_skill_effect_template"), PNCombatSource.Capture(stat)));
        Check(attackGrant.Handle.IsAlive && skill.TryAttack(Vector3.right), "Actual Eve Attack begins with registered OnHit Buff.");
        buffs.Remove(attackGrant.Handle); yield return new WaitForSecondsRealtime(.7f);
        Check(enemyBuffs.Snapshot().Any(x => x.Id == EveRegistrySession.Current.Buffs().ResolveIdentity(burnId)), "Actual PhysX Eve hit applies Burn from Action Snapshot after owner removal.");
        var rule = Newtonsoft.Json.Linq.JObject.Parse("{'readMode':'Live','categoryIds':['base:skill'],'effects':[{'trigger':'OnActivated','kind':'Operation','id':'base:apply_buff','data':{'buffId':'base:burn','input':{'stacks':2}},'target':'Self'}]}");
        using (skill.Effects.Contribute("activation Play fixture", new PNSkillContribution(rule, EveRegistrySession.Current)))
        {
            Check(skill.TrySkill() && buffs.Snapshot().Any(x => x.Id == EveRegistrySession.Current.Buffs().ResolveIdentity(burnId) && x.Stacks == 2), "Actual Eve Skill activation applies self Burn without requiring hit.");
        }
    }
    private void Cleanup()
    {
        if (cleaned) return; cleaned = true; EditorApplication.update -= Pump;
        try
        {
            if (fixture != null) { fixture.GetComponent<EveEntityLoader>()?.Unload(); Destroy(fixture); fixture = null; }
            if (enemyFixture != null) { enemyFixture.GetComponent<EveEntityLoader>()?.Unload(); Destroy(enemyFixture); enemyFixture = null; }
        }
        finally
        {
            foreach (var restore in restores) restore(); restores.Clear();
            if (keyboard != null && InputSystem.devices.Contains(keyboard)) InputSystem.RemoveDevice(keyboard);
            if (previousSettings != null) { InputSystem.settings = previousSettings; previousSettings.hideFlags = previousFlags; }
            if (settings != null) Destroy(settings); Application.runInBackground = previousBackground;
        }
    }
    private void OnDestroy() => Cleanup();
}
#endif
