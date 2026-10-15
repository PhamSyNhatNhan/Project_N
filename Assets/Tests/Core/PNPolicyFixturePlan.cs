using System;
using System.Collections.Generic;

// Pure fixture algorithms, also compiled by the standalone smoke runner.
public static class PNPolicyFixturePlan
{
    public sealed class NaturalActor
    {
        public double[] ApplyAt;
        public double StackPhase, ReapplyPhase;
    }
    public static NaturalActor[] Natural(int seed, int actors, int players, int playerBuffs, int enemyBuffs)
    {
        var rng = new Random(seed); var result = new NaturalActor[actors];
        for (int i = 0; i < actors; i++)
        {
            int count = i < players ? playerBuffs : enemyBuffs;
            double spawn = i < players ? 0 : rng.NextDouble() * 3;
            var entry = new NaturalActor { ApplyAt = new double[count] };
            for (int j = 0; j < count; j++) entry.ApplyAt[j] = spawn + rng.NextDouble() * .5;
            entry.StackPhase = rng.NextDouble() * .5; entry.ReapplyPhase = rng.NextDouble(); result[i] = entry;
        }
        return result;
    }
    public static int[] SelectAoe(bool[] isLiveDot, int depth)
    {
        if (isLiveDot == null || depth < 1) throw new ArgumentException("Live DOT slots and positive depth required.");
        var chosen = new List<int>(depth);
        for (int i = 0; i < isLiveDot.Length && chosen.Count < depth; i++)
            if (i != 1 && i != isLiveDot.Length - 1 && isLiveDot[i]) chosen.Add(i);
        if (chosen.Count != depth) throw new ArgumentException("AoE depth exceeds live DOT slots outside stack/reapply churn; workload must not be substituted.");
        return chosen.ToArray();
    }
}
