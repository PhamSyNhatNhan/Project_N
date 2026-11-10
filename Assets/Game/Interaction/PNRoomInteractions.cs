#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The interactables of the roguelite rooms (Docs/RogueliteFramePlan.md F5), registered as `project_n:room_start`, `room_exit`, `rest_heal`, `shop_heal` (and `base:` aliases):
//       room_start  the start switch of a combat or boss room: presses the room's start (host only).
//       room_exit   a gate of a cleared room: takes the team through it (host only); the gate of the last room ends the run as a victory.
//       rest_heal   a fountain: heals every living player of the room by `params.ratio` of their maximum health (once, everyone).
//       shop_heal   a shop heal: costs `params.cost` from the team wallet and heals by `params.ratio` (repeatable, host only).
//       shop_rogue  the Roguelite Buff shop of a shop room: opens the shop screen on every machine (host only; the buying itself is the `project_n:rogue_command` command).
//     A handler keeps no state: it asks the run director (the owner of the room, the wallet and the gates) and acts through it. A map names its interactables by definition id on the
//     PNInteractable of each object; which objects are open, and when, is decided by the room director.
// VI: Các vật tương tác của phòng roguelite: công tắc bắt đầu, cổng ra, đài hồi máu, hồi máu ở cửa hàng. Handler không giữ trạng thái, hỏi PNRunDirector và làm qua nó.
public static class PNRoomInteractions
{
    public static readonly EveId RoomStart = EveId.Parse("project_n:room_start"), RoomExit = EveId.Parse("project_n:room_exit"), RestHeal = EveId.Parse("project_n:rest_heal"), ShopHeal = EveId.Parse("project_n:shop_heal"), ShopRogue = EveId.Parse("project_n:shop_rogue");
    public const string KindKeyPrefix = "project_n:room.kind.";
    public static string KindKey(PNRoomKind kind) => KindKeyPrefix + kind.ToString().ToLowerInvariant();
    public static readonly string FinishKey = KindKeyPrefix + "finish";

    // EN: The icon of a gate's label: a label key `project_n:room.kind.<kind>` for one of the five kinds gives that kind's icon
    //     (white line on transparent with a soft halo, Resources/UI/Icons/RoomKinds/<kind>.png, placeholder art the user redraws later); any other key (the finish gate, a definition label) gives null.
    // VI: Biểu tượng của nhãn cổng: khoá `project_n:room.kind.<loại>` của một trong năm loại phòng cho biểu tượng loại đó; khoá khác (cổng thắng, nhãn của định nghĩa) cho null.
    private static readonly System.Collections.Generic.Dictionary<string, Texture2D> kindIcons = new System.Collections.Generic.Dictionary<string, Texture2D>(StringComparer.Ordinal);
    public static Texture2D KindIcon(string labelKey)
    {
        if (string.IsNullOrEmpty(labelKey) || !labelKey.StartsWith(KindKeyPrefix, StringComparison.Ordinal)) return null;
        string kind = labelKey.Substring(KindKeyPrefix.Length);
        if (kind != "combat" && kind != "boss" && kind != "rest" && kind != "shop" && kind != "event") return null;
        if (!kindIcons.TryGetValue(kind, out var icon) || icon == null) kindIcons[kind] = icon = Resources.Load<Texture2D>("UI/Icons/RoomKinds/" + kind);
        return icon;
    }

    private static JObject Data(string permission, string uses, string label, JObject handlerParams = null)
    {
        var data = new JObject { ["range"] = 1.6, ["permission"] = permission, ["uses"] = uses, ["label"] = label };
        if (handlerParams != null) data["params"] = handlerParams;
        return data;
    }

    public static void Register(EveRegistryHub hub)
    {
        var instances = hub.Instances();
        instances.Register(new PNInteractableDefinition(RoomStart, typeof(PNRoomStartHandler), () => new PNRoomStartHandler(), Data("host", "once", "project_n:interact.room_start")));
        instances.Register(new PNInteractableDefinition(RoomExit, typeof(PNRoomExitHandler), () => new PNRoomExitHandler(), Data("host", "repeat", "project_n:interact.room_exit")));
        instances.Register(new PNInteractableDefinition(RestHeal, typeof(PNRestHealHandler), () => new PNRestHealHandler(), Data("everyone", "once", "project_n:interact.rest_heal", new JObject { ["ratio"] = 0.5 }), PNHealParams.Validate));
        instances.Register(new PNInteractableDefinition(ShopHeal, typeof(PNShopHealHandler), () => new PNShopHealHandler(), Data("host", "repeat", "project_n:interact.shop_heal", new JObject { ["cost"] = 30, ["ratio"] = 1.0 }), PNHealParams.ValidateShop));
        instances.Register(new PNInteractableDefinition(ShopRogue, typeof(PNRogueShopHandler), () => new PNRogueShopHandler(), Data("host", "repeat", "project_n:interact.shop_rogue")));
        foreach (var id in new[] { RoomStart, RoomExit, RestHeal, ShopHeal, ShopRogue })
            instances.RegisterBase<PNInteractableDefinition>(new EveId("base", id.Path), id, "project_n", compatible: (original, replacement) => true);
    }

