using System;
using EveEngine;

// EN: G6c. A cast's key and its random numbers.
//     The key is shared by the owner's guess and the authority's real cast: the input that began the cast (the wire layer's key, `PNSkill.PendingCastKey`) shifted left by `PNSkillCastKey.InputShift`, and the casts
//     that begin with no input of their own (the next stroke of a held Repeat, a chained cast) take the previous key + 1. Offline there is no wire key, so the same rule runs on a local counter.
//     `cast.Random` is seeded by that key (and the skill's entity), so the guess and the real cast roll the same numbers; a skill author just calls `cast.Random.Chance(0.3)` and never thinks about it.
//     Hit reports (`PNSkillActionContext.Hit`) and decisions (`Choose`) travel under the key, so the authority can match them to the cast they belong to.
// VI: G6c. Khóa của một cast và số ngẫu nhiên của nó. Khóa chung giữa lần đoán của chủ và cast thật của authority: khóa input bắt đầu cast dịch trái `InputShift`; cast không có input riêng (nhát Repeat kế,
//     cast nối) lấy khóa trước + 1. `cast.Random` gieo theo khóa đó (và entity của skill) nên lần đoán và cast thật ra cùng số.
public static class PNSkillCastKey
{
    public const int InputShift = 8;
    public const long MaxStep = 255;
    public static long FromInput(long inputKey) => inputKey << InputShift;
    public static long InputOf(long castKey) => castKey >> InputShift;
}

// A small deterministic generator (SplitMix64): the same seed gives the same numbers on every machine and every platform.
public sealed class PNSkillRandom
{
    private ulong state;
    public PNSkillRandom(ulong seed) { state = seed; }
    public static ulong Seed(long castKey, long entityId) => unchecked((ulong)castKey * 0x9E3779B97F4A7C15UL ^ (ulong)entityId * 0xC2B2AE3D27D4EB4FUL);
    public ulong NextUInt64()
    {
        unchecked
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
    // [0, 1)
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
    public float NextFloat() => (float)NextDouble();
    // [min, max)
    public int Range(int min, int max)
    {
        if (max <= min) throw new ArgumentOutOfRangeException(nameof(max));
        return (int)(min + (long)(NextDouble() * ((long)max - min)));
    }
    public float Range(float min, float max) => min + NextFloat() * (max - min);
    public bool Chance(double probability) => probability >= 1d || probability > 0d && NextDouble() < probability;
}

public sealed partial class PNSkillInvocation
{
    private PNSkillRandom random; private int steps;
    // 0 until the skill gives the cast a key (BeginActivation); a cast that has no key reports nothing to the authority.
    public long CastKey { get; internal set; }
    // The cast's random numbers: the owner's guess and the authority's cast draw the same sequence.
    public PNSkillRandom Random
    {
        get
        {
            if (random == null) { var stat = State?.Skill == null ? null : State.Skill.GetComponent<PNStat>(); random = new PNSkillRandom(PNSkillRandom.Seed(CastKey, stat == null ? 0 : stat.EntityId.Value)); }
            return random;
        }
    }
    // The next ordinal of an action context of this cast that reports a hit (the authority tells reports apart by it).
    internal int NextStep() => (int)Math.Min(PNSkillCastKey.MaxStep, steps++);
}
