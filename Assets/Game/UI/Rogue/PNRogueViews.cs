#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// EN: The views of Roguelite Buff (Docs/RogueBuffPlan.md section 8, mockup Docs/Mockups/RogueBuffDemo.html approved 2026-11-10). They read the `project_n:rogue_state` view (`PNRogueStateSyncs`) and the
//     wallet of `project_n:run_state`, never the run director, so a client sees the same screens as the host; only the host has controls, and every action is one `project_n:rogue_command` message
//     (the run decides, the view only asks). All text is localized; icons are `Resources` textures named by the definition.
//       PNRogueOfferView  choose one of the offered items (left/right + Enter, or a tap); a client sees the same three and a waiting line.
//       PNRogueShopView   the shop of a shop room: shelves with prices, reroll, the team's items with sell / exchange (Up/Down + Enter, Backspace closes).
//       PNRogueHudView    the numbers the local player's items show (`Display`), beside the buffs; this machine's own entity only.
//       PNRogueListView   the team's items by constellation, opened from the pause menu (see `PNPauseMenu`).
// VI: Các view của Roguelite Buff theo mockup đã duyệt. Đọc khối `rogue_state` và ví, không đọc run director; chỉ host có điều khiển, mỗi hành động là một lệnh `rogue_command`.
// A switch for reviews only (`PNRunFlowPlayReview`): a Player started by a script often does not hold the window focus, and the views read pad and key input only while the application is focused (like the interaction list). With it on, the Roguelite views read input whatever the focus. Off in the game.
public static class PNRogueReviewSwitch { public static bool IgnoreFocus; public static int Sent; public static string Last = ""; public static bool LastAccepted; }

internal static class PNRogueUi
{
    private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    private static Guid catalogSession;
    private static PNRogueCatalog catalog;

    // The catalog of the session being played (rebuilt when the session changes).
    internal static PNRogueCatalog Catalog()
    {
        if (!EveRegistrySession.IsReady) return null;
        var id = EveRegistrySession.Context.SessionId;
        if (catalog == null || catalogSession != id)
        {
            try { catalog = PNRogueCatalog.Build(EveRegistrySession.Current); catalogSession = id; }
            catch (Exception error) { Debug.LogException(error); catalog = null; }
        }
        return catalog;
    }

    internal static PNGameSession Game() => PNGameBootstrap.Runtime != null ? PNGameBootstrap.Runtime.GetComponent<PNGameSession>() : null;
    // Only the host (or a solo player) may pick, buy and sell; a client only looks.
    internal static bool IsHost(PNGameSession game) => game != null && game.Session != null && game.Session.Role != EveSessionRole.Client;
    internal static void Send(PNGameSession game, object payload)
    {
        var command = JObject.FromObject(payload); PNRogueReviewSwitch.Sent++; PNRogueReviewSwitch.Last = command.ToString(Newtonsoft.Json.Formatting.None);
        PNRogueReviewSwitch.LastAccepted = game != null && game.Sync != null && game.Sync.Send(PNSyncTarget.Session, PNRogueCommandHandler.Command, command);
    }

    internal static Texture2D Icon(PNRogueDefinition definition)
    {
        if (!icons.TryGetValue(definition.Icon, out var texture) || texture == null) icons[definition.Icon] = texture = Resources.Load<Texture2D>(definition.Icon);
        return texture;
    }

    internal static string Name(PNRogueDefinition definition) => PNLocalization.Text(definition.Name);
    internal static string Description(PNRogueDefinition definition) => PNLocalization.Text(definition.Description);
    internal static string KindText(PNRogueDefinition definition) => PNLocalization.Text(definition.Kind == PNRogueKind.Lodestar ? "project_n:ui.rogue.kind.lodestar" : "project_n:ui.rogue.kind.spark");
    internal static string Kind(PNRogueItemDefinition item, PNRogueCatalog catalog)
        => KindText(item) + (catalog != null && catalog.TryConstellation(item.Constellation, out var constellation) ? " · " + Name(constellation) : "");