    // The run and the room the interactable's world belongs to, or null (another world, no run, a client that does not run it).
    internal static PNRoomDirector RoomOf(PNInteractionContext context)
    {
        var room = PNRunDirector.Find(context.Session)?.Room;
        return room != null && room.World == context.World ? room : null;
    }
}

internal static class PNHealParams
{
    public static void Validate(JToken value)
    {
        var obj = PNBuffContentBatch.Object(value); PNBuffContentBatch.Fields(obj, "ratio");
        Ratio(obj);
    }
    public static void ValidateShop(JToken value)
    {
        var obj = PNBuffContentBatch.Object(value); PNBuffContentBatch.Fields(obj, "cost", "ratio");
        int cost = PNBuffContentBatch.Integer(obj, "cost");
        if (cost < 0 || cost > PNRogueliteLimits.MaxShards) throw new FormatException("shop_heal cost must be an integer in [0, " + PNRogueliteLimits.MaxShards + "].");
        Ratio(obj);
    }
    private static void Ratio(JObject obj)
    {
        double ratio = PNBuffContentBatch.Number(obj, "ratio");
        if (ratio <= 0d || ratio > 1d) throw new FormatException("heal ratio must be above 0 and at most 1.");
    }
    // Heal every living player of the room's world by a ratio of their maximum health.
    public static int HealTeam(PNInteractionContext context, double ratio)
    {
        int healed = 0;
        foreach (var player in context.Session.Entities.Snapshot(context.World).Where(x => x.IsReady && PNEntityRoles.IsPlayer(x)))
        {
            var stat = player.GetComponent<PNStat>();
            if (stat == null || !stat.HasHealth || stat.IsDead) continue;
            stat.SendHeal(stat, stat.GetMax(PNContentIds.Health) * ratio); healed++;
        }
        return healed;
    }
}

public sealed class PNRoomStartHandler : IPNInteractionHandler
{
    public PNInteractionAvailability CanUse(PNInteractionContext context)
    {
        var run = PNRunDirector.Find(context.Session);
        if (run == null) return PNInteractionAvailability.Yes; // a machine that does not run the run cannot tell: the authority checks
        var room = PNRoomInteractions.RoomOf(context);
        return room != null && room.StartAvailable ? PNInteractionAvailability.Yes : PNInteractionAvailability.Denied();
    }
    public void Interact(PNInteractionContext context) { PNRoomInteractions.RoomOf(context)?.PressStart(); }
}

public sealed class PNRoomExitHandler : IPNInteractionHandler
{
    public PNInteractionAvailability CanUse(PNInteractionContext context)
    {
        var run = PNRunDirector.Find(context.Session);
        if (run == null) return PNInteractionAvailability.Yes;
        var room = PNRoomInteractions.RoomOf(context);
        return room != null && room.Flow.Phase == PNRoomPhase.Cleared && run.Phase == PNRunPhase.Playing ? PNInteractionAvailability.Yes : PNInteractionAvailability.Denied();
    }
    public void Interact(PNInteractionContext context)
    {
        var run = PNRunDirector.Find(context.Session); var room = PNRoomInteractions.RoomOf(context);
        if (run == null || room == null) return;
        if (run.IsLastRoom) { run.Complete(); return; }
        int gate = room.GateIndex((context.Owner as PNWorldObject)?.Key);
        if (gate >= 0) run.Advance(gate);
    }
}

public sealed class PNRestHealHandler : IPNInteractionHandler
{
    public PNInteractionAvailability CanUse(PNInteractionContext context) => PNInteractionAvailability.Yes;
    public void Interact(PNInteractionContext context) { PNHealParams.HealTeam(context, context.Definition.Settings.Params.Value<double>("ratio")); }
}

public sealed class PNShopHealHandler : IPNInteractionHandler
{
    public PNInteractionAvailability CanUse(PNInteractionContext context)
    {
        var run = PNRunDirector.Find(context.Session);
        if (run == null || run.State == null) return PNInteractionAvailability.Yes;
        return run.State.Shards >= context.Definition.Settings.Params.Value<int>("cost") ? PNInteractionAvailability.Yes : PNInteractionAvailability.Denied();
    }
    public void Interact(PNInteractionContext context)
    {
        var run = PNRunDirector.Find(context.Session);
        if (run == null || run.State == null || !run.State.IsRunning) return;
        var settings = context.Definition.Settings;
        if (!run.State.TrySpendShards(settings.Params.Value<int>("cost"), "shop_heal")) return;
        PNHealParams.HealTeam(context, settings.Params.Value<double>("ratio"));
        run.Raise();
    }
}
public sealed class PNRogueShopHandler : IPNInteractionHandler
{
    public PNInteractionAvailability CanUse(PNInteractionContext context)
    {
        var rogue = PNRunDirector.Find(context.Session)?.Rogue;
        return rogue != null && rogue.Shop != null && !rogue.ShopOpen ? PNInteractionAvailability.Yes : PNInteractionAvailability.Denied();
    }
    public void Interact(PNInteractionContext context) { PNRunDirector.Find(context.Session)?.Rogue?.Execute(new Newtonsoft.Json.Linq.JObject { ["op"] = "open" }); }
}
#endif
