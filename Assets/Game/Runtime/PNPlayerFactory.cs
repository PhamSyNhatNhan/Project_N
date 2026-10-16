#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

public readonly struct PNLocalPlayerBound
{
    public readonly Transform Target;
    public PNLocalPlayerBound(Transform target){Target=target;}
    public static readonly EveId Event=EveId.Parse("project_n:local_player_bound");
}
// Factory instances, including deferred Unity destruction, are never authored enrollment candidates.
public sealed class PNFactoryEntity : MonoBehaviour { }
public sealed class PNPlayerFactory : IPNEntityFactory
{
    private readonly EveSessionContext session;
    private readonly Dictionary<GameObject,EveEasyPooling> pools=new Dictionary<GameObject,EveEasyPooling>();
    private readonly Dictionary<EveEntityLoader,EveEasyPooling> borrowed=new Dictionary<EveEntityLoader,EveEasyPooling>();
    private readonly HashSet<EveEntityLoader> authored=new HashSet<EveEntityLoader>();
    private readonly Dictionary<GameObject,IPNEntityResourceOwner[]> resources=new Dictionary<GameObject,IPNEntityResourceOwner[]>();
    private PNPlayerSpawnPoint Spawn=>UnityEngine.Object.FindObjectsByType<PNPlayerSpawnPoint>().Single();
    public PNPlayerFactory(EveSessionContext session){this.session=session;}
    public IEnumerable<PNEntityRequest> SceneEntities()
    {
        var keys=UnityEngine.Object.FindObjectsByType<EveSceneEntityKey>(FindObjectsInactive.Include);
        EveSceneEntityKey.ValidateSet(keys);
        foreach(var loader in UnityEngine.Object.FindObjectsByType<EveEntityLoader>(FindObjectsInactive.Include))
        {
            if(loader.GetComponent<PNFactoryEntity>()!=null)continue;
            var key=loader.GetComponent<EveSceneEntityKey>();
            if(key==null)throw new InvalidOperationException("Authored entity is missing stable metadata: "+loader.name);
            if(loader.gameObject.activeInHierarchy)throw new InvalidOperationException("Authored entity must be inactive before enrollment: "+loader.name);
            yield return new PNEntityRequest {SceneKey=key.SceneKey,EntityKey=key.EntityKey,Character=loader.GetComponent<PNStat>().CharacterDefinitionId,Position=loader.transform.position};
        }
    }
    public PNEntityRequest Player(long owner)
    {
        if(owner<0)throw new ArgumentOutOfRangeException(nameof(owner));
        var spawn=Spawn;spawn.Validate();
        return new PNEntityRequest {Player=true,Owner=owner,Character="project_n:eve",SceneKey=spawn.SceneKey,EntityKey="",Position=spawn.transform.position};
    }
    public EveEntityLoader Get(PNEntityRequest request)
    {
        EveEntityLoader loader;
        if(request.Player)
        {
            var spawn=Spawn;spawn.Validate();var prefab=spawn.Prefab;
            if(prefab==null)throw new InvalidOperationException("Player spawn point needs a prefab.");
            if(!pools.TryGetValue(prefab,out var pool))
            {
                pool=new EveEasyPooling(prefab);pools.Add(prefab,pool);
                pool.Created+=item=>{item.AddComponent<PNFactoryEntity>();resources[item]=item.GetComponentsInChildren<MonoBehaviour>(true).OfType<IPNEntityResourceOwner>().ToArray();};
                pool.Destroyed+=DisposeResources;
            }
            var root=pool.Get();
            try{loader=root.GetComponent<EveEntityLoader>();if(loader==null)throw new InvalidOperationException("Factory entity lost its loader.");borrowed.Add(loader,pool);}
            catch{pool.Release(root);throw;}

        }
        else
        {
            var key=UnityEngine.Object.FindObjectsByType<EveSceneEntityKey>(FindObjectsInactive.Include).Single(x=>x.SceneKey==request.SceneKey&&x.EntityKey==request.EntityKey);
            loader=key.GetComponent<EveEntityLoader>();
            if(loader.gameObject.activeInHierarchy||!authored.Add(loader))throw new InvalidOperationException("Authored entity already enrolled.");
        }
        return loader;
    }
    public void Configure(EveEntityLoader loader,PNEntityRequest request)
    {
        var gate=loader.GetComponent<PNNetworkInputGate>();if(gate!=null)gate.Blocked=false;
        if(!request.Player)return;
        var spawn=Spawn;var root=loader.gameObject;
        if(request.SceneKey!=spawn.SceneKey||request.Character!="project_n:eve")throw new InvalidOperationException("Player factory content/scene mismatch.");
            SceneManager.MoveGameObjectToScene(root,spawn.gameObject.scene);
            root.transform.position=request.Position;
            // Explicit initialization occurs before prepare. Awake reuses these same input owners.
            loader.GetComponent<PNPlayerMove>()?.EnsureInputInitialized();
            loader.GetComponent<PNPlayerSkill>()?.EnsureInputInitialized();
            foreach(var shadow in root.GetComponentsInChildren<MonoBehaviour>(true).OfType<IPNSceneEntityVisual>())shadow.BindGround(spawn.Ground);
    }
    public void Return(EveEntityLoader loader)
    {
        if(loader==null){borrowed.Remove(loader);authored.Remove(loader);return;}
        if(loader.State!=EveEntityLoadState.Idle)throw new InvalidOperationException("Cleanup must complete before return.");
        loader.gameObject.SetActive(false);loader.GetComponent<EveEntityIdentity>().ResetIdentity();
        if(borrowed.TryGetValue(loader,out var pool)){pool.Release(loader.gameObject);borrowed.Remove(loader);}
        else authored.Remove(loader);
    }
    public void Published(EveEntityLoader loader)
    {
        loader.gameObject.SetActive(true);
        var id=loader.GetComponent<EveEntityIdentity>();
        if(loader.GetComponent<PNPlayerStat>()!=null && (session.Role==EveSessionRole.Offline||id.OwnerId==(session.Role==EveSessionRole.Host?0:session.LocalOwnerId.GetValueOrDefault(-1))))
            foreach(var error in EveEventManager.Global.PublishSafely(PNLocalPlayerBound.Event,new PNLocalPlayerBound(loader.transform)))Debug.LogException(error,loader);
    }
    public void Dispose()
    {
        if(borrowed.Count!=0||authored.Count!=0)throw new InvalidOperationException("Factory still owns resident entities.");
        foreach(var root in resources.Keys.ToArray())DisposeResources(root);
        foreach(var pool in pools.Values)pool.Dispose();pools.Clear();
    }
    private void DisposeResources(GameObject root)
    {
        if(!resources.TryGetValue(root,out var owners))return;
        foreach(var owner in owners)owner.DisposeEntityResources();
        resources.Remove(root);
    }
}
#endif
