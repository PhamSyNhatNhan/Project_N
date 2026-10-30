#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class PNStarweaverEditorChecks
{
    public static string RunConstellation()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Idle Editor required");
        var ink=PNVisualPaperSettings.Ink;var mode=PNVisualPaperSettings.Mode;
        string output=Application.dataPath+"/../.utmp/StarweaverImpactV6";Directory.CreateDirectory(output);
        int baseline=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starweaver effect "));
        try
        {
            using(var rig=new PNVisualBakeCheck.Rig(true,1,960,640))
            {
                rig.Camera.orthographicSize=1.95f;rig.Camera.transform.position=new Vector3(0,4.7f,-4.5f);rig.Camera.transform.LookAt(new Vector3(0,.2f,0));
                rig.Camera.backgroundColor=new Color(.09f,.105f,.13f,1);
                var actor=rig.Actors[0];actor.HideStrings();var facing=actor.GetComponent<PNCameraFacingTarget>();facing.FootAnchor=new Vector3(-1.55f,0,0);facing.Align();
                var floor=new GameObject("Preview floor");SceneManager.MoveGameObjectToScene(floor,rig.Scene);
                var mesh=new Mesh{vertices=new[]{new Vector3(-6,-.01f,-6),new Vector3(6,-.01f,-6),new Vector3(6,-.01f,6),new Vector3(-6,-.01f,6)},triangles=new[]{0,1,2,0,2,3},colors=Enumerable.Repeat(new Color(.19f,.22f,.20f,1),4).ToArray()};mesh.RecalculateBounds();
                var mat=new Material(Resources.Load<Shader>("PNStarweaverEffects")){renderQueue=2000};floor.AddComponent<MeshFilter>().sharedMesh=mesh;floor.AddComponent<MeshRenderer>().sharedMaterial=mat;
                try
                {
                    using(var fx=new PNStarweaverTelegraph(Vector3.zero,1.2f,rig.Scene,2f,rig.Camera))
                    {
                        float[] times={.12f,.65f,1.35f,1.65f,1.9f,2.08f,2.22f,2.65f,3.17f};
                        int frame=0;
                        foreach(float t in times)
                        {
                            fx.SetPhase(t/2f,t>=2f);
                            if(frame<3&&fx.VisibleStars!=3)throw new Exception("Small stars must appear together");
                            var effectRoot=rig.Scene.GetRootGameObjects().First(g=>g.name=="Starweaver constellation");
                            var centralStar=effectRoot.transform.Find("Central star");
                            var smallStar=effectRoot.transform.Find("Star 1");
                            if(t==.65f && (centralStar.localPosition.y<=smallStar.localPosition.y || centralStar.localScale.x<=smallStar.localScale.x))throw new Exception("Central star must be higher and larger");
                            if(t==1.65f && (Vector3.Distance(smallStar.localPosition,centralStar.localPosition)>1 || smallStar.localPosition.z>=0))throw new Exception("Small star did not fly toward the centre");
                            if(t==1.65f)
                            {
                                fx.SetPhase(1.74f/2,false);
                                if(effectRoot.transform.Find("Star 1").gameObject.activeSelf||!effectRoot.transform.Find("Star 2").gameObject.activeSelf||!effectRoot.transform.Find("Star 3").gameObject.activeSelf)throw new Exception("First arrival must precede the other two stars");
                                fx.SetPhase(1.82f/2,false);
                                if(effectRoot.transform.Find("Star 2").gameObject.activeSelf||!effectRoot.transform.Find("Star 3").gameObject.activeSelf)throw new Exception("Second arrival must precede the third star");
                            }
                            if(t==1.9f && (smallStar.gameObject.activeSelf || !centralStar.gameObject.activeSelf))throw new Exception("Merge must finish before central detonation");
                            if(t==.65f)
                            {
                                if(Vector3.Distance(smallStar.localPosition,new Vector3(0,.25f,1.2f))>.001f)throw new Exception("Small node left its floor vertex");
                                var head=effectRoot.transform.Find("Intake head 0");var line=effectRoot.transform.Find("Gold packet 0").GetComponent<LineRenderer>();
                                float oldHead=Vector3.Distance(head.localPosition,centralStar.localPosition),oldPacket=Vector3.Distance(line.GetPosition(8),centralStar.localPosition);
                                fx.SetPhase(.7f/2,false);
                                if(Vector3.Distance(head.localPosition,centralStar.localPosition)>=oldHead||Vector3.Distance(line.GetPosition(8),centralStar.localPosition)>=oldPacket)throw new Exception("Energy did not travel INWARD");
                                var frozen=head.localPosition;for(int j=0;j<60;j++)fx.SetPhase(.7f/2,false);if(head.localPosition!=frozen)throw new Exception("Intake moved on frozen clock");
                            }
                            if(t==2.08f)
                            {
                                if(fx.ParticleCount==0)throw new Exception("Burst particles missing");
                                if(Vector3.Distance(effectRoot.transform.Find("Rising constellation burst").localPosition,centralStar.localPosition)>.001f)throw new Exception("Explosion did not originate at the charged star");
                                if(effectRoot.transform.Find("Shattered star 0").GetComponentsInChildren<MeshRenderer>(true).Length!=4)throw new Exception("Expected four large crystal wings");
                            }
                            Shot((frame++).ToString("D2"));
                            if(t==2.22f)
                            {
                                var root=rig.Scene.GetRootGameObjects().First(g=>g.name=="Starweaver constellation");
                                var shock=root.transform.Find("Outward shockwave");
                                if(Quaternion.Angle(shock.localRotation,Quaternion.Euler(90,0,0))>.01f)throw new Exception("Shockwave rotated");
                                if(root.transform.Find("Star 1").gameObject.activeSelf)throw new Exception("Intact star survived the shatter pulse");
                                var fragment=root.transform.Find("Shattered star 0/Facet plane/Broken facet 0");
                                if(!fragment.gameObject.activeInHierarchy||fragment.localPosition.magnitude<.1f)throw new Exception("Facets did not break outward");
                                int frozenCount=fx.ParticleCount;var frozenPosition=fragment.localPosition;
                                for(int i=0;i<60;i++)fx.SetPhase(t/2f,true);
                                if(frozenCount!=fx.ParticleCount||fragment.localPosition!=frozenPosition)throw new Exception("Frozen clock advanced shards/particles");
                                var data=rig.Camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();data.SetRenderer(0);Shot("09_renderer2d");data.SetRenderer(1);
                            }
                        }
                        int particles=fx.ParticleCount;for(int i=0;i<60;i++)fx.SetPhase(times[times.Length-1]/2f,true);
                        if(particles!=fx.ParticleCount)throw new Exception("Particles advanced on frozen action clock");
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(floor);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(mat);}
                void Shot(string name)
                {
                    rig.Camera.Render();var previous=RenderTexture.active;RenderTexture.active=rig.Target;
                    var image=new Texture2D(960,640,TextureFormat.RGBA32,false);
                    try{image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());}
                    finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
                }
            }
            if(Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starweaver effect "))!=baseline)throw new Exception("Cast material leaked");
            string report="PASS vertex nodes, inward purple intake/gold packets, frozen intake clock, staggered spiral merge, raised explosion, four thick shards, cleanup; 10 rendered phases (forward + 2D) at "+output;
            File.WriteAllText(output+"/native.txt",report);return report;
        }
        finally{PNVisualPaperSettings.Ink=ink;PNVisualPaperSettings.Mode=mode;PNVisualRuntime.Release();}
    }
    public static string RunMuzzle()
    {
        if(EditorApplication.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Stopped Editor required");
        var scene=EditorSceneManager.NewPreviewScene();PNProjectileManager manager=null;GameObject go=null;bool began=false;
        try
        {
            var docs=new PNGameContentSource(Application.streamingAssetsPath+"/BaseContent","unused",Array.Empty<string>()).ReadDocuments().ToArray();
            var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
            EveRegistrySession.Begin();began=true;EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:starweaver-muzzle"));
            var mg=new GameObject("Muzzle projectile manager");SceneManager.MoveGameObjectToScene(mg,scene);manager=mg.AddComponent<PNProjectileManager>();manager.Initialize(EveRegistrySession.Context);
            go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starweaver/Prefab/Starweaver.prefab"));go.SetActive(false);SceneManager.MoveGameObjectToScene(go,scene);
            go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(978111),EveId.Parse("project_n:starweaver"),0);go.GetComponent<EveEntityLoader>().Load(hub);go.SetActive(true);
            PNVisualPacks.Set("project_n",Application.streamingAssetsPath+"/BaseContent");go.GetComponent<PNVisualBody>().Begin(hub.Characters.Get(EveId.Parse("project_n:starweaver")),hub);
            var skill=go.GetComponent<PNStarweaverSkill>();var weapon=go.GetComponent<PNStarweaverPresentation>();var direction=Vector3.right;
            var action=skill.CreateAction(PNStarweaverCombatContent.Weave,out var context,Array.Empty<PNStat>(),direction,new Vector3(9,9,9));
            try
            {
                action.Start(context);action.AdvanceAuxiliary(.38f);weapon.TickWeapon(0);var rear=weapon.TipPosition;
                if(manager.ActiveCount!=0)throw new Exception("Weave fired before registered windup");
                action.AdvanceAuxiliary(.12f);var muzzle=weapon.TipPosition;
                if(Vector3.Dot(muzzle-rear,direction)<.12f)throw new Exception("Needle did not thrust after drawing back");
                var shots=manager.Snapshot();if(shots.Count!=3)throw new Exception("Muzzle volley did not contain three shots");
                for(int i=0;i<3;i++)
                {
                    if(Vector3.Distance(shots[i].Position,muzzle)>.00001f)throw new Exception("Projectile did not originate at live needle tip");
                    if(Mathf.Abs(Vector3.Angle(shots[i].Forward,direction)-(i==1?0:18))>.01f)throw new Exception("Muzzle changed existing fan spread");
                    manager.End(shots[i].Handle,PNProjectileEndReason.Requested);
                }
                action.Exit(context);
            }
            finally{action.Dispose();}
            var cancelled=skill.CreateAction(PNStarweaverCombatContent.Weave,out var cc,Array.Empty<PNStat>(),direction,Vector3.zero);
            try{cancelled.Start(cc);cancelled.AdvanceAuxiliary(.25f);cancelled.Cancel(cc);if(manager.ActiveCount!=0)throw new Exception("Cancelled windup emitted projectiles");}finally{cancelled.Dispose();}
            string report="PASS registered volley at live needle tip, no shot before .5s, backward/thrust travel, three unchanged fan headings, cancel before release";
            Directory.CreateDirectory(Application.dataPath+"/../.utmp/StarweaverMuzzleV2");File.WriteAllText(Application.dataPath+"/../.utmp/StarweaverMuzzleV2/muzzle-native.txt",report);return report;
        }
        finally{if(manager!=null)manager.Shutdown();try{if(began)EveRegistrySession.End();}finally{if(go!=null)UnityEngine.Object.DestroyImmediate(go);EditorSceneManager.ClosePreviewScene(scene);PNVisualRuntime.Release();}}
    }
    public static string RunCaster()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Idle Editor required");
        string output=Application.dataPath+"/../.utmp/StarweaverMuzzleV2";Directory.CreateDirectory(output);
        var ink=PNVisualPaperSettings.Ink;var mode=PNVisualPaperSettings.Mode;
        int baseline=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starweaver cast "));
        try
        {
            using(var rig=new PNVisualBakeCheck.Rig(true,0,640,640))
            {
                var docs=new PNGameContentSource(Application.streamingAssetsPath+"/BaseContent","unused",Array.Empty<string>()).ReadDocuments().ToArray();
                var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
                var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starweaver/Prefab/Starweaver.prefab"));go.SetActive(false);SceneManager.MoveGameObjectToScene(go,rig.Scene);
                foreach(var b in go.GetComponents<MonoBehaviour>())b.enabled=false;go.SetActive(true);
                var body=go.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveId.Parse("project_n:starweaver")),rig.Hub);var actor=body.Actor;actor.HideStrings();
                var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=rig.Camera;facing.Align();
                rig.Camera.orthographicSize=.43f;rig.Camera.transform.position=new Vector3(.05f,.26f,-5);rig.Camera.backgroundColor=new Color(.24f,.30f,.27f,1);
                var presentation=go.GetComponent<PNStarweaverPresentation>();var needle=go.transform.Find("Golden needle");
                try
                {
                    foreach(bool triangle in new[]{false,true})
                    {
                        float windup=triangle?2:.5f;actor.Play(triangle?"cast_constellation":"cast_weave",true);
                        using(var fx=PNStarweaverCastVisual.Create(go.transform,presentation,actor,triangle,windup,Vector3.right,rig.Camera))
                        {
                            int frame=0;foreach(float t in triangle?new[]{.25f,1f,1.73f,1.81f,1.89f,2.02f,2.35f}:new[]{.1f,.38f,.52f,.7f})
                            {
                                actor.SetPhase(t/(windup+(triangle?1.2f:.35f)));actor.Tick(0);fx.SetTime(t);presentation.TickWeapon(0);fx.SetTime(t);
                                var mote=fx.transform.Find("Gathering mote 0");var frozen=mote.position;for(int i=0;i<60;i++){fx.SetTime(t);presentation.TickWeapon(0);}
                                if(Vector3.Distance(mote.position,frozen)>.00001f)throw new Exception("Caster particles drifted on frozen Action age");
                                if(!triangle&&t==.52f&&Vector3.Dot(-needle.up,Vector3.right)<.5f)throw new Exception("Needle did not aim toward the shot");
                                Shot((triangle?"skill2_":"skill1_")+(frame++).ToString("D2"));
                            }
                            var data=rig.Camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();data.SetRenderer(1);fx.SetTime(windup+.02f);Shot(triangle?"skill2_forward":"skill1_forward");data.SetRenderer(0);
                        }
                        presentation.TickWeapon(0);
                    }
                }
                finally{body.Clear();UnityEngine.Object.DestroyImmediate(go);}
                void Shot(string name)
                {
                    rig.Camera.Render();var prev=RenderTexture.active;RenderTexture.active=rig.Target;var image=new Texture2D(640,640,TextureFormat.RGBA32,false);
                    try{image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());}finally{RenderTexture.active=prev;UnityEngine.Object.DestroyImmediate(image);}
                }
            }
            if(Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starweaver cast "))!=baseline)throw new Exception("Caster materials leaked after completion/dispose");
            string report="PASS real puppet+weapon cast aim, held Action-age motes, both skills/renderers, dispose cleanup; 13 captures at "+output;File.WriteAllText(output+"/native.txt",report);return report;
        }
        finally{PNVisualPaperSettings.Ink=ink;PNVisualPaperSettings.Mode=mode;PNVisualRuntime.Release();}
    }
    public static string FireNeedleReview()
    {
        var enemy=UnityEngine.Object.FindAnyObjectByType<PNStarweaverSkill>();var player=UnityEngine.Object.FindAnyObjectByType<PNPlayerStat>();
        var manager=PNProjectileManager.Resolve(EveRegistrySession.Context,enemy.gameObject.scene);var source=PNCombatSource.Capture(enemy.GetComponent<PNStat>());
        var damage=source.Snapshot.PrepareDamage(150,true,0,new[]{EveId.Parse("base:magic")});var point=player.GetComponent<Collider>().bounds.center;point.x-=2;
        for(int i=0;i<3;i++)
        {
            var launch=new PNProjectileLaunch(PNStarweaverCombatContent.Bolt,null,source,PNTargetLayers.Of(PNLayers.Player),damage);
            var receipt=manager.Create(launch,new PNProjectileSpawn(enemy.gameObject.scene,point,Quaternion.AngleAxis(-9+i*9,Vector3.up)*Vector3.right));
            if(receipt.Status!=PNProjectileStatus.Created)throw new Exception("Needle review creation "+receipt.Status+" "+receipt.Error);
        }
        return "Created three registered review needles, targeted at player collider height";
    }
    public static string RunNeedles()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Idle Editor required");
        string output=Application.dataPath+"/../.utmp/StarweaverMuzzleV2/Needles";Directory.CreateDirectory(output);
        var ink=PNVisualPaperSettings.Ink;var mode=PNVisualPaperSettings.Mode;
        int baseline=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starweaver needle "));
        try
        {
            using(var rig=new PNVisualBakeCheck.Rig(true,1,960,640))
            {
                rig.Camera.orthographicSize=1.45f;rig.Camera.transform.position=new Vector3(0,4.7f,-4.5f);rig.Camera.transform.LookAt(new Vector3(0,.2f,0));rig.Camera.backgroundColor=new Color(.34f,.4f,.37f,1);
                rig.Actors[0].gameObject.SetActive(false);
                var visuals=new PNStarweaverNeedleVisual[3];var origin=new Vector3(-1,.15f,-.3f);var directions=new Vector3[3];
                try
                {
                    for(int i=0;i<3;i++){directions[i]=Quaternion.AngleAxis(-9+i*9,Vector3.up)*Vector3.right;visuals[i]=PNStarweaverNeedleVisual.Preview(rig.Scene,rig.Camera);visuals[i].Render(origin,directions[i],0);}
                    int f=0;
                    foreach(float t in new[]{.04f,.16f,.32f})
                    {
                        for(int i=0;i<3;i++)visuals[i].Render(origin+directions[i]*(5*t),directions[i],t);
                        if(Vector3.Distance(visuals[0].transform.Find("Golden needle eye").position,origin+directions[0]*(5*t))>.00001f)throw new Exception("Needle head was lifted away from projectile muzzle");
                        var line=visuals[0].transform.Find("Woven gold filament 0").GetComponent<LineRenderer>();var frozen=line.GetPosition(20);
                        for(int j=0;j<60;j++)visuals[0].Render(origin+directions[0]*(5*t),directions[0],t);
                        if(line.GetPosition(20)!=frozen)throw new Exception("Needle threads moved on frozen projectile age");
                        Shot((f++).ToString("D2"));
                    }
                    var data=rig.Camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();data.SetRenderer(0);Shot("03_renderer2d");data.SetRenderer(1);
                    var strand=visuals[0].transform.Find("Woven gold filament 0").GetComponent<LineRenderer>();
                    var fixedPoint=origin+directions[0]*1.6f;
                    visuals[0].Render(fixedPoint,directions[0],.18f);var heldBend=strand.GetPosition(20);
                    visuals[0].Render(fixedPoint,directions[0],.20f);
                    if(strand.GetPosition(20)==heldBend)throw new Exception("Strand shape remained rigid while its root was held");
                    visuals[0].Render(fixedPoint,directions[0],.42f);
                    if(strand.gameObject.activeSelf)throw new Exception("Gold strand did not disappear between births");
                    visuals[0].Render(fixedPoint,directions[0],.55f);
                    if(!strand.gameObject.activeSelf)throw new Exception("Gold strand did not reappear");
                    float shortLength=Vector3.Distance(strand.GetPosition(0),strand.GetPosition(39));
                    visuals[0].Render(fixedPoint,directions[0],.65f);
                    if(Vector3.Distance(strand.GetPosition(0),strand.GetPosition(39))<=shortLength)throw new Exception("Reborn strand did not extend");
                    if(visuals[0].transform.Find("Violet energy wake").GetComponent<LineRenderer>().widthMultiplier<.3f)throw new Exception("Violet-black wake too narrow");
                    for(int i=0;i<3;i++){visuals[i].Dispose();visuals[i]=null;}
                    var impact=PNStarweaverNeedleVisual.Preview(rig.Scene,rig.Camera,true);
                    try{f=4;foreach(float t in new[]{.025f,.12f,.28f}){impact.Render(Vector3.up*.15f,Vector3.right,t);Shot((f++).ToString("D2"));}}finally{impact.Dispose();}
                }
                finally{foreach(var v in visuals)if(v!=null)v.Dispose();}
                void Shot(string name)
                {
                    rig.Camera.Render();var prev=RenderTexture.active;RenderTexture.active=rig.Target;var image=new Texture2D(960,640,TextureFormat.RGBA32,false);
                    try{image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());}finally{RenderTexture.active=prev;UnityEngine.Object.DestroyImmediate(image);}
                }
            }
            if(Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starweaver needle "))!=baseline)throw new Exception("Needle materials leaked");
            string report="PASS wider needle wake, soft travelling bend, strand disappearance/rebirth/extension, frozen projectile-age threads, both renderers, contact phases and resource disposal; 7 captures at "+output;File.WriteAllText(output+"/native.txt",report);return report;
        }
        finally{PNVisualPaperSettings.Ink=ink;PNVisualPaperSettings.Mode=mode;PNVisualRuntime.Release();}
    }
    public static string RunWeapon()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit preview required");
        var ink=PNVisualPaperSettings.Ink;var mode=PNVisualPaperSettings.Mode;
        string output=Application.dataPath+"/../.utmp/StarweaverWeaponHover";Directory.CreateDirectory(output);
        try
        {
            using(var rig=new PNVisualBakeCheck.Rig(true,0,640,640))
            {
                var docs=new PNGameContentSource(Application.streamingAssetsPath+"/BaseContent","unused",Array.Empty<string>()).ReadDocuments().ToArray();
                var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starweaver/Prefab/Starweaver.prefab");
                var go=UnityEngine.Object.Instantiate(prefab);go.SetActive(false);SceneManager.MoveGameObjectToScene(go,rig.Scene);
                // No AI/loader in this render fixture; only build the real prefab's puppet and weapon.
                foreach(var b in go.GetComponents<MonoBehaviour>())b.enabled=false;
                go.SetActive(true);
                var body=go.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveId.Parse("project_n:starweaver")),rig.Hub);
                var actor=body.Actor;actor.HideStrings();actor.Play("idle",true);actor.Tick(.4f);
                var face=actor.GetComponent<PNCameraFacingTarget>();face.ViewCamera=rig.Camera;face.Align();
                rig.Camera.orthographicSize=.43f;rig.Camera.transform.position=new Vector3(.05f,.26f,-5);
                var presentation=go.GetComponent<PNStarweaverPresentation>();var weapon=go.transform.Find("Golden needle");
                presentation.TickWeapon(0);var p0=weapon.position;var r0=weapon.rotation;
                Shot("00_idle");presentation.TickWeapon(.7f);var p1=weapon.position;
                if(Vector3.Distance(p0,p1)<.015f||Quaternion.Angle(r0,weapon.rotation)<1f)throw new Exception("Needle still rigid at idle");
                Shot("01_rise");var paused=weapon.position;var pausedRotation=weapon.rotation;
                for(int i=0;i<60;i++)presentation.TickWeapon(0);
                if(weapon.position!=paused||Quaternion.Angle(weapon.rotation,pausedRotation)>.001f)throw new Exception("Needle drifted on frozen clock");
                presentation.TickWeapon(1.4f);Shot("02_fall");
                actor.SetFacingLeft(true);actor.Tick(0);presentation.TickWeapon(0);
                if(actor.transform.InverseTransformPoint(weapon.position).x>=0)throw new Exception("Needle did not follow facing");Shot("03_left");
                body.Clear();UnityEngine.Object.DestroyImmediate(go);
                return "PASS real prefab: idle translation/rock, frozen-clock stability, facing; rendered 4 phases to "+output;

                void Shot(string name)
                {
                    rig.Camera.Render();var previous=RenderTexture.active;RenderTexture.active=rig.Target;
                    var image=new Texture2D(640,640,TextureFormat.RGBA32,false);
                    try{image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());}
                    finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
                }
            }
        }
        finally{PNVisualPaperSettings.Ink=ink;PNVisualPaperSettings.Mode=mode;PNVisualRuntime.Release();}
    }
    public static string Run()
    {
        if(EditorApplication.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Idle Editor required");
        var scene=EditorSceneManager.NewPreviewScene();bool started=false;int n=0;
        void Check(bool ok){if(!ok)throw new Exception("Starweaver check "+n);n++;}
        try
        {
            var docs=new PNGameContentSource(Application.streamingAssetsPath+"/BaseContent","unused",Array.Empty<string>()).ReadDocuments().ToArray();
            var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
            EveRegistrySession.Begin();started=true;EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:starweaver"));
            var timing=(Newtonsoft.Json.Linq.JToken)hub.Get<EveSkillDefinition>().Get(PNStarweaverCombatContent.Triangle).Data;
            Check((double)timing["action"]["perform"]["windup"]==2 && (double)timing["action"]["perform"]["recovery"]==1.2);
            var go=new GameObject("Starweaver stat probe");go.SetActive(false);SceneManager.MoveGameObjectToScene(go,scene);
            go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(978100),EveId.Parse("project_n:starweaver"),0);
            var stat=go.AddComponent<PNEnemyStat>();
            go.AddComponent<Rigidbody>();
            var capsule=go.AddComponent<CapsuleCollider>();capsule.radius=.2f;capsule.height=1.4f;capsule.center=Vector3.up*.7f;
            var movement=go.AddComponent<PNEnemyMove>();
            go.AddComponent<EveEntityLoader>().Load(hub);
            Check(movement.IsReady && movement.CurrentJumpSpeed==3 && !movement.CanJump);
            Check(stat.IsReady&&stat.GetCurrent(PNContentIds.Health)==2000);
            Check(stat.GetCurrent(PNContentIds.Lorica)==500);
            Check(stat.Evaluate(PNContentIds.AttackPower)==100&&stat.Evaluate(EveId.Parse("base:move_speed"))==1);
            Check(stat.Evaluate(PNContentIds.CritRate)==10&&stat.Evaluate(PNContentIds.CritDamage)==150);
            var source=stat.CaptureCombatSource();var prepared=source.PrepareDamage(150,true,0,EveId.Parse("base:magic"));
            Check(source.CreateDamage(prepared,false).Amount==150);Check(source.CreateDamage(prepared,true).Amount==225);
            prepared=source.PrepareDamage(250,true,0,EveId.Parse("base:magic"));Check(source.CreateDamage(prepared,true).Amount==375);
            Check(PNStarweaverTriangleGeometry.Intersects(0,0,1.2f,-.05f,-.05f,.05f,.05f));
            Check(!PNStarweaverTriangleGeometry.Intersects(0,0,1.2f,2,2,3,3));
            Check(PNStarweaverTriangleGeometry.Intersects(0,0,1.2f,-.01f,1.19f,.01f,1.3f));
            var prefab=Resources.Load<PNEntityPrefabCatalog>(PNEntityPrefabCatalog.ResourcePath).Find("starweaver");
            Check(prefab!=null&&prefab.GetComponent<PNStarweaverBehavior>()!=null&&prefab.GetComponent<PNVisualBody>()!=null);
            return "PASS "+n+" Starweaver native content/Move-load/no-jump/stat/crit/geometry/prefab checks; actual AI combat not covered";
        }
        finally{try{if(started)EveRegistrySession.End();}finally{EditorSceneManager.ClosePreviewScene(scene);}}
    }
}
#endif

