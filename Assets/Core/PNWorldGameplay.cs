using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// Live targeting is distinct from immutable historical damage/source snapshots.
public static class PNWorldGameplay
{
    public static bool CanExecute(MonoBehaviour entity)
    { var context=entity?.GetComponent<EveEntityLoader>()?.LoadContext;return context==null||context.CanExecute; }
    public static bool SameWorld(MonoBehaviour a,MonoBehaviour b)
    {
        if(a==null||b==null)return false;
        var x=a.GetComponent<EveEntityIdentity>();var y=b.GetComponent<EveEntityIdentity>();
        return ReferenceEquals(x?.World,y?.World);
    }
    public static bool CanTarget(MonoBehaviour source,MonoBehaviour target)=>SameWorld(source,target)&&CanExecute(source)&&CanExecute(target);
    public static void RequireTargets(MonoBehaviour source,IEnumerable<PNStat> targets)
    { foreach(var target in targets)if(!CanTarget(source,target))throw new InvalidOperationException("Live target is held, unavailable or in another world."); }
#if UNITY_5_3_OR_NEWER
    public static EveWorldContext ForScene(EveSessionContext session,UnityEngine.SceneManagement.Scene scene)
    {
        foreach(var world in session.Worlds.Snapshot())if(world.PhysicsOwner==scene)return world;
        return null;
    }
#endif
}
