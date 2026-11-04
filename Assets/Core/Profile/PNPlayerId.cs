using System;

// EN: The id that names one person across sessions (Docs/RunSavePlan.md 5.1): 32 lowercase hex characters (a GUID without dashes), made once and kept in the profile.
//     It only tells people apart (a saved run's seat, a returning client); it proves nothing, coop here trusts its players. The text form is opaque so a platform id can replace it later.
// VI: Mã nêu một người xuyên các phiên: 32 ký tự hex thường (GUID bỏ gạch), tạo một lần và giữ trong profile. Chỉ để phân biệt người, không xác thực.
public static class PNPlayerId
{
    public const int Length = 32;

    public static string New() => Guid.NewGuid().ToString("N");

    public static bool IsValid(string id)
    {
        if (id == null || id.Length != Length) return false;
        foreach (var c in id) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
        return true;
    }

    public static string Require(string id, string what = "player id")
    {
        if (!IsValid(id)) throw new FormatException(what + " must be " + Length + " lowercase hex characters.");
        return id;
    }
}
