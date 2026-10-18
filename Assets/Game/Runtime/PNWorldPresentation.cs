#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Cinemachine;
using UnityEngine.UIElements;

// Render/audio gates never disable actor roots, physics or gameplay behaviours.
[DefaultExecutionOrder(31990)]
public sealed class PNWorldPresentation : MonoBehaviour
{
    private static readonly Dictionary<EveSessionContext,PNWorldPresentation> services=new Dictionary<EveSessionContext,PNWorldPresentation>();
    public static void RegisterActor(EveSessionContext session,GameObject actor){if(services.TryGetValue(session,out var service))service.Register(actor);}
    private readonly HashSet<Component> hidden=new HashSet<Component>();
    private readonly Dictionary<Component,EveWorldContext> staging=new Dictionary<Component,EveWorldContext>();
    public static void StageScene(EveSessionContext session,EveWorldContext world,UnityEngine.SceneManagement.Scene scene)
    {
        if(!services.TryGetValue(session,out var service))return;
        foreach(var root in scene.GetRootGameObjects())
        {
            foreach(var component in root.GetComponentsInChildren<Component>(true))if(component is Renderer||component is AudioSource||component is Volume||component is UIDocument||component is Camera||component is Light||component is AudioListener||component is ReflectionProbe||component is Canvas||component is CinemachineVirtualCameraBase||component is IPNWorldPresentationPart)service.staging[component]=world;
            service.Register(root);
        }
    }
    private EveSessionContext session;
    private PNWorldRuntime worlds;
    private PNLocalWorldContext local;
    private readonly Dictionary<Component,Action> restore=new Dictionary<Component,Action>();
    private readonly HashSet<PNWorldPresentationRoot> roots=new HashSet<PNWorldPresentationRoot>();
    private PNWorldEnvironment initialEnvironment;
    private EveEventSubscription registration;
    public void Bind(EveSessionContext value,PNWorldRuntime runtime,PNLocalWorldContext context)
    {
        Release();session=value;services.Add(value,this);worlds=runtime;local=context;initialEnvironment=PNWorldEnvironment.Capture(UnityEngine.SceneManagement.SceneManager.GetActiveScene());context.Changed+=Refresh;runtime.WorldReady+=RegisterWorld;runtime.WorldUnloading+=Unloading;
        registration=EveEventManager.Global.Subscribe<PNWorldPresentationAdded>(PNWorldPresentationAdded.Event,(_,notice)=>{if(ReferenceEquals(notice.World.Session,session))Register(notice.Root);});value.RegisterCleanup(Release);
    }
    public void RegisterWorld(EveWorldContext world)
    {
        foreach(var slot in worlds.Get(world).Slots)foreach(var root in slot.Roots.Where(x=>x!=null))Register(root);
        if(local.World==null&&world==worlds.DefaultWorld)local.Select(world);Refresh();
    }
    public void Register(GameObject root)
    {
        if(root==null)return;var watcher=root.GetComponent<PNWorldPresentationRoot>()??root.AddComponent<PNWorldPresentationRoot>();watcher.Owner=this;roots.Add(watcher);watcher.Scan();
    }
    internal void Scan(PNWorldPresentationRoot root)
    {
        foreach(var item in root.GetComponentsInChildren<Component>(true))
        {
            if(item==null||restore.ContainsKey(item))continue;
            if(item is IPNWorldPresentationPart part){bool value=part.PresentationVisible;restore.Add(item,()=>part.PresentationVisible=value);}
            else if(item is Renderer r){bool value=r.forceRenderingOff;restore.Add(r,()=>r.forceRenderingOff=value);}
            else if(item is AudioSource audio){bool value=audio.mute;restore.Add(audio,()=>audio.mute=value);}
            else if(item is Volume volume){float value=volume.weight;restore.Add(volume,()=>volume.weight=value);}
            else if(item is UIDocument document){var value=document.rootVisualElement.style.display;restore.Add(document,()=>document.rootVisualElement.style.display=value);}
            else if(item is CinemachineVirtualCameraBase||item is Light||item is Camera||item is AudioListener||item is ReflectionProbe||item is Canvas){var behaviour=(Behaviour)item;bool value=behaviour.enabled;restore.Add(item,()=>behaviour.enabled=value);}
        }
        Refresh();
    }
    private void Unloading(EveWorldContext world){if(local.World==world)local.Select(null);}
    public void Refresh()
    {
        if(session?.IsActive!=true)return;
        foreach(var pair in restore.ToArray())
        {
            var component=pair.Key;if(component==null){restore.Remove(component);hidden.Remove(component);staging.Remove(component);continue;}
            var world=PNWorldGameplay.ForScene(session,component.gameObject.scene);
            if(world==null)staging.TryGetValue(component,out world);else staging.Remove(component);
            bool visible=world==null||local.World==world;
            // Restore only on a view boundary/registration, not every frame: gameplay
            // may change enabled/mute while visible without being reset by this service.
            if(visible){if(hidden.Remove(component))pair.Value();continue;}
            if(hidden.Add(component))restore[component]=Capture(component);
            Hide(component);
        }
        if(local.World!=null)worlds.Get(local.World).Environment?.Apply();
    }
    private static Action Capture(Component component)
    {
        if(component is IPNWorldPresentationPart part){bool value=part.PresentationVisible;return ()=>part.PresentationVisible=value;}
        if(component is Renderer r){bool value=r.forceRenderingOff;return ()=>r.forceRenderingOff=value;}
        if(component is AudioSource audio){bool value=audio.mute;return ()=>audio.mute=value;}
        if(component is Volume volume){float value=volume.weight;return ()=>volume.weight=value;}
        if(component is UIDocument document){var value=document.rootVisualElement.style.display;return ()=>document.rootVisualElement.style.display=value;}
        var behaviour=(Behaviour)component;bool enabled=behaviour.enabled;return ()=>behaviour.enabled=enabled;
    }
    private static void Hide(Component component)
    {
            if(component is IPNWorldPresentationPart part)part.PresentationVisible=false;
            else if(component is Renderer r)r.forceRenderingOff=true;
            else if(component is AudioSource audio)audio.mute=true;
            else if(component is Volume volume)volume.weight=0;
            else if(component is UIDocument document)document.rootVisualElement.style.display=DisplayStyle.None;
            else ((Behaviour)component).enabled=false;
    }
    private void LateUpdate()
    {
        if(local?.Actor!=null&&!local.Actor.IsReady)local.Select(null);
        foreach(var world in session.Worlds.Snapshot().Where(x=>x.State==EveWorldState.Loading&&x.PhysicsOwner.IsValid()))
            foreach(var root in world.PhysicsOwner.GetRootGameObjects())if(root.GetComponent<PNWorldPresentationRoot>()?.Owner!=this)Register(root);
        foreach(var component in hidden.ToArray())if(component!=null)Hide(component);else{hidden.Remove(component);restore.Remove(component);}
        foreach(var root in roots.ToArray())if(root==null)roots.Remove(root);else if(root.Dirty){root.Dirty=false;root.Scan();}
    }
    public void Release()
    {
        if(local!=null)local.Changed-=Refresh;if(worlds!=null){worlds.WorldReady-=RegisterWorld;worlds.WorldUnloading-=Unloading;}
        registration?.Dispose();registration=null;
        foreach(var pair in restore)if(pair.Key!=null&&hidden.Contains(pair.Key))pair.Value();restore.Clear();hidden.Clear();staging.Clear();foreach(var root in roots)if(root!=null)root.Owner=null;roots.Clear();initialEnvironment?.Apply();initialEnvironment=null;if(session!=null)services.Remove(session);session=null;worlds=null;local=null;
    }
    private void OnDestroy()=>Release();
}
public sealed class PNWorldPresentationRoot : MonoBehaviour
{
    internal PNWorldPresentation Owner;
    internal bool Dirty;
    public void RefreshRegistration()=>Scan();
    internal void Scan(){Owner?.Scan(this);foreach(var child in GetComponentsInChildren<Transform>(true)){if(child==transform)continue;var watcher=child.GetComponent<PNWorldPresentationRoot>()??child.gameObject.AddComponent<PNWorldPresentationRoot>();watcher.Owner=Owner;}}
    private void OnTransformChildrenChanged(){Dirty=true;var root=transform.root.GetComponent<PNWorldPresentationRoot>();if(root!=null)root.Dirty=true;}
}
#endif
