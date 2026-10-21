using System.Collections.Generic;

// Neutral lobby/LAN contract. Game never references Mirror; Composition supplies the implementation
// through the same driver object that implements IPNRoomSessionDriver.
public struct PNLobbyPeerInfo
{
    public long Owner;
    public bool Host, Ready, Local;
    // Character the peer picked in the character menu (a registered playable character id), empty until it picks one.
    public string Character;
    public string Label => Host ? "Chủ phòng" : "Người chơi " + Owner;
}
public sealed class PNLobbyInfo
{
    public IReadOnlyList<PNLobbyPeerInfo> Peers { get; }
    public bool Locked { get; }
    public bool Started { get; }
    public int Capacity { get; }
    public PNLobbyInfo(IReadOnlyList<PNLobbyPeerInfo> peers, bool locked, bool started, int capacity)
    { Peers = peers ?? new List<PNLobbyPeerInfo>(); Locked = locked; Started = started; Capacity = capacity; }
    // Every non-host peer is ready. The host starts the run, so the host flag itself is not a condition.
    public bool AllReady { get { if (Peers.Count == 0) return false; foreach (var peer in Peers) if (!peer.Host && !peer.Ready) return false; return true; } }
    public bool LocalReady { get { foreach (var peer in Peers) if (peer.Local) return peer.Host || peer.Ready; return false; } }
}
public struct PNLanRoomInfo
{
    public string Name, Address, Mode, Platform;
    public int Port, Players, Capacity;
    public bool Locked;
    // Non-empty when this client cannot join (platform, content, version, full, locked).
    public string Unavailable;
}
public interface IPNLobbyDriver
{
    // No room object, transport or pending teardown remains; a new room may be opened.
    bool IsIdle { get; }
    string LastFailure { get; }
    // Null when no room is open.
    PNLobbyInfo Lobby { get; }
    void SetReady(bool ready);
    // Tells the host which playable character the local player picked (the host validates it). Safe to call before a room exists (ignored).
    void SetCharacter(string characterId);
    // Host only. A locked room refuses new admissions and stops advertising.
    void LockAdmissions(bool locked);
    // Host only. Tells every peer the run has started.
    void MarkRunStarted();
    void StartDiscovery();
    void StopDiscovery();
    IReadOnlyList<PNLanRoomInfo> Rooms { get; }
    IReadOnlyList<string> LocalAddresses { get; }
    int Port { get; }
}
