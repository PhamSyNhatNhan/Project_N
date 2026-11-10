#if UNITY_5_3_OR_NEWER
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EveEngine;
using UnityEngine;

// EN: The one saved run of this machine (Docs/RunSavePlan.md 5.4-5.5): where the file lives, writing it when a room is entered, deleting it when a run ends, and the check of whether a saved
//     run can still be played with the content that is loaded now. Menus and the run director ask this; none of them touches the file. `-pn-run-save-dir=<folder>` points a Player at
//     another folder (a review never touches the real save); tests call Use.
// VI: Bản lưu run duy nhất của máy: nơi đặt tệp, ghi khi vào phòng, xoá khi run kết thúc, và kiểm tra bản lưu còn chơi được với nội dung đang nạp không.
public enum PNRunSaveProblem { None, Version, Content, Mode, Definition, Position, Character, Person }

public static class PNRunSaveService
{
    public const string DirectoryFlag = "-pn-run-save-dir=";
    private static PNRunSaveStore store;

    public static void Use(PNRunSaveStore from) { store = from; }
    public static void ResetStatics() { store = null; }

    private static PNRunSaveStore Store
    {
        get
        {
            if (store != null) return store;
            string folder = null;
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(DirectoryFlag, StringComparison.Ordinal)) folder = argument.Substring(DirectoryFlag.Length);
            return store = new PNRunSaveStore(string.IsNullOrWhiteSpace(folder) ? Path.Combine(Application.persistentDataPath, "RunSave") : folder);
        }
    }

    // What is on disk now (read each time: the file is small and the answer must be fresh).
    public static PNRunSaveLoad Peek() => Store.Load();
    public static bool SaveRefused => Store.SaveRefused;

    public static void Write(PNRunState state, string mode)
        => Store.Save(PNRunSave.Capture(state, EveGameRuntime.CurrentRulesVersion, ContentHash(), mode, DateTime.UtcNow));

    public static void Delete()
    {
        try { Store.Delete(); }
        catch (Exception e) when (e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException) { Debug.LogWarning("Run save not deleted: " + e.Message); }
    }

    // The seat of a person in a save (null when that person has none).
    public static PNRunSaveSeat SeatOf(PNRunSave save, string playerId)
    {
        if (save == null) throw new ArgumentNullException(nameof(save));
        foreach (var seat in save.Seats) if (seat.PlayerId == playerId) return seat;
        return null;
    }

    // One short text for the content this machine has loaded: equal on two machines that can play each other's runs.
    public static string ContentHash()
    {
        var compatibility = PNGameBootstrap.Runtime?.Content?.Compatibility ?? throw new InvalidOperationException("Content is not loaded.");
        var text = new StringBuilder(compatibility.RulesVersion).Append('\n');
        foreach (var pair in compatibility.ContentHashes.OrderBy(x => x.Key, StringComparer.Ordinal)) text.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())).Select(b => b.ToString("x2")));
    }

    // EN: Can this saved run be played with the content loaded now, by the person at this machine? None, or the first thing that is wrong. This machine (the host, or a player alone) must hold a seat of
    //     the save; the other seats are taken by the people who join, and a seat nobody takes is left out of the run.
    public static PNRunSaveProblem Check(PNRunSave save, EveRegistryHub registry)
    {
        if (save == null) throw new ArgumentNullException(nameof(save));
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (save.RulesVersion != EveGameRuntime.CurrentRulesVersion) return PNRunSaveProblem.Version;
        if (save.Content != ContentHash()) return PNRunSaveProblem.Content;
        if (save.Mode != "roguelite") return PNRunSaveProblem.Mode;
        if (!registry.RogueliteRuns().TryGet(save.Definition, out var run)) return PNRunSaveProblem.Definition;
        try
        {
            var planner = new PNRunPlanner(run, id => registry.RogueliteFloors().TryGet(id, out var floor) ? floor : null);
            if (save.Floor >= run.Floors.Count) return PNRunSaveProblem.Position;
            if (planner.FloorAt(save.Floor).Rooms.All(x => x.Room != save.Room)) return PNRunSaveProblem.Position;
        }
        catch (Exception e) when (e is InvalidOperationException || e is ArgumentOutOfRangeException) { return PNRunSaveProblem.Position; }
        if (!registry.RogueliteRooms().TryGet(save.Room, out _) || save.Options.Any(x => !registry.RogueliteRooms().TryGet(x, out _))) return PNRunSaveProblem.Position;
        // The items the team holds: a run without Roguelite Buff holds none; a run with it holds a set that is valid for the content loaded now.
        if (run.Rogue == null ? save.Loadout.Count > 0 : PNRogueSet.Problem(PNRogueCatalog.Build(registry), save.Loadout) != null) return PNRunSaveProblem.Position;
        foreach (var seat in save.Seats)
        {
            if (!registry.Characters.TryGet(seat.Character, out var character)) return PNRunSaveProblem.Character;
            PNPlayableSettings playable;
            try { playable = PNPlayableSettings.Parse(character.Data); } catch (FormatException) { return PNRunSaveProblem.Character; }
            if (playable == null || !PNPlayerProfileService.CanPlay(seat.Character.ToString(), playable)) return PNRunSaveProblem.Character;
        }
        if (SeatOf(save, PNPlayerProfileService.PlayerId) == null) return PNRunSaveProblem.Person;
        return PNRunSaveProblem.None;
    }
}
#endif
