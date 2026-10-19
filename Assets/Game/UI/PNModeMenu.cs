#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.UIElements;

// Placeholder flow UI: a "Chế độ" button beside the pause toggle (Hall), the room browser, the lobby panel and short notices.
// All actions go through PNGameFlow, so a later in-world interactable can start the same flow.
internal sealed class PNModeMenu : IDisposable
{
    private enum Page { None, Mode, Join }
    private readonly PNUiRuntime owner;
    private readonly VisualElement root, panel, modePage, joinPage, roomList, lobby, peerList, toast, loading;
    private readonly Button toggle, readyButton, startButton;
    private readonly TextField addressField;
    private readonly Label joinHint, lobbyAddress, lobbyHint, toastLabel;
    private Page page;
    private string roomSignature = "", peerSignature = "", shownMessage;
    private double messageAt, lastRoomRefresh;
    private bool searching, disposed;
    internal const double ToastSeconds = 6;

    public PNModeMenu(PNUiRuntime owner, VisualElement root)
    {
        this.owner = owner; this.root = root;
        toggle = Choice("pn-mode-toggle", "Chế độ", () => Show(Page.Mode)); toggle.AddToClassList("pn-mode-toggle"); root.Add(toggle);
        panel = Surface("pn-mode-panel"); root.Add(panel);
        modePage = new VisualElement(); panel.Add(modePage);
        modePage.Add(Title("Roguelite"));
        modePage.Add(Choice("pn-mode-offline", "Chơi một mình", () => { Show(Page.None); PNGameFlow.StartRoguelite(PNFlowMode.Offline); }));
        modePage.Add(Choice("pn-mode-host", "Tạo phòng LAN", () => { Show(Page.None); PNGameFlow.StartRoguelite(PNFlowMode.Host); }));
        modePage.Add(Choice("pn-mode-join", "Vào phòng LAN", () => Show(Page.Join)));
        modePage.Add(Choice("pn-mode-close", "Đóng", () => Show(Page.None)));
        joinPage = new VisualElement(); panel.Add(joinPage);
        joinPage.Add(Title("Phòng LAN"));
        roomList = new ScrollView { name = "pn-room-list" }; roomList.AddToClassList("pn-room-list"); joinPage.Add(roomList);
        joinHint = Hint(""); joinPage.Add(joinHint);
        addressField = new TextField("Địa chỉ host") { name = "pn-address", value = "" }; addressField.AddToClassList("pn-flow-field"); joinPage.Add(addressField);
        joinPage.Add(Choice("pn-join-address", "Vào bằng địa chỉ", JoinByAddress));
        joinPage.Add(Choice("pn-join-refresh", "Làm mới", Refresh));
        joinPage.Add(Choice("pn-join-back", "Quay lại", () => Show(Page.Mode)));
        lobby = Surface("pn-lobby-panel"); root.Add(lobby);
        lobby.Add(Title("Sảnh roguelite"));
        lobbyAddress = Hint(""); lobby.Add(lobbyAddress);
        peerList = new VisualElement(); lobby.Add(peerList);
        lobbyHint = Hint(""); lobby.Add(lobbyHint);
        readyButton = Choice("pn-lobby-ready", "Sẵn sàng", ToggleReady); lobby.Add(readyButton);
        startButton = Choice("pn-lobby-start", "Bắt đầu", PNGameFlow.StartRun); lobby.Add(startButton);
        lobby.Add(Choice("pn-lobby-leave", "Rời phòng", () => PNGameFlow.ReturnToHall("Đã rời phòng.")));
        toast = new VisualElement { name = "pn-flow-toast", pickingMode = PickingMode.Ignore }; toast.AddToClassList("pn-flow-toast");
        toastLabel = new Label(); toastLabel.AddToClassList("pn-flow-label"); toast.Add(toastLabel); root.Add(toast);
        loading = new VisualElement { name = "pn-flow-loading", pickingMode = PickingMode.Position }; loading.AddToClassList("pn-flow-loading");
        var loadingLabel = new Label("Đang tải…"); loadingLabel.AddToClassList("pn-flow-title"); loading.Add(loadingLabel); root.Add(loading);
        Show(Page.None);
    }
    private static VisualElement Surface(string name)
    { var e = new VisualElement { name = name }; e.AddToClassList("pn-panel-surface"); e.AddToClassList("pn-flow-panel"); e.AddToClassList(name); return e; }
    private static Label Title(string text) { var l = new Label(text); l.AddToClassList("pn-flow-title"); return l; }
    private static Label Hint(string text) { var l = new Label(text); l.AddToClassList("pn-flow-hint"); return l; }
    private static Button Choice(string name, string text, Action click)
    { var b = new Button(click) { name = name, text = text }; b.AddToClassList("pn-flow-button"); return b; }
    private IPNLobbyDriver Driver => PNGameBootstrap.LobbyDriver;

