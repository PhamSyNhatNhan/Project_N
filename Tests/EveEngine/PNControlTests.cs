using System;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

public static class PNControlTests
{
    public static void Run()
    {
        var runtime = new EveGameRuntime(); runtime.Initialize(); var hub = EveRegistrySession.Current;
        try
        {
            Check(hub.ControlPermissions().Count == 12 && hub.ControlPermissions().IsSealed, "Sealed permission registry");
            foreach (var id in PNControlIds.All) Check(hub.ControlPermissions().Get(new EveId("base", id.Path)).Id == id, "Canonical permission alias");
            var customHub = new EveRegistryHub(); new PNContentModule().Register(customHub);
            var custom = EveId.Parse("addon:interact"); customHub.ControlPermissions().Register(new PNControlPermissionDefinition(custom));
            customHub.Seal();
            using (var customState = new PNControlState(customHub))
            { using (customState.Acquire("custom", new[] { custom })) Check(!customState.IsAllowed(custom), "Code modules can extend permission vocabulary"); }
            using (var replica = new PNControlState(hub, () => false))
            {
                Reject(() => replica.Acquire("client", new[] { PNControlIds.Move }), "Replica cannot mutate control leases");
                Reject(() => replica.SetBaseAllowed(PNControlIds.Move, false), "Replica cannot mutate control base");
                Check(replica.IsAllowed(PNControlIds.Move), "Read-only permission query remains available");
            }
            using (var state = new PNControlState(hub))
            using (var other = new PNControlState(hub))
            {
                int notices = 0; state.Changed += () => notices++;
                var a = state.Acquire("A", new[] { PNControlIds.Move }); var b = state.Acquire("B", new[] { EveId.Parse("base:move") });
                Check(notices == 1 && other.IsAllowed(PNControlIds.Move), "Overlap notifications and entity isolation");
                var snapshot = state.Snapshot(); a.Dispose();
                Check(!state.IsAllowed(PNControlIds.Move) && notices == 1, "Older lease release preserves newer lock");
                b.Dispose(); b.Dispose(); Check(state.IsAllowed(PNControlIds.Move) && notices == 2 && snapshot.Count == 2, "Idempotent release and immutable snapshot");
                using (state.Acquire("base", new[] { PNControlIds.Move })) state.SetBaseAllowed(PNControlIds.Move, false);
                Check(!state.IsAllowed(PNControlIds.Move), "Release retains current base permission"); state.SetBaseAllowed(PNControlIds.Move, true);
                Reject(() => state.Acquire("bad", new[] { PNControlIds.Move, EveId.Parse("test:missing") }), "Atomic multi-permission validation");
                Check(state.Snapshot().Count == 0, "Invalid lease leaves no locks");
                int failures = 0; state.ObserverFailed += error => failures++; state.Changed += () => { throw new Exception("observer"); };
                using (state.Acquire("observer", new[] { PNControlIds.ForcedMotion }))
                {
                    Check(!state.IsAllowed(PNControlIds.ForcedMotion) && state.IsAllowed(PNControlIds.VoluntaryMotion), "Forced and voluntary permissions independent despite observer exception");
                }
                Check(failures == 2 && state.IsAllowed(PNControlIds.ForcedMotion), "Observer failure cannot strand a lease");
                Reject(() => state.Acquire("bad", skillIds: new[] { EveId.Parse("test:missing") }), "Unknown skill rejected");
                var stale = state.Acquire("stale", new[] { PNControlIds.Move }); state.Dispose(); stale.Dispose();
                Check(!stale.IsActive && other.IsAllowed(PNControlIds.Move), "Disposed state invalidates lease");
            }
            Integration(hub);
        }
        finally { EveRegistrySession.End(); }
    }
    private static void Integration(EveRegistryHub hub)
    {
        var loader = new EveTestLoader(); var stat = new EveCharacterStat(); var skill = new EveSlashProbeSkill();
        var identity = new EveEntityIdentity(); identity.Assign(new EveEntityId(989900), EveId.Parse("project_n:eve"), 0);
        var move = new EveTestPlayerMove(); var abilities = new PNAbilityManager();
        MonoBehaviour[] components = { loader, identity, stat, skill, move, abilities, new Rigidbody(), new Collider() };
        foreach (var c in components) { c.Components = components; c.gameObject.SetActive(true); }
        try
        {
            skill.InitializeInput(); loader.Load(hub);
            Check(loader.GetComponents<PNControlManager>().Length == 1, "Preparation creates one shared manager");
            var state = skill.Controls;
            Func<bool>[] tries = { () => skill.TryAttack(), () => skill.TrySkill(), () => skill.TryDash(), () => skill.TryUltimate(), () => skill.TryBurst() };
            var groups = new[] { PNControlIds.Attack, PNControlIds.Skill, PNControlIds.Dash, PNControlIds.Ultimate, PNControlIds.Burst };
            var ids = new[] { PNGameplayContentModule.EveAttackEntry, PNGameplayContentModule.EveSkillEntry, EveId.Parse("project_n:eve_dash"), PNGameplayContentModule.EveUltimateEntry, PNGameplayContentModule.EveBurstEntry };
            skill.BurstGauge.Add(150);
            for (int i = 0; i < groups.Length; i++)
            {
                double gauge = skill.BurstGauge.Current, dash = skill.DashRecovery.Current, cast = skill.SkillRecovery.Current, ulti = skill.UltimateRecovery.Current;
                using (state.Acquire("group", new[] { groups[i] })) Check(!tries[i](), "Direct Try category rejected");
                using (state.Acquire("exact", skillIds: new[] { new EveId("base", ids[i].Path) })) Check(!tries[i](), "Exact skill entry rejected");
                Check(gauge == skill.BurstGauge.Current && dash == skill.DashRecovery.Current && cast == skill.SkillRecovery.Current && ulti == skill.UltimateRecovery.Current && !skill.IsBusy, "Denied activation preserves resources and phase");
            }
            using (state.Acquire("all", new[] { PNControlIds.SkillAll })) foreach (var activate in tries) Check(!activate(), "Global lease blocks every Try");
            var external = state.Acquire("external", new[] { PNControlIds.Move, PNControlIds.FaceControl, PNControlIds.Jump });
            Check(!move.CanMove && !move.CanJump && !move.CanFlip, "Effective movement getters");
            Check(move.TryFaceSkill(new Vector2(-1, 0)) && move.FacingDirection == -1, "Skill-facing under ordinary facing lock");
            using (state.Acquire("face skill", new[] { PNControlIds.FaceSkill })) Check(!move.TryFaceSkill(Vector2.right) && move.FacingDirection == -1, "Skill-facing cannot bypass external lock");
            Check(skill.TryDash(), "Walking lock permits voluntary Dash"); Time.deltaTime = .2f; skill.Tick();
            Check(!move.CanMove && state.Snapshot().Count == 1, "Dash cleanup preserves external owner");
            external.Dispose(); Check(move.CanMove, "Last movement owner unlocks");
            using (state.Acquire("motion", new[] { PNControlIds.VoluntaryMotion })) Check(!skill.TryDash() && !skill.TrySkill() && !skill.TryUltimate(), "Motion permission precedes resource commits");
            move.MenuInputBlocked = true;
            var contextAction = skill.CreateAction(PNGameplayContentModule.EveSlash, out var context);
            using (var operation = hub.Operations().Get(EveId.Parse("base:lock_movement")).Create())
            { operation.Execute(context, null); move.MenuInputBlocked = false; Check(!move.CanMove, "Menu release cannot release action lease"); }
            contextAction.Dispose(); Check(move.CanInput && move.CanMove, "Operation does not capture menu gate into base input");
            Check(skill.TryDash() && skill.TryInterrupt(EveId.Parse("test:interrupt")) && !skill.IsBusy && state.Snapshot().Count == 0, "Explicit interruption cleans nested leases");
            var old = state.Acquire("old", new[] { PNControlIds.Move }); loader.Unload(); loader.Load(hub); old.Dispose();
            Check(skill.Controls.IsAllowed(PNControlIds.Move) && !old.IsActive, "Reload invalidates old state");
        }
        finally { skill.DestroyInput(); loader.Unload(); Time.deltaTime = .02f; }
    }
}
