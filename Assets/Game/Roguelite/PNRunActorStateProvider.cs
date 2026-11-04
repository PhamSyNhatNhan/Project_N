#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: What a run carries of a player from one room to the next (Docs/RogueliteFramePlan.md 8.1). A new room rebuilds the player from scratch, so this keeps only the health as a
//     ratio and whether the player was dead. It reads no Buff, Ability, movement or control state (so, unlike the baseline actor provider, a player with live Buffs can be captured:
//     they are dropped on purpose). Restoring puts the ratio on the new actor's own maximum; a dead player comes back with the run's revive ratio. Registered as
//     `project_n:run_actor_state` and named by the transfer request, not by any world policy.
// VI: Cái run mang theo của một người chơi sang phòng sau: tỉ lệ máu và đã chết hay chưa. Không đọc Buff/Ability/di chuyển nên người chơi còn Buff vẫn chụp được (Buff bị bỏ có chủ đích).
public sealed class PNRunActorStateProvider : IPNWorldStateProvider
{
    public static readonly EveId Id = EveId.Parse("project_n:run_actor_state");
    public const int Schema = 1;
    public const double DefaultReviveRatio = 0.3d;

    public JToken Capture(PNWorldStateContext context)
    {
        var actor = context.Actor ?? throw new InvalidOperationException("Run state needs an actor.");
        var stat = actor.GetComponent<PNStat>() ?? throw new InvalidOperationException("Run state needs the actor's stat.");
        var owner = actor.LoadContext.Identity.OwnerId;
        // A continued run's first move: the body in the lobby is at full health, the run record holds the health the person had when the run was saved.
        var director = PNRunDirector.Find(context.Session);
        if (director != null && director.HealthFromState && director.State.TryGetPlayer(owner, out var saved))
            return new JObject { ["owner"] = owner, ["health"] = saved.HealthRatio, ["dead"] = saved.Dead };
        double ratio = 1d; bool dead = false;
        if (stat.HasHealth)
        {
            double max = stat.GetMax(PNContentIds.Health), current = stat.GetCurrent(PNContentIds.Health);
            dead = stat.IsDead;
            ratio = max > 0d && !double.IsNaN(max) && !double.IsInfinity(max) ? Math.Max(0d, Math.Min(1d, current / max)) : 1d;
        }
        // The run's own record follows what was read, so the end screen and the next room agree.
        if (PNRunDirector.Find(context.Session)?.State.TryGetPlayer(owner, out var player) == true) { player.SetHealthRatio(ratio); if (dead) player.MarkDead(); }
        return new JObject { ["owner"] = owner, ["health"] = ratio, ["dead"] = dead };
    }

    public void Validate(PNWorldStateContext context, JToken value)
    {
        var state = value as JObject ?? throw new FormatException("Run state object required.");
        foreach (var property in state.Properties()) if (property.Name != "owner" && property.Name != "health" && property.Name != "dead") throw new FormatException("Unknown run state field " + property.Name);
        if (state["owner"]?.Type != JTokenType.Integer || state.Value<long>("owner") < 0) throw new FormatException("Run state owner required.");
        if (state["dead"]?.Type != JTokenType.Boolean) throw new FormatException("Run state dead flag required.");
        if (state["health"]?.Type != JTokenType.Float && state["health"]?.Type != JTokenType.Integer) throw new FormatException("Run state health ratio required.");
        double ratio = state.Value<double>("health");
        if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < 0d || ratio > 1d) throw new FormatException("Run state health ratio must be in [0, 1].");
    }

    public void Restore(PNWorldStateContext context, JToken value)
    {
        Validate(context, value);
        var state = (JObject)value; var stat = context.Actor.GetComponent<PNStat>() ?? throw new InvalidOperationException("Run state needs the actor's stat.");
        if (!stat.HasHealth) return;
        double ratio = state.Value<double>("health");
        if (state.Value<bool>("dead")) ratio = PNRunDirector.Find(context.Session)?.ReviveHealth ?? DefaultReviveRatio;
        double max = stat.GetMax(PNContentIds.Health);
        stat.SetCurrent(PNContentIds.Health, Math.Min(max, Math.Max(1d, max * ratio)));
        if (PNRunDirector.Find(context.Session)?.State.TryGetPlayer(context.Actor.LoadContext.Identity.OwnerId, out var player) == true && state.Value<bool>("dead")) player.Revive(ratio);
    }

    public void Dispose() { }
}
#endif
