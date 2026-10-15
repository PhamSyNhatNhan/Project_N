using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// EN: Opt-in standalone collision experiment, not a production Projectile API.
// VI: Thử nghiệm collision Player tường minh, không phải API Projectile sản phẩm.
public sealed class PNProjectileCollisionHitTarget : MonoBehaviour { public int Id; }
public sealed class PNProjectileCollisionBody : MonoBehaviour, EveIPoolable
{
    public PNProjectileCollisionPlayerBenchmark.World Owner;
    public int Index;
    public SphereCollider Shape;
    public Transform Tail;
    public void OnPoolCreated() { Shape=GetComponent<SphereCollider>();Tail=transform.Find("Tail"); }
    public void OnPoolGet(EveEasyPooling p) { Owner=null;if(Shape!=null)Shape.enabled=false; }
    public void OnPoolRelease() { Owner=null;if(Shape!=null)Shape.enabled=false; }
    private void OnTriggerEnter(Collider other) { Owner?.Trigger(Index,other); }
    private void OnTriggerStay(Collider other) { if(Owner!=null && Owner.IncludeStayCallbacks)Owner.Trigger(Index,other); }
}

public sealed class PNProjectileCollisionPlayerBenchmark : MonoBehaviour
{
    public const int ModelSteps=16;
    public const float Dt=1f/60f;
    public const int TargetLayer=30,WallLayer=29,BulletLayer=31;
    public enum Method { Trigger, Overlap, Sweep }
    public enum Pattern { Player90Homing, BossSin }
    private static readonly double TickMs=1000d/Stopwatch.Frequency;
    private const int LifeSlots=8;
    private const int Samples=120,Warmup=30;
    [Serializable] public sealed class EnvironmentData
    {
        public string unity=Application.unityVersion,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,graphics=SystemInfo.graphicsDeviceType.ToString();
        public bool editor=Application.isEditor,development=UnityEngine.Debug.isDebugBuild,batch=Application.isBatchMode;
        public int memoryMb=SystemInfo.systemMemorySize,samples=Samples,warmup=Warmup,modelSteps=ModelSteps;
        public string caveats="Mono normal Player; empty fixture scene; collision-only synthetic workload. Fixed simulation1/60 per Update; uncapped frame rate. Shared16-piece motion plan is timed, reference oracle excluded. No production combat/stat/AI/UI/network cost. VFX is two identical glow quads. Overlap/trigger are discrete samples; native sweep queries moving targets at endpoint pose. Hit masks are target-life contacts, not HP commits.";
    }
    public sealed class State
    {
        public readonly int Id;
        public readonly Vector3[] Path=new Vector3[ModelSteps+1];
        public Vector3 Position,Origin,Direction;
        public float Age,Life,Speed,Radius;
        public int Generation;
        private readonly int lifeCapacity;
        public State(int id,int lifeCapacity=LifeSlots) { this.lifeCapacity=lifeCapacity;Id=id;Reset(0,false); }
        public void Reset(float time,bool next)
        {
            if(next)Generation++;
            if(Generation>=lifeCapacity)throw new Exception("Life reference buffer capacity exceeded");
            float a=Id*2.399963f+Generation*.21f+time*.1f;
            Position=Origin=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*12;
            Direction=-Origin.normalized;Age=0;Life=.9f+(Id%11)*.08f;
            Speed=Id%20==0?60:14+(Id%7)*2;Radius=Id%10==0?.035f:.12f;
        }
        public void Plan(int frame,Pattern pattern,int targetCount)
        {
            Path[0]=Position;
            bool homing=pattern==Pattern.Player90Homing && Id%10!=0;
            for(int sub=1;sub<=ModelSteps;sub++)
            {
                float scale=Id%23==0 || (Position-new Vector3(-3,0,2)).sqrMagnitude<2.25f?0:1;
                float delta=Dt/ModelSteps*scale;Age+=delta;
                if(homing)
                {
                    Vector3 wanted=(TargetAt(pattern,Id%targetCount,(frame+sub/(float)ModelSteps)*Dt)-Position).normalized;
                    Direction=Vector3.RotateTowards(Direction,wanted,delta*5,0);
                    Position+=Direction*(Speed*delta);
                }
                else
                {
                    Vector3 forward=-Origin.normalized,side=Vector3.Cross(Vector3.up,forward);
                    float frequency=12+(Id%5)*9,amplitude=.8f+(Id%4)*.4f;
                    Position=Origin+forward*(Speed*Age)+side*(amplitude*Mathf.Sin(Age*frequency));
                }
                Path[sub]=Position;
            }
        }
    }
    public static Vector3 TargetAt(Pattern pattern,int id,float time)
    {
        if(pattern==Pattern.BossSin)return new Vector3(Mathf.Sin(time*3)*2,0,Mathf.Cos(time*2.3f)*2);
        float a=id*Mathf.PI*2/8;
        return new Vector3(Mathf.Cos(a)*4+Mathf.Sin(time*4+id)*1.2f,0,Mathf.Sin(a)*4+Mathf.Cos(time*3.7f+id)*1.2f);
    }
    private static double DistanceSegmentSquared(Vector3 a,Vector3 b)
    {
        Vector3 d=b-a;double denom=d.sqrMagnitude;
        double t=denom>1e-12?Math.Max(0,Math.Min(1,-Vector3.Dot(a,d)/denom)):0;
        Vector3 nearest=a+d*(float)t;return nearest.sqrMagnitude;
    }
    private static double Cross(Vector3 a,Vector3 b)=>a.x*b.z-a.z*b.x;
    private static double SegmentDistance(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
    {
        Vector3 ab=b-a,cd=d-c;double denominator=Cross(ab,cd);
        if(Math.Abs(denominator)>1e-12)
        {
            double t=Cross(c-a,cd)/denominator,u=Cross(c-a,ab)/denominator;
            if(t>=0 && t<=1 && u>=0 && u<=1)return 0;
        }
        return Math.Min(Math.Min(DistanceSegmentSquared(a-c,b-c),DistanceSegmentSquared(a-d,b-d)),
            Math.Min(DistanceSegmentSquared(c-a,d-a),DistanceSegmentSquared(c-b,d-b)));
    }
    private static double WallDistance(Vector3 a,Vector3 b)
    {
        bool Inside(Vector3 p)=>Mathf.Abs(p.x)<=30 && p.z>=4.96f && p.z<=5.04f;
        if(Inside(a) || Inside(b))return 0;
        var p0=new Vector3(-30,0,4.96f);var p1=new Vector3(30,0,4.96f);var p2=new Vector3(30,0,5.04f);var p3=new Vector3(-30,0,5.04f);
        return Math.Min(Math.Min(SegmentDistance(a,b,p0,p1),SegmentDistance(a,b,p1,p2)),Math.Min(SegmentDistance(a,b,p2,p3),SegmentDistance(a,b,p3,p0)));
    }
    private static ulong ReferenceContact(State state,int frame,Pattern pattern,int targets)
    {
        ulong mask=0;float radius=state.Radius+.25f;
        for(int sub=1;sub<=ModelSteps;sub++)
        {
            float t0=(frame+(sub-1f)/ModelSteps)*Dt,t1=(frame+sub/(float)ModelSteps)*Dt;
            for(int id=0;id<targets;id++)for(int side=0;side<2;side++)
            {
                Vector3 shift=new Vector3(side==0?-.1f:.1f,0,0);
                var a=state.Path[sub-1]-TargetAt(pattern,id,t0)-shift;
                var b=state.Path[sub]-TargetAt(pattern,id,t1)-shift;
                if(DistanceSegmentSquared(a,b)<=radius*radius)mask|=1UL<<id;
            }
            // EN: Exact planar sphere/box-path distance, including rounded corners/endpoints.
            // VI: Khoảng cách đường cầu/hộp trên mặt phẳng, gồm góc và đầu tường.
            if(WallDistance(state.Path[sub-1],state.Path[sub])<=state.Radius*state.Radius)mask|=1UL<<targets;
        }
        return mask;
    }
    private static ulong[] Reference(Pattern pattern,int count)
    {
        var masks=new ulong[count*LifeSlots];var states=new State[count];for(int i=0;i<count;i++)states[i]=new State(i);
        for(int frame=0;frame<Warmup+Samples;frame++)foreach(var state in states)
        {
            if(state.Age>=state.Life)state.Reset(frame*Dt,true);
            state.Plan(frame,pattern,pattern==Pattern.Player90Homing?8:1);
            masks[state.Generation*count+state.Id]|=ReferenceContact(state,frame,pattern,pattern==Pattern.Player90Homing?8:1);
        }
        return masks;
    }
    private static int Bits(ulong x) { int n=0;while(x!=0){x&=x-1;n++;}return n; }
    public struct Timing
    {
        public double Plan,Place,Physics,Query,Pool,Render,Step,Frame;
        public long Alloc;
        public int Queries,Callbacks,Respawns;
    }
    public sealed class World : IDisposable
    {
        private readonly Scene scene;
        private readonly PhysicsScene physics;
        private readonly Method method;
        private readonly Pattern pattern;
        private readonly int steps,count,targetCount;
        private readonly bool render;
        private readonly State[] states;
        private readonly PNProjectileCollisionBody[] bodies;
        private readonly Transform[] targets;
        private readonly Collider[] overlaps=new Collider[64];
        private readonly RaycastHit[] casts=new RaycastHit[64];
        private readonly GameObject template;
        private readonly EveEasyPooling pool;
        private readonly Material material;
        private readonly Mesh mesh;
        private readonly Camera camera;
        private readonly RenderTexture texture;
        private readonly UniversalRenderPipeline.SingleCameraRequest renderRequest;
        public readonly ulong[] Contacts;
        // Existing collision experiment keeps Enter+Stay. New policy fixture can select Enter only.
        public bool IncludeStayCallbacks = true;
        public int Queries,Callbacks,Respawns;
        private bool disposed;
        public World(Method method,int steps,Pattern pattern,int count,bool render,int serial,int lifeCapacity=LifeSlots)
        {
            this.method=method;this.steps=steps;this.pattern=pattern;this.count=count;this.render=render;
            targetCount=pattern==Pattern.Player90Homing?8:1;
            if(lifeCapacity<LifeSlots || lifeCapacity>4096)throw new ArgumentOutOfRangeException(nameof(lifeCapacity));
            Contacts=new ulong[checked(count*lifeCapacity)];states=new State[count];bodies=new PNProjectileCollisionBody[count];targets=new Transform[targetCount];
            scene=SceneManager.CreateScene("Disposable Collision Player "+serial,new CreateSceneParameters(LocalPhysicsMode.Physics3D));physics=scene.GetPhysicsScene();
            try
            {
                for(int id=0;id<targetCount;id++)
                {
                    var root=Root("Target "+id);root.layer=TargetLayer;targets[id]=root.transform;
                    var rb=root.AddComponent<Rigidbody>();rb.isKinematic=true;rb.useGravity=false;
                    for(int side=0;side<2;side++)
                    {
                        var child=new GameObject("Target subcollider");child.layer=TargetLayer;child.transform.SetParent(root.transform,false);
                        child.AddComponent<PNProjectileCollisionHitTarget>().Id=id;
                        var sphere=child.AddComponent<SphereCollider>();sphere.radius=.25f;sphere.center=new Vector3(side==0?-.1f:.1f,0,0);
                    }
                }
                var wall=Root("Wall");wall.layer=WallLayer;wall.transform.position=new Vector3(0,0,5);wall.AddComponent<PNProjectileCollisionHitTarget>().Id=targetCount;
                wall.AddComponent<BoxCollider>().size=new Vector3(60,2,.08f);
                template=Root("Inactive collision template");template.SetActive(false);template.layer=BulletLayer;template.AddComponent<PNProjectileCollisionBody>();
                if(method==Method.Trigger)
                {
                    var shape=template.AddComponent<SphereCollider>();shape.isTrigger=true;
                    var rb=template.AddComponent<Rigidbody>();rb.useGravity=false;rb.isKinematic=true;rb.collisionDetectionMode=CollisionDetectionMode.Discrete;
                }
                if(render)
                {
                    var shader=Shader.Find("Hidden/ProjectN/ProjectileBenchmark");if(shader==null || !shader.isSupported)throw new Exception("Benchmark shader missing/unsupported in Player");
                    material=new Material(shader){name="Owned collision benchmark glow",enableInstancing=true};
                    mesh=new Mesh{name="Owned collision benchmark quad"};mesh.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(.5f,0,.5f),new Vector3(-.5f,0,.5f)};
                    mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();
                    Visual("Glow",new Vector3(.4f,1,.4f));Visual("Tail",new Vector3(.2f,1,1.1f));
                    camera=Root("Owned collision camera").AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<BulletLayer;camera.orthographic=true;camera.orthographicSize=17;
                    camera.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Base;
                    camera.transform.SetPositionAndRotation(Vector3.up*35,Quaternion.Euler(90,0,0));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.018f,.024f,.035f);
                    texture=new RenderTexture(1280,720,24){name="Owned collision render texture"};texture.Create();camera.targetTexture=texture;
                    renderRequest=new UniversalRenderPipeline.SingleCameraRequest{destination=texture};
                }
                pool=new EveEasyPooling(template,count,count);
                for(int i=0;i<count;i++){states[i]=new State(i,lifeCapacity);Borrow(i);}
                PlaceTargets(0);Physics.SyncTransforms();
            }
            catch{Dispose();throw;}
        }
        private GameObject Root(string name){var root=new GameObject(name);SceneManager.MoveGameObjectToScene(root,scene);return root;}
        private void Visual(string name,Vector3 scale)
        {
            var child=new GameObject(name);child.layer=BulletLayer;child.transform.SetParent(template.transform,false);child.transform.localScale=scale;
            child.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        private void Borrow(int id)
        {
            var go=pool.Get();SceneManager.MoveGameObjectToScene(go,scene);var body=go.GetComponent<PNProjectileCollisionBody>();bodies[id]=body;body.Owner=this;body.Index=id;
            go.transform.position=states[id].Position;if(body.Shape!=null){body.Shape.radius=states[id].Radius;body.Shape.enabled=true;}go.SetActive(true);
        }
        private void PlaceTargets(float time){for(int id=0;id<targetCount;id++)targets[id].position=TargetAt(pattern,id,time);}
        private void Hit(int body,Collider collider)
        {
            var target=collider.GetComponent<PNProjectileCollisionHitTarget>();
            if(target!=null)Contacts[states[body].Generation*count+body]|=1UL<<target.Id;
        }
        public void Trigger(int body,Collider other){Callbacks++;Hit(body,other);}
        private void Overlap(int id,Vector3 at)
        {
            Queries++;int n=physics.OverlapSphere(at,states[id].Radius,overlaps,(1<<TargetLayer)|(1<<WallLayer),QueryTriggerInteraction.Ignore);
            if(n==overlaps.Length)throw new Exception("Overlap buffer saturated");
            for(int j=0;j<n;j++){Hit(id,overlaps[j]);overlaps[j]=null;}
        }
        public Timing Tick(int frame)
        {
            long before=GC.GetAllocatedBytesForCurrentThread(),begin=Stopwatch.GetTimestamp(),t=begin;var result=new Timing();
            for(int id=0;id<count;id++)if(states[id].Age>=states[id].Life)
            {pool.Release(bodies[id].gameObject);states[id].Reset(frame*Dt,true);Borrow(id);Respawns++;}
            result.Pool=(Stopwatch.GetTimestamp()-t)*TickMs;t=Stopwatch.GetTimestamp();
            foreach(var s in states)s.Plan(frame,pattern,targetCount);
            result.Plan=(Stopwatch.GetTimestamp()-t)*TickMs;
            for(int sub=1;sub<=steps;sub++)
            {
                int end=sub*ModelSteps/steps,start=(sub-1)*ModelSteps/steps;t=Stopwatch.GetTimestamp();
                PlaceTargets((frame+sub/(float)steps)*Dt);
                for(int id=0;id<count;id++)
                {
                    bodies[id].transform.position=states[id].Path[end];
                    if(render && sub==steps)
                    {
                        Vector3 d=states[id].Path[end]-states[id].Path[Math.Max(0,end-1)];if(d.sqrMagnitude>1e-9f)bodies[id].transform.rotation=Quaternion.LookRotation(d,Vector3.up);
                        bodies[id].Tail.localPosition=new Vector3(0,.005f,-.5f);
                    }
                }
                result.Place+=(Stopwatch.GetTimestamp()-t)*TickMs;t=Stopwatch.GetTimestamp();Physics.SyncTransforms();
                if(method==Method.Trigger)physics.Simulate(Dt/steps);
                result.Physics+=(Stopwatch.GetTimestamp()-t)*TickMs;t=Stopwatch.GetTimestamp();
                if(method!=Method.Trigger)for(int id=0;id<count;id++)
                {
                    if(method==Method.Overlap){Overlap(id,states[id].Path[end]);continue;}
                    // EN: Include start overlap: sphere casts do not guarantee initial-overlap hits.
                    // VI: Kiểm tra overlap đầu đoạn; sphere cast không bảo đảm nhận vật đang chồng.
                    Overlap(id,states[id].Path[start]);
                    Vector3 d=states[id].Path[end]-states[id].Path[start];float distance=d.magnitude;
                    if(distance<1e-7f){Overlap(id,states[id].Path[end]);continue;}
                    Queries++;int n=physics.SphereCast(states[id].Path[start],states[id].Radius,d/distance,casts,distance,(1<<TargetLayer)|(1<<WallLayer),QueryTriggerInteraction.Ignore);
                    if(n==casts.Length)throw new Exception("Sweep buffer saturated");
                    for(int j=0;j<n;j++){Hit(id,casts[j].collider);casts[j]=default;}
                }
                result.Query+=(Stopwatch.GetTimestamp()-t)*TickMs;
            }
            t=Stopwatch.GetTimestamp();if(render)Render();result.Render=(Stopwatch.GetTimestamp()-t)*TickMs;
            result.Step=(Stopwatch.GetTimestamp()-begin)*TickMs;result.Alloc=GC.GetAllocatedBytesForCurrentThread()-before;result.Queries=Queries;result.Callbacks=Callbacks;result.Respawns=Respawns;return result;
        }
        public void Render()
        {
            if(GraphicsSettings.currentRenderPipeline==null)camera.Render();
            else RenderPipeline.SubmitRenderRequest(camera,renderRequest);
        }
        public void Capture(string path,bool validate=false)
        {
            if(texture==null)return;var prior=RenderTexture.active;var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
                if(validate && image.GetPixels32().Count(p=>p.b>p.r+20 && p.g>p.r+20)<100)throw new Exception("Offscreen VFX validation failed: no visible projectile glow");
            }
            finally{RenderTexture.active=prior;UnityEngine.Object.Destroy(image);}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;if(camera!=null){camera.enabled=false;camera.targetTexture=null;}
            pool?.Dispose();if(texture!=null){texture.Release();UnityEngine.Object.Destroy(texture);}if(material!=null)UnityEngine.Object.Destroy(material);if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            if(scene.IsValid() && scene.isLoaded)SceneManager.UnloadSceneAsync(scene);
        }
    }
    private string output;
    private World world;
    private IEnumerator sequence;
    private readonly List<string> summaries=new List<string>();
    private bool done;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var args=Environment.GetCommandLineArgs();if(!args.Contains("-pn-projectile-collision-benchmark"))return;
        int pos=Array.IndexOf(args,"-pn-output");if(pos<0 || pos+1>=args.Length)throw new ArgumentException("-pn-output required");
        var root=new GameObject("Disposable Player Collision Suite");var runner=root.AddComponent<PNProjectileCollisionPlayerBenchmark>();runner.output=args[pos+1];runner.Begin();
    }
    private void Begin()
    {
        Directory.CreateDirectory(output);File.WriteAllText(output+"/Result.txt","Running");File.WriteAllText(output+"/Environment.json",JsonUtility.ToJson(new EnvironmentData(),true));
        Application.runInBackground=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        for(int layer=0;layer<32;layer++)Physics.IgnoreLayerCollision(BulletLayer,layer,layer!=TargetLayer && layer!=WallLayer);
        Physics.IgnoreLayerCollision(TargetLayer,TargetLayer,true);Physics.IgnoreLayerCollision(TargetLayer,WallLayer,true);
        sequence=Suite();
    }
    private static double P(Timing[] data,Func<Timing,double> read,double p)
    {var values=data.Select(read).OrderBy(x=>x).ToArray();return values[Math.Min(values.Length-1,(int)Math.Ceiling(p*values.Length)-1)];}
    private static string F(double n)=>n.ToString("0.######",CultureInfo.InvariantCulture);
    private IEnumerator Suite()
    {
        if(Application.isEditor || UnityEngine.Debug.isDebugBuild)throw new Exception("Normal Standalone Player required for release benchmark");
        if(DistanceSegmentSquared(new Vector3(-1,0,.1f),new Vector3(1,0,.1f))>.010001 || WallDistance(new Vector3(0,0,4),new Vector3(0,0,6))!=0 || WallDistance(new Vector3(31,0,4),new Vector3(31,0,6))<.999)
            throw new Exception("Reference geometry self-check failed");
        File.WriteAllText(output+"/OracleChecks.txt","PASS relative moving-sphere segment and box crossing/corner distance self-checks; reference16-piece motion plan");
        summaries.Add("case,count,pattern,method,steps,render,repeat,plan_p50_ms,place_p50_ms,physics_p50_ms,query_p50_ms,pool_p50_ms,render_submit_p50_ms,step_p50_ms,step_p95_ms,step_p99_ms,frame_p50_ms,frame_p95_ms,frame_p99_ms,alloc_p50_bytes,expected,detected,missed,false_positive,queries,callbacks,respawns");
        int serial=0;
        for(int repeat=0;repeat<2;repeat++)foreach(int count in repeat==0?new[]{300,500,1000}:new[]{1000,500,300})foreach(Pattern pattern in Enum.GetValues(typeof(Pattern)))
        {
            File.WriteAllText(output+"/Progress.txt","Reference "+pattern+" "+count);
            var expected=Reference(pattern,count);yield return null;
            foreach(bool render in new[]{false,true})foreach(Method method in repeat==0?new[]{Method.Trigger,Method.Overlap,Method.Sweep}:new[]{Method.Sweep,Method.Overlap,Method.Trigger})foreach(int steps in repeat==0?new[]{1,2,4}:new[]{4,2,1})
            {
                string name=pattern+"-"+count+"-"+method+steps+"-"+(render?"VFX":"CPU")+"-r"+repeat;
                File.WriteAllText(output+"/Progress.txt","Running "+(++serial)+"/216 "+name);
                world=new World(method,steps,pattern,count,render,serial);yield return null;yield return null;
                if(render && serial==10){world.Render();world.Capture(output+"/RenderProbe.png",true);File.WriteAllText(output+"/RenderValidation.txt","PASS visible offscreen VFX via explicit render submission");}
                for(int frame=0;frame<Warmup;frame++){world.Tick(frame);yield return null;}
                var samples=new Timing[Samples];long previous=Stopwatch.GetTimestamp();
                for(int frame=0;frame<Samples;frame++)
                {
                    long now=Stopwatch.GetTimestamp();double elapsed=(now-previous)*TickMs;previous=now;
                    var sample=world.Tick(Warmup+frame);sample.Frame=elapsed;samples[frame]=sample;yield return null;
                }
                int truth=0,hits=0,miss=0,falseHits=0;
                for(int slot=0;slot<expected.Length;slot++){truth+=Bits(expected[slot]);hits+=Bits(world.Contacts[slot]);miss+=Bits(expected[slot]&~world.Contacts[slot]);falseHits+=Bits(world.Contacts[slot]&~expected[slot]);}
                var last=samples[Samples-1];
                summaries.Add(string.Join(",",new[]{name,count.ToString(),pattern.ToString(),method.ToString(),steps.ToString(),render.ToString(),repeat.ToString(),F(P(samples,x=>x.Plan,.5)),F(P(samples,x=>x.Place,.5)),F(P(samples,x=>x.Physics,.5)),F(P(samples,x=>x.Query,.5)),F(P(samples,x=>x.Pool,.5)),F(P(samples,x=>x.Render,.5)),F(P(samples,x=>x.Step,.5)),F(P(samples,x=>x.Step,.95)),F(P(samples,x=>x.Step,.99)),F(P(samples,x=>x.Frame,.5)),F(P(samples,x=>x.Frame,.95)),F(P(samples,x=>x.Frame,.99)),F(P(samples,x=>x.Alloc,.5)),truth.ToString(),hits.ToString(),miss.ToString(),falseHits.ToString(),last.Queries.ToString(),last.Callbacks.ToString(),last.Respawns.ToString()}));
                var raw=new StringBuilder("frame,plan_ms,place_ms,physics_ms,query_ms,pool_ms,render_submit_ms,step_ms,frame_ms,alloc_bytes\n");
                for(int i=0;i<samples.Length;i++){var s=samples[i];raw.Append(i).Append(',').Append(string.Join(",",new[]{F(s.Plan),F(s.Place),F(s.Physics),F(s.Query),F(s.Pool),F(s.Render),F(s.Step),F(s.Frame),s.Alloc.ToString()})).Append('\n');}
                File.WriteAllText(output+"/"+name+".csv",raw.ToString());File.WriteAllLines(output+"/Summary.csv",summaries);
                if(render && count==1000 && repeat==0)world.Capture(output+"/"+name+".png",true);
                world.Dispose();world=null;yield return null;yield return null;
            }
        }
        File.WriteAllText(output+"/Result.txt","PASS: 216 measured cases; "+Samples+" measured frames each; actual trigger callbacks, overlap and sweep; missed/false contacts reported against16-piece moving-target reference");
    }
    private void Update()
    {
        if(done || sequence==null)return;
        try{if(sequence.MoveNext())return;Finish(null);}catch(Exception error){Finish(error);}
    }
    private void Finish(Exception error)
    {
        done=true;(sequence as IDisposable)?.Dispose();sequence=null;world?.Dispose();world=null;
        if(error!=null)File.WriteAllText(output+"/Result.txt","FAIL: "+error);
        File.WriteAllText(output+"/Cleanup.txt","remainingBodies="+FindObjectsByType<PNProjectileCollisionBody>(FindObjectsSortMode.None).Length+";frame="+Time.frameCount);
        Application.Quit(error==null?0:1);
    }
    private void OnDestroy(){world?.Dispose();}
}
