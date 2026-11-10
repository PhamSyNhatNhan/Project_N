using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// EN: The three registered definitions of Roguelite Buff (Docs/RogueBuffPlan.md section 3): a constellation (a group with its own base mechanism), a Spark and a Lodestar (the small
//     and the big item of a constellation; Spark and Lodestar point UP to their constellation so a mod can add items to a base constellation). All are immutable content read from
//     `extensions.project_n` (rogueConstellations / rogueSparks / rogueLodestars). Each names a registered implementation by `factoryId` (the code class that gives the item its own
//     effect, validated against `data`) and may carry a shared `stats` block (modifiers the framework adds to the player as one source). Unknown fields are refused, every number is
//     finite and inside the hard limits (refused, never clamped). `availability` only decides whether the item may APPEAR (offers, shop); an item already owned always loads.
// VI: Ba định nghĩa của Roguelite Buff: chòm sao, Tia sáng, Sao dẫn. Spark/Lodestar trỏ lên chòm. Mỗi định nghĩa chọn lớp mã bằng `factoryId`, có khối `data` do lớp đó kiểm tra và khối
//     `stats` dùng chung. `availability` chỉ quyết định việc xuất hiện; mục đã sở hữu luôn nạp.
public static class PNRogueLimits
{
    public const int MaxStats = 8, MaxText = 128, MaxPrice = 100000, MaxOwned = 64, MaxOffer = 5, MaxStock = 8, MaxDefinitionsPerDocument = 1024;
    public const double MaxWeight = 1000;
}

public enum PNRogueAvailability { Default, Locked, Forbidden }
public enum PNRogueKind { Constellation, Spark, Lodestar }

public abstract class PNRogueDefinition : EveIIdentified
{
    private readonly JArray stats;
    public EveId Id { get; }
    public abstract PNRogueKind Kind { get; }
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    public PNRogueAvailability Availability { get; }
    public EveId FactoryId { get; }
    // The factory's own tuning, an object the factory validated; a copy is returned (the definition stays immutable).
    public JObject Data => (JObject)data.DeepClone();
    private readonly JObject data;
    // The shared stats block as written (so it takes part in the content hash); the parsed form is `Modifiers`.
    public JArray StatsJson => (JArray)stats.DeepClone();
    [JsonIgnore] public IReadOnlyList<EveStatModifier> Modifiers { get; }

    protected PNRogueDefinition(EveId id, string name, string description, string icon, PNRogueAvailability availability, EveId factoryId, JObject data, JArray stats)
    {
        if (!id.IsValid || id.ToString().Length > PNRogueLimits.MaxText) throw new FormatException("a Roguelite Buff definition needs a valid id.");
        Id = id; Name = Key(name, "name"); Description = Key(description, "description");
        if (string.IsNullOrEmpty(icon)) throw new FormatException("icon is required.");
        Icon = EveResourcePath.Validate(icon);
        if (!System.Enum.IsDefined(typeof(PNRogueAvailability), availability)) throw new FormatException("availability is unknown.");
        Availability = availability;
        if (!factoryId.IsValid) throw new FormatException("factoryId is required.");
        FactoryId = factoryId;
        this.data = data == null ? new JObject() : (JObject)data.DeepClone();
        this.stats = stats == null ? new JArray() : (JArray)stats.DeepClone();
        if (this.stats.Count > PNRogueLimits.MaxStats) throw new FormatException("stats holds at most " + PNRogueLimits.MaxStats + " modifiers.");
        var list = new List<EveStatModifier>();
        foreach (var token in this.stats)
        {
            var obj = PNBuffContentBatch.Object(token);
            PNBuffContentBatch.Fields(obj, "statId", "value", "group", "calculationId", "contributionId");
            list.Add(PNStatModifierData.Parse(obj));
        }
        Modifiers = list.AsReadOnly();
    }

    private static string Key(string value, string what)
    {
        if (string.IsNullOrEmpty(value) || value.Length > PNRogueLimits.MaxText || !EveId.TryParse(value, out _)) throw new FormatException(what + " is a namespaced localization key of at most " + PNRogueLimits.MaxText + " characters.");
        return value;
    }

