#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The Roguelite Buff of one run, on the host (Docs/RogueBuffPlan.md section 6): the team's set of items (`PNRunState.Loadout`), the offer waiting after a room was cleared, and the shop of a shop
//     room. It is the ONLY place that changes the set (every command from the host's own screen or from a client goes through `Execute`), so every rule is in one path. It changes the set and the wallet
//     and nothing else: what the items do starts when the next floor builds the players (decision D6, `PNRogueBuffManager`).
//     The offer is drawn when a room with `reward.rogue` is cleared and the shop stock when a shop room is entered; both draw from the Reward stream AFTER the run was saved for that room, so a continued
//     run enters the room with the stream where it stood and draws the same items again, and nothing about them is saved. While an offer waits the gates stay shut.
// VI: Roguelite Buff của một run, ở host: tập mục (`PNRunState.Loadout`), lựa chọn đang chờ sau khi dọn phòng, và cửa hàng của phòng cửa hàng. Đây là nơi DUY NHẤT đổi tập. Chỉ đổi tập và ví; hiệu lực
//     từ tầng sau. Lựa chọn rút lúc dọn phòng, kho hàng rút lúc vào phòng, đều từ luồng Reward sau khi đã lưu run nên tiếp tục run ra giống hệt. Có lựa chọn đang chờ thì cổng giữ khoá.
public sealed class PNRogueRun
{
    private readonly PNRunDirector director;
    private readonly PNRunState state;
    private readonly List<PNRogueItemDefinition> offer = new List<PNRogueItemDefinition>();
    public PNRogueRunRules Rules { get; }
    public PNRogueCatalog Catalog { get; }
    public PNRogueSet Set { get; }
    public Func<PNRogueAvailability, bool> Allows { get; }
    // The shop of the shop room being played, or null in any other room.
    public PNRogueShop Shop { get; private set; }
    // The shop screen is open (the host used the shop; every machine shows it).
    public bool ShopOpen { get; private set; }
    public IReadOnlyList<PNRogueItemDefinition> Offer => offer;
    public bool HasPendingOffer => offer.Count > 0;

    public PNRogueRun(PNRunDirector director, PNRunState state, PNRogueRunRules rules, PNRogueCatalog catalog, Func<PNRogueAvailability, bool> allows)
    {
        this.director = director ?? throw new ArgumentNullException(nameof(director));
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Allows = allows ?? throw new ArgumentNullException(nameof(allows));
        Set = new PNRogueSet(catalog, state.Loadout);
    }

    // The default answer to "may an item of this availability appear": default always, forbidden only with the testing switch, locked never (nothing unlocks yet).
    public static bool DefaultAllows(PNRogueAvailability availability) => availability == PNRogueAvailability.Default || availability == PNRogueAvailability.Forbidden && PNForbiddenSwitch.Allowed;

    // ---- events from the run ----
    // A room was entered (the run is already saved for it): the offer of the room before is gone, a shop room gets its stock.
    internal void RoomEntered(PNRoomDefinition room)
    {
        offer.Clear(); ShopOpen = false;
        Shop = null;
        if (room.Kind == PNRoomKind.Shop) { Shop = new PNRogueShop(state, Rules, Catalog, Allows); Shop.Fill(state.Random); }
        director.Raise();
    }

    // A room was cleared: a room with `reward.rogue` offers items; when nothing qualifies, shards instead.
    internal void RoomCleared(PNRoomDefinition room)
    {
        if (!room.Reward.Rogue) return;
        var drawn = PNRogueOffer.Draw(PNRogueOffer.Eligible(Catalog, Set, Allows), Rules.Offer, state.Random);
        offer.Clear(); offer.AddRange(drawn);
        if (offer.Count == 0 && Rules.EmptyOfferShards > 0) state.AddShards(Rules.EmptyOfferShards, "rogue_empty");
    }

    // ---- commands (the host's own screen and the clients' messages both arrive here) ----
    public bool Execute(JObject command)
    {
        if (command == null || director.Phase != PNRunPhase.Playing || director.State != state || !state.IsRunning) return false;
        string op = command.Value<string>("op");
        bool changed;
        switch (op)
        {
            case "pick": changed = Pick(command); break;
            case "buy": changed = Shop != null && ShopOpen && Index(command) is int b && Shop.Buy(b) == PNRogueShopResult.Ok; break;
            case "sell": changed = Shop != null && ShopOpen && Item(command, "id") is PNRogueItemDefinition s && Shop.Sell(s.Id) == PNRogueShopResult.Ok; break;
            case "exchange": changed = Shop != null && ShopOpen && Item(command, "from") is PNRogueItemDefinition f && Item(command, "to") is PNRogueItemDefinition t && Shop.Exchange(f.Id, t.Id) == PNRogueShopResult.Ok; break;
            case "reroll": changed = Shop != null && ShopOpen && Shop.Reroll(state.Random) == PNRogueShopResult.Ok; break;
            case "open": changed = Shop != null && !ShopOpen && (ShopOpen = true); break;
            case "close": changed = Shop != null && ShopOpen && !(ShopOpen = false); break;
            default: changed = false; break;
        }
        if (changed) director.Raise();
        return changed;
    }

    private static int? Index(JObject command) => command["index"] != null && command["index"].Type == JTokenType.Integer ? command.Value<int>("index") : (int?)null;
    private PNRogueItemDefinition Item(JObject command, string field) => command[field] != null && command[field].Type == JTokenType.String && Catalog.TryItem(command.Value<string>(field), out var item) ? item : null;

    private bool Pick(JObject command)
    {
        var index = Index(command);
        if (!HasPendingOffer || !index.HasValue || index.Value < 0 || index.Value >= offer.Count) return false;
        var item = offer[index.Value];
        if (Set.WhyNotAdd(item.Id) != null) return false;
        Set.Add(item.Id); offer.Clear();
        director.Room?.RefreshGates();
        return true;
    }

    internal void Clear() { offer.Clear(); Shop = null; ShopOpen = false; }
}

// EN: The command that carries every Roguelite Buff action to the host: `{ "op": "pick" | "buy" | "sell" | "exchange" | "reroll" | "open" | "close", ... }`. Only the host's own player may send it (picking,
//     buying and selling are the host's, decision D9/D10); anything else is ignored without a word.
public sealed class PNRogueCommandHandler : IPNSyncCommandHandler
{
    public static readonly EveId Command = EveId.Parse("project_n:rogue_command");
    public const int CommandBytes = 256;

    public void Execute(PNSyncCommandContext context)
    {
        if (context.SenderOwnerId != 0 || !(context.Payload is JObject payload)) return;
        PNRunDirector.Find(context.Session)?.Rogue?.Execute(payload);
    }
}
#endif
