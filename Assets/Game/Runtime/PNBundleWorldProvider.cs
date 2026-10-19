#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// The registration captures selected catalogue descriptors; no path is supplied by peers.
public sealed class PNBundleWorldProvider : IPNWorldProvider,IPNPortableWorldProvider,IPNWorldLifecycleParticipant
{
    private readonly EveBundleDescriptor descriptor;
    private readonly string sceneKey;
    private PNWorldBundleLease lease;
    private PNSceneWorldProvider sceneProvider;
    public PNBundleWorldProvider(EveBundleDescriptor descriptor,string sceneKey)
    {this.descriptor=descriptor??throw new ArgumentNullException(nameof(descriptor));if(!descriptor.Scenes.ContainsKey(sceneKey))throw new ArgumentException("Declared scene key required.");this.sceneKey=sceneKey;}
    public JToken CaptureLoadDescriptor(PNWorldBuildContext context)=>new JObject{["bundle"]=descriptor.Id,["hash"]=descriptor.ContentHash,["scene"]=sceneKey};
    public void ValidateLoadDescriptor(JToken value)
    {if((string)value["bundle"]!=descriptor.Id||(string)value["hash"]!=descriptor.ContentHash||(string)value["scene"]!=sceneKey)throw new FormatException("Saved bundle selection differs.");}
    public IEnumerator Load(PNWorldBuildContext context)
    {
        lease=PNWorldBundleService.Resolve(context.World.Session).Acquire(descriptor.Id);
        sceneProvider=new PNSceneWorldProvider(descriptor.Scenes[sceneKey]);
        // Entry owns this lifecycle participant before Load runs. Release after ALL source/physics scenes, not merely the empty source scene.
        while(!lease.Ready)yield return null;
        string path=descriptor.Scenes[sceneKey];if(!lease.Bundle.GetAllScenePaths().Contains(path,StringComparer.Ordinal))throw new FormatException("Bundle omits declared scene: "+path);
        var load=sceneProvider.Load(context);
        try{while(load.MoveNext())yield return load.Current;}finally{(load as IDisposable)?.Dispose();}
    }
    public void WorldReady(PNWorldBuildContext context) { }
    public void WorldUnloading(PNWorldBuildContext context) { }
    public void WorldDisposed(PNWorldBuildContext context)
    {if(sceneProvider?.CleanupPending==true)throw new EveWorldCleanupPendingException("Bundle scene cleanup still pending.");lease?.Dispose();lease=null;}
}
#endif
