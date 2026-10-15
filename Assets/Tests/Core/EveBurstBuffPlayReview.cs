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

public sealed class EveBurstBuffPlayReview : MonoBehaviour
{
    public static string Status = "Idle";
    private readonly List<GameObject> fixtures = new List<GameObject>();
    private readonly List<string> evidence = new List<string>();
    private bool background, cleaned;
    private int checks;
    public static string Run()
    {
        if (!Application.isPlaying || !EveRegistrySession.IsReady || EditorApplication.isPaused) throw new InvalidOperationException("Ready unpaused Play required.");
        Status = "Running"; var root = new GameObject("Eve Burst Buff Play review").AddComponent<EveBurstBuffPlayReview>(); EditorApplication.update += root.Pump; return Status;
    }
    private void Pump() => EditorApplication.QueuePlayerLoopUpdate();
    private void Start() => StartCoroutine(Guarded());
    private void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; evidence.Add("PASS " + message); }
    private IEnumerator Wait(Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + 8;
        while (!condition()) { if (Time.realtimeSinceStartup > deadline) throw new Exception("Play condition timed out"); yield return null; }
    }
    private IEnumerator Guarded()
    {
        background = Application.runInBackground; Application.runInBackground = true;
        var sequences = new Stack<IEnumerator>(); sequences.Push(Sequence());
        while (true)
        {
            object next;
            try
            {
                if (sequences.Count == 0) { Status = "PASS " + checks + " Eve Burst Buff Play checks"; break; }
                var sequence = sequences.Peek();
                if (!sequence.MoveNext()) { (sequences.Pop() as IDisposable)?.Dispose(); continue; }
                next = sequence.Current;
                if (next is IEnumerator nested) { sequences.Push(nested); continue; }
            }
            catch (Exception error) { Status = "FAIL " + error; break; }
            yield return next;
        }
        foreach (var sequence in sequences) (sequence as IDisposable)?.Dispose(); Cleanup(); Directory.CreateDirectory(".utmp/EveBurstBuffReview"); File.WriteAllLines(".utmp/EveBurstBuffReview/PlayResults.txt", evidence.Concat(new[] { Status })); Destroy(gameObject);
    }
    private GameObject Root(string name, long id, string character, Vector3 position)
    {
        var obj = new GameObject(name); fixtures.Add(obj); obj.SetActive(false); obj.transform.position = position; obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), 0); return obj;
    }
    private IEnumerator Sequence()
    {
        var actor = Root("Eve Burst fixture", 989301, "project_n:eve", new Vector3(500, 5, 500)); var stat = actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
        var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
        var move = actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveBurstBuffProbe>(); var loader = actor.AddComponent<EveEntityLoader>(); loader.Load(EveRegistrySession.Current); actor.SetActive(true); yield return null;
        move.PC.devices = Array.Empty<InputDevice>();
        foreach (var action in new[] { skill.PC.Controller.Attack, skill.PC.Controller.Skill, skill.PC.Controller.Ulti, skill.PC.Controller.Dash, skill.PC.Controller.Burst })
            action.bindingMask = new InputBinding { groups = "EveBurstReviewIsolated" };
        var targets = new List<PNEnemyStat>();
        for (int i = 0; i < 2; i++)
        {
            var obj = Root("Burst Play target " + i, 989302 + i, "project_n:dummy", actor.transform.position + new Vector3(.45f + i * .12f, .4f, 0));
            var target = obj.AddComponent<PNEnemyStat>(); obj.AddComponent<SphereCollider>().radius = .15f; obj.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current); obj.SetActive(true); targets.Add(target);
        }
        yield return null; Physics.SyncTransforms();
        var burnId = EveRegistrySession.Current.Buffs().ResolveIdentity(EveId.Parse("base:burn"));
        int Burn(PNEnemyStat target) => (int)(target.GetComponent<PNBuffManager>().Buffs.Snapshot().FirstOrDefault(x => x.Id == burnId)?.Stacks ?? 0);
        Check(skill.TryAttack(Vector3.right), "Actual Attack starts outside Burst."); yield return Wait(() => !skill.IsBusy);
        Check(targets.All(x => Burn(x) == 0), "Actual outside-Burst hit grants no Burn.");
        skill.BurstGauge.Add(150); Check(skill.TryBurst() && skill.BlazingAssault != null, "Actual Burst grants Blazing Assault.");
        Check(skill.TryAttack(Vector3.right), "Actual Burst normal Attack starts."); yield return Wait(() => !skill.IsBusy);
        Check(targets.All(x => Burn(x) == 1) && skill.RisingMomentum == null, "Real Burst contacts apply Burn, not Momentum, on normal Attack.");
        int peak = 0;
        var ownerBuffs = actor.GetComponent<PNBuffManager>().Buffs;
        Action watch = () => { if (skill.RisingMomentum != null) peak = Math.Max(peak, (int)skill.RisingMomentum.Stacks.Value); };
        ownerBuffs.Changed += watch;
        skill.Hold(); yield return Wait(() => skill.RisingMomentum?.Stacks >= 2);
        Check(skill.RisingMomentum.Stacks >= 2 && stat.Evaluate(PNContentIds.AttackSpeed) >= 170 && stat.Evaluate(PNContentIds.AttackPower) >= 132, "Normal Update accumulates Momentum and reads Live speed/ATK.");
        skill.Release(); yield return Wait(() => !skill.IsBusy);
        Check(skill.RisingMomentum == null && stat.Evaluate(PNContentIds.AttackSpeed) == 150 && stat.Evaluate(PNContentIds.AttackPower) == 110, "Released held chain resets bonus after final stroke.");
        Check(peak >= 2 && targets.All(x => Burn(x) >= 3), "Repeat hits grant Burn while accumulated tiers remain per stroke.");
        ownerBuffs.Changed -= watch;
        foreach (var target in targets) target.gameObject.SetActive(false); skill.SlashStacks.Clear(); skill.SlashStacks.Add(1); skill.Release(); skill.Hold(); yield return Wait(() => !skill.IsBusy);
        Check(skill.RisingMomentum == null && skill.SlashStacks.Current == 0, "Actual missed repeat/exhaustion gives no Momentum."); skill.Release();
        foreach (var target in targets) target.gameObject.SetActive(true); actor.transform.position = body.position = new Vector3(500, 5, 500); Physics.SyncTransforms(); skill.SlashStacks.Add(4); skill.Hold(); yield return Wait(() => skill.RisingMomentum != null);
        skill.enabled = false; yield return null;
        Check(skill.RisingMomentum == null && skill.BlazingAssault != null && !skill.IsBusy, "Real OnDisable resets chain and preserves Burst-owned Buff."); skill.enabled = true; yield return null;
        skill.Release(); skill.Hold(); yield return Wait(() => skill.RisingMomentum != null); skill.BurstState.End();
        Check(skill.RisingMomentum == null && skill.BlazingAssault == null && !skill.IsBusy && stat.Evaluate(PNContentIds.AttackSpeed) == 100, "Actual Burst end removes both Buffs and restores speed.");
        Check(targets.All(x => Burn(x) > 0), "Burn on targets survives Burst end.");
        loader.Unload(); loader.Load(EveRegistrySession.Current); Check(skill.RisingMomentum == null && skill.BlazingAssault == null, "Actual reload retires old owned handles.");
    }
    private void Cleanup()
    {
        if (cleaned) return; cleaned = true; EditorApplication.update -= Pump;
        try { foreach (var obj in fixtures) if (obj != null) { obj.GetComponent<EveEntityLoader>()?.Unload(); Destroy(obj); } fixtures.Clear(); }
        finally { Application.runInBackground = background; }
    }
    private void OnDestroy() => Cleanup();
}
#endif
