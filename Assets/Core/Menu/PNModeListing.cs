using System;
using System.Collections.Generic;
using EveEngine;

public enum PNModeStart { Offline, Host, Join }

// EN: One way to start a mode, shown as a button on the setup screen. Join asks for a room next (room list), the others start at once.
// VI: Một cách bắt đầu chế độ, hiện thành nút ở màn thiết lập.
public sealed class PNModeStartOption
{
    public const int MaxLabelLength = 40;
    public string Id { get; }
    public string Label { get; }
    public PNModeStart Start { get; }
    public PNModeStartOption(string id, string label, PNModeStart start)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 32) throw new ArgumentException("Start option id must be 1..32 characters.");
        foreach (var c in id) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) throw new ArgumentException("Start option id must be lowercase ASCII letters, digits or _.");
        if (string.IsNullOrWhiteSpace(label) || label.Length > MaxLabelLength) throw new ArgumentException("Start option label must be 1.." + MaxLabelLength + " characters.");
        if (!Enum.IsDefined(typeof(PNModeStart), start)) throw new ArgumentOutOfRangeException(nameof(start));
        Id = id; Label = label; Start = start;
    }
}

// EN: How a mode appears in the mode menu (Docs/ModeMenuPlan.md): a gate card with a name, a description, an accent colour, optional art and
//     the ways to start it. A mode without a listing (the Hall) is never shown. Strings are plain bounded text (the UI is Vietnamese only).
// VI: Cách một chế độ xuất hiện trong menu chế độ; chế độ không có listing (Hall) không hiện.
public sealed class PNModeListing
{
    public const int MaxNameLength = 32, MaxDescriptionLength = 120, MaxStarts = 6;
    public string Name { get; }
    public string Description { get; }
    public int Order { get; }
    public string Art { get; }
    public IReadOnlyList<PNModeStartOption> Starts { get; }
    private readonly int[] accent;
    public int[] AccentRgb => (int[])accent.Clone();

    public PNModeListing(string name, string description, int[] accentRgb, string art, int order, IReadOnlyList<PNModeStartOption> starts)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength) throw new ArgumentException("Mode name must be 1.." + MaxNameLength + " characters.");
        description = description ?? "";
        if (description.Length > MaxDescriptionLength) throw new ArgumentException("Mode description is longer than " + MaxDescriptionLength + ".");
        accentRgb = accentRgb ?? new[] { 200, 200, 200 };
        if (accentRgb.Length != 3) throw new ArgumentException("Mode accent needs three numbers.");
        foreach (var v in accentRgb) if (v < 0 || v > 255) throw new ArgumentException("Mode accent values must be 0..255.");
        if (starts == null || starts.Count < 1 || starts.Count > MaxStarts) throw new ArgumentException("A mode needs 1.." + MaxStarts + " start options.");
        var seen = new HashSet<string>();
        foreach (var s in starts) if (s == null || !seen.Add(s.Id)) throw new ArgumentException("Start options must be unique and non-null.");
        Name = name; Description = description; accent = (int[])accentRgb.Clone(); Order = order;
        Art = string.IsNullOrEmpty(art) ? "" : EveResourcePath.Validate(art);
        var copy = new List<PNModeStartOption>(starts); Starts = copy.AsReadOnly();
    }

    // EN: Mobile builds list only the shipped content (mods are PC only).
    public static bool VisibleOn(EveId modeId, bool mobile) => !mobile || modeId.Namespace == "project_n" || modeId.Namespace == "base";
}
