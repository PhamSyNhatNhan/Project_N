using System;
using System.Text;
using Newtonsoft.Json.Linq;

// EN: Stella, the currency outside a run (Docs/StellaPlan.md). A run pays it out once, when it is summed up (won, lost or retired): a part for the floors passed, a part for the shards
//     earned in the run (not the shards left, so spending them in a shop never costs stella) and a small random roll that grows with the floors passed. The roll is a pure function of the
//     run id and the person id, so coming back to a saved run and summing it up again gives the same number: it cannot be re-rolled. Everything here is integer or fixed-point maths on
//     fixed algorithms, so every machine of a coop run computes the same amount for the same person.
// VI: Stella, tiền ngoài run. Trả một lần khi run kết thúc: theo số tầng đã qua, theo shard đã kiếm (không phải shard còn lại) và một số ngẫu nhiên nhỏ tăng theo số tầng.
//     Số ngẫu nhiên chỉ phụ thuộc mã run và mã người chơi nên tổng kết lại ra cùng số, không rút lại được.
public sealed class PNStellaRule
{
    public const int MaxPerFloor = 10000, MaxRandomPerFloor = 10000;
    public const double MaxPerShard = 100;
    public const int MaxPerRun = 1000000;
    public int PerFloor { get; }
    public double PerShard { get; }
    public int RandomPerFloor { get; }

    public PNStellaRule(int perFloor, double perShard, int randomPerFloor)
    {
        if (perFloor < 0 || perFloor > MaxPerFloor) throw new FormatException("stella perFloor must be an integer in [0, " + MaxPerFloor + "].");
        if (double.IsNaN(perShard) || double.IsInfinity(perShard) || perShard < 0 || perShard > MaxPerShard) throw new FormatException("stella perShard must be a number in [0, " + MaxPerShard + "].");
        if (randomPerFloor < 0 || randomPerFloor > MaxRandomPerFloor) throw new FormatException("stella randomPerFloor must be an integer in [0, " + MaxRandomPerFloor + "].");
        PerFloor = perFloor; PerShard = perShard; RandomPerFloor = randomPerFloor;
    }

    public static PNStellaRule Parse(JObject obj)
    {
        PNBuffContentBatch.Fields(obj, "perFloor", "perShard", "randomPerFloor");
        foreach (var required in new[] { "perFloor", "perShard", "randomPerFloor" }) if (obj[required] == null) throw new FormatException("stella " + required + " is required.");
        return new PNStellaRule(PNRogueliteParse.IntegerIn(obj, "perFloor", 0, MaxPerFloor), PNRogueliteParse.NumberIn(obj, "perShard", 0, MaxPerShard), PNRogueliteParse.IntegerIn(obj, "randomPerFloor", 0, MaxRandomPerFloor));
    }
}

public static class PNStella
{
    // The floors a run has been through to the end: a won run went through all of them; a lost or retired one through the floors before the one it stood on (Floor is 0-based).
    public static int FloorsPassed(bool victory, int floor, int floorCount)
    {
        if (floorCount < 0) throw new ArgumentOutOfRangeException(nameof(floorCount));
        if (victory) return floorCount;
        return Math.Max(0, Math.Min(floor, floorCount));
    }

    // The stella one person gets for a run. A null rule pays nothing. floorsPassed <= 0 pays nothing at all, the roll included.
    public static int Compute(PNStellaRule rule, int floorsPassed, int shardsEarned, Guid runId, string personId)
    {
        if (rule == null || floorsPassed <= 0) return 0;
        if (shardsEarned < 0) shardsEarned = 0;
        double part = (double)rule.PerFloor * floorsPassed + rule.PerShard * shardsEarned;
        long total = (long)Math.Floor(part);
        long span = (long)rule.RandomPerFloor * floorsPassed;
        if (span > 0) total += (long)(Hash(runId, personId) % (ulong)(span + 1));
        if (total < 0) total = 0;
        return (int)Math.Min(total, PNStellaRule.MaxPerRun);
    }

    // Stable on every platform (FNV-1a over the id bytes, then SplitMix64's finaliser): string.GetHashCode and System.Random are not.
    internal static ulong Hash(Guid runId, string personId)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in runId.ToByteArray()) h = (h ^ b) * 1099511628211UL;
            h = (h ^ 0xFF) * 1099511628211UL;
            foreach (byte b in Encoding.UTF8.GetBytes(personId ?? "")) h = (h ^ b) * 1099511628211UL;
            h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
            h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
            return h ^ (h >> 31);
        }
    }
}
