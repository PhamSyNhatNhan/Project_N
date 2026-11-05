#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;

// EN: A run summed up from a save, shown by the end screen when there is no run in the session (Docs/RunSavePlan.md Q12/Q13): "Sum up now" turns the saved run into a snapshot with the
//     result `retired`, the end screen shows it instead of the session's snapshot, and the button clears it. Nothing here is a run: it is the numbers of a saved one.
// VI: Run đã lưu được tổng kết: màn kết thúc hiện snapshot này (kết quả `retired`) khi phiên không có run; nút bấm xoá nó.
public static class PNRunSummary
{
    public static PNRunSnapshot Pending { get; private set; }

    public static void ShowRetired(PNRunSave save)
    {
        if (save == null) throw new ArgumentNullException(nameof(save));
        Pending = new PNRunSnapshot
        {
            Phase = "ended", Result = "retired", RunId = save.RunId.ToString("N"), Definition = save.Definition.ToString(), Shards = save.Shards, Floor = save.Floor, Step = save.Step,
            Seconds = (int)save.Seconds, Kills = save.Kills, Rooms = save.Cleared.Values.Sum(), Earned = save.ShardsEarned,
            Players = save.Seats.Select((x, i) => (long)i).ToArray()
        };
    }

    public static void Clear() { Pending = null; }
}
#endif
