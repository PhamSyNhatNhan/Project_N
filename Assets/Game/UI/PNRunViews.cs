#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEngine;
using UnityEngine.UIElements;

// EN: The two HUD views of a run (Docs/RogueliteFramePlan.md, Docs/Mockups/RunFlowDemo.html, approved 2026-11-04). Both follow the UI rules: UI Toolkit, no backdrop (text sits straight on
//     the scene with a tight dark shadow), localized text, hidden under any open menu.
//       PNRunHudView          the room's kind (top left), the team wallet (top right, a lit star) and the 3-2-1 countdown (centre), read from the run director on this machine.
//       PNInteractionListView the usable entries near the player in a fixed column at the right-middle (the nearest selected, a light rim on it; dimmed when this machine may not use it),
//                             the input of PNInteractionControls, and a tap on a row on mobile.
// VI: Hai view HUD của run theo mockup đã duyệt: PNRunHudView (loại phòng, ví đội, đếm ngược) và PNInteractionListView (danh sách vật tương tác cố định ở phải-giữa, mục mờ khi không dùng được).
internal sealed class PNRunHudView : IDisposable
{
    private readonly PNUiRuntime owner;
    private readonly VisualElement root;
    private readonly Label room, wallet, count, caption;
    private readonly PNSparkleGlyph star;
    private bool disposed;
    private int shownCount = -1;

    internal PNRunHudView(PNUiRuntime owner, VisualElement host)
    {
        this.owner = owner;
        root = new VisualElement { name = "run-hud", pickingMode = PickingMode.Ignore }; root.AddToClassList("pn-run-hud");
        var font = Resources.Load<Font>("PNFont/Philosopher-Regular"); if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);
        room = new Label { pickingMode = PickingMode.Ignore }; room.AddToClassList("pn-run-room"); root.Add(room);
        var walletRow = new VisualElement { pickingMode = PickingMode.Ignore }; walletRow.AddToClassList("pn-run-wallet");
        star = new PNSparkleGlyph(true, true, new Color(.906f, .725f, .561f)); star.AddToClassList("pn-run-wallet-star"); walletRow.Add(star);
        wallet = new Label { pickingMode = PickingMode.Ignore }; wallet.AddToClassList("pn-run-wallet-text"); walletRow.Add(wallet); root.Add(walletRow);
        var centre = new VisualElement { pickingMode = PickingMode.Ignore }; centre.AddToClassList("pn-run-count");
        count = new Label { pickingMode = PickingMode.Ignore }; count.AddToClassList("pn-run-count-number"); centre.Add(count);
        caption = new Label { pickingMode = PickingMode.Ignore }; caption.AddToClassList("pn-run-count-caption"); centre.Add(caption);
        root.Add(centre);
        host.Insert(0, root); root.style.display = DisplayStyle.None;
    }

    internal void Refresh(PNStat player)
    {
        if (disposed) return;
        var snap = PNRunStateSyncs.Current();
        bool show = snap.Playing && player != null && !owner.MenuOpen;
        root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) { shownCount = -1; return; }
        room.text = PNLocalization.Text(PNRoomInteractions.KindKey(snap.RoomKind));
        wallet.text = snap.Money.ToString();
        int seconds = snap.Countdown;
        if (seconds != shownCount)
        {
            shownCount = seconds;
            count.text = seconds > 0 ? seconds.ToString() : "";
            caption.text = seconds > 0 ? PNLocalization.Text("project_n:hud.countdown") : "";
        }
    }

    public void Dispose() { if (disposed) return; disposed = true; root.RemoveFromHierarchy(); }
}

// EN: The end-of-run screen (mockup D): a title (Complete / Defeated), the team's round portraits with a different rim each, four rows of the run's summary and one button back to the Hall.
//     No backdrop; the text sits on the scene. Shown on this machine when its run director has ended with Victory or Defeat (Abandoned and Failed leave without a screen). The out-of-run
//     currency has no row yet. A client sees it once the run state is published (F6b). The portraits are a rimmed disc with the star until the characters have portrait art.
// VI: Màn kết thúc run: tiêu đề, chân dung từng người (viền khác màu), bốn dòng tóm tắt, một nút về sảnh chính.
internal sealed class PNRunEndView : IDisposable
{
    private static readonly Color[] Rims = { Color.white, new Color(.74f, .84f, 1f), new Color(1f, .82f, .62f), new Color(.78f, .95f, .78f) };
    private readonly PNUiRuntime owner;
    private readonly VisualElement root, team, rows;
    private readonly Label title;
    private readonly Button button;
    private string signature = "";
    private bool disposed;

