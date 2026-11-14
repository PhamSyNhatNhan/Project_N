#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/EveMovesetRefactorPlan.md E0: golden trace of Eve's moveset. Every scenario is driven only through the semantic slot input (RequestSlot, as the binding
// runtime receives it from the keyboard), one fixed 1/60 s frame at a time, and every frame writes one line: cast, phase, combo state, every Ability of the entity,
// stats, position, facing, the targets' health and Buffs. The first run records Tests/EveEngine/Baselines/EveMovesetTrace.baseline.txt from the code as it is; every later
// step of the refactor must reproduce it byte for byte. Edit Mode, preview scene, nothing saved.
//   EveMovesetTraceChecks.Run()                 compares two fresh traces with each other and with the baseline (fails when there is no baseline)
//   EveMovesetTraceChecks.Run(record: true)     the same, then replaces the baseline (use only for the very first recording or an approved change)
//   EveMovesetTraceChecks.Run(mutate: "...")    proves the trace can fail: "combo_reset" shortens ComboReset, "attack_speed" adds +1 attack speed
// E1 changes where the combo fields live: only EveTraceProbe.ComboState() has to follow them, the trace format stays.
public sealed class EveTraceProbe : EveCharacterSkill
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(object owner, string name) => (T)typeof(EveCharacterSkill).GetField(name, Private).GetValue(owner);
    public void Tick(float delta) => AdvanceAttack(delta);
    public void Prepare() { if (pc == null) Awake(); OnEnable(); }
    public bool Slot(EveId slot, PNSkillInputPhase phase, PNSkillInteraction interaction) => RequestSlot(slot, new PNSkillInputEvent(phase, interaction));
    public static string Num(double v) { v = Math.Round(v, 4); if (v == 0) v = 0; return v.ToString("0.####", CultureInfo.InvariantCulture); }
    // Combo state: the step/stroke counters and the exhausted mark are Abilities since E1 (the idle timer is a local clock); rendered exactly as the old fields were.
    private static readonly string[] ComboAbilities = { "eve_combo_step", "eve_repeat_stroke", "eve_ultimate_stroke", "eve_repeat_exhausted" };
    private int Count(EveId id) => Abilities.Find(id)[0].Snapshot().Amount ?? 0;
    private string ComboState() => "combo=" + Count(ComboStepId) + " idle=" + Num(Field<float>(this, "comboIdle")) + " rs=" + Count(RepeatStrokeId)
        + " us=" + Count(UltimateStrokeId) + " rex=" + (Abilities.Find(RepeatExhaustedId)[0].Snapshot().IsActive ? 1 : 0) + " rh=" + (Field<bool>(this, "repeatHeld") ? 1 : 0)
        + " pend=" + (Field<string>(this, "pendingAttackProfile") ?? "-") + " pr=" + Num(Field<float>(this, "phaseRemaining")) + " rw=" + (Field<bool>(this, "rewardedHit") ? 1 : 0)
        + " bf=" + (Field<bool>(this, "burstFinisher") ? 1 : 0) + " ur=" + (Field<bool>(this, "ultimateRewarded") ? 1 : 0) + " rc=" + (Field<bool>(this, "repeatChainActive") ? 1 : 0);
    private static string Short(EveId id) { var text = id.ToString(); int at = text.IndexOf(':'); return at < 0 ? text : text.Substring(at + 1); }
    public string Line()
    {
        var stat = GetComponent<PNStat>(); var body = GetComponent<Rigidbody>(); var move = GetComponent<PNPlayerMove>(); var p = transform.position;
        var sb = new StringBuilder();
        sb.Append("cast=").Append(CurrentCast).Append(" ph=").Append(CurrentPhase).Append(" atk=").Append(IsAttacking ? 1 : 0).Append(" burst=").Append(IsInBurst ? 1 : 0)
          .Append(" dash=").Append(IsDashing ? 1 : 0).Append(' ').Append(ComboState());
        sb.Append(" | ab:");
        foreach (var ability in Abilities.Instances)
        {
            var s = ability.Snapshot(); if (Array.IndexOf(ComboAbilities, Short(s.Id)) >= 0) continue; // new in E1: shown through ComboState, so the line stays as recorded
            sb.Append(' ').Append(Short(s.Id)).Append('=').Append(s.Amount.HasValue ? s.Amount.Value.ToString(CultureInfo.InvariantCulture) : "-")
              .Append('/').Append(Num(s.CooldownRemaining)).Append('/').Append(s.IsActive ? 1 : 0).Append('/').Append(s.DurationRemaining.HasValue ? Num(s.DurationRemaining.Value) : "-");
        }
        sb.Append(" | atkp=").Append(Num(stat.Evaluate(PNContentIds.AttackPower))).Append(" atks=").Append(Num(stat.Evaluate(PNContentIds.AttackSpeed)))
          .Append(" hp=").Append(Num(stat.GetCurrent(PNContentIds.Health)));
        sb.Append(" | pos=").Append(Num(p.x)).Append(',').Append(Num(p.y)).Append(',').Append(Num(p.z));
        if (body != null) sb.Append(" rb=").Append(Num(body.position.x)).Append(',').Append(Num(body.position.y)).Append(',').Append(Num(body.position.z));
        sb.Append(" face=").Append(Num(move.FacingDirection)).Append(" canmove=").Append(move.CanMove ? 1 : 0);
        sb.Append(" tgt=").Append(CurrentTarget == null ? "-" : CurrentTarget.name);
        sb.Append(" vis=").Append(AttackVisual == null ? "-" : Num(AttackVisual.Radius) + "@" + Num(AttackVisual.Progress));
        sb.Append(" ba=").Append(BlazingAssault == null ? "-" : string.Format(CultureInfo.InvariantCulture, "{0}", BlazingAssault.Stacks))
          .Append(" rm=").Append(RisingMomentum == null ? "-" : string.Format(CultureInfo.InvariantCulture, "{0}", RisingMomentum.Stacks));
        return sb.ToString();
    }
}

