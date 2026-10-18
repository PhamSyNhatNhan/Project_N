using System;
using EveEngine;

// Neutral startup contract. Network implementation is supplied by Composition, not Game.
public sealed class PNPlayConfig
{
    public EveSessionRole Role { get; }
    public string ModeId { get; }
    public string Address { get; }
    public PNNetworkProfile Network { get; }
    public EvePreparationProfile Preparation { get; }
    public PNPlayConfig(EveSessionRole role = EveSessionRole.Offline, string modeId = "roguelite", string address = "localhost", PNNetworkProfile network = null, EvePreparationProfile preparation = null)
    {
        if (!Enum.IsDefined(typeof(EveSessionRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (string.IsNullOrWhiteSpace(modeId)) throw new ArgumentException("Mode required.", nameof(modeId));
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Address required.", nameof(address));
        Role = role; ModeId = modeId; Address = address;
        Network=network??new PNNetworkProfile();Preparation=preparation??new EvePreparationProfile();
        Network.Validate();Preparation.Validate();
    }
    public static PNPlayConfig FromArguments(string[] arguments)
    {
        var role = EveSessionRole.Offline; string mode = "roguelite", address = "localhost";
        var network=new PNNetworkProfile();
        foreach (string value in arguments ?? Array.Empty<string>())
        {
            if (value.StartsWith("--pn-role=", StringComparison.Ordinal))
            {
                if (!Enum.TryParse(value.Substring(10), true, out role) || !Enum.IsDefined(typeof(EveSessionRole), role))
                    throw new ArgumentException("Invalid --pn-role.");
            }
            else if (value.StartsWith("--pn-mode=", StringComparison.Ordinal)) mode = value.Substring(10);
            else if (value.StartsWith("--pn-address=", StringComparison.Ordinal)) address = value.Substring(13);
            else if(value=="--pn-no-character")network.SelectPlayer=false;
        }
        return new PNPlayConfig(role, mode, address, network);
    }
}

public interface IPNNetworkSessionDriver
{
    bool IsRunning { get; }
    // May complete asynchronously. Before gameplay readiness, establish the selected role via runtime.BeginSession.
    // The driver owns failed-start/transport teardown and registers required cleanup on its play context.
    void Begin(EveGameRuntime runtime, PNPlayConfig config);
    void End();
}

// Optional companion capability: existing drivers/mods keep their original interface.
public interface IPNRoomSessionDriver
{
    string ModeId { get; }
    bool IsRoomOpen { get; }
    bool IsPlaying { get; }
    bool CanBeginPlay { get; }
    void OpenRoom(EveGameRuntime runtime, PNPlayConfig config);
    void BeginPlay(string modeId);
    void EndPlay();
    void CloseRoom();
}