    // Fields every definition has; `extra` adds the ones of its kind.
    internal static JObject Check(JObject obj, params string[] extra)
    {
        PNBuffContentBatch.Fields(obj, new[] { "id", "name", "description", "icon", "availability", "factoryId", "data", "stats" }.Concat(extra).ToArray());
        foreach (var required in new[] { "name", "description", "icon", "factoryId" }.Concat(extra.Where(x => x != "requires"))) if (obj[required] == null) throw new FormatException(required + " is required.");
        return obj;
    }
    internal static PNRogueAvailability ReadAvailability(JObject obj)
        => obj["availability"] == null ? PNRogueAvailability.Default
            : PNRogueliteParse.Enum(obj, "availability", ("default", PNRogueAvailability.Default), ("locked", PNRogueAvailability.Locked), ("forbidden", PNRogueAvailability.Forbidden));
    internal static JObject ReadData(JObject obj) => obj["data"] == null ? new JObject() : obj["data"] as JObject ?? throw new FormatException("data must be an object.");
    internal static JArray ReadStats(JObject obj) => obj["stats"] == null ? new JArray() : PNRogueliteParse.ArrayIn(obj, "stats", 0, PNRogueLimits.MaxStats);
}

public sealed class PNRogueConstellationDefinition : PNRogueDefinition
{
    public override PNRogueKind Kind => PNRogueKind.Constellation;
    public PNRogueConstellationDefinition(EveId id, string name, string description, string icon, PNRogueAvailability availability, EveId factoryId, JObject data = null, JArray stats = null)
        : base(id, name, description, icon, availability, factoryId, data, stats) { }
    public static PNRogueConstellationDefinition Parse(JObject obj, EveId id)
    {
        Check(obj);
        return new PNRogueConstellationDefinition(id, PNBuffContentBatch.String(obj, "name"), PNBuffContentBatch.String(obj, "description"), PNBuffContentBatch.String(obj, "icon"),
            ReadAvailability(obj), PNRogueliteParse.Id(obj["factoryId"], "factoryId"), ReadData(obj), ReadStats(obj));
    }
}

// A Spark or a Lodestar: an item of one constellation with a weight (how often it is drawn) and a price (the shop).
public abstract class PNRogueItemDefinition : PNRogueDefinition
{
    public EveId Constellation { get; }
    public double Weight { get; }
    public int Price { get; }
    protected PNRogueItemDefinition(EveId id, string name, string description, string icon, PNRogueAvailability availability, EveId factoryId, JObject data, JArray stats,
        EveId constellation, double weight, int price) : base(id, name, description, icon, availability, factoryId, data, stats)
    {
        if (!constellation.IsValid) throw new FormatException("constellation is required.");
        if (double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0 || weight > PNRogueLimits.MaxWeight) throw new FormatException("weight is above 0 and at most " + PNRogueLimits.MaxWeight + ".");
        if (price < 0 || price > PNRogueLimits.MaxPrice) throw new FormatException("price must be an integer in [0, " + PNRogueLimits.MaxPrice + "].");
        Constellation = constellation; Weight = weight; Price = price;
    }
}

public sealed class PNRogueSparkDefinition : PNRogueItemDefinition
{
    public override PNRogueKind Kind => PNRogueKind.Spark;
    // A Lodestar of the SAME constellation that has to be owned before this Spark may be offered (null = none).
    public EveId? Requires { get; }
    public PNRogueSparkDefinition(EveId id, string name, string description, string icon, PNRogueAvailability availability, EveId factoryId, JObject data, JArray stats,
        EveId constellation, double weight, int price, EveId? requires = null)
        : base(id, name, description, icon, availability, factoryId, data, stats, constellation, weight, price)
    { if (requires.HasValue && !requires.Value.IsValid) throw new FormatException("requires must be a valid id."); Requires = requires; }
    public static PNRogueSparkDefinition Parse(JObject obj, EveId id)
    {
        Check(obj, "constellation", "requires", "weight", "price");
        return new PNRogueSparkDefinition(id, PNBuffContentBatch.String(obj, "name"), PNBuffContentBatch.String(obj, "description"), PNBuffContentBatch.String(obj, "icon"),
            ReadAvailability(obj), PNRogueliteParse.Id(obj["factoryId"], "factoryId"), ReadData(obj), ReadStats(obj),
            PNRogueliteParse.Id(obj["constellation"], "constellation"), PNRogueliteParse.NumberIn(obj, "weight", 0, PNRogueLimits.MaxWeight, minExclusive: true),
            PNRogueliteParse.IntegerIn(obj, "price", 0, PNRogueLimits.MaxPrice), obj["requires"] == null || obj["requires"].Type == JTokenType.Null ? (EveId?)null : PNRogueliteParse.Id(obj["requires"], "requires"));
    }
}