public static class EveMovesetTraceChecks
{
    public const string BaselinePath = "Tests/EveEngine/Baselines/EveMovesetTrace.baseline.txt";
    public const string CurrentPath = ".utmp/EveMovesetTrace/current.txt";
    private const float Dt = 1f / 60f;

    private sealed class Ctx
    {
        public string Name; public EveTraceProbe Skill; public EveCharacterStat Caster; public PNAbilityManager Abilities; public PNBuffManager CasterBuffs;
        public DummyStat Near, Low; public List<string> Lines = new List<string>(); public int Frame;
        public Dictionary<EveId, (int Started, PNSkillInteraction Kind)> Holding = new Dictionary<EveId, (int, PNSkillInteraction)>();
        public const int RepeatEvery = 6;
        public void Event(string text) => Lines.Add(Name + "#" + Frame.ToString("D4", CultureInfo.InvariantCulture) + " EVENT " + text);
        public void Tap(EveId slot)
        {
            bool a = Skill.Slot(slot, PNSkillInputPhase.Started, PNSkillInteraction.Tap), b = Skill.Slot(slot, PNSkillInputPhase.Performed, PNSkillInteraction.Tap), c = Skill.Slot(slot, PNSkillInputPhase.Canceled, PNSkillInteraction.Tap);
            Event("tap " + slot + " started=" + a + " performed=" + b + " canceled=" + c);
        }
        public void Hold(EveId slot)
        {
            Event("hold " + slot + " started=" + Skill.Slot(slot, PNSkillInputPhase.Started, PNSkillInteraction.Repeat)); Holding[slot] = (Frame, PNSkillInteraction.Repeat);
        }
        public void Release(EveId slot)
        {
            Holding.Remove(slot); Event("release " + slot + " canceled=" + Skill.Slot(slot, PNSkillInputPhase.Canceled, PNSkillInteraction.Repeat));
        }
        public void Step()
        {
            foreach (var pair in Holding.ToArray())
                if ((Frame - pair.Value.Started) % RepeatEvery == 0) Event("repeat " + pair.Key + " performed=" + Skill.Slot(pair.Key, PNSkillInputPhase.Performed, pair.Value.Kind));
            Abilities.Advance(Dt); CasterBuffs.Advance(Dt);
            foreach (var dummy in new[] { Near, Low }) if (dummy != null && dummy.isActiveAndEnabled) dummy.GetComponent<PNBuffManager>()?.Advance(Dt);
            Skill.Tick(Dt); Physics.SyncTransforms(); Frame++;
            Lines.Add(Name + "#" + Frame.ToString("D4", CultureInfo.InvariantCulture) + " " + Skill.Line() + " | near=" + Dummy(Near) + " low=" + Dummy(Low));
        }
        public void Run(int frames) { for (int i = 0; i < frames; i++) Step(); }
        public void Spam(EveId slot, int every, int frames) { for (int i = 0; i < frames; i++) { if (i % every == 0) Tap(slot); Step(); } }
        public void RunUntilIdle(int max) { for (int i = 0; i < max && Skill.IsBusy; i++) Step(); }
        private static string Dummy(DummyStat d)
        {
            if (d == null) return "-";
            var buffs = d.GetComponent<PNBuffManager>()?.Buffs.Snapshot().Select(x => x.Id.ToString() + ":" + x.Stacks).OrderBy(x => x, StringComparer.Ordinal) ?? Enumerable.Empty<string>();
            return EveTraceProbe.Num(d.GetCurrent(PNContentIds.Health)) + "[" + string.Join(";", buffs) + "]" + (d.isActiveAndEnabled ? "" : "(off)");
        }
    }

