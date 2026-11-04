#if UNITY_5_3_OR_NEWER
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Runs on the authority when a player uses an interactable (sync command `project_n:interact`, no payload; Docs/InteractablePlan.md I2). The generic checks come first: the object
//     exists and is open, its definition and handler are known, the sender has a living player in that world, within the definition's range (plus a little slack for latency), the
//     permission, then the handler's own CanUse. A command that fails a check is dropped without a word (the sender's list already showed why the entry was dimmed). On success the handler
//     acts and a once-only object spends its use. Offline and the host send to themselves through the same path.
// VI: Chạy ở authority khi người chơi dùng vật tương tác: kiểm tra chung (còn mở, người gửi có người chơi sống trong tầm, quyền, CanUse) rồi handler làm việc.
public sealed class PNInteractCommandHandler : IPNSyncCommandHandler
{
    public const float RangeSlack = .35f;

    public void Execute(PNSyncCommandContext context)
    {
        var worldObject = context.TargetObject as PNWorldObject;
        var item = worldObject != null ? worldObject.GetComponent<PNInteractable>() : null;
        if (item == null || !item.IsBound || !item.Available || item.Definition == null) return;
        if (context.Session == null || !context.Session.Worlds.TryGet(context.Target.World, context.Target.Incarnation, out var world)) return;
        var actor = context.Session.Entities.Snapshot(world).FirstOrDefault(x => x.IsReady && PNEntityRoles.IsPlayer(x) && x.LoadContext.Identity.OwnerId == context.SenderOwnerId);
        if (actor == null) return;
        var stat = actor.GetComponent<PNStat>();
        if (stat == null || stat.IsDead) return;
        var settings = item.Definition.Settings;
        var anchor = item.AnchorPosition; var position = actor.transform.position;
        if (new Vector2(position.x - anchor.x, position.z - anchor.z).magnitude > settings.Range + RangeSlack) return;
        // Owner 0 is the host's own player (and the only player when offline).
        if (settings.Permission == PNInteractionPermission.Host && context.SenderOwnerId != 0) return;
        var request = new PNInteractionContext(context.Session, world, actor, context.SenderOwnerId, item.Definition, context.Target, worldObject, true);
        if (!item.Definition.Handler.CanUse(request).Usable) return;
        item.Definition.Handler.Interact(request);
        item.Consume();
    }
}
#endif
