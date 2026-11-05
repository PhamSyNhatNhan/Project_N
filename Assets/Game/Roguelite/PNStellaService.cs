#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// EN: Pays stella out on this machine (Docs/StellaPlan.md 3.3-3.4). The end screen asks `Settle` with the run it shows (the session's snapshot, or a saved run summed up from the continue screen);
//     the first call for a run computes this person's amount and adds it to the profile, any later call for the same run only gives the same number back, so the screen can redraw (a language
//     change, a new sync revision) without paying twice. The profile remembers the last run paid, so even a restart cannot pay a run twice. Each machine pays its own person, from the run
//     numbers every machine already has.
// VI: Cộng stella ở máy này: màn kết thúc gọi Settle với run đang hiện; lần đầu tính và cộng vào hồ sơ, các lần sau chỉ trả lại cùng số. Hồ sơ nhớ run đã trả gần nhất nên khởi động lại cũng không trả hai lần.
public static class PNStellaService
{
    private static string lastRun = "";
    private static int lastAmount;

    public static int Balance => PNPlayerProfileService.Stella;

    public static void ResetStatics() { lastRun = ""; lastAmount = 0; }

    // The stella this person gets for the run in `snapshot` (0 while the run is not over or its data is not usable). Pays it at most once per run.
    public static int Settle(PNRunSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.Ended) return 0;
        if (snapshot.Result != "victory" && snapshot.Result != "defeat" && snapshot.Result != "retired") return 0;
        if (string.IsNullOrEmpty(snapshot.RunId) || !Guid.TryParseExact(snapshot.RunId, "N", out var run)) return 0;
        if (snapshot.RunId == lastRun) return lastAmount;
        if (!EveRegistrySession.IsReady || !EveId.TryParse(snapshot.Definition ?? "", out var id) || !EveRegistrySession.Current.RogueliteRuns().TryGet(id, out var definition)) return 0;
        int floors = PNStella.FloorsPassed(snapshot.Result == "victory", snapshot.Floor, definition.Floors.Count);
        int amount = PNStella.Compute(definition.Stella, floors, snapshot.Earned, run, PNPlayerProfileService.PlayerId);
        PNPlayerProfileService.CreditStella(snapshot.RunId, amount);
        lastRun = snapshot.RunId; lastAmount = amount;
        return amount;
    }
}
#endif