    private static readonly EveId Attack = PNSkillSlots.Attack, SkillSlot = PNSkillSlots.Skill, Ult = PNSkillSlots.Ultimate, DashSlot = PNSkillSlots.Dash, Burst = PNSkillSlots.Burst;

    private static readonly (string Name, Action<Ctx> Setup, Action<Ctx> Script)[] Scenarios =
    {
        ("combo_chain", null, c => c.Spam(Attack, 3, 300)),
        ("combo_reset", null, c =>
        {
            c.Tap(Attack); c.Run(50); c.Tap(Attack); c.Run(50); c.Tap(Attack); c.Run(50);
            c.Run((int)Math.Ceiling((c.Skill.Moveset.ComboReset + .2f) * 60f));
            c.Tap(Attack); c.Run(50); c.Tap(Attack); c.Run(50);
        }),
        ("skill_then_attack", null, c => { c.Tap(SkillSlot); c.Spam(Attack, 3, 260); }),
        ("skill_cooldown_rejects", null, c => { c.Tap(SkillSlot); c.Run(60); c.Tap(SkillSlot); c.Run(10); c.Tap(Burst); c.Run(10); c.Tap(DashSlot); c.Run(40); }),
        ("burst_repeat_chain", c => c.Skill.BurstGauge.Add(150), c => { c.Tap(Burst); c.Run(5); c.Hold(Attack); c.Run(420); c.Release(Attack); c.Run(60); }),
        ("burst_repeat_release_early", c => c.Skill.BurstGauge.Add(150), c => { c.Tap(Burst); c.Run(5); c.Hold(Attack); c.Run(70); c.Release(Attack); c.Run(120); c.Tap(Attack); c.Run(60); }),
        ("burst_skill_attack", c => c.Skill.BurstGauge.Add(150), c => { c.Tap(Burst); c.Run(5); c.Spam(Attack, 3, 90); c.Tap(SkillSlot); c.Spam(Attack, 3, 150); }),
        ("ultimate_plain", null, c => { c.Skill.UltimateRecovery.ResetToFull(); c.Tap(Ult); c.Run(20); c.Tap(DashSlot); c.Run(300); }),
        ("ultimate_burst", c => c.Skill.BurstGauge.Add(150), c => { c.Tap(Burst); c.Run(5); c.Skill.UltimateRecovery.ResetToFull(); c.Tap(Ult); c.Run(20); c.Tap(DashSlot); c.Run(400); }),
        ("dash_mid_combo", null, c =>
        {
            c.Tap(Attack); c.Run(10); c.Tap(DashSlot); c.Run(40); c.Tap(Attack); c.Run(60); c.Tap(Attack); c.Run(6); c.Tap(DashSlot); c.Run(40);
            c.Tap(SkillSlot); c.Run(8); c.Tap(DashSlot); c.Run(40); c.Tap(Attack); c.Run(60);
        }),
        ("interrupt_attack", null, c => { c.Tap(Attack); c.Run(8); c.Event("interrupt=" + c.Skill.TryInterrupt(EveId.Parse("test:stun"))); c.Run(30); c.Tap(Attack); c.Run(60); }),
        ("interrupt_ultimate_charge", null, c => { c.Skill.UltimateRecovery.ResetToFull(); c.Tap(Ult); c.Run(30); c.Event("interrupt=" + c.Skill.TryInterrupt(EveId.Parse("test:stun"))); c.Run(40); c.Tap(Attack); c.Run(60); }),
        ("interrupt_repeat", c => c.Skill.BurstGauge.Add(150), c => { c.Tap(Burst); c.Run(5); c.Hold(Attack); c.Run(40); c.Event("interrupt=" + c.Skill.TryInterrupt(EveId.Parse("test:stun"))); c.Run(30); c.Release(Attack); c.Run(30); }),
        ("burst_end_during_repeat", c => c.Skill.BurstGauge.Add(150), c => { c.Tap(Burst); c.Run(5); c.Hold(Attack); c.Run(40); c.Skill.BurstState.End(); c.Run(30); c.Release(Attack); c.Run(30); }),
        ("death_mid_repeat", c => c.Skill.BurstGauge.Add(150), c => { c.Tap(Burst); c.Run(5); c.Hold(Attack); c.Run(40); c.Caster.SetCurrent(PNContentIds.Health, 0); c.Run(40); c.Release(Attack); c.Run(10); }),
        ("death_mid_ultimate", null, c => { c.Skill.UltimateRecovery.ResetToFull(); c.Tap(Ult); c.Run(40); c.Caster.SetCurrent(PNContentIds.Health, 0); c.Run(40); c.Tap(Attack); c.Run(10); }),
        ("attack_speed_plus_50", c => c.Caster.SetSource(EveId.Parse("test:speed"), new[] { new EveStatModifier(PNContentIds.AttackSpeed, 50d, 0) }), c =>
        {
            c.Spam(Attack, 3, 120); c.Tap(SkillSlot); c.Spam(Attack, 3, 120); c.Skill.UltimateRecovery.ResetToFull(); c.Tap(Ult); c.Run(300);
        }),
        ("attack_speed_burst_repeat", c => { c.Skill.BurstGauge.Add(150); c.Caster.SetSource(EveId.Parse("test:speed"), new[] { new EveStatModifier(PNContentIds.AttackSpeed, 50d, 0) }); },
            c => { c.Tap(Burst); c.Run(5); c.Hold(Attack); c.Run(300); c.Release(Attack); c.Run(30); }),
    };