    // A round (Spark, constellation) or rounded-square (Lodestar) badge with the item's icon: the rim of the HUD avatar, a grey body so the white icon reads on any floor.
    internal static VisualElement Badge(PNRogueDefinition definition, int size)
    {
        var badge = new VisualElement { pickingMode = PickingMode.Ignore }; badge.AddToClassList("pn-rogue-badge");
        if (definition.Kind == PNRogueKind.Lodestar) badge.AddToClassList("pn-rogue-badge-lode");
        badge.style.width = size; badge.style.height = size;
        var texture = Icon(definition); if (texture != null) badge.style.backgroundImage = texture;
        return badge;
    }

    internal static Label Text(string text, string cls) { var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(cls); return label; }

    internal static VisualElement Root(VisualElement host, string name, string cls)
    {
        var root = new VisualElement { name = name, pickingMode = PickingMode.Ignore }; root.AddToClassList(cls);
        var font = Resources.Load<Font>("PNFont/Philosopher-Regular"); if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);
        host.Insert(0, root); root.style.display = DisplayStyle.None;
        return root;
    }
}

// The choice after a cleared room: up to five tiles in a row, the selected one raised and rimmed.
internal sealed class PNRogueOfferView : IDisposable
{
    private readonly PNUiRuntime owner;
    private readonly VisualElement root, tiles;
    private readonly Label title, waiting;
    private readonly InputAction left = new InputAction("RogueOfferLeft", InputActionType.Button), right = new InputAction("RogueOfferRight", InputActionType.Button), confirm = new InputAction("RogueOfferConfirm", InputActionType.Button);
    private string signature = "";
    private int selected;
    private bool disposed;

    internal PNRogueOfferView(PNUiRuntime owner, VisualElement host)
    {
        this.owner = owner;
        root = PNRogueUi.Root(host, "rogue-offer", "pn-rogue-offer");
        title = PNRogueUi.Text("", "pn-rogue-title"); root.Add(title);
        tiles = new VisualElement { pickingMode = PickingMode.Ignore }; tiles.AddToClassList("pn-rogue-tiles"); root.Add(tiles);
        waiting = PNRogueUi.Text("", "pn-rogue-waiting"); root.Add(waiting);
        left.AddBinding("<Keyboard>/leftArrow"); left.AddBinding("<Gamepad>/dpad/left");
        right.AddBinding("<Keyboard>/rightArrow"); right.AddBinding("<Gamepad>/dpad/right");
        confirm.AddBinding("<Keyboard>/enter"); confirm.AddBinding("<Keyboard>/numpadEnter"); confirm.AddBinding("<Gamepad>/buttonSouth");
        left.Enable(); right.Enable(); confirm.Enable();
    }

    // The local player can act on this screen (the host, while it is shown): the virtual stick and buttons of a phone must not sit over it, or a finger on the left half would move the character instead of choosing.
    internal bool Interactive { get; private set; }

    internal void Refresh()
    {
        if (disposed) return;
        var view = PNRogueStateSyncs.Current(); var game = PNRogueUi.Game(); var catalog = PNRogueUi.Catalog();
        bool show = view.HasOffer && catalog != null && game != null && !owner.MenuOpen;
        root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        Interactive = show && PNRogueUi.IsHost(game);
        if (!show) { signature = ""; selected = 0; return; }
        bool host = PNRogueUi.IsHost(game) && (Application.isFocused || PNRogueReviewSwitch.IgnoreFocus);
        if (host)
        {
            if (left.WasPressedThisFrame()) selected = Mathf.Max(0, selected - 1);
            if (right.WasPressedThisFrame()) selected = Mathf.Min(view.Offer.Length - 1, selected + 1);
            if (confirm.WasPressedThisFrame()) Pick(game, selected);
        }
        string key = new StringBuilder().Append(PNLocalization.Revision).Append('|').Append(string.Join(",", view.Offer)).Append('|').Append(PNRogueUi.IsHost(game)).Append('|').Append(selected).ToString();
        if (key == signature) return;
        signature = key; Rebuild(view, catalog, game);
    }

    private void Pick(PNGameSession game, int index) { PNRogueUi.Send(game, new { op = "pick", index }); }

