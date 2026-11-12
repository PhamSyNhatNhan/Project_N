#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// EN: The role an entity was spawned with, kept on the object for the systems that read it (Docs/EntitySpawnPlan.md section 3.8). The factory sets it every
//     time it configures the object, so a pooled instance never keeps the previous role. Persist is read when a world checkpoint is taken and Lifetime by
//     PNEntityLifetime; Owner, FollowsOwner and Loot are kept for systems that do not exist yet and have no effect today. Whether an enemy counts for a room clear or drops loot is the world's: entity modules attach PNRoomClearMarker / PNLootSource (Docs/SandboxPlan.md G6).
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
    // The id of the loot the character drops (`body.loot`); whether it drops any is the world's (feature `project_n:loot`, PNLootSource). No loot system reads it yet.
    public EveId? Loot { get; private set; }
    public double? Lifetime { get; private set; }
    // The role's despawn override (null = none): read by the despawn service of a world that has the feature.
    public PNDespawnOverride Despawn { get; private set; }
    // The character's `body.vision` in metres (0 = not declared).
    public float Vision { get; private set; }

    // For an object the factory did not build (a verification fixture): names its role and whether it is a player, nothing else.
    public void Declare(string entity, bool isPlayer) { Entity = entity; IsPlayer = isPlayer; Persist = true; Despawn = null; Vision = 0f; }

    public void Apply(string entity, PNEntitySettings settings, PNBodySettings body, double? lifetime, bool isPlayer)
    {
        IsPlayer = isPlayer; Entity = entity; Owner = settings.Owner; Persist = settings.Persist; FollowsOwner = settings.FollowsOwner;
        Loot = body.Loot; Lifetime = lifetime; Despawn = settings.Despawn; Vision = body.Vision;
    }
}

// EN: Removes its entity after a number of seconds of the entity's own time (paused world, time stop and slow motion stretch it). The removal goes through the
//     scheduler's normal retire path, so the entity is unloaded and returned to its pool like any other; nothing is destroyed directly.
// VI: Gỡ entity sau một số giây theo đồng hồ riêng của nó; việc gỡ đi qua đường retire chuẩn của scheduler.
public sealed class PNEntityLifetime : MonoBehaviour
{
    private Action retire;
    private readonly PNHoldSet holds = new PNHoldSet(owner => owner is UnityEngine.Object unityOwner && unityOwner == null);
    public double Remaining { get; private set; }
    public bool Expired { get; private set; }
    public bool Armed => retire != null && !Expired;
    // EN: Holds (Docs/SandboxPlan.md G4). While a handle lives the despawn service leaves this entity alone (a quest giver, a pet someone is escorting, an enemy in a scripted scene). A handle ends when it is
    //     disposed or when its owner object is destroyed; the entity's own timed `Lifetime` above is a different thing and is not stopped by a hold.
    // VI: Giữ: còn handle thì dịch vụ hết hạn không gỡ thực thể. Handle hết khi Dispose hoặc chủ bị hủy; `Lifetime` theo giờ ở trên là việc khác, hold không dừng nó.
    public PNHold Hold(object owner) => holds.Acquire(owner);
    public bool IsHeld => holds.IsHeld;
    public int HoldCount => holds.Count;

    public void Begin(double seconds, Action retire)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d) throw new ArgumentOutOfRangeException(nameof(seconds));
        Remaining = seconds; Expired = false; this.retire = retire ?? throw new ArgumentNullException(nameof(retire)); enabled = true; holds.Clear();
    }
    // A pooled instance reused for a role without a lifetime.
    public void Cancel() { retire = null; Expired = false; Remaining = 0d; enabled = false; holds.Clear(); }
    // The lifetime component of an entity (added when it has none; it stays disabled until a timed lifetime begins).
    public static PNEntityLifetime Of(GameObject entity)
    {
        var life = entity.GetComponent<PNEntityLifetime>();
        if (life == null) { life = entity.AddComponent<PNEntityLifetime>(); life.enabled = false; }
        return life;
    }
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
