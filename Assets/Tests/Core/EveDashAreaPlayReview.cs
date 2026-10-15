#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public sealed class EveDashAreaPlayReview : MonoBehaviour
{
    public static string Status = "Idle";
    private readonly List<GameObject> fixtures = new List<GameObject>();
    private readonly List<string> evidence = new List<string>();
    private readonly List<PNAreaHandle> handles = new List<PNAreaHandle>();
    private Keyboard keyboard; private bool background, cleaned;
    public static string Run()
    {
        if (!Application.isPlaying || EditorApplication.isPaused || !EveRegistrySession.IsReady) throw new InvalidOperationException("Ready unpaused Play required.");
        Status = "Running"; new GameObject("Disposable Eve Dash area Play review").AddComponent<EveDashAreaPlayReview>(); return Status;
    }
    private void Start() { background = Application.runInBackground; Application.runInBackground = true; EditorApplication.update += Pump; StartCoroutine(Guarded()); }
    private void Pump() => EditorApplication.QueuePlayerLoopUpdate();
    private void Check(bool ok, string name) { if (!ok) throw new Exception(name); evidence.Add("PASS " + name); }
    private IEnumerator Guarded()
    {
        var iterator = Review();
        try
        {
            while (true)
            {
                object next; try { if (!iterator.MoveNext()) break; next = iterator.Current; }
                catch (Exception error) { Status = "FAIL " + error; evidence.Add(Status); yield break; }
                yield return next;
            }
            Status = "PASS " + evidence.Count(x => x.StartsWith("PASS ")) + " Eve Dash area Play checks"; evidence.Add(Status);
        }
        finally { (iterator as IDisposable)?.Dispose(); Cleanup(); Directory.CreateDirectory(".utmp/EveDashAreaReview"); File.WriteAllLines(".utmp/EveDashAreaReview/PlayResults.txt", evidence); Destroy(gameObject); }
    }
    private GameObject Root(string name, long id, string character, Vector3 position, int layer)
    {
        var obj = new GameObject(name); fixtures.Add(obj); obj.SetActive(false); obj.transform.position = position; obj.layer = layer;
        obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), 0); return obj;
    }
    private IEnumerator Review()
    {
        var origin = new Vector3(500, 5, 500); var eve = Root("Dash input fixture Eve", 991101, "project_n:eve", origin, 6);
        eve.AddComponent<EveTimeScale>(); var stat = eve.AddComponent<EveCharacterStat>(); eve.AddComponent<PNAbilityManager>(); var body = eve.AddComponent<Rigidbody>(); body.useGravity = false;
        var capsule = eve.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f; capsule.center = Vector3.up * .3f;
        var move = eve.AddComponent<PNPlayerMove>(); var skill = eve.AddComponent<EveCharacterSkill>(); var loader = eve.AddComponent<EveEntityLoader>(); loader.Load(EveRegistrySession.Current); eve.SetActive(true);
        var enemyRoot = Root("Dash field input receiver", 991102, "project_n:dummy", origin + Vector3.right * 2, 7); enemyRoot.AddComponent<EveTimeScale>(); enemyRoot.AddComponent<SphereCollider>().radius = .2f;
        var enemy = enemyRoot.AddComponent<PNEnemyStat>(); enemyRoot.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current); enemyRoot.SetActive(true);
        yield return null; move.PC.devices = Array.Empty<InputDevice>();
        foreach (var action in new[] { skill.PC.Controller.Attack, skill.PC.Controller.Skill, skill.PC.Controller.Ulti, skill.PC.Controller.Dash, skill.PC.Controller.Burst }) action.bindingMask = new InputBinding { groups = "DashReviewIsolated" };
        skill.PC.Controller.Dash.Disable(); skill.PC.Controller.Dash.AddBinding("<Keyboard>/f12", groups: "DashReviewIsolated"); skill.RefreshInput();
        keyboard = InputSystem.AddDevice<Keyboard>();
        move.SetMoveDirection(Vector2.right); move.SetMoveDirection(Vector2.zero); Physics.SyncTransforms();
        var areas = PNAreaManager.Resolve(EveRegistrySession.Context, eve.scene); var before = areas.Snapshot().Select(x => x.Handle.InstanceId).ToHashSet();
        yield return null;
        int performed = 0; skill.PC.Controller.Dash.performed += _ => performed++;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F12)); InputSystem.Update();
        evidence.Add("Input diagnostic: enabled=" + skill.PC.Controller.Dash.enabled + " controls=" + skill.PC.Controller.Dash.controls.Count + " canInput=" + skill.CanInput + " ready=" + skill.IsReady + " key=" + keyboard.f12Key.isPressed + " performed=" + performed + " charges=" + skill.DashRecovery.Current + " cast=" + skill.CurrentCast);
        Check(skill.IsDashing && skill.DashRecovery.Current == 2, "Real Input System press activates Eve Dash and consumes one charge");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        yield return new WaitForSecondsRealtime(.3f);
        Check(!skill.IsDashing && Math.Abs(body.position.x - origin.x - 2) < .03, "Real Update moves Rigidbody exactly two units");
        var created = areas.Snapshot().Where(x => !before.Contains(x.Handle.InstanceId) && x.IsRunning).ToArray(); handles.AddRange(created.Select(x => x.Handle));
        Check(created.Length == 1 && Vector3.Distance(created[0].Position, body.position) < .01, "Input Dash completion produces one endpoint region");
        yield return new WaitForSecondsRealtime(.08f);
        Check(Math.Abs(enemy.GetComponent<EveTimeScale>().Scale - .1f) < .00001 && eve.GetComponent<EveTimeScale>().Scale == 1, "Enemy slowed while Eve retains own clock");
        var fixedCenter = created[0].Position; body.position += Vector3.right; eve.transform.position = body.position; Physics.SyncTransforms(); yield return new WaitForSecondsRealtime(.08f);
        Check(areas.Get(created[0].Handle).Position == fixedCenter, "Dash region remains fixed after Eve moves");
        body.position = origin; eve.transform.position = origin; Physics.SyncTransforms();
        Check(skill.TryDash(), "Second actual Dash starts"); yield return new WaitForSecondsRealtime(.3f);
        var all = areas.Snapshot().Where(x => !before.Contains(x.Handle.InstanceId) && x.IsRunning).ToArray(); handles.AddRange(all.Select(x => x.Handle));
        Check(all.Length == 2 && enemy.GetComponent<EveTimeScale>().Scale == 0, "Actual repeated Dash keeps independent overlapping regions");
        Check(skill.TryDash(), "Third Dash starts before cancellation"); skill.TryInterrupt(PNControlIds.Dash); yield return new WaitForSecondsRealtime(.2f);
        Check(areas.Snapshot().Count(x => !before.Contains(x.Handle.InstanceId) && x.IsRunning) == 2, "Cancelled Play Dash adds no region");
        loader.Unload(); yield return new WaitForSecondsRealtime(.1f);
        Check(all.All(x => x.Handle.IsAlive), "15-second independent regions survive Eve unload");
        foreach (var handle in handles) handle.End(); yield return new WaitForSecondsRealtime(.4f);
        Check(all.All(x => !x.Handle.IsAlive) && enemy.GetComponent<EveTimeScale>().Scale == 1, "Exact Play cleanup restores enemy timescale");
    }
    private void Cleanup()
    {
        if (cleaned) return; cleaned = true; EditorApplication.update -= Pump;
        foreach (var handle in handles) handle.End();
        foreach (var fixture in fixtures) if (fixture != null) { fixture.GetComponent<EveEntityLoader>()?.Unload(); Destroy(fixture); }
        fixtures.Clear(); if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); Application.runInBackground = background;
    }
    private void OnDestroy() => Cleanup();
}
#endif