    private void Rebuild(PNRogueView view, PNRogueCatalog catalog, PNGameSession game)
    {
        bool host = PNRogueUi.IsHost(game);
        title.text = PNLocalization.Text("project_n:ui.rogue.pick.title");
        waiting.text = host ? "" : PNLocalization.Text("project_n:ui.rogue.pick.waiting");
        tiles.Clear();
        for (int i = 0; i < view.Offer.Length; i++)
        {
            if (!catalog.TryItem(view.Offer[i], out var item)) continue;
            int index = i;
            var tile = new VisualElement { pickingMode = host ? PickingMode.Position : PickingMode.Ignore }; tile.AddToClassList("pn-rogue-tile");
            if (host && i == selected) tile.AddToClassList("pn-rogue-sel");
            tile.Add(PNRogueUi.Badge(item, 64));
            tile.Add(PNRogueUi.Text(PNRogueUi.Name(item), "pn-rogue-name"));
            tile.Add(PNRogueUi.Text(PNRogueUi.Kind(item, catalog), "pn-rogue-kind"));
            tile.Add(PNRogueUi.Text(PNRogueUi.Description(item), "pn-rogue-desc"));
            if (item.Kind == PNRogueKind.Lodestar) tile.Add(PNRogueUi.Text(PNLocalization.Text("project_n:ui.rogue.one_per"), "pn-rogue-tag"));
            if (host) tile.RegisterCallback<PointerDownEvent>(e => { selected = index; Pick(game, index); e.StopPropagation(); });
            tiles.Add(tile);
        }
    }

    public void Dispose() { if (disposed) return; disposed = true; left.Dispose(); right.Dispose(); confirm.Dispose(); root.RemoveFromHierarchy(); }
}

// The shop screen: shelves on the left, the team's own items on the right; one flat list of rows the host walks with Up/Down.
internal sealed class PNRogueShopView : IDisposable
{
    private struct Cell { public Action Act; public VisualElement Element; }
    private readonly PNUiRuntime owner;
    private readonly VisualElement root, shelves, own, reroll;
    private readonly Label shelvesTitle, ownTitle, rerollLabel, rerollPrice;
    private readonly Button close;
    private readonly InputAction up = new InputAction("RogueShopUp", InputActionType.Button), down = new InputAction("RogueShopDown", InputActionType.Button),
        confirm = new InputAction("RogueShopConfirm", InputActionType.Button), back = new InputAction("RogueShopBack", InputActionType.Button);
    private readonly List<Cell> cells = new List<Cell>();
    private string signature = "";
    private int selected;
    private PNRogueLodestarDefinition exchanging;   // choosing what to exchange this Lodestar for
    private bool disposed;

    internal PNRogueShopView(PNUiRuntime owner, VisualElement host)
    {
        this.owner = owner;
        root = PNRogueUi.Root(host, "rogue-shop", "pn-rogue-shop");
        var columns = new VisualElement { pickingMode = PickingMode.Ignore }; columns.AddToClassList("pn-rogue-shop-columns"); root.Add(columns);
        var leftColumn = new VisualElement { pickingMode = PickingMode.Ignore }; leftColumn.AddToClassList("pn-rogue-shop-left"); columns.Add(leftColumn);
        shelvesTitle = PNRogueUi.Text("", "pn-rogue-heading"); leftColumn.Add(shelvesTitle);
        shelves = new VisualElement { pickingMode = PickingMode.Ignore }; shelves.AddToClassList("pn-rogue-shelves"); leftColumn.Add(shelves);
        reroll = new VisualElement { pickingMode = PickingMode.Ignore }; reroll.AddToClassList("pn-rogue-reroll"); leftColumn.Add(reroll);
        rerollLabel = PNRogueUi.Text("", "pn-rogue-button-text"); rerollPrice = PNRogueUi.Text("", "pn-rogue-price"); reroll.Add(rerollLabel); reroll.Add(rerollPrice);
        var rightColumn = new VisualElement { pickingMode = PickingMode.Ignore }; rightColumn.AddToClassList("pn-rogue-shop-right"); columns.Add(rightColumn);
        ownTitle = PNRogueUi.Text("", "pn-rogue-heading"); rightColumn.Add(ownTitle);
        own = new VisualElement { pickingMode = PickingMode.Ignore }; own.AddToClassList("pn-rogue-own"); rightColumn.Add(own);
        close = new Button(Close) { focusable = false }; close.AddToClassList("pn-rogue-close"); root.Add(close);
        up.AddBinding("<Keyboard>/upArrow"); up.AddBinding("<Gamepad>/dpad/up");
        down.AddBinding("<Keyboard>/downArrow"); down.AddBinding("<Gamepad>/dpad/down");
        confirm.AddBinding("<Keyboard>/enter"); confirm.AddBinding("<Keyboard>/numpadEnter"); confirm.AddBinding("<Gamepad>/buttonSouth");
        back.AddBinding("<Keyboard>/backspace"); back.AddBinding("<Gamepad>/buttonEast");
        up.Enable(); down.Enable(); confirm.Enable(); back.Enable();
    }

