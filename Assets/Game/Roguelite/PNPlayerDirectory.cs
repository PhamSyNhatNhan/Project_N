#if UNITY_5_3_OR_NEWER
using System;

// EN: Who is who in this play (Docs/RunSavePlan.md 5.6): the owner a session gave a person, and the person's own id (PNPlayerId) that outlives the session. A saved run is keyed by the
//     person; the owner changes every session. The room driver of a hosted room implements this for everyone in the room; alone (offline) there is only the local person, owner 0.
// VI: Ai là ai trong phiên này: owner phiên cấp cho một người và mã của chính người đó (sống lâu hơn phiên). Bản lưu khoá theo người; owner đổi mỗi phiên.
public interface IPNPlayerDirectory
{
    // The person behind this owner, or null when not known.
    string PlayerIdOf(long owner);
    // The owner this session gave that person, or null when the person is not here.
    long? OwnerOf(string playerId);
}

public sealed class PNLocalPlayerDirectory : IPNPlayerDirectory
{
    public string PlayerIdOf(long owner) => owner == 0 ? PNPlayerProfileService.PlayerId : null;
    public long? OwnerOf(string playerId) => playerId != null && playerId == PNPlayerProfileService.PlayerId ? 0L : (long?)null;
}

public static class PNPlayerDirectories
{
    private static readonly IPNPlayerDirectory local = new PNLocalPlayerDirectory();
    // The directory of the room when one is open (host or client), else the local person alone.
    public static IPNPlayerDirectory Current => PNGameBootstrap.IsRoomOpen && PNGameBootstrap.LobbyDriver is IPNPlayerDirectory room ? room : local;
}
#endif