    private static string Trace(string mutate)
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Eve trace requires idle Edit Mode.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        long id = 987000; var all = new List<string>();
        T Entity<T>(string name, Vector3 position, string character, out EveEntityLoader loader) where T : PNStat
        {
            var root = new GameObject(name); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview); root.transform.position = position;
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(++id), EveId.Parse(character), 1);
            var stat = root.AddComponent<T>().Declared(); stat.gameObject.layer = stat is PNEnemyStat ? PNLayers.Enemy : stat is PNPlayerStat ? PNLayers.Player : stat.gameObject.layer;
            if (character == "project_n:eve")
            {
                root.AddComponent<Rigidbody>().useGravity = false;
                var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f; capsule.center = Vector3.up * .3f;
                root.AddComponent<PNPlayerMove>(); root.AddComponent<PNAbilityManager>();
                root.AddComponent<EveTraceProbe>().Prepare();
            }
            else { var sphere = root.AddComponent<SphereCollider>(); sphere.radius = .08f; }
            loader = root.AddComponent<EveEntityLoader>(); loader.Load(hub); root.SetActive(true); return stat;
        }
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:eve_trace"));
        var areaRoot = new GameObject("Eve trace area fixture"); SceneManager.MoveGameObjectToScene(areaRoot, preview);
        var areaManager = areaRoot.AddComponent<PNAreaManager>(); areaManager.Initialize(EveRegistrySession.Context);
        var projectileManager = areaRoot.AddComponent<PNProjectileManager>(); projectileManager.Initialize(EveRegistrySession.Context, new PNProjectileProfile { MaxActive = 20 }); projectileManager.ManualAdvance = true;
        try
        {
            foreach (var scenario in Scenarios)
            {
                var caster = Entity<EveCharacterStat>("Eve", Vector3.zero, "project_n:eve", out var casterLoader);
                var near = Entity<DummyStat>("Near", new Vector3(.45f, .4f, 0), "project_n:dummy", out var nearLoader);
                var low = Entity<DummyStat>("Low", new Vector3(0, .4f, 3), "project_n:dummy", out var lowLoader); low.SetCurrent(PNContentIds.Health, 10000);
                caster.SetSource(EveId.Parse("test:crit_off"), new[] { new EveStatModifier(PNContentIds.CritRate, -20d, 0) });
                var ctx = new Ctx { Name = scenario.Name, Skill = caster.GetComponent<EveTraceProbe>(), Caster = caster, Abilities = caster.GetComponent<PNAbilityManager>(), CasterBuffs = caster.GetComponent<PNBuffManager>(), Near = near, Low = low };
                if (mutate == "combo_reset") ctx.Skill.Moveset.ComboReset *= .1f;
                if (mutate == "attack_speed") caster.SetSource(EveId.Parse("test:mutate"), new[] { new EveStatModifier(PNContentIds.AttackSpeed, 1d, 0) });
                // The Buff managers start with their first Advance (EveBurstBuffEditorChecks does the same); a Buff applied before it stays pending.
                ctx.CasterBuffs.Advance(0); near.GetComponent<PNBuffManager>()?.Advance(0); low.GetComponent<PNBuffManager>()?.Advance(0);
                Physics.SyncTransforms();
                try
                {
                    scenario.Setup?.Invoke(ctx); ctx.Lines.Add(scenario.Name + "#0000 " + ctx.Skill.Line() + " | near=" + ctx.Near.GetCurrent(PNContentIds.Health) + " low=" + ctx.Low.GetCurrent(PNContentIds.Health));
                    scenario.Script(ctx);
                }
                finally
                {
                    try { ctx.Skill.TryInterrupt(EveId.Parse("test:end")); } catch (Exception) { }
                    foreach (var loader in new[] { casterLoader, nearLoader, lowLoader }) try { loader.Unload(); } catch (Exception) { }
                    // Not destroyed: the session's entity directory unloads again at the end. Disabled objects no longer answer physics queries of the next scenario.
                    foreach (var o in new[] { caster.gameObject, near.gameObject, low.gameObject }) o.SetActive(false);
                }
                all.AddRange(ctx.Lines);
            }
        }
        finally
        {
            try { projectileManager.Shutdown(); areaManager.Shutdown(); EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        if (original.isDirty != dirty) throw new Exception("Eve trace changed active scene dirty state.");
        return string.Join("\n", all) + "\n";
    }

    private static string Hash(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16); }

    // The Skill's homing projectile logs an Edit Mode "Destroy" error on dispose (see PNTrailHomingProjectile.Dispose), which makes the MCP command report an error
    // although the check passed; Execute writes the verdict to a file the caller reads instead.
    public const string ResultPath = ".utmp/EveMovesetTrace/result.txt";
    public static string Execute(bool record = false, string mutate = null)
    {
        string text;
        try { text = Run(record, mutate); } catch (Exception error) { text = "FAIL " + error.Message; }
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)); File.WriteAllText(ResultPath, text + Environment.NewLine, new UTF8Encoding(false)); return text;
    }

    public static string Run(bool record = false, string mutate = null)
    {
        string first = Trace(mutate), second = Trace(mutate);
        Directory.CreateDirectory(Path.GetDirectoryName(CurrentPath)); File.WriteAllText(CurrentPath, first, new UTF8Encoding(false));
        if (first != second) { File.WriteAllText(CurrentPath + ".second", second, new UTF8Encoding(false)); throw new Exception("Eve trace is not deterministic: first=" + Hash(first) + " second=" + Hash(second) + " (" + Diff(first, second) + ")"); }
        int lines = first.Split('\n').Length - 1;
        if (record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)); File.WriteAllText(BaselinePath, first, new UTF8Encoding(false));
            return "RECORDED " + lines + " trace lines, " + Scenarios.Length + " scenarios, hash " + Hash(first) + " -> " + BaselinePath;
        }
        if (!File.Exists(BaselinePath)) throw new FileNotFoundException("No baseline yet; run EveMovesetTraceChecks.Run(record: true) once on the unchanged code.", BaselinePath);
        string baseline = File.ReadAllText(BaselinePath);
        if (baseline != first) throw new Exception("Eve trace differs from the baseline: " + Diff(baseline, first) + " (current " + Hash(first) + ", baseline " + Hash(baseline) + ")");
        return "PASS " + lines + " trace lines in " + Scenarios.Length + " scenarios match the baseline (deterministic over two runs), hash " + Hash(first);
    }

    private static string Diff(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            string l = i < x.Length ? x[i] : "<end>", r = i < y.Length ? y[i] : "<end>";
            if (l != r) return "first difference at line " + (i + 1) + ":\n  expected: " + l + "\n  actual:   " + r;
        }
        return "no difference";
    }
}
#endif