    private void Close() { exchanging = null; PNRogueUi.Send(PNRogueUi.Game(), new { op = "close" }); }

    // Same as the pick screen: the host can act on the shop, so a phone's virtual stick and buttons step aside while it is shown.
    internal bool Interactive { get; private set; }

    internal void Refresh()
    {
        if (disposed) return;
        var view = PNRogueStateSyncs.Current(); var game = PNRogueUi.Game(); var catalog = PNRogueUi.Catalog();
        bool show = view.ShopOpen && catalog != null && game != null && !owner.MenuOpen;
        root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        Interactive = show && game != null && PNRogueUi.IsHost(game);
        if (!show) { signature = ""; selected = 0; exchanging = null; return; }
        bool host = PNRogueUi.IsHost(game);
        if (host && (Application.isFocused || PNRogueReviewSwitch.IgnoreFocus))
        {
            if (up.WasPressedThisFrame()) selected = Mathf.Max(0, selected - 1);
            if (down.WasPressedThisFrame()) selected = Mathf.Min(Mathf.Max(0, cells.Count - 1), selected + 1);
            if (confirm.WasPressedThisFrame() && selected < cells.Count) cells[selected].Act?.Invoke();
            if (back.WasPressedThisFrame()) { if (exchanging != null) { exchanging = null; selected = 0; } else Close(); }
        }
        var key = new StringBuilder().Append(PNLocalization.Revision).Append('|').Append(host).Append('|').Append(selected).Append('|').Append(exchanging?.Id.ToString()).Append('|').Append(PNRunStateSyncs.Current().Shards)
            .Append('|').Append(view.RerollPrice).Append('|').Append(view.Rerolls).Append('|').Append(string.Join(",", view.Set)).Append('|').Append(string.Join(",", view.ShelfItems)).Append('|').Append(string.Join(",", view.ShelfSold));
        if (key.ToString() == signature) return;
        signature = key.ToString(); Rebuild(view, catalog, game, host);
    }

