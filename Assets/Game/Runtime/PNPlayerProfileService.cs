using System;
using System.IO;
using UnityEngine;

// EN: The machine's player profile (Docs/ModeMenuPlan.md section 7): one file `<persistentDataPath>/Profile/profile.json`, loaded on first use.
//     Forbidden characters stay out of the menu and are refused by the host; `--pn-allow-forbidden` re-enables them only in the Editor and
//     Development builds. Menu and lobby code ask `CanPlay`; they never read the file themselves.
// VI: Hồ sơ người chơi của máy, nạp lười; cờ --pn-allow-forbidden chỉ có tác dụng ở Editor và Development build.
public static class PNPlayerProfileService
{
    public const string AllowForbiddenFlag = PNForbiddenSwitch.Flag;
    private static PNProfileStore store;
    private static PNPlayerProfile current;
    public static PNProfileLoadStatus LoadStatus { get; private set; } = PNProfileLoadStatus.Missing;
    public static bool AllowForbidden => PNForbiddenSwitch.Allowed;

    public static PNPlayerProfile Current { get { EnsureLoaded(); return current; } }

    private static void EnsureLoaded()
    {
        if (current != null) return;
        Load(new PNProfileStore(Path.Combine(Application.persistentDataPath, "Profile")));
    }

    // Tests and tools point the service at another folder through this.
    public static void Load(PNProfileStore from)
    {
        store = from ?? throw new ArgumentNullException(nameof(from));
        var result = store.Load();
        current = result.Profile; LoadStatus = result.Status;
        if (result.Status == PNProfileLoadStatus.CorruptReset || result.Status == PNProfileLoadStatus.NewerVersion)
            Debug.LogWarning("Player profile: " + result.Status + " (" + store.FilePath + ")");
    }

    public static void ResetStatics() { store = null; current = null; LoadStatus = PNProfileLoadStatus.Missing; }

    public static PNCharacterState State(string id, PNPlayableSettings playable) => Current.Effective(id, playable, AllowForbidden);
    public static bool CanPlay(string id, PNPlayableSettings playable) => Current.CanPlay(id, playable, AllowForbidden);

    public static void SetLastCharacter(string id) { Current.SetLastCharacter(id); Save(); }
    public static string LastRun => Current.LastRun;
    public static void SetLastRun(string id) { Current.SetLastRun(id); Save(); }

    // EN: This person's id (Docs/RunSavePlan.md 5.1). Made and saved the first time it is asked for. `-pn-player-id=<32 hex>` overrides it for this process only and is never saved, so two
    //     processes on one machine (a host and a client in a test) can be told apart.
    // VI: Mã của người chơi này: tạo và lưu ở lần hỏi đầu tiên. `-pn-player-id=<32 hex>` chỉ ghi đè cho tiến trình này và không lưu.
    public const string PlayerIdFlag = "-pn-player-id=";
    public static string PlayerId
    {
        get
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (!argument.StartsWith(PlayerIdFlag, StringComparison.Ordinal)) continue;
                string forced = argument.Substring(PlayerIdFlag.Length);
                if (PNPlayerId.IsValid(forced)) return forced;
                Debug.LogWarning(PlayerIdFlag + " needs " + PNPlayerId.Length + " lowercase hex characters; ignored.");
            }
            if (Current.EnsurePlayerId()) Save();
            return Current.PlayerId;
        }
    }
    public static void SetState(string id, PNCharacterState state) { Current.SetState(id, state); Save(); }

    // The stella balance of this person (Docs/StellaPlan.md). A run is paid at most once: false = it already was.
    public static int Stella => Current.Stella;
    public static bool CreditStella(string runId, int amount) { bool paid = Current.CreditStella(runId, amount); if (paid) Save(); return paid; }

    private static void Save()
    {
        try { store.Save(current); }
        catch (Exception e) when (e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException)
        { Debug.LogWarning("Player profile not saved: " + e.Message); }
    }
}
