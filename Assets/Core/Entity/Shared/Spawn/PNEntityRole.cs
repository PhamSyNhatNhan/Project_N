#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// EN: The role an entity was spawned with, kept on the object for the systems that read it (Docs/EntitySpawnPlan.md section 3.8). The factory sets it every
//     time it configures the object, so a pooled instance never keeps the previous role. Persist is read when a world checkpoint is taken and Lifetime by
//     PNEntityLifetime; Owner, FollowsOwner, CountsForClear, DropsLoot and Loot are kept for systems that do not exist yet (summons, floor clear, loot) and have no effect today.
// VI: Vai trò mà entity được sinh ra, giữ trên object cho các hệ cần đọc. Factory đặt lại mỗi lần cấu hình nên instance trong pool không giữ vai trò cũ.
public sealed class PNEntityRole : MonoBehaviour
{
    public string Entity { get; private set; }
    // Identity: the role's recipe says it is a player (PNEntityRoles.IsPlayer reads this). Whether a human drives the entity is a separate question (PNEntityControl).
    public bool IsPlayer { get; private set; }
    // No effect yet: who controls an entity is its identity's OwnerId.
    public PNEntityOwner Owner { get; private set; }
    // Written into the world checkpoint (read by PNWorldLifetimeController).
    public bool Persist { get; private set; }
    // No effect yet: no owner/summon relation exists.
    public bool FollowsOwner { get; private set; }
    // No effect yet: no floor-clear condition reads it.
    public bool CountsForClear { get; private set; }
    // No effect yet: no loot system reads these two.
    public bool DropsLoot { get; private set; }
    public EveId? Loot { get; private set; }
    public double? Lifetime { get; private set; }

    // For an object the factory did not build (a verification fixture): names its role and whether it is a player, nothing else.
    public void Declare(string entity, bool isPlayer) { Entity = entity; IsPlayer = isPlayer; Persist = true; }

    public void Apply(string entity, PNEntitySettings settings, PNBodySettings body, double? lifetime, bool isPlayer)
    {
        IsPlayer = isPlayer; Entity = entity; Owner = settings.Owner; Persist = settings.Persist; FollowsOwner = settings.FollowsOwner; CountsForClear = settings.CountsForClear;
        DropsLoot = settings.DropsLoot; Loot = body.Loot; Lifetime = lifetime;
    }
}

// EN: Removes its entity after a number of seconds of the entity's own time (paused world, time stop and slow motion stretch it). The removal goes through the
//     scheduler's normal retire path, so the entity is unloaded and returned to its pool like any other; nothing is destroyed directly.
// VI: Gỡ entity sau một số giây theo đồng hồ riêng của nó; việc gỡ đi qua đường retire chuẩn của scheduler.
public sealed class PNEntityLifetime : MonoBehaviour
{
    private Action retire;
    public double Remaining { get; private set; }
    public bool Expired { get; private set; }
    public bool Armed => retire != null && !Expired;

    public void Begin(double seconds, Action retire)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d) throw new ArgumentOutOfRangeException(nameof(seconds));
        Remaining = seconds; Expired = false; this.retire = retire ?? throw new ArgumentNullException(nameof(retire)); enabled = true;
    }
    // A pooled instance reused for a role without a lifetime.
    public void Cancel() { retire = null; Expired = false; Remaining = 0d; enabled = false; }
    // Entity time only runs while the entity is ready and its world may simulate.
    public void Advance(double seconds)
    {
        if (!Armed || seconds <= 0d) return;
        var loader = GetComponent<EveEntityLoader>();
        if (loader == null || !loader.IsReady || loader.LoadContext?.CanSimulate != true) return;
        Remaining -= seconds;
        if (Remaining > 0d) return;
        Expired = true; var action = retire; retire = null;
        action();
    }
    private void Update()
    {
        if (!Armed) return;
        var scale = GetComponent<EveTimeScale>();
        float factor = scale != null && scale.isActiveAndEnabled ? scale.Scale : 1f;
        if (factor > 0f) Advance(Time.deltaTime * factor);
    }
}
#endif