    private void Rebuild(PNRogueView view, PNRogueCatalog catalog, PNGameSession game, bool host)
    {
        int wallet = PNRunStateSyncs.Current().Shards;
        var shop = host ? PNRunDirector.Find(game.Session)?.Rogue?.Shop : null;
        cells.Clear(); shelves.Clear(); own.Clear();
        shelvesTitle.text = PNLocalization.Text("project_n:ui.rogue.shop.shelves"); ownTitle.text = PNLocalization.Text("project_n:ui.rogue.shop.own");
        close.text = PNLocalization.Text("project_n:ui.rogue.shop.close"); close.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
        for (int i = 0; i < view.ShelfItems.Length; i++)
        {
            int index = i; bool sold = view.ShelfSold[i];
            catalog.TryItem(view.ShelfItems[i], out var item);
            var row = new VisualElement { pickingMode = host && item != null && !sold ? PickingMode.Position : PickingMode.Ignore }; row.AddToClassList("pn-rogue-shelf");
            if (sold || item == null) row.AddToClassList("pn-rogue-sold");
            if (item != null)
            {
                row.Add(PNRogueUi.Badge(item, 40));
                var text = new VisualElement { pickingMode = PickingMode.Ignore }; text.AddToClassList("pn-rogue-shelf-text");
                text.Add(PNRogueUi.Text(PNRogueUi.Name(item), "pn-rogue-name")); text.Add(PNRogueUi.Text(sold ? PNLocalization.Text("project_n:ui.rogue.shop.sold") : PNRogueUi.Kind(item, catalog) + " · " + PNRogueUi.Description(item), "pn-rogue-kind"));
                row.Add(text);
                if (!sold) row.Add(Price(item.Price, wallet));
            }
            else row.Add(PNRogueUi.Text(PNLocalization.Text("project_n:ui.rogue.shop.empty"), "pn-rogue-kind"));
            Action act = item != null && !sold ? () => PNRogueUi.Send(game, new { op = "buy", index }) : (Action)null;
            if (host && act != null) row.RegisterCallback<PointerDownEvent>(e => { selected = index; act(); e.StopPropagation(); });
            shelves.Add(row); cells.Add(new Cell { Act = act, Element = row });
        }
        rerollLabel.text = PNLocalization.Text("project_n:ui.rogue.shop.reroll");
        rerollPrice.Clear(); rerollPrice.text = view.RerollPrice.ToString();
        reroll.pickingMode = host ? PickingMode.Position : PickingMode.Ignore;
        reroll.EnableInClassList("pn-rogue-poor", wallet < view.RerollPrice);
        Action rerollAct = () => PNRogueUi.Send(game, new { op = "reroll" });
        reroll.UnregisterCallback<PointerDownEvent>(OnRerollDown); reroll.userData = rerollAct;
        if (host) reroll.RegisterCallback<PointerDownEvent>(OnRerollDown);
        cells.Add(new Cell { Act = rerollAct, Element = reroll });
        if (exchanging != null) BuildExchange(catalog, game, shop, wallet, host);
        else BuildOwn(view, catalog, game, shop, host);
        selected = Mathf.Clamp(selected, 0, Mathf.Max(0, cells.Count - 1));
        for (int i = 0; i < cells.Count; i++) cells[i].Element.EnableInClassList("pn-rogue-sel", host && i == selected);
    }

    private void OnRerollDown(PointerDownEvent e) { (reroll.userData as Action)?.Invoke(); e.StopPropagation(); }

    private VisualElement Price(int price, int wallet)
    {
        var box = new VisualElement { pickingMode = PickingMode.Ignore }; box.AddToClassList("pn-rogue-pricebox");
        if (price > wallet) box.AddToClassList("pn-rogue-poor");
        var star = new PNSparkleGlyph(true, true, new Color(.906f, .725f, .561f)); star.AddToClassList("pn-rogue-star"); box.Add(star);
        box.Add(PNRogueUi.Text(price.ToString(), "pn-rogue-price"));
        return box;
    }

    private void BuildOwn(PNRogueView view, PNRogueCatalog catalog, PNGameSession game, PNRogueShop shop, bool host)
    {
        foreach (var id in view.Set)
        {
            if (!catalog.TryItem(id, out var item)) continue;
            var lodestar = item as PNRogueLodestarDefinition;
            var row = new VisualElement { pickingMode = PickingMode.Ignore }; row.AddToClassList("pn-rogue-ownrow");
            row.Add(PNRogueUi.Badge(item, 32));
            var text = new VisualElement { pickingMode = PickingMode.Ignore }; text.AddToClassList("pn-rogue-shelf-text");
            text.Add(PNRogueUi.Text(PNRogueUi.Name(item), "pn-rogue-name")); text.Add(PNRogueUi.Text(PNRogueUi.Kind(item, catalog), "pn-rogue-kind")); row.Add(text);
            Action act;
            if (lodestar != null)
            {
                act = () => { exchanging = lodestar; selected = 0; signature = ""; };
                if (host) row.Add(MakeButton(PNLocalization.Text("project_n:ui.rogue.shop.exchange"), act));
            }
            else
            {
                var itemId = item.Id.ToString(); int gain = shop != null ? shop.SellPrice(item) : 0;
                act = () => PNRogueUi.Send(game, new { op = "sell", id = itemId });
                if (host) row.Add(MakeButton(PNLocalization.Text("project_n:ui.rogue.shop.sell") + (gain > 0 ? " +" + gain : ""), act));
            }
            own.Add(row); cells.Add(new Cell { Act = host ? act : null, Element = row });
        }
    }

