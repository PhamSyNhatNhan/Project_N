#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

public class PNControlMoveEditorProbe : PNPlayerMove
{
    protected override void Awake() { if (pc == null) base.Awake(); }
    public void InitializeInput() { Awake(); OnEnable(); }
    public void RetireEditorInput()
    { if (pc == null) return; pc.Disable(); UnityEngine.Object.DestroyImmediate(pc.asset); pc = null; }
    public void TickInput() => Update();
    public void TickPhysics() => FixedUpdate();
}

public static class PNControlEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Control checks require stopped session and idle Edit Mode.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene(); Keyboard keyboard = null; PNControlMoveEditorProbe reviewedMove = null; PNAreaManager reviewedAreas = null; bool started = false; int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
            Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        try
        {
            EveRegistrySession.Begin(); started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:control_native"));
            var areaRoot = new GameObject("Control fixture area service"); SceneManager.MoveGameObjectToScene(areaRoot, preview); reviewedAreas = areaRoot.AddComponent<PNAreaManager>(); reviewedAreas.Initialize(EveRegistrySession.Context);
            var root = new GameObject("Control native Eve fixture"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(989901), EveId.Parse("project_n:eve"), 0);
            var stat = root.AddComponent<EveCharacterStat>(); root.AddComponent<PNAbilityManager>();
            var body = root.AddComponent<Rigidbody>(); body.useGravity = false;
            var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f; capsule.center = Vector3.up * .3f;
            var move = root.AddComponent<PNControlMoveEditorProbe>(); reviewedMove = move; move.InitializeInput();
            var skill = root.AddComponent<PNEveAttackSpeedProbe>();
            var loader = root.AddComponent<EveEntityLoader>(); loader.Load(hub); root.SetActive(true);
            var state = skill.Controls;
            Check(root.GetComponents<PNControlManager>().Length == 1, "One manager prepared before Load.");
            skill.BurstGauge.Add(150);
            Func<bool>[] tries = { () => skill.TryAttack(), () => skill.TrySkill(), () => skill.TryDash(), () => skill.TryUltimate(), () => skill.TryBurst() };
            var categories = new[] { PNControlIds.Attack, PNControlIds.Skill, PNControlIds.Dash, PNControlIds.Ultimate, PNControlIds.Burst };
            var ids = new[] { PNGameplayContentModule.EveAttackEntry, PNGameplayContentModule.EveSkillEntry, EveId.Parse("project_n:eve_dash"), PNGameplayContentModule.EveUltimateEntry, PNGameplayContentModule.EveBurstEntry };
            for (int i = 0; i < categories.Length; i++)
            {
                double gauge = skill.BurstGauge.Current, dash = skill.DashRecovery.Current, cast = skill.SkillRecovery.Current, ulti = skill.UltimateRecovery.Current;
                using (state.Acquire("native group", new[] { categories[i] })) Check(!tries[i](), "Native category gate " + categories[i]);
                using (state.Acquire("native exact", skillIds: new[] { new EveId("base", ids[i].Path) })) Check(!tries[i](), "Native exact ID gate " + ids[i]);
                Check(gauge == skill.BurstGauge.Current && dash == skill.DashRecovery.Current && cast == skill.SkillRecovery.Current && ulti == skill.UltimateRecovery.Current && !skill.IsBusy, "Native denied activation has no side effect.");
            }
            using (state.Acquire("native all", new[] { PNControlIds.SkillAll })) foreach (var activate in tries) Check(!activate(), "Global gate.");
            move.SetMoveDirection(Vector2.right); move.TickPhysics();
            Check(move.MoveDirection.x > .9f && body.linearVelocity.x > 0, "Native movement command produces Rigidbody velocity.");
            float start = body.position.x; preview.GetPhysicsScene().Simulate(.02f);
            Check(body.position.x > start, "PhysX advances walking position.");
            var outer = state.Acquire("native outer", new[] { PNControlIds.Move, PNControlIds.FaceControl, PNControlIds.Jump });
            move.TickInput(); move.TickPhysics();
            Check(move.MoveDirection.sqrMagnitude == 0 && Math.Abs(body.linearVelocity.x) < .001f && !move.CanJump, "Lease blocks held walking and jump.");
            Check(move.TryFaceSkill(new Vector2(-1, 0)) && move.FacingDirection == -1, "Skill facing works under ordinary lock.");
            using (state.Acquire("native facing", new[] { PNControlIds.FaceSkill })) Check(!move.TryFaceSkill(Vector2.right) && move.FacingDirection == -1, "Skill facing respects external lease.");
            start = body.position.x;
            Check(skill.TryDash(), "Walking lease does not prevent voluntary Dash."); skill.TickAttack(skill.DashSettings.Motion.Duration + .01f);
            Check(Math.Abs(Math.Abs(body.position.x - start) - skill.DashSettings.Motion.Distance) < .01f && !skill.IsBusy && state.Snapshot().Count == 1 && !move.CanMove, "Registered Dash distance and nested cleanup.");
            outer.Dispose(); Check(move.CanMove, "Outer lease restores effective movement.");
            move.CanMove = false;
            using (state.Acquire("native base", new[] { PNControlIds.Move })) { }
            Check(!move.CanMove, "Base false survives lease release."); move.CanMove = true;
            using (state.Acquire("native motion", new[] { PNControlIds.VoluntaryMotion })) Check(!skill.TryDash() && !skill.TrySkill() && !skill.TryUltimate(), "Motion denied before cast commits.");
            skill.MenuInputBlocked = true;
            var action = skill.CreateAction(EveId.Parse("base:eve_slash"), out var context);
            using (var operation = hub.Operations().Get(EveId.Parse("base:lock_movement")).Create())
            { operation.Execute(context, null); skill.MenuInputBlocked = false; Check(!move.CanMove, "Menu release preserves operation lease."); }
            action.Dispose(); Check(move.CanMove && skill.CanInput, "Operation does not poison base input.");
            Check(skill.TryDash() && skill.TryInterrupt(EveId.Parse("test:interrupt")) && !skill.IsBusy && state.Snapshot().Count == 0, "Explicit interruption releases native Action leases.");
            var old = state.Acquire("native stale", new[] { PNControlIds.Move }); loader.Unload(); loader.Load(hub); old.Dispose();
            Check(!old.IsActive && skill.Controls.IsAllowed(PNControlIds.Move), "Native reload invalidates old ownership.");
            loader.Unload();
        }
        finally
        {
            if (reviewedMove != null) reviewedMove.RetireEditorInput();
            if (reviewedAreas != null) reviewedAreas.Shutdown();
            try { if (keyboard != null) InputSystem.RemoveDevice(keyboard); }
            finally { try { if (started) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(preview); } }
        }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene unchanged.");
        return "PASS " + checks + " native control checks; PhysX walking, Rigidbody Dash, category/global/exact gates, no resource consumption, facing, menu and lifecycle.";
    }
}
#endif
