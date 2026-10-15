using System;
using System.Linq;
using static EveSessionSmokeTests;
public static class PNPolicyFixturePlanTests
{
    public static void Run()
    {
        var one = PNPolicyFixturePlan.Natural(20261012, 64, 4, 30, 10);
        var two = PNPolicyFixturePlan.Natural(20261012, 64, 4, 30, 10);
        Check(one.Zip(two, (a,b) => a.ApplyAt.SequenceEqual(b.ApplyAt) && a.StackPhase == b.StackPhase && a.ReapplyPhase == b.ReapplyPhase).All(x=>x), "Fixture schedule identical with same seed, independent of callback/frame order.");
        Check(!one[4].ApplyAt.SequenceEqual(PNPolicyFixturePlan.Natural(17,64,4,30,10)[4].ApplyAt), "Fixture schedule responds to seed.");
        var mixed = Enumerable.Range(0,10).Select(i=>i%3==0).ToArray();
        Check(PNPolicyFixturePlan.SelectAoe(mixed,2).SequenceEqual(new[]{0,3}), "AoE selects actual DOT slots, outside both churn handles.");
        Check(PNPolicyFixturePlan.SelectAoe(mixed,3).SequenceEqual(new[]{0,3,6}), "AoE maximal safe wave64 depth is three.");
        Reject(()=>PNPolicyFixturePlan.SelectAoe(mixed,4), "AoE rejects invalid depth instead of silently lowering workload.");
        Check(PNPolicyFixturePlan.SelectAoe(new[]{true,true,true,true,true},3).SequenceEqual(new[]{0,2,3}), "Normal20 AoE excludes stack slot1 and last reapply slot.");
        Reject(()=>PNPolicyFixturePlan.SelectAoe(new[]{true,true,true,true,true},4), "Normal20 rejects depth4.");
    }
}