    private void BuildExchange(PNRogueCatalog catalog, PNGameSession game, PNRogueShop shop, int wallet, bool host)
    {
        ownTitle.text = PNLocalization.Text("project_n:ui.rogue.shop.exchange_to") + " · " + PNRogueUi.Name(exchanging);
        var targets = shop != null ? shop.ExchangeTargets(exchanging.Id) : new List<PNRogueLodestarDefinition>();
        foreach (var target in targets)
        {
            var row = new VisualElement { pickingMode = host ? PickingMode.Position : PickingMode.Ignore }; row.AddToClassList("pn-rogue-ownrow");
            row.Add(PNRogueUi.Badge(target, 32));
            var text = new VisualElement { pickingMode = PickingMode.Ignore }; text.AddToClassList("pn-rogue-shelf-text");
            text.Add(PNRogueUi.Text(PNRogueUi.Name(target), "pn-rogue-name")); text.Add(PNRogueUi.Text(PNRogueUi.Description(target), "pn-rogue-kind")); row.Add(text);
            int cost = shop.ExchangeCost(target); row.Add(Price(cost, wallet));
            string from = exchanging.Id.ToString(), to = target.Id.ToString();
            Action act = () => { exchanging = null; selected = 0; signature = ""; PNRogueUi.Send(game, new { op = "exchange", from, to }); };
            if (host) row.RegisterCallback<PointerDownEvent>(e => { act(); e.StopPropagation(); });
            own.Add(row); cells.Add(new Cell { Act = act, Element = row });
        }
        var cancel = MakeButton(PNLocalization.Text("project_n:ui.rogue.back"), () => { exchanging = null; selected = 0; signature = ""; });
        if (host) own.Add(cancel);
    }

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button(click) { text = text, focusable = false }; button.AddToClassList("pn-rogue-button");
        return button;
    }

    public void Dispose() { if (disposed) return; disposed = true; up.Dispose(); down.Dispose(); confirm.Dispose(); back.Dispose(); root.RemoveFromHierarchy(); }
}

// The numbers the local player's items show (`PNRogueDisplay`), one badge each beside the buffs; only an entity this machine builds has them (host and solo).
internal sealed class PNRogueHudView : IDisposable
{
    private readonly PNUiRuntime owner;
    private readonly VisualElement root;
    private string signature = "";
    private bool disposed;

    internal PNRogueHudView(PNUiRuntime owner, VisualElement host)
    {
        this.owner = owner;
        root = PNRogueUi.Root(host, "rogue-hud", "pn-rogue-hud");
    }

    internal void Refresh(PNStat player)
    {
        if (disposed) return;
        var view = player != null ? player.GetComponent<PNRogueEntityView>() : null; var catalog = PNRogueUi.Catalog();
        bool show = view != null && view.IsBound && catalog != null && !owner.MenuOpen;
        var shown = show ? view.Items : null;
        if (shown == null || shown.Count == 0) { root.style.display = DisplayStyle.None; signature = ""; return; }
        root.style.display = DisplayStyle.Flex;
        var key = new StringBuilder().Append(PNLocalization.Revision);
        foreach (var item in shown) { var d = item.Display; key.Append('|').Append(item.Id).Append(d.TopLeft).Append(',').Append(d.TopRight).Append(',').Append(d.BottomLeft).Append(',').Append(d.BottomRight); }
        if (key.ToString() == signature) return;
        signature = key.ToString(); root.Clear();
        foreach (var item in shown)
        {
            if (!catalog.TryItem(item.Id, out var definition)) continue;
            var d = item.Display;
            var cell = new VisualElement { pickingMode = PickingMode.Ignore }; cell.AddToClassList("pn-rogue-hud-cell");
            cell.Add(PNRogueUi.Badge(definition, 42));
            Corner(cell, d.TopLeft, "pn-rogue-num-tl"); Corner(cell, d.TopRight, "pn-rogue-num-tr"); Corner(cell, d.BottomLeft, "pn-rogue-num-bl"); Corner(cell, d.BottomRight, "pn-rogue-num-br");
            root.Add(cell);
        }
    }

