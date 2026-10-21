#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// EN: Shared look of the setup and room screens: a centred column of choice buttons that look like the pause menu's buttons (near-white,
//     rounded, with the white outline kept a small gap away from the card: `pause-choice` + `PNPauseDrawing` button halo). One press picks;
//     hover, press and keyboard focus light the button up through the pause menu's own USS states.
// VI: Giao diện chung cho màn thiết lập và danh sách phòng: cột nút giống nút pause, bấm một lần là chọn.
internal static class PNMenuButtons
{
    public static Button Choice(string name, string text, Action click, bool primary = false)
    {
        var b = new Button(click) { name = name, text = text };
        b.AddToClassList("pause-choice"); if (primary) b.AddToClassList("pause-primary");
        var halo = new PNPauseDrawing(PNPauseDrawing.Shape.ButtonHalo); halo.AddToClassList("pause-button-halo"); b.Insert(0, halo);
        return b;
    }

    // Centred column that follows the screen width (pause card: 300 px wide, at most the screen minus 48).
    public static VisualElement Column(string name)
    {
        var c = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
        c.style.position = Position.Absolute; c.style.left = Length.Percent(50); c.style.top = Length.Percent(50);
        c.style.width = 300; c.style.marginLeft = -150; c.style.justifyContent = Justify.Center;
        return c;
    }
}

// Screen 2: the ways to start the chosen mode, declared by the mode (PNModeListing.Starts).
internal sealed class PNSetupScreen : IPNMenuScreen
{
    private readonly VisualElement root, column;
    private readonly List<VisualElement> items = new List<VisualElement>();
    private readonly Color tint;

    private readonly PNListedMode mode;
    private readonly Action<PNListedMode, PNModeStartOption> onStart;
    internal void Choose(PNModeStartOption start) => onStart(mode, start);
    internal void Choose(int index) => Choose(mode.Listing.Starts[index]);

    public PNSetupScreen(PNListedMode mode, Action<PNListedMode, PNModeStartOption> onStart)
    {
        this.mode = mode; this.onStart = onStart;
        var a = mode.Listing.AccentRgb; tint = new Color(a[0] / 255f, a[1] / 255f, a[2] / 255f, 1f);
        root = new VisualElement { name = "pn-setup-screen", pickingMode = PickingMode.Ignore };
        root.style.position = Position.Absolute; root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
        column = PNMenuButtons.Column("pn-setup-column"); root.Add(column);
        var n = mode.Listing.Starts.Count; column.style.height = n * 82; column.style.marginTop = -n * 41;
        foreach (var start in mode.Listing.Starts)
        {
            var captured = start;
            var button = PNMenuButtons.Choice("pn-start-" + captured.Id, captured.Label, () => Choose(captured));
            column.Add(button); items.Add(button);
        }
    }

    public VisualElement Root => root;
    public IReadOnlyList<VisualElement> Items => items;
    public bool UsesBackdrop => true;
    public Color Tint => tint;
    public bool HandleBack() => false;
    public void Tick(float dt) { }
    public void Entered() { if (items.Count > 0) items[0].Focus(); }
    public void Animate(bool entering, float elapsed) { }
    public void Dispose() { root.RemoveFromHierarchy(); }
}

// Screen 2b: LAN rooms (refreshed twice a second while open) and a manual address; one press on a room joins it.
internal sealed class PNRoomScreen : IPNMenuScreen
{
    private readonly IPNLobbyDriver driver;
    private readonly Action<string> onJoin;
    private readonly VisualElement root, column, list;
    private readonly TextField address;
    private readonly List<VisualElement> items = new List<VisualElement>();
    private string signature = "?";
    private double lastRefresh;
    private bool searching, disposed;
    private readonly Color tint;

    public PNRoomScreen(IPNLobbyDriver driver, Color tint, Action<string> onJoin)
    {
        this.driver = driver; this.onJoin = onJoin; this.tint = tint;
        root = new VisualElement { name = "pn-room-screen", pickingMode = PickingMode.Ignore };
        root.style.position = Position.Absolute; root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
        column = PNMenuButtons.Column("pn-room-column"); column.style.top = Length.Percent(18); column.style.justifyContent = Justify.FlexStart; root.Add(column);
        var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "pn-room-list", horizontalScrollerVisibility = ScrollerVisibility.Hidden }; scroll.style.maxHeight = 300; list = scroll;
        // A scroll view clips its content, so the buttons' glow and ring (they reach out 6 px) need room inside it; the margin gives the width back.
        scroll.contentContainer.style.paddingLeft = scroll.contentContainer.style.paddingRight = scroll.contentContainer.style.paddingTop = scroll.contentContainer.style.paddingBottom = 10;
        scroll.style.marginLeft = scroll.style.marginRight = -10; column.Add(list);
        address = new TextField { name = "pn-address" }; address.AddToClassList("pn-address-field"); column.Add(address);
        column.Add(PNMenuButtons.Choice("pn-join-address", "Vào bằng địa chỉ", JoinAddress));
        items.Add(column);
        try { driver?.StartDiscovery(); searching = driver != null; } catch (Exception error) { Debug.LogWarning("Room discovery failed: " + error.Message); }
    }

    // Joins a room as if its button (or the address button) had been pressed; used by the review harness.
    internal void Join(string roomAddress) => onJoin(roomAddress);

    private void JoinAddress()
    {
        string value = address.value?.Trim();
        if (string.IsNullOrEmpty(value)) return;
        onJoin(value);
    }

    public VisualElement Root => root;
    public IReadOnlyList<VisualElement> Items => items;
    public bool UsesBackdrop => true;
    public Color Tint => tint;
    public bool HandleBack() => false;
    public void Entered() { }
    public void Animate(bool entering, float elapsed) { }

    public void Tick(float dt)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (disposed || now - lastRefresh < .5) return;
        lastRefresh = now;
        var rooms = driver?.Rooms ?? Array.Empty<PNLanRoomInfo>(); int localPort = driver?.Port ?? 0;
        var sb = new System.Text.StringBuilder();
        foreach (var r in rooms) sb.Append(r.Address).Append(':').Append(r.Port).Append('/').Append(r.Players).Append('/').Append(r.Locked).Append('/').Append(r.Unavailable).Append(';');
        string next = sb.ToString(); if (next == signature) return;
        signature = next; list.Clear();
        foreach (var room in rooms)
        {
            var info = room; string reason = info.Unavailable ?? (localPort != 0 && info.Port != localPort ? "Khác cổng" : null);
            string text = info.Name + "  " + info.Address + "  (" + info.Players + "/" + info.Capacity + ")" + (reason != null ? "  — " + reason : "");
            var button = PNMenuButtons.Choice("pn-room-" + info.Address, "", () => onJoin(info.Address)); button.SetEnabled(reason == null);
            // Long names end in an ellipsis instead of being cut off; the button keeps overflow visible for its halo, so the clipping is on the label.
            var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList("pn-room-text"); button.Add(label); list.Add(button);
        }
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try { driver?.StopDiscovery(); } catch (Exception error) { Debug.LogException(error); }
        root.RemoveFromHierarchy();
    }
}
#endif