    private void Show(Page next)
    {
        if (page == Page.Join && next != Page.Join) StopSearch();
        page = next;
        panel.style.display = next == Page.None ? DisplayStyle.None : DisplayStyle.Flex;
        modePage.style.display = next == Page.Mode ? DisplayStyle.Flex : DisplayStyle.None;
        joinPage.style.display = next == Page.Join ? DisplayStyle.Flex : DisplayStyle.None;
        if (next == Page.Join) { joinHint.text = "Đang tìm phòng trong mạng LAN…"; roomSignature = "?"; StartSearch(); }
    }
    private void StartSearch() { try { Driver?.StartDiscovery(); searching = Driver != null; } catch (Exception error) { joinHint.text = "Không tìm được phòng: " + error.Message; } }
    private void StopSearch() { try { Driver?.StopDiscovery(); } catch (Exception error) { Debug.LogException(error); } searching = false; }
    private void Refresh() { StopSearch(); roomSignature = "?"; StartSearch(); }
    private void JoinByAddress()
    {
        string address = addressField.value?.Trim();
        if (string.IsNullOrEmpty(address)) { joinHint.text = "Nhập địa chỉ của chủ phòng (ví dụ 192.168.1.20)."; return; }
        Show(Page.None); PNGameFlow.StartRoguelite(PNFlowMode.Join, address);
    }
    private void ToggleReady() { var info = Driver?.Lobby; if (info != null) Driver.SetReady(!info.LocalReady); }

    // Called every frame from PNUiRuntime.RefreshUI.
    public void Tick()
    {
        if (disposed) return;
        var phase = PNGameFlow.Phase;
        bool hall = phase == PNFlowPhase.Hall && EveRegistrySession.IsReady;
        toggle.style.display = hall && !owner.MenuOpen && page == Page.None ? DisplayStyle.Flex : DisplayStyle.None;
        if (!hall && page != Page.None) Show(Page.None);
        loading.style.display = phase == PNFlowPhase.Loading ? DisplayStyle.Flex : DisplayStyle.None;
        TickRooms(); TickLobby(phase); TickToast();
    }
    private void TickRooms()
    {
        if (page != Page.Join) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now - lastRoomRefresh < .5) return;
        lastRoomRefresh = now;
        var rooms = Driver?.Rooms ?? Array.Empty<PNLanRoomInfo>();
        int localPort = Driver?.Port ?? 0;
        string signature = string.Join(";", rooms.Select(r => r.Address + ":" + r.Port + "/" + r.Players + "/" + r.Locked + "/" + r.Unavailable));
        if (signature == roomSignature) return;
        roomSignature = signature; roomList.Clear();
        foreach (var room in rooms)
        {
            var info = room; string reason = info.Unavailable ?? (localPort != 0 && info.Port != localPort ? "Khác cổng" : null);
            string text = info.Name + "  " + info.Address + "  (" + info.Players + "/" + info.Capacity + ")" + (reason != null ? "  — " + reason : "");
            var button = Choice("pn-room-" + info.Address, text, () => { Show(Page.None); PNGameFlow.StartRoguelite(PNFlowMode.Join, info.Address); });
            button.SetEnabled(reason == null); roomList.Add(button);
        }
        joinHint.text = rooms.Count == 0 ? (searching ? "Chưa thấy phòng nào. Nhập địa chỉ nếu bạn biết." : "Không tìm được phòng LAN. Nhập địa chỉ thủ công.") : "Chọn một phòng hoặc nhập địa chỉ.";
    }
    private void TickLobby(PNFlowPhase phase)
    {
        bool show = phase == PNFlowPhase.Lobby && EveRegistrySession.IsReady;
        lobby.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) { peerSignature = ""; return; }
        var info = Driver?.Lobby; bool host = PNGameFlow.IsHost;
        if (info == null) { lobbyHint.text = "Đang đồng bộ phòng…"; return; }
        string signature = string.Join(";", info.Peers.Select(p => p.Owner + (p.Ready ? "r" : "w"))) + info.Locked + info.Started;
        if (signature != peerSignature)
        {
            peerSignature = signature; peerList.Clear();
            foreach (var peer in info.Peers)
            {
                var row = new Label(peer.Label + (peer.Local ? " (bạn)" : "") + "  —  " + (peer.Host || peer.Ready ? "sẵn sàng" : "chưa sẵn sàng")); row.AddToClassList("pn-flow-label"); peerList.Add(row);
            }
        }
        lobbyAddress.text = host ? "Địa chỉ LAN: " + string.Join(", ", (Driver.LocalAddresses.Count > 0 ? Driver.LocalAddresses : new[] { "không rõ" })) + "  cổng " + Driver.Port : "Đã vào phòng của chủ phòng.";
        readyButton.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
        readyButton.text = info.LocalReady ? "Huỷ sẵn sàng" : "Sẵn sàng";
        startButton.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
        bool can = PNGameFlow.CanStartRun(out var reason);
        startButton.SetEnabled(can);
        lobbyHint.text = info.Started ? "Đang bắt đầu run…" : host ? (can ? "Mọi người đã sẵn sàng." : reason) : (info.LocalReady ? "Đang chờ chủ phòng bắt đầu." : "Bấm Sẵn sàng khi bạn xong.");
    }
    private void TickToast()
    {
        string message = PNGameFlow.Message;
        if (message != shownMessage) { shownMessage = message; messageAt = Time.realtimeSinceStartupAsDouble; toastLabel.text = message ?? ""; }
        bool visible = !string.IsNullOrEmpty(shownMessage) && Time.realtimeSinceStartupAsDouble - messageAt < ToastSeconds;
        toast.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; StopSearch();
        foreach (var element in new[] { toggle, panel, lobby, toast, loading }) element?.RemoveFromHierarchy();
    }
}
#endif