public sealed class PNRogueLodestarDefinition : PNRogueItemDefinition
{
    public override PNRogueKind Kind => PNRogueKind.Lodestar;
    public PNRogueLodestarDefinition(EveId id, string name, string description, string icon, PNRogueAvailability availability, EveId factoryId, JObject data, JArray stats,
        EveId constellation, double weight, int price) : base(id, name, description, icon, availability, factoryId, data, stats, constellation, weight, price) { }
    public static PNRogueLodestarDefinition Parse(JObject obj, EveId id)
    {
        Check(obj, "constellation", "weight", "price");
        return new PNRogueLodestarDefinition(id, PNBuffContentBatch.String(obj, "name"), PNBuffContentBatch.String(obj, "description"), PNBuffContentBatch.String(obj, "icon"),
            ReadAvailability(obj), PNRogueliteParse.Id(obj["factoryId"], "factoryId"), ReadData(obj), ReadStats(obj),
            PNRogueliteParse.Id(obj["constellation"], "constellation"), PNRogueliteParse.NumberIn(obj, "weight", 0, PNRogueLimits.MaxWeight, minExclusive: true),
            PNRogueliteParse.IntegerIn(obj, "price", 0, PNRogueLimits.MaxPrice));
    }
}

// EN: The `rogue` block of a run (Docs/RogueBuffPlan.md section 3): the one authority for the numbers of the Roguelite Buff of that run. A run without the block has no Roguelite Buff.
//     Every field is required when the block is present: `offer` choices at the end of a floor (1-5), the shards an empty offer pays, and the shop (stock size, reroll price base and step,
//     the share of the price a sold Spark pays back, the share of the new Lodestar's price an exchange pays back).
// VI: Khối `rogue` của run: nguồn duy nhất của các con số Roguelite Buff. Không có khối = run không có Roguelite Buff. Có khối thì mọi trường bắt buộc.
public sealed class PNRogueRunRules
{
    public int Offer { get; }
    public int EmptyOfferShards { get; }
    public int ShopStock { get; }
    public int RerollBase { get; }
    public int RerollStep { get; }
    public double SellRatio { get; }
    public double ExchangeRatio { get; }
    public PNRogueRunRules(int offer, int emptyOfferShards, int shopStock, int rerollBase, int rerollStep, double sellRatio, double exchangeRatio)
    {
        if (offer < 1 || offer > PNRogueLimits.MaxOffer) throw new FormatException("rogue offer must be an integer in [1, " + PNRogueLimits.MaxOffer + "].");
        if (emptyOfferShards < 0 || emptyOfferShards > PNRogueliteLimits.MaxShards) throw new FormatException("rogue emptyOfferShards must be an integer in [0, " + PNRogueliteLimits.MaxShards + "].");
        if (shopStock < 1 || shopStock > PNRogueLimits.MaxStock) throw new FormatException("rogue shop stock must be an integer in [1, " + PNRogueLimits.MaxStock + "].");
        if (rerollBase < 0 || rerollBase > PNRogueLimits.MaxPrice || rerollStep < 0 || rerollStep > PNRogueLimits.MaxPrice) throw new FormatException("rogue shop reroll prices are integers in [0, " + PNRogueLimits.MaxPrice + "].");
        if (double.IsNaN(sellRatio) || double.IsInfinity(sellRatio) || sellRatio < 0 || sellRatio > 1 || double.IsNaN(exchangeRatio) || double.IsInfinity(exchangeRatio) || exchangeRatio < 0 || exchangeRatio > 1)
            throw new FormatException("rogue shop sellRatio and exchangeRatio are numbers in [0, 1].");
        Offer = offer; EmptyOfferShards = emptyOfferShards; ShopStock = shopStock; RerollBase = rerollBase; RerollStep = rerollStep; SellRatio = sellRatio; ExchangeRatio = exchangeRatio;
    }
    public static PNRogueRunRules Parse(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "offer", "emptyOfferShards", "shop");
        foreach (var required in new[] { "offer", "emptyOfferShards", "shop" }) if (obj[required] == null) throw new FormatException("rogue " + required + " is required.");
        var shop = PNBuffContentBatch.Object(obj["shop"]);
        PNBuffContentBatch.Fields(shop, "stock", "rerollBase", "rerollStep", "sellRatio", "exchangeRatio");
        foreach (var required in new[] { "stock", "rerollBase", "rerollStep", "sellRatio", "exchangeRatio" }) if (shop[required] == null) throw new FormatException("rogue shop " + required + " is required.");
        return new PNRogueRunRules(PNRogueliteParse.IntegerIn(obj, "offer", 1, PNRogueLimits.MaxOffer), PNRogueliteParse.IntegerIn(obj, "emptyOfferShards", 0, PNRogueliteLimits.MaxShards),
            PNRogueliteParse.IntegerIn(shop, "stock", 1, PNRogueLimits.MaxStock), PNRogueliteParse.IntegerIn(shop, "rerollBase", 0, PNRogueLimits.MaxPrice), PNRogueliteParse.IntegerIn(shop, "rerollStep", 0, PNRogueLimits.MaxPrice),
            PNRogueliteParse.NumberIn(shop, "sellRatio", 0, 1), PNRogueliteParse.NumberIn(shop, "exchangeRatio", 0, 1));
    }
}
