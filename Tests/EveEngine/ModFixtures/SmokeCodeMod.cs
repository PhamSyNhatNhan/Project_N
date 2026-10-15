using System;
using System.IO;
using System.Reflection;
using EveEngine;
[assembly: AssemblyVersion("1.0.0.0")]

// EN: Separate compiled fixture proves DLL dependencies, override and lifecycle rollback.
// VI: Fixture biên dịch riêng chứng minh dependency DLL, override và rollback vòng đời.
public sealed class SmokeCodeMod : EveCodeMod
{
    private static string Mode => Environment.GetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE");
    private static void Trace(string message)
    {
        var path = Environment.GetEnvironmentVariable("PROJECT_N_MOD_TEST_TRACE");
        if (!string.IsNullOrEmpty(path)) File.AppendAllText(path, message + "\n");
    }
    public override void Register(EveModRegistrationContext context)
    {
        Trace("register");
        if (Mode == "register") throw new Exception("Fixture registration failure.");
        var id = new EveId(context.Namespace, "scale");
        context.Register(new PNStatCalculationDefinition(id, typeof(SmokeCalculation)));
        context.Override<PNStatCalculationDefinition>(EveId.Parse("base:scale"), id);
    }
    public override void OnSessionStarted(EveSessionContext context)
    {
        Trace("start:" + context.Registry.IsSealed + ":" + EveRegistrySession.IsReady);
        // Compile/run the current API4 surface from a separately built mod DLL.
        var flat = new EveStatModifier(PNContentIds.Heal,10d,0,PNContentIds.Mitigation,EveId.Parse("base:before"));
        if(flat.Clone().ContributionId != flat.ContributionId || context.Registry.Contributions().Get(flat.ContributionId).Id != PNContentIds.Before)
            throw new Exception("Flat contribution API/clone/registry unavailable to independently compiled mod.");
        foreach (var tag in new[] { PNContentIds.AttackTag, PNContentIds.SkillTag, PNContentIds.UltimateTag, PNContentIds.DashTag, PNContentIds.BurstTag })
            if (context.Registry.Tags().Get(tag).Id != tag || context.Registry.Stats.TryGet(tag, out _))
                throw new Exception("Category tag constants must select tags independently of stats.");
        var values = new PNSkillValues(new[] { new PNSkillValue(3d), new PNSkillValue(4d) });
        var parts = values.EvaluateEach(null);
        if (parts.Count != 2 || parts[0] != 3d || parts[1] != 4d) throw new Exception("Composite value API lost component identity.");
        var heal = new PNHeal(context.SessionId, new EveEntityId(880001), 5d, 4d);
        using (var operation = context.Registry.Operations().Get(EveId.Parse("base:heal")).Create())
            if (heal.Amount != 5d || heal.BaseAmount != 4d || !(operation is PNHealOperation))
                throw new Exception("Heal snapshot/registered operation API missing.");
        if (Mode == "start") throw new Exception("Fixture start failure.");
    }
    public override void OnSessionEnded(EveSessionContext context)
    {
        Trace("end:" + context.IsActive);
        if (Mode == "end") throw new Exception("Fixture end failure.");
    }
    public override void Dispose()
    {
        Trace("dispose");
        if (Mode == "dispose") throw new Exception("Fixture dispose failure.");
    }
}
public sealed class SmokeCalculation : PNStatCalculation
{
    public override double Calculate(PNStatCalculationContext context) => context.ApplyGroups(true) + SmokeDependency.Amount;
}
