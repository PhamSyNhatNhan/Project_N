#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
public sealed class PNEntityRequest
{
    public string Character,SceneKey,EntityKey;
    public long Owner;
    public Vector3 Position;
    public EveWorldContext World;
    public ulong WorldIncarnation;
    public ulong MembershipRevision=1;
    public string ContentSlot,SpawnEntry;
    // Entity role id (kind "entity" of the instances registry). Template is the older name of the same value: wire and checkpoints still carry it,
    // and the factory keeps the two equal.
    public string Entity;
    public string Template;
    // Seconds of entity time before the entity is removed; overrides the role's lifetime (null = the role's).
    public double? Lifetime;
}
public interface IPNEntityFactory : IDisposable
{
    IEnumerable<PNEntityRequest> SceneEntities();
    PNEntityRequest Player(long owner);
    EveEntityLoader Get(PNEntityRequest request);
    void Configure(EveEntityLoader loader,PNEntityRequest request);
    void Return(EveEntityLoader loader);
    void Published(EveEntityLoader loader);
}
public interface IPNSessionPolicy
{
    string ModeId {get;}
    int MaximumClients {get;}
    double GraceSeconds {get;}
    void OnOwnerLeft(Action retire);
    // Default screen for this mode's transitions; default interface member keeps already-built mode policies compatible.
    EveEngine.EveId TransitionScreen => default;
}
#endif
#if UNITY_5_3_OR_NEWER
public interface IPNSceneEntityVisual { void BindGround(UnityEngine.Collider ground); }
#endif

public interface IPNEntityResourceOwner { void DisposeEntityResources(); }