    private static void Corner(VisualElement cell, long? value, string cls)
    {
        if (!value.HasValue) return;
        var label = PNRogueUi.Text(value.Value.ToString(), "pn-rogue-num"); label.AddToClassList(cls); cell.Add(label);
    }

    public void Dispose() { if (disposed) return; disposed = true; root.RemoveFromHierarchy(); }
}

// The team's items by constellation (opened from the pause menu); everyone may look.
internal sealed class PNRogueListView : IDisposable
{
    public VisualElement Root { get; }
    private readonly Action back;
    private readonly ScrollView scroll;
    private bool disposed;

    internal PNRogueListView(VisualElement parent, Action back)
    {
        this.back = back;
        Root = new VisualElement { name = "pn-rogue-list", focusable = true }; Root.AddToClassList("pn-rogue-list"); Root.AddToClassList("pn-panel-surface");
        var font = Resources.Load<Font>("PNFont/Philosopher-Regular"); if (font != null) Root.style.unityFontDefinition = FontDefinition.FromFont(font);
        Root.Add(PNRogueUi.Text(PNLocalization.Text("project_n:ui.rogue.list.title"), "pn-rogue-heading"));
        scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden }; scroll.AddToClassList("pn-rogue-list-scroll"); Root.Add(scroll);
        var button = new Button(Cancel) { text = PNLocalization.Text("project_n:ui.rogue.back") }; button.AddToClassList("pn-rogue-button"); button.AddToClassList("pn-rogue-list-back"); Root.Add(button);
        Root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { Cancel(); e.StopImmediatePropagation(); } });
        Root.RegisterCallback<NavigationCancelEvent>(e => { Cancel(); e.StopImmediatePropagation(); });
        parent.Add(Root); Fill(); Root.Focus();
    }

    private void Fill()
    {
        var view = PNRogueStateSyncs.Current(); var catalog = PNRogueUi.Catalog();
        if (catalog == null || view.Set.Length == 0) { scroll.Add(PNRogueUi.Text(PNLocalization.Text("project_n:ui.rogue.list.empty"), "pn-rogue-kind")); return; }
        foreach (var constellation in catalog.Constellations)
        {
            var owned = view.Set.Select(x => catalog.TryItem(x, out var item) ? item : null).Where(x => x != null && x.Constellation == constellation.Id).ToList();
            if (owned.Count == 0) continue;
            var group = new VisualElement(); group.AddToClassList("pn-rogue-group");
            group.Add(PNRogueUi.Text(PNRogueUi.Name(constellation), "pn-rogue-name")); group.Add(PNRogueUi.Text(PNRogueUi.Description(constellation), "pn-rogue-kind"));
            var items = new VisualElement(); items.AddToClassList("pn-rogue-group-items");
            foreach (var item in owned)
            {
                var cell = new VisualElement { tooltip = PNRogueUi.Description(item) }; cell.AddToClassList("pn-rogue-list-item");
                cell.Add(PNRogueUi.Badge(item, 40)); cell.Add(PNRogueUi.Text(PNRogueUi.Name(item), "pn-rogue-list-name")); items.Add(cell);
            }
            group.Add(items);
            if (!owned.Any(x => x.Kind == PNRogueKind.Lodestar)) group.Add(PNRogueUi.Text(PNLocalization.Text("project_n:ui.rogue.list.none"), "pn-rogue-kind"));
            scroll.Add(group);
        }
    }

    internal void Cancel() { if (disposed) return; back?.Invoke(); }
    public void Dispose() { if (disposed) return; disposed = true; Root.RemoveFromHierarchy(); }
}
#endif
