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
    public bool Player;
    public EveWorldContext World;
    public ulong WorldIncarnation;
    public ulong MembershipRevision=1;
    public string ContentSlot,SpawnEntry;
    public string Template;
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
}
#endif
#if UNITY_5_3_OR_NEWER
public interface IPNSceneEntityVisual { void BindGround(UnityEngine.Collider ground); }
#endif

public interface IPNEntityResourceOwner { void DisposeEntityResources(); }
