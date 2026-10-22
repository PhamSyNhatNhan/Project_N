#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Wires the menu screens to the game (Docs/ModeMenuPlan.md): the Hall flow (mode -> setup -> [rooms] -> character -> start through
//     PNGameFlow) and the lobby's character change. It is the only place that knows the order of the screens and talks to the profile.
//     The menu closes before the game starts, so the scene change never runs under a held input lock.
// VI: Nối các màn menu với game: luồng ở Hall (chế độ -> thiết lập -> [phòng] -> nhân vật -> bắt đầu) và đổi nhân vật trong sảnh.
internal sealed class PNMenuController
{
    private readonly PNUiRuntime owner;
    public PNMenuController(PNUiRuntime owner) { this.owner = owner; }

    private PNMenuFlow Flow => owner.MenuFlow;
    private static IPNLobbyDriver Driver => PNGameBootstrap.LobbyDriver;
    private static bool Mobile => PNMobilePlatform.IsMobile(PNMobilePlatform.UsesSimulatedScreen);

    public bool IsOpen => Flow != null && Flow.IsOpen;

    // Opens the whole flow from the Hall.
    public bool OpenHall()
    {
        if (Flow == null || Flow.IsOpen || !EveRegistrySession.IsReady) return false;
        var modes = PNModeCatalog.Listed(EveRegistrySession.Current, Mobile);
        if (modes.Count == 0) return false;
        return Flow.Open(new PNModeScreen(modes, PickMode));
    }

    // Opens straight on the character screen to change the character (the lobby); back / close just closes it.
    public bool OpenCharacterChange()
    {
        if (Flow == null || Flow.IsOpen || !EveRegistrySession.IsReady) return false;
        return Flow.Open(CharacterScreen(entry =>
        {
            PNPlayerProfileService.SetLastCharacter(entry.Id);
            Driver?.SetCharacter(entry.Id);
            Flow.Close();
        }));
    }

    private void PickMode(PNListedMode mode) => Flow.Push(new PNSetupScreen(mode, StartChosen));

    private void StartChosen(PNListedMode mode, PNModeStartOption start)
    {
        if (start.Start == PNModeStart.Join)
        {
            var a = mode.Listing.AccentRgb;
            Flow.Push(new PNRoomScreen(Driver, new Color(a[0] / 255f, a[1] / 255f, a[2] / 255f, 1f), address => Flow.Push(CharacterScreen(entry => Launch(mode, start.Start, address, entry)))));
            return;
        }
        Flow.Push(CharacterScreen(entry => Launch(mode, start.Start, null, entry)));
    }

    private PNCharacterScreen CharacterScreen(Action<PNPlayableEntry> onContinue)
    {
        var profile = PNPlayerProfileService.Current;
        var entries = PNPlayableCatalog.Playable(EveRegistrySession.Current.Characters.Snapshot().Values, profile, PNPlayerProfileService.AllowForbidden);
        return new PNCharacterScreen(entries, profile.LastCharacter, Team, onContinue);
    }

    // Name / status rows for the team column: the room's peers when there is a room; alone (no room yet) there is no team to show.
    private static IReadOnlyList<KeyValuePair<string, string>> Team()
    {
        var lobby = Driver?.Lobby;
        if (lobby == null || lobby.Peers.Count == 0) return null;
        return lobby.Peers.Select(p => new KeyValuePair<string, string>(p.Label + (p.Local ? " (bạn)" : ""), string.IsNullOrEmpty(p.Character) ? "đang chọn" : "đã chọn")).ToList();
    }

    private void Launch(PNListedMode mode, PNModeStart start, string address, PNPlayableEntry entry)
    {
        PNPlayerProfileService.SetLastCharacter(entry.Id);
        PNGameFlow.PrepareStart(mode.Id.ToString(), start == PNModeStart.Offline);
        var role = start == PNModeStart.Offline ? PNFlowMode.Offline : start == PNModeStart.Host ? PNFlowMode.Host : PNFlowMode.Join;
        var flow = Flow;
        Action begin = null;
        // The game start ends the current session synchronously (it unbinds this UI), so it must not run inside the flow's own Tick:
        // start it on the next frame, from a coroutine of the UI runtime (which outlives the session).
        begin = () => { flow.Closed -= begin; owner.StartCoroutine(StartNextFrame(mode.Id.ToString(), role, address)); };
        flow.Closed += begin;
        flow.Close();
    }

    private static System.Collections.IEnumerator StartNextFrame(string modeId, PNFlowMode role, string address)
    {
        yield return null;
        PNGameFlow.StartMode(modeId, role, address);
    }
}
#endif
