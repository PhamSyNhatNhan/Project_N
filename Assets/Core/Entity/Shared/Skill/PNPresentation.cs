using System;
using System.Collections.Generic;

// EN: G8 (Docs/SkillNetworkFrameworkPlan.md 4.6). The look of an entity that is NOT an Action but that other players must see: a weapon in its Burst form, a charge held, a stance. Logic names a
//     value (`Presentation.Set("burst", "1")`); the authority keeps the last value of every key and the framework sends the whole picture to every client, so a player who joins late sees it too.
//     A client never writes it (a `Set` there is ignored: the value is the authority's), it only reads it (`Get`, `Changed`). An observer has no Ability (that block goes to the owner only), so
//     this is the one channel for this kind of state. Display only: not a trust boundary.
// VI: Hình dạng của một thực thể mà không phải Action nhưng người khác phải thấy (vũ khí ở dạng Burst, đang tụ lực). Logic đặt giá trị (`Presentation.Set`); authority giữ giá trị cuối của từng khóa và
//     bộ khung gửi cả bức tranh cho mọi client, nên người vào muộn cũng thấy. Client không ghi (Set bị bỏ qua), chỉ đọc.
public sealed class PNPresentation
{
    public const int MaxEntries = 8, MaxKeyLength = 24, MaxValueLength = 24;
    private readonly SortedDictionary<string, string> values = new SortedDictionary<string, string>(StringComparer.Ordinal);
    private readonly Func<bool> canWrite;
    // (key, value) after a change; an empty value means the key was cleared. Raised on every machine: where the value was set, and where the picture was applied.
    public event Action<string, string> Changed;
    // Raised only where `Set` changed something (the authority): the sync component sends the new picture.
    public event Action Dirty;
    public PNPresentation(Func<bool> canWrite) { this.canWrite = canWrite ?? throw new ArgumentNullException(nameof(canWrite)); }
    public int Count => values.Count;
    public IReadOnlyDictionary<string, string> Values => values;
    public string Get(string key) => key != null && values.TryGetValue(key, out var value) ? value : "";

    public static bool IsValidKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength) return false;
        foreach (var c in key) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')) return false;
        return true;
    }
    public static bool IsValidValue(string value) => value != null && value.Length <= MaxValueLength;

    // The authority sets a value ("" clears it). Returns whether anything changed; a machine that may not write ignores the call.
    public bool Set(string key, string value)
    {
        if (!IsValidKey(key)) throw new ArgumentException("A presentation key is lowercase letters, digits and underscores (at most " + MaxKeyLength + ").", nameof(key));
        if (!IsValidValue(value)) throw new ArgumentException("A presentation value is text of at most " + MaxValueLength + " characters.", nameof(value));
        if (!canWrite()) return false;
        if (!Write(key, value, true)) return false;
        Dirty?.Invoke(); return true;
    }

    // A client takes the authority's whole picture: keys that vanished are cleared, the others are set; `Changed` is raised for each difference. Returns false (nothing applied) for a malformed picture.
    public bool Apply(IReadOnlyDictionary<string, string> picture)
    {
        if (picture == null || picture.Count > MaxEntries) return false;
        foreach (var pair in picture) if (!IsValidKey(pair.Key) || !IsValidValue(pair.Value) || pair.Value.Length == 0) return false;
        foreach (var gone in new List<string>(values.Keys)) if (!picture.ContainsKey(gone)) Write(gone, "", false);
        foreach (var pair in picture) Write(pair.Key, pair.Value, false);
        return true;
    }

    // Everything is forgotten without raising `Changed` (the entity is unloading).
    public void Reset() { values.Clear(); }

    private bool Write(string key, string value, bool enforceLimit)
    {
        if (value.Length == 0)
        {
            if (!values.Remove(key)) return false;
        }
        else
        {
            if (values.TryGetValue(key, out var old) && old == value) return false;
            if (enforceLimit && !values.ContainsKey(key) && values.Count >= MaxEntries) throw new InvalidOperationException("An entity holds at most " + MaxEntries + " presentation values.");
            values[key] = value;
        }
        Changed?.Invoke(key, value); return true;
    }
}

public partial class PNSkill
{
    private PNPresentation presentation;
    // The entity's look for other players (G8). Only the authority's writes count; every machine can read and listen.
    public PNPresentation Presentation => presentation ?? (presentation = new PNPresentation(() => HasGameplayAuthority));
}
