#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// Explicit one-shot enemy placement in a prepared scene world. The entity host owns publication/pooling.
public sealed class PNSceneEnemySpawn : MonoBehaviour
{
    [SerializeField] private string character="project_n:starweaver";
    private bool requested;private int generation;private EveEntityLoader spawned;
    private void OnEnable(){requested=false;generation++;}
    private void Update()
    {
        if(requested||!EveRegistrySession.IsReady)return;
        var session=EveRegistrySession.Context;if(!session.HasAuthority)return;
        var world=PNWorldGameplay.ForScene(session,gameObject.scene);
        if(world==null||!world.CanExecute||PNWorldNavigation.Find(world)==null)return;
        var host=PNEntityHosts.Find(session);if(host==null)return;
        string contentSlot=null;
        foreach(var service in FindObjectsByType<PNGameSession>(FindObjectsSortMode.None))
        {
            if(!ReferenceEquals(service.Session,session))continue;
            foreach(var slot in service.Worlds.Get(world).Slots)
                foreach(var root in slot.Roots)
                    if(root!=null&&transform.IsChildOf(root.transform))contentSlot=slot.Id;
        }
        if(contentSlot==null)return;
        int token=generation;
        var request=new PNEntityRequest{Entity="project_n:enemy",Template="project_n:enemy",Character=character,World=world,WorldIncarnation=world.Incarnation,ContentSlot=contentSlot,SceneKey="ermode:enemy",EntityKey="",Position=transform.position};
        requested=true;
        try
        {
            var candidate=host.TrySpawn(request,published:loader=>
            {
                if(this==null||!isActiveAndEnabled||generation!=token){host.Retire(loader);return;}
                spawned=loader;
            },failed:error=>{if(this!=null&&generation==token)Debug.LogException(error,this);});
            if(candidate==null)requested=false;
        }
        catch(Exception error){Debug.LogException(error,this);}
    }
    private void OnDisable()
    {
        generation++;
        if(spawned!=null&&EveRegistrySession.IsReady)
            PNEntityHosts.Find(EveRegistrySession.Context)?.Retire(spawned);
        spawned=null;
    }
#if UNITY_EDITOR
    private void OnDrawGizmos(){Gizmos.color=new Color(.7f,.4f,1);Gizmos.DrawWireSphere(transform.position+Vector3.up*.2f,.2f);}
#endif
}
#endif
