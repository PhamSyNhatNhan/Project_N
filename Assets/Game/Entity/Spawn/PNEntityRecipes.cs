using EveEngine;
#if UNITY_5_3_OR_NEWER
using System.Linq;
using UnityEngine;
#endif

// EN: Recipes of the built-in entity roles (Docs/EntitySpawnPlan.md section 3.2). The factory calls Configure while the object is inactive (before
//     prepare: input owners, ground binding; must not publish or simulate) and Published after the entity is active in its world (host, offline and
//     client alike: local-player binding). The factory itself moves the object into its world and sets its position first.
// VI: Recipe của các vai trò dựng sẵn. Configure chạy khi object còn inactive; Published chạy sau khi entity đã active trong world.
public abstract class PNEntityRecipe : IPNEntityRecipe
{
    public abstract bool IsPlayer { get; }
#if UNITY_5_3_OR_NEWER
    public virtual void Configure(EveEntityLoader loader, PNEntityRequest request, PNPlayerSpawnPoint spawn) { }
    public virtual void Published(EveSessionContext session, EveEntityLoader loader) { }
#endif
}

// A role that is owned and driven by a player peer: input, ground binding, local-player binding.
public sealed class PNPlayerEntityRecipe : PNEntityRecipe
{
    public override bool IsPlayer => true;
#if UNITY_5_3_OR_NEWER
    public override void Configure(EveEntityLoader loader, PNEntityRequest request, PNPlayerSpawnPoint spawn)
    {
        // Explicit initialization occurs before prepare. Awake reuses these same input owners.
        if (Application.isPlaying) { loader.GetComponent<PNPlayerMove>()?.EnsureInputInitialized(); loader.GetComponent<PNPlayerSkill>()?.EnsureInputInitialized(); }
        if (spawn != null) foreach (var shadow in loader.gameObject.GetComponentsInChildren<MonoBehaviour>(true).OfType<IPNSceneEntityVisual>()) shadow.BindGround(spawn.Ground);
    }
    public override void Published(EveSessionContext session, EveEntityLoader loader)
    {
        var id = loader.GetComponent<EveEntityIdentity>();
        if ((session.Role == EveSessionRole.Offline || id.OwnerId == (session.Role == EveSessionRole.Host ? 0 : session.LocalOwnerId.GetValueOrDefault(-1))))
        {
            if (ReferenceEquals(PNLocalWorldContext.Current?.Session, session)) PNLocalWorldContext.Current.Select(id.World, loader);
            foreach (var error in EveEventManager.Global.PublishSafely(PNLocalPlayerBound.Event, new PNLocalPlayerBound(loader.transform))) Debug.LogException(error, loader);
        }
    }
#endif
}

// A role the host owns and simulates (enemy, NPC, summon, prop): no input and no local-player binding.
public sealed class PNHostEntityRecipe : PNEntityRecipe { public override bool IsPlayer => false; }
