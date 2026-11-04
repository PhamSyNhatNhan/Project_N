using System;

// EN: The random numbers of one run (Docs/RogueliteFramePlan.md section 6): one seed, three streams. A draw from one stream never moves another, so adding a draw to the
//     rewards later does not change which rooms a seed offers. The algorithm is fixed here (SplitMix64) and uses integer arithmetic only, so the same seed gives the same
//     numbers on every platform and Unity version; UnityEngine.Random is never used.
// VI: Số ngẫu nhiên của một run: một seed, ba luồng tách riêng. Thuật toán cố định (SplitMix64), chỉ dùng số nguyên nên mọi nền tảng cho cùng kết quả.
public enum PNRunStream { Layout = 0, Spawn = 1, Reward = 2 }

public sealed class PNRunRandom
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;
    private readonly ulong[] states = new ulong[3];
    public ulong Seed { get; }

    public PNRunRandom(ulong seed)
    {
        Seed = seed;
        for (int i = 0; i < states.Length; i++) states[i] = Mix(unchecked(seed + (ulong)(i + 1) * Gamma));
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    public ulong NextUInt64(PNRunStream stream)
    {
        int index = (int)stream;
        if (index < 0 || index >= states.Length) throw new ArgumentOutOfRangeException(nameof(stream));
        unchecked { states[index] += Gamma; }
        return Mix(states[index]);
    }

    // [0, 1) with 53 random bits.
    public double NextDouble(PNRunStream stream) => (NextUInt64(stream) >> 11) * (1.0 / 9007199254740992.0);

    // [0, exclusiveMax), without modulo bias.
    public int NextInt(PNRunStream stream, int exclusiveMax)
    {
        if (exclusiveMax < 1) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        ulong bound = (ulong)exclusiveMax, limit = ulong.MaxValue - ulong.MaxValue % bound;
        ulong value;
        do value = NextUInt64(stream); while (value >= limit);
        return (int)(value % bound);
    }
}
