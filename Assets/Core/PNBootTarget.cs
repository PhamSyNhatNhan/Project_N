using System;

// EN: Where the boot (Title scene -> Loading #1) leads once the content is loaded (Docs/LoadingHandoffPlan.md P6): the Hall (the default), a scene
//     by path (a scene that was opened directly in the Editor or a test, started again through the one load path) or a mode id (started like the
//     menu's "Tiếp tục", with the role and address of --pn-role / --pn-address). Parsed from `--pn-auto-start=<value>`; pure, no Unity types.
// VI: Nơi lần nạp đầu dẫn tới sau khi nội dung đã nạp: Hall (mặc định), một scene theo đường dẫn hoặc một mode id. Đọc từ --pn-auto-start=<giá trị>.
public enum PNBootTargetKind { Hall, Scene, Mode }

public sealed class PNBootTarget
{
    public const string Flag = "--pn-auto-start=";
    public const int MaxLength = 256;
    public PNBootTargetKind Kind { get; }
    // The scene path for Scene, the mode id for Mode, "hall" for Hall.
    public string Value { get; }
    private PNBootTarget(PNBootTargetKind kind, string value) { Kind = kind; Value = value; }

    public static readonly PNBootTarget Hall = new PNBootTarget(PNBootTargetKind.Hall, "hall");
    public static PNBootTarget Scene(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxLength || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".unity", StringComparison.Ordinal) || path.Contains("..") || path.Contains("\\"))
            throw new FormatException("A scene target must be an Assets/... .unity path without '..'.");
        return new PNBootTarget(PNBootTargetKind.Scene, path);
    }
    public static PNBootTarget Mode(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 128) throw new FormatException("A mode target must be 1..128 characters.");
        foreach (var c in id) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == ':' || c == '-' || c == '.')) throw new FormatException("A mode id allows letters, digits and _ : - . only.");
        return new PNBootTarget(PNBootTargetKind.Mode, id);
    }

    // "" / "hall" -> Hall; a value with '/' or ending in ".unity" -> Scene; anything else -> Mode. Throws FormatException for a malformed value.
    public static PNBootTarget Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "hall", StringComparison.OrdinalIgnoreCase)) return Hall;
        value = value.Trim();
        if (value.Length > MaxLength) throw new FormatException("The auto-start target is too long.");
        return value.Contains("/") || value.EndsWith(".unity", StringComparison.Ordinal) ? Scene(value) : Mode(value);
    }

    // The target from the command line, or null when the flag is absent. A malformed value throws (a typo must not silently open the Hall).
    public static PNBootTarget FromArguments(string[] arguments)
    {
        foreach (var argument in arguments ?? Array.Empty<string>())
            if (argument != null && argument.StartsWith(Flag, StringComparison.Ordinal)) return Parse(argument.Substring(Flag.Length));
        return null;
    }
    public override string ToString() => Kind + ":" + Value;
}
