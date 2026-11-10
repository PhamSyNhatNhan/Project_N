using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

public enum PNRogueShopResult { Ok, BadSlot, Empty, Sold, NoMoney, Unavailable, NotOwned, NotSellable, NotLodestar, WrongConstellation, AlreadyOwned }

public sealed class PNRogueShopSlot
{
    // The item on sale, or null when the shop had nothing left to put here.
    public PNRogueItemDefinition Item { get; internal set; }
    public bool Sold { get; internal set; }
}

// EN: The Roguelite Buff shop of one shop room (Docs/RogueBuffPlan.md section 4): a few items drawn when the room is entered, bought with the team wallet, a Spark sold back, a Lodestar
//     exchanged for another of its constellation (a Lodestar is never sold), the stock rerolled for a price that grows. Pure over a `PNRunState` (the wallet and the item list) and the numbers
//     of the run's `rogue` block. Every action changes only the set and the wallet (decision D6): what an item does starts when the next floor builds the players. A refund goes back to the
//     wallet WITHOUT counting as shards earned (it is not income, and stella is paid on shards earned).
// VI: Cửa hàng Roguelite Buff của một phòng cửa hàng: vài mục rút lúc vào phòng, mua bằng ví đội, bán lại Tia sáng, đổi Sao dẫn (không bán được), làm mới với giá tăng dần. Chỉ đổi tập và ví;
//     hiệu lực từ tầng sau. Tiền hoàn không tính là shard kiếm được.
public sealed class PNRogueShop
{
    private readonly PNRunState state;
    private readonly PNRogueRunRules rules;
    private readonly PNRogueCatalog catalog;
    private readonly PNRogueSet set;
    private readonly Func<PNRogueAvailability, bool> allows;
    private readonly PNRogueShopSlot[] slots;

    public PNRogueShop(PNRunState state, PNRogueRunRules rules, PNRogueCatalog catalog, Func<PNRogueAvailability, bool> allows)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.allows = allows ?? throw new ArgumentNullException(nameof(allows));
        set = new PNRogueSet(catalog, state.Loadout);
        slots = Enumerable.Range(0, rules.ShopStock).Select(_ => new PNRogueShopSlot()).ToArray();
    }

    public IReadOnlyList<PNRogueShopSlot> Slots => slots;
    public int Rerolls { get; private set; }
    public int RerollPrice => checked(rules.RerollBase + rules.RerollStep * Rerolls);
    public int SellPrice(PNRogueItemDefinition item) => (int)Math.Floor(item.Price * rules.SellRatio);
    public int ExchangeCost(PNRogueItemDefinition target) => (int)Math.Floor(target.Price * rules.ExchangeRatio);

    // Puts a fresh stock on the shelves (called once when the room is entered). Draws from the Reward stream.
    public void Fill(PNRunRandom random)
    {
        foreach (var slot in slots) { slot.Item = null; slot.Sold = false; }
        Rerolls = 0;
        Restock(random);
    }

    // The unsold slots get new items: distinct, not owned, drawn by weight; a slot with nothing left to show stays empty.
    private void Restock(PNRunRandom random)
    {
        var open = slots.Where(x => !x.Sold).ToArray();
        if (open.Length == 0) return;
        var pool = PNRogueOffer.Eligible(catalog, set, allows);
        var drawn = PNRogueOffer.Draw(pool, Math.Min(open.Length, PNRogueLimits.MaxOffer), random);
        // a shop may hold more than MaxOffer items: keep drawing for the rest from what is still left
        while (drawn.Count < open.Length)
        {
            var rest = pool.Where(x => !drawn.Contains(x)).ToList();
            var more = PNRogueOffer.Draw(rest, Math.Min(open.Length - drawn.Count, PNRogueLimits.MaxOffer), random);
            if (more.Count == 0) break;
            drawn.AddRange(more);
        }
        for (int i = 0; i < open.Length; i++) open[i].Item = i < drawn.Count ? drawn[i] : null;
    }

    public PNRogueShopResult Buy(int index)
    {
        if (index < 0 || index >= slots.Length) return PNRogueShopResult.BadSlot;
        var slot = slots[index];
        if (slot.Item == null) return PNRogueShopResult.Empty;
        if (slot.Sold) return PNRogueShopResult.Sold;
        var item = slot.Item;
        if (set.WhyNotAdd(item.Id) != null || !allows(item.Availability) || !allows(catalog.Constellation(item.Constellation).Availability)) return PNRogueShopResult.Unavailable;
        if (!state.TrySpendShards(item.Price, "rogue_buy")) return PNRogueShopResult.NoMoney;
        set.Add(item.Id); slot.Sold = true;
        return PNRogueShopResult.Ok;
    }

    // Sells an owned Spark back: its price times the sell ratio goes to the wallet (not as shards earned) and the item is free to be offered again.
    public PNRogueShopResult Sell(EveId id)
    {
        if (!catalog.TryItem(id, out var item) || !set.Has(id)) return PNRogueShopResult.NotOwned;
        if (item is PNRogueLodestarDefinition) return PNRogueShopResult.NotSellable;
        state.RefundShards(Math.Min(SellPrice(item), PNRunState.MaxShards - state.Shards), "rogue_sell");   // at the wallet ceiling the refund is cut, the sale still happens
        set.Remove(id);
        return PNRogueShopResult.Ok;
    }

    // The Lodestars a held Lodestar may be exchanged for: the others of its constellation that are allowed to appear.
    public List<PNRogueLodestarDefinition> ExchangeTargets(EveId from)
    {
        if (!catalog.TryItem(from, out var held) || !(held is PNRogueLodestarDefinition) || !set.Has(from)) return new List<PNRogueLodestarDefinition>();
        return catalog.ItemsOf(held.Constellation).OfType<PNRogueLodestarDefinition>()
            .Where(x => x.Id != from && allows(x.Availability) && allows(catalog.Constellation(x.Constellation).Availability)).ToList();
    }

    // Pays the exchange cost (the new Lodestar's price times the exchange ratio) and swaps the two in place.
    public PNRogueShopResult Exchange(EveId from, EveId to)
    {
        if (!catalog.TryItem(from, out var held) || !set.Has(from)) return PNRogueShopResult.NotOwned;
        if (!(held is PNRogueLodestarDefinition)) return PNRogueShopResult.NotLodestar;
        if (!catalog.TryItem(to, out var target) || !(target is PNRogueLodestarDefinition) || target.Constellation != held.Constellation) return PNRogueShopResult.WrongConstellation;
        if (set.Has(to)) return PNRogueShopResult.AlreadyOwned;
        if (!allows(target.Availability) || !allows(catalog.Constellation(target.Constellation).Availability)) return PNRogueShopResult.Unavailable;
        if (!state.TrySpendShards(ExchangeCost(target), "rogue_exchange")) return PNRogueShopResult.NoMoney;
        set.Exchange(from, to);
        return PNRogueShopResult.Ok;
    }

    // Pays the reroll price (which then grows by the step) and redraws every unsold slot.
    public PNRogueShopResult Reroll(PNRunRandom random)
    {
        if (!state.TrySpendShards(RerollPrice, "rogue_reroll")) return PNRogueShopResult.NoMoney;
        Rerolls++;
        Restock(random);
        return PNRogueShopResult.Ok;
    }
}