    internal PNRunEndView(PNUiRuntime owner, VisualElement host)
    {
        this.owner = owner;
        root = new VisualElement { name = "run-end", pickingMode = PickingMode.Ignore }; root.AddToClassList("pn-end-root");
        var font = Resources.Load<Font>("PNFont/Philosopher-Regular"); if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);
        title = new Label { pickingMode = PickingMode.Ignore }; title.AddToClassList("pn-end-title"); root.Add(title);
        team = new VisualElement { pickingMode = PickingMode.Ignore }; team.AddToClassList("pn-end-team"); root.Add(team);
        rows = new VisualElement { pickingMode = PickingMode.Ignore }; rows.AddToClassList("pn-end-rows"); root.Add(rows);
        button = new Button(Leave) { focusable = true }; button.AddToClassList("pn-end-button");
        button.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space) { Leave(); e.StopPropagation(); } });
        root.Add(button);
        host.Add(root); root.style.display = DisplayStyle.None;
    }

    private static void Leave() { if (!PNGameFlow.Busy) PNGameFlow.ReturnToHall(); }

    internal static string Clock(double seconds)
    {
        int whole = Mathf.Max(0, (int)seconds); return (whole / 60).ToString("00") + ":" + (whole % 60).ToString("00");
    }

    internal void Refresh()
    {
        if (disposed) return;
        var snap = PNRunStateSyncs.Current();
        bool show = snap.Ended && (snap.Result == "victory" || snap.Result == "defeat") && !owner.MenuOpen;
        root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) { signature = ""; return; }
        string key = PNLocalization.Revision + "|" + snap.Result + "|" + snap.Rooms + "|" + snap.Kills + "|" + snap.Earned + "|" + snap.Seconds + "|" + snap.Players.Length;
        if (key == signature) return;
        bool first = signature.Length == 0; signature = key;
        bool win = snap.Result == "victory";
        root.EnableInClassList("pn-end-defeat", !win);
        title.text = PNLocalization.Text(win ? "project_n:run.end.victory" : "project_n:run.end.defeat");
        team.Clear();
        int index = 0;
        foreach (var player in snap.Players)
        {
            var avatar = new VisualElement { pickingMode = PickingMode.Ignore }; avatar.AddToClassList("pn-end-avatar");
            var rim = Rims[index++ % Rims.Length];
            avatar.style.borderTopColor = rim; avatar.style.borderRightColor = rim; avatar.style.borderBottomColor = rim; avatar.style.borderLeftColor = rim;
            var glyph = new PNSparkleGlyph(true); glyph.pickingMode = PickingMode.Ignore; glyph.AddToClassList("pn-end-avatar-star"); avatar.Add(glyph);
            team.Add(avatar);
        }
        rows.Clear();
        AddRow("project_n:run.end.time", Clock(snap.Seconds));
        AddRow("project_n:run.end.rooms", snap.Rooms.ToString());
        AddRow("project_n:run.end.kills", snap.Kills.ToString());
        AddRow("project_n:run.end.money", snap.Earned.ToString());
        button.text = PNLocalization.Text("project_n:run.end.hall");
        if (first) button.schedule.Execute(() => { if (!disposed) button.Focus(); }).ExecuteLater(50);
    }

    private void AddRow(string labelKey, string value)
    {
        var label = new Label(PNLocalization.Text(labelKey)) { pickingMode = PickingMode.Ignore }; label.AddToClassList("pn-end-label");
        var amount = new Label(value) { pickingMode = PickingMode.Ignore }; amount.AddToClassList("pn-end-value");
        rows.Add(label); rows.Add(amount);
    }

    public void Dispose() { if (disposed) return; disposed = true; root.RemoveFromHierarchy(); }
}

