#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;

public static class PNWorldPresentationChecks
{
    public static string Run()
    {
        var rows=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);rows.Add("PASS "+label);}
        var docs=new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNWorldFixtureModule()},docs);
        EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"fixture:presentation"));
        var session=EveRegistrySession.Context;var scheduler=new EveEntityPreparationScheduler(session,new EvePreparationProfile());var worlds=new PNWorldRuntime(session,scheduler);
        var local=new PNLocalWorldContext(session);var host=new GameObject("Presentation fixture service");var view=host.AddComponent<PNWorldPresentation>();view.Bind(session,worlds,local);
        void Pump(Func<bool> ready){for(int i=0;i<1000&&!ready();i++)scheduler.AdvanceFrame();if(!ready())throw new Exception("Presentation fixture did not drain.");}
        try
        {
            var a=worlds.Create(EveId.Parse("fixture:world"));var b=worlds.Create(EveId.Parse("fixture:world"));Pump(()=>a.CanExecute&&b.CanExecute);
            var ar=worlds.Get(a).Slots[0].Roots[0];var br=worlds.Get(b).Slots[0].Roots[0];
            var al=ar.AddComponent<Light>();var bl=br.AddComponent<Light>();var aa=ar.AddComponent<AudioSource>();var ba=br.AddComponent<AudioSource>();var av=ar.AddComponent<Volume>();var bv=br.AddComponent<Volume>();
            var ac=ar.AddComponent<Camera>();var bc=br.AddComponent<Camera>();var ab=ar.GetComponentsInChildren<Renderer>().First();var bb=br.GetComponentsInChildren<Renderer>().First();
            view.Register(ar);view.Register(br);local.Select(a);
            Check(!ab.forceRenderingOff&&bb.forceRenderingOff&&al.enabled&&!bl.enabled,"renderer/shadow and light gates isolate local world A");
            Check(!aa.mute&&ba.mute&&av.weight==1&&bv.weight==0&&ac.enabled&&!bc.enabled,"audio, volume and camera gates use the same world scope");
            b.AdvanceClock(.5);Check(b.GameplayTime==.5&&br.activeSelf,"hidden world keeps gameplay root and simulation clock active");
            local.Select(b);Check(ab.forceRenderingOff&&!bb.forceRenderingOff&&!al.enabled&&bl.enabled&&aa.mute&&!ba.mute,"view switch restores B and hides A without disabling gameplay");
            var spawned=GameObject.CreatePrimitive(PrimitiveType.Cube);spawned.transform.SetParent(ar.transform);view.Register(ar);Check(spawned.GetComponent<Renderer>().forceRenderingOff,"explicit dynamic presentation registration inherits hidden scope");
            var independent=GameObject.CreatePrimitive(PrimitiveType.Sphere);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(independent,a.PhysicsOwner);PNWorldPresentationRegistration.Register(a,independent);Check(independent.GetComponent<Renderer>().forceRenderingOff,"independent area/popup roots inherit scope through the Core registration seam");
            Check(local.Contains(bc)&&!local.Contains(ac),"local effect/camera event context rejects other-world senders");
            view.Release();Check(!ab.forceRenderingOff&&!bb.forceRenderingOff&&al.enabled&&bl.enabled&&!aa.mute&&!ba.mute&&ac.enabled&&bc.enabled,"presentation teardown restores owned render/audio/camera flags");
            Pump(()=>worlds.Unload(a));Pump(()=>worlds.Unload(b));Check(session.Worlds.Snapshot().Count==0,"presentation ownership does not pin world unload");
        }
        finally{view.Release();UnityEngine.Object.DestroyImmediate(host);EveRegistrySession.End();}
        return "PASS "+rows.Count+" native world presentation cases (gates, not rendered-frame/audio acceptance)\n"+string.Join("\n",rows);
    }
}
#endif
