using System.Linq;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

public static class EveMovesetTests
{
    public static void Run()
    {
        var runtime = new EveGameRuntime(); runtime.Initialize();
        var loader = new EveTestLoader(); var stat = new EveCharacterStat(); var skill = new EveSlashProbeSkill();
        var identity = new EveEntityIdentity(); identity.Assign(new EveEntityId(985000), EveId.Parse("project_n:eve"), 0);
        var move = new EveTestPlayerMove(); var manager = new PNAbilityManager();
        MonoBehaviour[] components = { loader, identity, stat, skill, move, manager, new Rigidbody(), new Collider() };
        foreach (var component in components) { component.Components = components; component.gameObject.SetActive(true); }
        void Tick(float seconds) { Time.deltaTime = seconds; skill.Tick(); }
        try
        {
            skill.InitializeInput(); loader.Load(EveRegistrySession.Current);
            Check(skill.AttackInput.HoldMode == PlayerSkillHoldMode.Repeat && skill.DashRecovery.Current == 3,
                "Eve initializes Repeat input and three sequential dash charges");
            Check(skill.DashSettings.Motion.Distance == 2, "Eve Dash selected payload distance is two units");
            var dashDefinition = EveRegistrySession.Current.Skills.GetEffective(EveId.Parse("base:eve_dash"));
            var effects = PNSkillMetadata.Parse((Newtonsoft.Json.Linq.JObject)dashDefinition.Data, EveRegistrySession.Current).Effects;
            Check(effects.Count(x => x.Trigger == "OnComplete" && x.Id == EveId.Parse("base:create_area") && x.Position == "Self") == 1, "Eve Dash has one endpoint area effect on successful completion");
            Check(skill.BurstGauge.Capacity == 150 && skill.SlashStacks.Capacity == 12 && !skill.TryBurst(),
                "Burst requires 100 points and has distinct gauge/stack capacities");
            Check(skill.TrySkill() && skill.BurstGauge.Current == 5 && skill.Empowerment.IsActive,
                "nondamaging skill activation grants five points and ten-second empowerment");
            Check(!skill.TryAttack() && !skill.TryUltimate(), "other casts cannot interrupt active skill");
            Check(skill.TryDash() && skill.CurrentCast == EveCast.Dash && skill.DashRecovery.Current == 2,
                "dash interrupts skill and consumes one charge");
            Tick(.2f); manager.Advance(2);
            Check(skill.TryDash(), "second dash can begin after first completes"); Tick(.2f);
            Check(skill.DashRecovery.Current == 1 && skill.DashRecovery.CooldownRemaining == 3,
                "second dash preserves current recovery progress");
            Check(skill.TryDash(), "third dash spends last charge"); Tick(.2f);
            Check(!skill.TryDash(), "empty dash blocks another cast"); manager.Advance(3);
            Check(skill.DashRecovery.Current == 1 && skill.DashRecovery.CooldownRemaining == 5,
                "five seconds restores only one dash and starts next sequential period");
            Check(skill.TryAttack() && !skill.Empowerment.IsActive && skill.CurrentPhase == EveCastPhase.Rush,
                "Empowered Attack consumes state and begins with rush");
            int beforeEmpowered = EveSweepAction.Invocations; Tick(.17f);
            Check(skill.CurrentPhase == EveCastPhase.Slash && EveSweepAction.Invocations == beforeEmpowered + 1,
                "Empowered rush transitions to one fresh sweep"); Tick(.4f);
            Check(skill.ComboIndex == 1 && skill.TryAttack(), "combo advances to left stroke"); Tick(.4f);
            Check(skill.ComboIndex == 2 && skill.TryAttack(), "combo advances to wide horizontal stroke"); Tick(.4f);
            Check(skill.ComboIndex == 0, "third normal attack cycles combo back to right stroke");
            Tick(1.2f); Check(skill.TryAttack() && skill.ComboIndex == 1, "idle gap resets combo"); Tick(.4f);
            Check(skill.TryUltimate() && !skill.TryDash(), "ultimate begins unconditionally at ready and rejects dash interrupt");
            Check(!skill.TrySkill() && !skill.TryBurst(), "ultimate blocks other cast inputs");
            Tick(.2f); Check(skill.CurrentPhase == EveCastPhase.Charge, "ultimate retreats then charges");
            Tick(1f); Check(skill.CurrentPhase == EveCastPhase.Rush, "ultimate charges exactly one second before rush");
            Tick(1f); Check(!skill.IsBusy && skill.UltimateRecovery.CooldownRemaining == 25,
                "normal ultimate completes one slash while independent cooldown stays intact");
            skill.SkillRecovery.ResetToFull(); Check(skill.TrySkill(), "skill can activate while ultimate cooling down");
            Check(skill.UltimateRecovery.CooldownRemaining == 23, "skill directly subtracts two seconds from current ultimate timer"); Tick(.2f);
            skill.BurstGauge.Add(150); Check(skill.TryBurst() && skill.BurstGauge.Current == 50 && skill.SlashStacks.Current == 6,
                "Burst spends 100 points, keeps overflow and creates six stacks");
            Check(stat.Evaluate(PNContentIds.AttackPower) == 110 && stat.Evaluate(PNContentIds.AttackSpeed) == 150,
                "Burst adds 100 base attack and 50 attack-speed percentage points");
            Check(skill.BlazingAssault?.Id == EveBurstBuffContent.BlazingAssault && skill.RisingMomentum == null,
                "Eve Burst grants registered Blazing Assault without Momentum before repeat hit");
            var buffCollection = stat.GetComponent<PNBuffManager>().Buffs;
            var source = PNBuffSource.Capture(stat);
            var momentum = buffCollection.Apply(new PNBuffApplyRequest(skill.Moveset.RisingMomentumBuffId, source));
            Check(momentum.Status == PNBuffStatus.Applied && buffCollection.Get(momentum.Handle).Stacks == 1 && System.Math.Abs(stat.Evaluate(PNContentIds.AttackPower) - 111) < 1e-8 && stat.Evaluate(PNContentIds.AttackSpeed) == 160,
                "Selected Momentum factory owns group2 ten percent ATK and ten speed points");
            buffCollection.Apply(new PNBuffApplyRequest(skill.Moveset.RisingMomentumBuffId, source));
            Check(buffCollection.Get(momentum.Handle).Stacks == 2 && System.Math.Abs(stat.Evaluate(PNContentIds.AttackPower) - 112) < 1e-8 && stat.Evaluate(PNContentIds.AttackSpeed) == 170,
                "Momentum reapply stacks per-hit bonuses linearly");
            var equipment = EveId.Parse("test:momentum_equipment"); stat.SetSource(equipment, new[] { new EveStatModifier(PNContentIds.AttackPower, .2, 2), new EveStatModifier(PNContentIds.AttackSpeed, 7, 0) });
            buffCollection.Remove(momentum.Handle);
            Check(System.Math.Abs(stat.Evaluate(PNContentIds.AttackPower) - 112) < 1e-8 && stat.Evaluate(PNContentIds.AttackSpeed) == 157,
                "Removing Momentum preserves independent same-group equipment and Burst modifiers"); stat.RemoveSource(equipment);
            var blazingHandle = skill.BlazingAssault.Handle;
            skill.SkillRecovery.ResetToFull(); Check(skill.TrySkill(), "skill activates in Burst");
            Check(skill.SlashStacks.Current == 11 && skill.BurstGauge.Current == 50,
                "Burst skill adds five stacks without gauge accumulation"); Tick(.2f);
            Check(skill.TryRepeatAttack() && skill.SlashStacks.Current == 10 && skill.Empowerment.IsActive,
                "Repeat spends one stack and does not consume next normal Attack empowerment");
            manager.Advance(15);
            Check(!skill.IsInBurst && !skill.IsBusy && skill.SlashStacks.Current == 0 && skill.BurstGauge.Current == 80,
                "Burst expiry cancels active Repeat, clears stacks and refunds 30 once");
            Check(stat.Evaluate(PNContentIds.AttackPower) == 10 && stat.Evaluate(PNContentIds.AttackSpeed) == 100 && move.CanMove,
                "Burst expiry removes only its bonuses and restores movement");
            Check(!blazingHandle.IsAlive && skill.BlazingAssault == null && skill.RisingMomentum == null,
                "Burst expiry retires exact Eve-owned Buff handles");
            skill.BurstGauge.Add(70); Check(skill.TryBurst(), "second Burst can activate without separate cooldown");
            skill.UltimateRecovery.ResetToFull(); Check(skill.TryUltimate(), "Burst ultimate captures three-hit finisher");
            manager.Advance(15);
            Check(skill.IsInBurst && !skill.BurstState.IsActive && stat.Evaluate(PNContentIds.AttackPower) == 110,
                "expired Burst retains effects during its ultimate finisher");
            int spawned = EveSweepAction.Invocations; Tick(3f);
            Check(EveSweepAction.Invocations == spawned + 3 && !skill.IsBusy && !skill.IsInBurst,
                "Burst ultimate runs three distinct slash actions then ends Burst");
            Check(skill.BurstGauge.Current == 80 && skill.SlashStacks.Current == 0,
                "Burst ultimate does not accumulate gauge and grants end refund exactly once");
            loader.Unload(); loader.Load(EveRegistrySession.Current);
            Check(skill.BurstGauge.Current == 0 && skill.DashRecovery.Current == 3 && !skill.IsBusy,
                "reload initializes fresh Eve resources and cast state");
        }
        finally { skill.DestroyInput(); loader.Unload(); Time.deltaTime = .02f; EveRegistrySession.End(); }
    }
}