internal sealed class PNInteractionListView : IDisposable
{
    private readonly PNUiRuntime owner;
    private readonly VisualElement root, column;
    private readonly PNInteractionScanner scanner = new PNInteractionScanner();
    private readonly PNInteractionControls controls = new PNInteractionControls();
    private string signature = "";
    private float refused;
    private bool disposed;
    private PNGameSession game;

    internal PNInteractionListView(PNUiRuntime owner, VisualElement host)
    {
        this.owner = owner;
        root = new VisualElement { name = "interaction-list", pickingMode = PickingMode.Ignore }; root.AddToClassList("pn-interact-root");
        var font = Resources.Load<Font>("PNFont/Philosopher-Regular"); if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);
        column = new VisualElement { pickingMode = PickingMode.Ignore }; column.AddToClassList("pn-interact-column"); root.Add(column);
        host.Insert(0, root); root.style.display = DisplayStyle.None;
    }

    internal void Refresh(PNStat player)
    {
        if (disposed) return;
        game = PNGameBootstrap.Runtime != null ? PNGameBootstrap.Runtime.GetComponent<PNGameSession>() : null;
        var actor = EveRegistrySession.IsReady ? PNLocalWorldContext.Current?.Actor : null;
        bool show = game != null && game.Sync != null && player != null && actor != null && !owner.MenuOpen && !owner.TestExpanded && Time.timeScale > 0f && Application.isFocused && PNLocalPlayerInput.Allows(actor);
        if (show) scanner.Refresh(game, actor); else scanner.Clear();
        if (show)
        {
            controls.Poll();
            if (controls.Next.WasPressedThisFrame()) scanner.Move(1);
            if (controls.Previous.WasPressedThisFrame()) scanner.Move(-1);
            if (controls.Interact.WasPressedThisFrame() && scanner.Entries.Count > 0 && !scanner.UseSelected(game)) refused = Time.unscaledTime;
        }
        var entries = scanner.Entries;
        root.style.display = show && entries.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        var key = new StringBuilder().Append(PNLocalization.Revision).Append(owner.ShowMobileControls).Append(controls.UsingGamepad);
        for (int i = 0; i < entries.Count; i++) key.Append('|').Append(entries[i].Item.Target).Append(entries[i].LabelKey).Append(entries[i].Usable).Append(i == scanner.Selected);
        if (key.ToString() != signature) { signature = key.ToString(); Rebuild(); }
        root.style.opacity = Time.unscaledTime - refused < .12f ? .55f : 1f;
    }

    private void Rebuild()
    {
        column.Clear();
        var entries = scanner.Entries; bool mobile = owner.ShowMobileControls;
        for (int i = 0; i < entries.Count; i++)
        {
            int index = i; var entry = entries[i];
            var row = new VisualElement { pickingMode = mobile ? PickingMode.Position : PickingMode.Ignore }; row.AddToClassList("pn-interact-row");
            if (i == scanner.Selected) row.AddToClassList("pn-interact-row-selected");
            if (!entry.Usable) row.AddToClassList("pn-interact-row-dim");
            if (i == scanner.Selected && entry.Usable && !mobile) { var key = new Label(controls.KeyLabel) { pickingMode = PickingMode.Ignore }; key.AddToClassList("pn-interact-key"); row.Add(key); }
            var label = new Label(PNLocalization.Text(entry.LabelKey)) { pickingMode = PickingMode.Ignore }; label.AddToClassList("pn-interact-label"); row.Add(label);
            var glyph = new PNSparkleGlyph(true); glyph.AddToClassList("pn-interact-star"); row.Add(glyph);
            if (mobile) row.RegisterCallback<PointerDownEvent>(e => { scanner.Select(index); if (!scanner.UseSelected(game)) refused = Time.unscaledTime; e.StopPropagation(); });
            column.Add(row);
        }
    }

    public void Dispose() { if (disposed) return; disposed = true; controls.Dispose(); root.RemoveFromHierarchy(); }
}
#endif
