#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Roguelite Buff as every machine sees it (Docs/RogueBuffPlan.md section 7): one Session sync block, `project_n:rogue_state`. The host writes it from `PNRogueRun` each time the run says something
//     changed; every machine (the host and an offline player too) reads it back through the same sink, so the pick screen, the shop, the list in the pause menu and the HUD look the same everywhere and a
//     client needs nothing of the run. Ids only, never translated text. The wallet is in `project_n:run_state` (`Shards`).
// VI: Roguelite Buff như mọi máy thấy: một khối Session `project_n:rogue_state`. Host ghi từ `PNRogueRun`; mọi máy đọc lại qua cùng một sink. Chỉ có id, không có chữ đã dịch.
public sealed class PNRogueView
{
    public static readonly PNRogueView None = new PNRogueView();
    // The run has Roguelite Buff at all.
    public bool Active;
    // Items the team owns, in the order they were taken.
    public string[] Set = new string[0];
    // The items waiting to be picked (empty = nothing to pick).
    public string[] Offer = new string[0];
    public bool HasShop, ShopOpen;
    // The shelves: the item id ("" = empty shelf) and whether it was bought.
    public string[] ShelfItems = new string[0];
    public bool[] ShelfSold = new bool[0];
    public int RerollPrice, Rerolls;
    public bool HasOffer => Offer.Length > 0;
}

public sealed class PNRogueStateSync : IPNSyncStateSource, IPNSyncStateSink, IDisposable
{
    public static readonly EveId State = EveId.Parse("project_n:rogue_state");
    public const int StateBytes = 4096;
    private readonly PNSyncService sync;
    private readonly PNRunDirector director;
    private IDisposable sinkLease, sourceLease;
    private bool disposed;
    public PNRogueView Current { get; private set; } = PNRogueView.None;
    public EveId StateId => State;

    public PNRogueStateSync(PNSyncService sync, PNRunDirector director)
    {
        this.sync = sync ?? throw new ArgumentNullException(nameof(sync)); this.director = director;
        sinkLease = sync.RegisterSink(PNSyncTarget.Session, this);
        if (sync.IsAuthority && director != null)
        {
            sourceLease = sync.RegisterSource(PNSyncTarget.Session, this);
            director.Changed += Touch; Touch();
        }
    }

    private void Touch() { if (!disposed) sync.MarkDirty(PNSyncTarget.Session, State); }

    public JToken Capture()
    {
        var o = new JObject(); var rogue = director.Rogue;
        o["active"] = rogue != null;
        if (rogue == null) return o;
        o["set"] = new JArray(rogue.Set.Ids.ToArray());
        if (rogue.HasPendingOffer) o["offer"] = new JArray(rogue.Offer.Select(x => x.Id.ToString()).ToArray());
        var shop = rogue.Shop;
        if (shop != null)
        {
            o["shop"] = new JObject
            {
                ["open"] = rogue.ShopOpen, ["rerolls"] = shop.Rerolls, ["price"] = shop.RerollPrice,
                ["shelves"] = new JArray(shop.Slots.Select(x => new JObject { ["id"] = x.Item == null ? "" : x.Item.Id.ToString(), ["sold"] = x.Sold }).ToArray())
            };
        }
        return o;
    }

    public void Apply(JToken value, ulong revision)
    {
        var o = value as JObject; var view = new PNRogueView();
        if (o != null && (bool?)o["active"] == true)
        {
            view.Active = true;
            if (o["set"] is JArray set) view.Set = set.Select(x => (string)x ?? "").ToArray();
            if (o["offer"] is JArray offer) view.Offer = offer.Select(x => (string)x ?? "").ToArray();
            if (o["shop"] is JObject shop)
            {
                view.HasShop = true; view.ShopOpen = (bool?)shop["open"] ?? false; view.Rerolls = (int?)shop["rerolls"] ?? 0; view.RerollPrice = (int?)shop["price"] ?? 0;
                var shelves = shop["shelves"] as JArray ?? new JArray();
                view.ShelfItems = shelves.Select(x => (string)x["id"] ?? "").ToArray(); view.ShelfSold = shelves.Select(x => (bool?)x["sold"] ?? false).ToArray();
            }
        }
        Current = view;
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (director != null) director.Changed -= Touch;
        sinkLease?.Dispose(); sourceLease?.Dispose(); sinkLease = sourceLease = null;
    }
}

// The Roguelite Buff of the play this machine is in, for views (None when there is no active play or no block yet).
public static class PNRogueStateSyncs
{
    public static PNRogueView Current()
    {
        if (!EveRegistrySession.IsReady || PNGameBootstrap.Runtime == null) return PNRogueView.None;
        var game = PNGameBootstrap.Runtime.GetComponent<PNGameSession>();
        return game != null && ReferenceEquals(game.Session, EveRegistrySession.Context) && game.RogueView != null ? game.RogueView.Current : PNRogueView.None;
    }
}
#endif
