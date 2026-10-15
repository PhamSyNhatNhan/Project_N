using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Experiment only. No production Projectile, Move, combat or content API is introduced.
// Every body owns its state. The grid indexes body references; it is not an entity-data store.
public sealed class PNProjectileBenchmarkBody : MonoBehaviour, EveIPoolable
{
    [NonSerialized] public int Id, Generation, Next;
    [NonSerialized] public bool Alive;
    [NonSerialized] public Vector3 Position, Previous, Direction, Origin;
    [NonSerialized] public float Age, Lifetime, Speed, Radius, Clock;
    [NonSerialized] public ulong HitMask;
    [NonSerialized] public SphereCollider Shape;
    [NonSerialized] public Transform Glow, Tail;
    public void OnPoolCreated()
    {
        Shape = GetComponent<SphereCollider>();
        Glow = transform.Find("Glow"); Tail = transform.Find("Tail");
    }
    public void OnPoolGet(EveEasyPooling pool) { Alive = false; HitMask = 0; Next = -1; }
    public void OnPoolRelease() { Alive = false; HitMask = 0; Next = -1; if (Shape != null) Shape.enabled = false; }
}

public sealed class PNProjectileBenchmarkTarget : MonoBehaviour
{
    public int Id;
}

public sealed class PNProjectileArchitectureBenchmark : MonoBehaviour
{
    public const string Folder = ".utmp/ProjectileBenchmark";
    public static string Status = "Idle";
    private const float StepSeconds = 1f / 60f;
    private const int Substeps = 4, TargetLayer = 30, BulletLayer = 31;
    private const string LaunchKey = "PNProjectileBenchmark.Launch";
    private const string AuditKey = "PNProjectileBenchmark.Audit";
    private const string SteadyKey = "PNProjectileBenchmark.Steady";
    private const string QueryKey = "PNProjectileBenchmark.QueryOnly";
    private static bool queryOnlyActive, checkingCorrectness;
    private enum Backend { ColliderKinematic, Grid, ColliderOnlyControl }
    private enum Pattern { Player90Homing, BossSin }
    private sealed class Case
    {
        public Backend Backend; public Pattern Pattern; public int Count, Repeat; public bool Render;
        public override string ToString() => Backend + "-" + Pattern + "-" + Count + "-" + (Render ? "VFX" : "CPU") + "-r" + Repeat;
    }
    private struct Sample
    {
        public double Move, Index, Physics, Hit, Area, Pool, Step, WallFrame, MainThread, RenderThread, Gpu;
        public long Alloc, FrameGc;
        public ulong Signature;
        public int Hits, Destroyed, Respawns, Candidates;
    }
    private sealed class World : IDisposable
    {
        public readonly Scene Scene;
        private readonly PhysicsScene physics;
        private readonly Backend backend;
        private readonly Pattern pattern;
        private readonly bool render;
        private readonly bool churn;
        private readonly bool skipSimulation, validateGeometry;
        public readonly PNProjectileBenchmarkBody[] Bodies;
        private readonly GameObject[] targets;
        private readonly Vector3[] targetPositions;
        private readonly Collider[] targetBuffer = new Collider[128], bulletBuffer;
        private readonly int[] heads = new int[32 * 32];
        private readonly PNProjectileBenchmarkBody[] candidates;
        private readonly EveEasyPooling pool;
        private readonly GameObject template;
        private readonly Camera camera;
        private readonly RenderTexture texture;
        private readonly Material material;
        private readonly Mesh mesh;
        private readonly Vector3 offset;
        private int generation;
        public int Hits, Destroyed, Respawns, CandidateCount;
        public ulong Signature;
        public int Active => pool.CountActive;
        public int Capacity => pool.CountAll;
        private bool disposed;
        private readonly List<GameObject> roots = new List<GameObject>();
        private static readonly double TickMs = 1000d / Stopwatch.Frequency;
        private static long Stamp() => Stopwatch.GetTimestamp();
        private static double Elapsed(long t) => (Stopwatch.GetTimestamp() - t) * TickMs;

        public World(Backend backend, Pattern pattern, int count, bool render, int serial, bool churn = true)
        {
            this.backend = backend; this.pattern = pattern; this.render = render; this.churn = churn;
            skipSimulation=queryOnlyActive;validateGeometry=checkingCorrectness;
            offset = new Vector3(4096, 0, 4096);
            Scene = SceneManager.CreateScene("Disposable Projectile Benchmark " + serial, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            physics = Scene.GetPhysicsScene();
            Bodies = new PNProjectileBenchmarkBody[count];
            candidates = new PNProjectileBenchmarkBody[count]; bulletBuffer = new Collider[count + 16];
            targets = new GameObject[pattern == Pattern.Player90Homing ? 12 : 1];
            targetPositions = new Vector3[targets.Length];
            try
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var target = Root("Target " + i); targets[i] = target;
                    target.layer = TargetLayer; target.AddComponent<PNProjectileBenchmarkTarget>().Id = i;
                    var body = target.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                    for (int c = 0; c < 2; c++)
                    {
                        var shapeRoot = new GameObject("Target subcollider " + c); shapeRoot.transform.SetParent(target.transform, false); shapeRoot.layer = TargetLayer;
                        var shape = shapeRoot.AddComponent<SphereCollider>(); shape.isTrigger = true; shape.radius = .45f;
                        shape.center = new Vector3(c == 0 ? -.1f : .1f, 0, 0); shape.excludeLayers = ~0;
                    }
                }
                var obstacle = Root("Common wall"); obstacle.layer = TargetLayer; obstacle.transform.position = offset + new Vector3(0, 0, 10);
                var box = obstacle.AddComponent<BoxCollider>(); box.size = new Vector3(20, 2, .4f); box.isTrigger = true; box.excludeLayers = ~0;
                template = Root("Projectile benchmark inactive template"); template.SetActive(false); template.layer = BulletLayer;
                template.AddComponent<PNProjectileBenchmarkBody>();
                if (backend != Backend.Grid)
                {
                    var shape = template.AddComponent<SphereCollider>(); shape.radius = .12f; shape.isTrigger = true; shape.excludeLayers = ~0;
                    if (backend == Backend.ColliderKinematic)
                    { var body = template.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false; }
                }
                if (render)
                {
                    var shader = Shader.Find("Hidden/ProjectN/ProjectileBenchmark");
                    if (shader == null) throw new InvalidOperationException("Benchmark shader unavailable");
                    material = new Material(shader) { name = "Owned projectile benchmark glow", enableInstancing = true };
                    mesh = new Mesh { name = "Owned projectile benchmark quad" };
                    mesh.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f), new Vector3(.5f,0,.5f), new Vector3(-.5f,0,.5f) };
                    mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }; mesh.triangles = new[] { 0,2,1,0,3,2 }; mesh.RecalculateBounds();
                    Visual(template.transform, "Glow", new Vector3(.48f,1,.48f));
                    Visual(template.transform, "Tail", new Vector3(.25f,1,1.3f));
                    var cr = Root("Owned benchmark camera"); camera = cr.AddComponent<Camera>(); camera.enabled = false;
                    camera.scene = Scene; camera.cullingMask = 1 << BulletLayer; camera.orthographic = true; camera.orthographicSize = 17;
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f,.024f,.035f);
                    camera.transform.SetPositionAndRotation(offset + Vector3.up * 35, Quaternion.Euler(90,0,0));
                    texture = new RenderTexture(1280,720,24) { name = "Owned projectile benchmark render target" }; texture.Create(); camera.targetTexture = texture;
                    camera.enabled = Application.isPlaying;
                }
                pool = new EveEasyPooling(template, count, count);
                for (int i = 0; i < count; i++) Spawn(i, 0);
                MoveTargets(0); Physics.SyncTransforms(); physics.Simulate(.0001f); BuildGrid();
            }
            catch { Dispose(); throw; }
        }
        private GameObject Root(string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); roots.Add(go); return go;
        }
        private void Visual(Transform parent, string name, Vector3 scale)
        {
            var go = new GameObject(name); go.layer = BulletLayer; go.transform.SetParent(parent, false); go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        private void Spawn(int id, float time)
        {
            var go = pool.Get(); SceneManager.MoveGameObjectToScene(go, Scene);
            var p = go.GetComponent<PNProjectileBenchmarkBody>(); Bodies[id] = p; p.Id = id; p.Generation = ++generation;
            float angle = id * 2.399963f + time * .13f;
            p.Origin = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)) * (pattern == Pattern.Player90Homing ? 12 : 14);
            p.Position = p.Previous = p.Origin; p.Direction = -p.Origin.normalized;
            p.Age = 0; p.Speed = 7f + id % 7; p.Radius = .12f; p.Lifetime = .8f + (id % 11) * .1f;
            p.Clock = id % 19 == 0 ? 0 : 1; p.HitMask = 0; p.Alive = true;
            p.transform.SetPositionAndRotation(offset + p.Position, Quaternion.LookRotation(p.Direction, Vector3.up));
            if (p.Shape != null) p.Shape.enabled = true;
            go.SetActive(true);
        }
        private void Retire(PNProjectileBenchmarkBody p, int reason)
        {
            if (!p.Alive) return;
            if (!churn && reason != 90) return;
            Event(p, reason); p.Alive = false; Destroyed++;
            if (p.Shape != null) p.Shape.enabled = false;
        }
        private void Event(PNProjectileBenchmarkBody p, int kind)
        {
            // Commutative event digest is independent of broadphase candidate order.
            unchecked { ulong v = (ulong)(p.Id + 1) * 0x9e3779b185ebca87UL ^ (ulong)p.Generation * 0xc2b2ae3d27d4eb4fUL ^ (ulong)(kind + 100); Signature += v ^ (v >> 29); }
        }
        private void MoveTargets(float time)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / targets.Length;
                var p = pattern == Pattern.Player90Homing ? new Vector3(Mathf.Cos(angle)*4 + Mathf.Sin(time*1.3f+i),0,Mathf.Sin(angle)*4+Mathf.Cos(time*1.1f+i)) : new Vector3(Mathf.Sin(time)*2,0,Mathf.Cos(time*.7f)*2);
                targetPositions[i] = p; targets[i].transform.position = offset + p;
            }
        }
        private void Move(float time, float dt)
        {
            MoveTargets(time);
            foreach (var p in Bodies)
            {
                if (!p.Alive) continue;
                // Own local clock: permanent stopped samples plus moving TimeStop region.
                float scale = p.Id % 19 == 0 || (p.Position - new Vector3(-3,0,2)).sqrMagnitude < 4 ? 0 : 1;
                p.Clock = scale; p.Previous = p.Position; p.Age += dt * scale;
                bool homing = pattern == Pattern.Player90Homing && p.Id % 10 != 0;
                if (homing)
                {
                    var wanted = (targetPositions[p.Id % targets.Length] - p.Position).normalized;
                    p.Direction = Vector3.RotateTowards(p.Direction, wanted, dt * scale * 4, 0);
                    p.Position += p.Direction * (p.Speed * dt * scale);
                }
                else
                {
                    Vector3 side = Vector3.Cross(Vector3.up, -p.Origin.normalized);
                    // Position evaluated from local age; substeps follow actual sine samples.
                    p.Position = p.Origin - p.Origin.normalized * (p.Speed*p.Age) + side * (1.3f * Mathf.Sin(p.Age*12));
                    if ((p.Position-p.Previous).sqrMagnitude > 1e-10f) p.Direction = (p.Position-p.Previous).normalized;
                }
                p.transform.position = offset + p.Position;
                if (render)
                { p.transform.rotation = Quaternion.LookRotation(p.Direction,Vector3.up); p.Tail.localPosition = new Vector3(0,.005f,-.55f); }
                if (p.Age >= p.Lifetime || p.Position.sqrMagnitude > 30*30) Retire(p, 80);
            }
        }
        private static int Cell(float n) => Mathf.Clamp(Mathf.FloorToInt((n+32)/2),0,31);
        private void BuildGrid()
        {
            for (int i = 0; i < heads.Length; i++) heads[i] = -1;
            for (int i = 0; i < Bodies.Length; i++)
            {
                var p = Bodies[i]; p.Next = -1; if (!p.Alive) continue;
                int cell = Cell(p.Position.z)*32+Cell(p.Position.x); p.Next = heads[cell]; heads[cell] = i;
            }
        }
        private int FindBox(Vector3 center, Vector3 half)
        {
            int count = 0;
            if (backend != Backend.Grid)
            {
                int n = physics.OverlapBox(offset+center, half+Vector3.one*.002f, bulletBuffer, Quaternion.identity, 1<<BulletLayer, QueryTriggerInteraction.Collide);
                if (n == bulletBuffer.Length) throw new Exception("Projectile query buffer saturated");
                for (int i=0;i<n;i++)
                { var p=bulletBuffer[i].GetComponent<PNProjectileBenchmarkBody>(); if(p!=null && p.Alive) candidates[count++]=p; bulletBuffer[i]=null; }
            }
            else
            {
                Vector3 extent=half+Vector3.one*.12f;
                int xmin=Cell(center.x-extent.x), xmax=Cell(center.x+extent.x), zmin=Cell(center.z-extent.z), zmax=Cell(center.z+extent.z);
                for(int z=zmin;z<=zmax;z++) for(int x=xmin;x<=xmax;x++)
                    for(int i=heads[z*32+x];i!=-1;i=Bodies[i].Next) if(Bodies[i].Alive) candidates[count++]=Bodies[i];
            }
            CandidateCount += count; return count;
        }
        private static bool InBox(PNProjectileBenchmarkBody p, Vector3 center, Vector3 half)
        {
            Vector3 d = p.Position-center;
            float x=Mathf.Max(Mathf.Abs(d.x)-half.x,0), y=Mathf.Max(Mathf.Abs(d.y)-half.y,0), z=Mathf.Max(Mathf.Abs(d.z)-half.z,0);
            return x*x+y*y+z*z <= p.Radius*p.Radius;
        }
        private void Areas(float time)
        {
            var center = new Vector3(Mathf.Sin(time*2)*5,0,-2);
            int n=FindBox(center, new Vector3(1.5f,1,1.5f));
            for(int i=0;i<n;i++) { var p=candidates[i]; if(p.Alive && (p.Position-center).sqrMagnitude <= (1.5f+p.Radius)*(1.5f+p.Radius)) Retire(p,81); candidates[i]=null; }
            center=new Vector3(Mathf.Sin(time*.8f)*3,0,0); var half=new Vector3(.2f,1,8);
            n=FindBox(center,half);
            for(int i=0;i<n;i++) { var p=candidates[i]; if(p.Alive && InBox(p,center,half)) Retire(p,82); candidates[i]=null; }
        }
        private void TargetHits()
        {
            foreach(var p in Bodies)
            {
                if(!p.Alive) continue;
                int n=physics.OverlapSphere(offset+p.Position,p.Radius,targetBuffer,1<<TargetLayer,QueryTriggerInteraction.Collide);
                if(n==targetBuffer.Length) throw new Exception("Target query buffer saturated");
                bool wall=false;
                ulong observed=0;
                for(int i=0;i<n;i++)
                {
                    var target=targetBuffer[i].GetComponentInParent<PNProjectileBenchmarkTarget>(); targetBuffer[i]=null;
                    if(target==null) { wall=true; continue; }
                    ulong bit=1UL<<target.Id;
                    observed|=bit;
                    if((p.HitMask&bit)!=0) continue;
                    p.HitMask|=bit; Hits++; Event(p,target.Id);
                }
                if(validateGeometry)
                {
                    ulong expected=0,uncertain=0;var query=offset+p.Position;
                    for(int id=0;id<targets.Length;id++)for(int side=0;side<2;side++)
                    {
                        var center=targets[id].transform.position+new Vector3(side==0?-.1f:.1f,0,0);
                        float distance=(query-center).sqrMagnitude, threshold=(p.Radius+.45f)*(p.Radius+.45f);
                        if(distance<=threshold)expected|=1UL<<id;
                        if(Mathf.Abs(distance-threshold)<.00002f)uncertain|=1UL<<id;
                    }
                    if(((observed^expected)&~uncertain)!=0)throw new Exception("Native overlap differs from sphere/sphere geometry oracle");
                }
                // Hit retention demonstrates per-instance dedup across target subcolliders/substeps.
                if(wall) Retire(p,83);
            }
        }
        public Sample Tick(int frame)
        {
            long before=GC.GetAllocatedBytesForCurrentThread(), start=Stamp(); var sample=new Sample();
            long t=Stamp();
            for(int i=0;i<Bodies.Length;i++)
            {
                var p=Bodies[i]; if(p.Alive) continue;
                pool.Release(p.gameObject); Spawn(i,frame*StepSeconds); Respawns++;
            }
            sample.Pool=Elapsed(t);
            for(int sub=0;sub<Substeps;sub++)
            {
                float time=(frame+(sub+1f)/Substeps)*StepSeconds;
                t=Stamp(); Move(time,StepSeconds/Substeps); sample.Move+=Elapsed(t);
                t=Stamp(); Physics.SyncTransforms(); if(!skipSimulation)physics.Simulate(StepSeconds/Substeps); sample.Physics+=Elapsed(t);
                t=Stamp(); if(backend==Backend.Grid) BuildGrid(); sample.Index+=Elapsed(t);
                t=Stamp(); TargetHits(); sample.Hit+=Elapsed(t);
                t=Stamp(); Areas(time); sample.Area+=Elapsed(t);
            }
            sample.Step=Elapsed(start); sample.Alloc=GC.GetAllocatedBytesForCurrentThread()-before;
            sample.Signature=Signature; sample.Hits=Hits; sample.Destroyed=Destroyed; sample.Respawns=Respawns; sample.Candidates=CandidateCount;
            return sample;
        }
        public bool SameState(World other)
        {
            if(Signature!=other.Signature || Hits!=other.Hits || Destroyed!=other.Destroyed || Respawns!=other.Respawns) return false;
            for(int i=0;i<Bodies.Length;i++)
            { var a=Bodies[i];var b=other.Bodies[i]; if(a.Generation!=b.Generation || a.Alive!=b.Alive || a.HitMask!=b.HitMask || (a.Position-b.Position).sqrMagnitude>1e-8f) return false; }
            return true;
        }
        public void CheckStoppedDestruction()
        {
            var p=Bodies[0]; p.Position=p.Previous=Vector3.zero; p.Clock=0; p.Age=0; p.Alive=true;
            if(p.Shape!=null)p.Shape.enabled=true;
            p.transform.position=offset; Physics.SyncTransforms(); physics.Simulate(.0001f); BuildGrid();
            Areas(0); if(p.Alive) throw new Exception("Wind wall did not destroy a stopped projectile");
        }
        public void CheckPoolMembership()
        {
            foreach(var p in Bodies) Retire(p,90);
            Physics.SyncTransforms(); physics.Simulate(.0001f); BuildGrid();
            if(FindBox(Vector3.zero,Vector3.one*30)!=0) throw new Exception("Retired projectile still returned by query");
            for(int i=0;i<Bodies.Length;i++) pool.Release(Bodies[i].gameObject);
            if(pool.CountActive!=0) throw new Exception("Pool active membership leaked");
        }
        public void Capture(string path)
        {
            if(!render) return;
            if(!Application.isPlaying) camera.Render();
            var previous=RenderTexture.active; var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try { RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG()); }
            finally { RenderTexture.active=previous;DestroyOwned(image); }
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true;
            if(camera!=null) { camera.enabled=false;camera.targetTexture=null; }
            pool?.Dispose();
            foreach(var root in roots) if(root!=null) DestroyOwned(root);
            if(texture!=null){texture.Release();DestroyOwned(texture);}
            if(material!=null)DestroyOwned(material); if(mesh!=null)DestroyOwned(mesh);
            if(Scene.IsValid() && Scene.isLoaded)
            {
#if UNITY_EDITOR
                if(!Application.isPlaying) EditorSceneManager.CloseScene(Scene,true); else
#endif
                SceneManager.UnloadSceneAsync(Scene);
            }
        }
    }

    private static void DestroyOwned(UnityEngine.Object value)
    {
        if(value==null) return;
        if(Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
    private static float DistanceSegment(Vector3 point,Vector3 a,Vector3 b)
    {
        Vector3 direction=b-a;
        float t=direction.sqrMagnitude>0?Mathf.Clamp01(Vector3.Dot(point-a,direction)/direction.sqrMagnitude):0;
        return Vector3.Distance(point,a+direction*t);
    }
    private World world;
    private IEnumerator sequence;
    private readonly List<string> correctness = new List<string>();
    private readonly List<string> summaries = new List<string>();
    private readonly List<Case> cases = new List<Case>();
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private ProfilerRecorder mainThread, renderThread, gcFrame;
    private bool cleaned, oldBackground;
    private int measuredFrames, warmupFrames;
    private bool steadyOnly;
    private bool queryOnly;
    private string output;
    private Scene original;
    private bool originalDirty;
    private long priorStamp;

    public static string RunNativeCorrectness()
    {
        if(!Application.isPlaying) throw new InvalidOperationException("Play Mode required for isolated local PhysX scenes");
        Directory.CreateDirectory(Folder); var evidence=new List<string>(); var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;
        int serial=0;
        checkingCorrectness=true;
        try
        {
        foreach(Pattern pattern in Enum.GetValues(typeof(Pattern)))
        {
            using(var a=new World(Backend.ColliderKinematic,pattern,300,false,++serial))
            using(var b=new World(Backend.Grid,pattern,300,false,++serial))
            using(var c=new World(Backend.ColliderOnlyControl,pattern,300,false,++serial))
            {
                for(int frame=0;frame<240;frame++)
                {
                    a.Tick(frame);b.Tick(frame);c.Tick(frame);
                    if(!a.SameState(b) || !a.SameState(c)) throw new Exception("Backend equivalence failed: "+pattern+" frame "+frame);
                }
                if(a.Hits==0 || a.Respawns==0 || a.Destroyed==0) throw new Exception("Correctness workload did not exercise hit/reuse");
                evidence.Add("PASS "+pattern+": 240 ticks x4 samples, per-body state/event parity for kinematic/grid/static; hits="+a.Hits+" destroyed="+a.Destroyed+" respawns="+a.Respawns);
                a.CheckStoppedDestruction();b.CheckStoppedDestruction();c.CheckStoppedDestruction(); evidence.Add("PASS stopped projectile destroyed by wind wall: "+pattern);
                a.CheckPoolMembership();b.CheckPoolMembership();c.CheckPoolMembership(); evidence.Add("PASS retired membership and pooled cleanup: "+pattern);
            }
        }
        // Analytic sine path: a chord between equal-phase endpoints misses a known intermediate contact.
        Vector3 start=new Vector3(0,0,0),end=new Vector3(1,0,0),target=new Vector3(.25f,0,1);
        bool chord=DistanceSegment(target,start,end)<.15f, sampled=false;
        for(int i=0;i<=64;i++){float u=i/64f;var p=new Vector3(u,0,Mathf.Sin(u*Mathf.PI*2));sampled|=(p-target).sqrMagnitude<.15f*.15f;}
        if(chord || !sampled) throw new Exception("Sine sampling oracle failed");
        evidence.Add("PASS sine oracle: endpoint chord misses contact; fine curve samples detect it (not a general no-tunnelling proof)");
        if(SceneManager.GetActiveScene()!=original || original.isDirty!=dirty)throw new Exception("Native correctness changed authored scene");
        evidence.Add("PASS authored scene preserved; no gameplay content registered or changed");
        evidence.Add("PASS sampled native target overlap matches sphere geometry oracle (near-boundary float tolerance excluded)");
        File.WriteAllLines(Folder+"/Correctness.txt",evidence); return string.Join("\n",evidence);
        }
        finally {checkingCorrectness=false;}
    }

    public static string StartSuite(int samples=120,int warmup=30,string outputFolder=null,bool steady=false,bool queryOnly=false)
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Play Mode required; use LaunchFromEditor from idle Edit");
        if(Status.StartsWith("Running",StringComparison.Ordinal))throw new InvalidOperationException("Benchmark already running");
        if(samples<60 || warmup<10)throw new ArgumentOutOfRangeException("Use >=60 samples and >=10 warmup frames");
        var host=new GameObject("Disposable Projectile Architecture Benchmark");
        var runner=host.AddComponent<PNProjectileArchitectureBenchmark>();
        runner.measuredFrames=samples;runner.warmupFrames=warmup;runner.steadyOnly=steady;runner.queryOnly=queryOnly;queryOnlyActive=queryOnly;
        runner.output=outputFolder??(queryOnly?Folder+(steady?"/QueryOnlySteady":"/QueryOnlyChurn"):(steady?Folder+"/Steady":Folder));
        runner.original=SceneManager.GetActiveScene();runner.originalDirty=runner.original.isDirty;
        runner.oldBackground=Application.runInBackground;Application.runInBackground=true;
        Directory.CreateDirectory(runner.output);
        File.WriteAllText(runner.output+"/Result.txt","Running; incomplete until PASS is written");
        File.WriteAllText(runner.output+"/Environment.json",JsonUtility.ToJson(new EnvironmentInfo { samplesPerCase=samples,warmupPerCase=warmup,queryOnly=queryOnly,lifecycle=steady?"steady: lifetime/area retirement suppressed; queries still executed":"churn: lifetime and moving destruction fields recycle objects" },true));
        runner.mainThread=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
        runner.renderThread=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Render Thread",1);
        runner.gcFrame=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
        runner.sequence=runner.Suite();Status="Running: preparing";
#if UNITY_EDITOR
        EditorApplication.update+=runner.Pump;
#endif
        return Status;
    }
    [Serializable] private sealed class EnvironmentInfo
    {
        public string unity=Application.unityVersion,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,graphics=SystemInfo.graphicsDeviceType.ToString();
        public int memoryMb=SystemInfo.systemMemorySize,vsync=QualitySettings.vSyncCount,targetFps=Application.targetFrameRate,width=1280,height=720;
        public bool editor=Application.isEditor;
        public int samplesPerCase=120,warmupPerCase=30,repeats=2,substeps=Substeps;
        public string lifecycle;
        public bool queryOnly;
        public float simulationDt=StepSeconds;
        public string caveats="Synthetic additive local PhysX scenes; other game/editor work remains running. Frame times include Editor/MCP/vsync. GPU timings may be unsupported. Fixed simulation dt, not wall-time catch-up. Minimal glow shader is representative, not production VFX. No Player/device performance claim.";
    }
    private IEnumerator Suite()
    {
        RunNativeCorrectness();
        if(output!=Folder)File.Copy(Folder+"/Correctness.txt",output+"/Correctness.txt",true);
        yield return null; yield return null;
        // Balanced ordering: reverse A/B on repeat 2 and reverse load order.
        for(int repeat=0;repeat<2;repeat++)
            foreach(int count in queryOnly?new[]{1000}:repeat==0?new[]{300,500,1000}:new[]{1000,500,300})
                foreach(Pattern pattern in Enum.GetValues(typeof(Pattern)))
                    foreach(bool render in steadyOnly && count!=1000?new[]{false}:new[]{false,true})
                        foreach(Backend backend in repeat==0?new[]{Backend.ColliderKinematic,Backend.Grid}:new[]{Backend.Grid,Backend.ColliderKinematic})
                            cases.Add(new Case{Backend=backend,Pattern=pattern,Count=count,Render=render,Repeat=repeat});
        // Collider-only is a diagnostic control, not assumed to be the recommended moving-collider setup.
        if(!steadyOnly && !queryOnly)foreach(int count in new[]{300,500,1000})foreach(Pattern pattern in Enum.GetValues(typeof(Pattern)))
                cases.Add(new Case{Backend=Backend.ColliderOnlyControl,Pattern=pattern,Count=count,Render=false,Repeat=0});
        summaries.Add("case,move_p50_ms,index_p50_ms,physics_p50_ms,hit_p50_ms,area_p50_ms,pool_p50_ms,step_p50_ms,step_p95_ms,step_p99_ms,frame_p50_ms,frame_p95_ms,frame_p99_ms,main_p50_ms,render_p50_ms,gpu_p50_ms,alloc_p50_bytes,frame_gc_p50_bytes,over16_67,over33_33,hits,destroyed,respawns,candidates,pool_capacity,managed_bytes,native_global_bytes");
        int serial=0;
        foreach(var item in cases)
        {
            Status="Running "+(serial+1)+"/"+cases.Count+": "+item;
            File.WriteAllText(output+"/Progress.txt",Status);
            world=new World(item.Backend,item.Pattern,item.Count,item.Render,++serial,!steadyOnly);
            yield return null; yield return null;
            for(int frame=0;frame<warmupFrames;frame++){world.Tick(frame);yield return null;}
            var samples=new Sample[measuredFrames]; priorStamp=Stopwatch.GetTimestamp();
            long managed=GC.GetTotalMemory(false),native=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            for(int frame=0;frame<samples.Length;frame++)
            {
                long now=Stopwatch.GetTimestamp();double wall=(now-priorStamp)*1000d/Stopwatch.Frequency;priorStamp=now;
                double main=mainThread.Valid?mainThread.LastValue*1e-6:-1,rt=renderThread.Valid?renderThread.LastValue*1e-6:-1;
                long frameAlloc=gcFrame.Valid?gcFrame.LastValue:-1;
                double gpu=FrameTimingManager.GetLatestTimings(1,timings)>0 && timings[0].gpuFrameTime>0?timings[0].gpuFrameTime:-1;
                var sample=world.Tick(warmupFrames+frame); sample.WallFrame=wall;sample.MainThread=main;sample.RenderThread=rt;sample.Gpu=gpu;sample.FrameGc=frameAlloc;
                samples[frame]=sample;FrameTimingManager.CaptureFrameTimings();yield return null;
            }
            WriteCase(item,samples,managed,native,world.Capacity);
            if(item.Render && item.Count==1000 && item.Repeat==0)world.Capture(output+"/"+item+".png");
            world.CheckPoolMembership();world.Dispose();world=null;
            yield return null; yield return null;
        }
        File.WriteAllLines(output+"/Summary.csv",summaries);
        Status="PASS: "+cases.Count+" cases; "+measuredFrames+" measured frames each; CPU/grid comparison and rendered frames captured";
    }
    private static double P(Sample[] samples,Func<Sample,double> select,double percentile=.5)
    { var values=samples.Select(select).OrderBy(x=>x).ToArray();return values[Math.Min(values.Length-1,(int)Math.Ceiling(percentile*values.Length)-1)]; }
    private static string F(double n)=>n.ToString("0.######",CultureInfo.InvariantCulture);
    private void WriteCase(Case item,Sample[] samples,long managed,long native,int capacity)
    {
        var last=samples[samples.Length-1];
        var row=new[]{item.ToString(),F(P(samples,s=>s.Move)),F(P(samples,s=>s.Index)),F(P(samples,s=>s.Physics)),F(P(samples,s=>s.Hit)),F(P(samples,s=>s.Area)),F(P(samples,s=>s.Pool)),F(P(samples,s=>s.Step)),F(P(samples,s=>s.Step,.95)),F(P(samples,s=>s.Step,.99)),F(P(samples,s=>s.WallFrame)),F(P(samples,s=>s.WallFrame,.95)),F(P(samples,s=>s.WallFrame,.99)),F(P(samples,s=>s.MainThread)),F(P(samples,s=>s.RenderThread)),F(P(samples,s=>s.Gpu)),F(P(samples,s=>s.Alloc)),F(P(samples,s=>s.FrameGc)),samples.Count(s=>s.WallFrame>16.667).ToString(),samples.Count(s=>s.WallFrame>33.333).ToString(),last.Hits.ToString(),last.Destroyed.ToString(),last.Respawns.ToString(),last.Candidates.ToString(),capacity.ToString(),managed.ToString(),native.ToString()};
        summaries.Add(string.Join(",",row));File.WriteAllLines(output+"/Summary.csv",summaries);
        var raw=new StringBuilder("frame,move_ms,index_ms,physics_ms,hit_ms,area_ms,pool_ms,step_ms,wall_frame_ms,main_thread_ms,render_thread_ms,gpu_ms,step_alloc_bytes,frame_gc_bytes,event_signature,hits,destroyed,respawns,candidates\n");
        for(int i=0;i<samples.Length;i++){var s=samples[i];raw.Append(i).Append(',').Append(string.Join(",",new[]{F(s.Move),F(s.Index),F(s.Physics),F(s.Hit),F(s.Area),F(s.Pool),F(s.Step),F(s.WallFrame),F(s.MainThread),F(s.RenderThread),F(s.Gpu),s.Alloc.ToString(),s.FrameGc.ToString(),s.Signature.ToString(),s.Hits.ToString(),s.Destroyed.ToString(),s.Respawns.ToString(),s.Candidates.ToString()})).Append('\n');}
        File.WriteAllText(output+"/"+item+".csv",raw.ToString());
    }
    private void Update()
    {
        if(cleaned || sequence==null)return;
        try { if(sequence.MoveNext())return; Finish(null); }
        catch(Exception error){Finish(error);}
    }
    private void Finish(Exception error)
    {
        if(error!=null)Status="FAIL: "+error;
        Cleanup();File.WriteAllText(output+"/Result.txt",Status);
        File.WriteAllText(output+"/RuntimeAudit.txt","originalScene="+original.name+";dirtyBefore="+originalDirty+";dirtyAfter="+original.isDirty+";remainingBenchmarkBodies="+FindObjectsByType<PNProjectileBenchmarkBody>(FindObjectsSortMode.None).Length+";timeScale="+Time.timeScale);
        Destroy(gameObject);
#if UNITY_EDITOR
        if(SessionState.GetBool(LaunchKey,false))EditorApplication.delayCall+=()=>EditorApplication.ExitPlaymode();
#endif
    }
    private void Cleanup()
    {
        if(cleaned)return;cleaned=true;
        (sequence as IDisposable)?.Dispose();sequence=null;world?.Dispose();world=null;
        mainThread.Dispose();renderThread.Dispose();gcFrame.Dispose();Application.runInBackground=oldBackground;
#if UNITY_EDITOR
        EditorApplication.update-=Pump;
#endif
    }
    private void OnDestroy()
    {
        if(!cleaned && sequence!=null)
        { Status="CANCELLED before all benchmark cases completed";if(!string.IsNullOrEmpty(output))File.WriteAllText(output+"/Result.txt",Status); }
        Cleanup();
    }
#if UNITY_EDITOR
    private void Pump(){if(Application.isPlaying)EditorApplication.QueuePlayerLoopUpdate();}
    [InitializeOnLoadMethod] private static void RegisterLaunch()
    { EditorApplication.playModeStateChanged-=PlayChanged;EditorApplication.playModeStateChanged+=PlayChanged; }
    public static string LaunchFromEditor()
        => Launch(false);
    public static string LaunchSteadyFromEditor()
        => Launch(true);
    public static string LaunchQueryOnlyChurnFromEditor()=>Launch(false,true);
    public static string LaunchQueryOnlySteadyFromEditor()=>Launch(true,true);
    private static string Launch(bool steady,bool queryOnly=false)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle Edit Mode required");
        var scene=SceneManager.GetActiveScene();if(scene.isDirty)throw new InvalidOperationException("Clean authored scene required; benchmark will not save it");
        SessionState.SetString(AuditKey,scene.path+"|"+scene.isDirty+"|"+Time.timeScale+"|"+Application.runInBackground+"|"+QualitySettings.vSyncCount+"|"+Application.targetFrameRate);
        SessionState.SetBool(QueryKey,queryOnly);SessionState.SetBool(SteadyKey,steady);SessionState.SetBool(LaunchKey,true);EditorApplication.EnterPlaymode();return "Entering Play: native correctness first, then "+(queryOnly?16:steady?32:54)+" benchmark cases";
    }
    private static void PlayChanged(PlayModeStateChange change)
    {
        if(!SessionState.GetBool(LaunchKey,false))return;
        if(change==PlayModeStateChange.EnteredPlayMode)
            EditorApplication.delayCall+=()=>{try{StartSuite(steady:SessionState.GetBool(SteadyKey,false),queryOnly:SessionState.GetBool(QueryKey,false));}catch(Exception e){Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/Result.txt","FAIL launch: "+e);EditorApplication.ExitPlaymode();}};
        if(change==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(LaunchKey,false);var scene=SceneManager.GetActiveScene();
            var counts=new object[]{0,0,0};typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries")?.GetMethod("GetCountsByType",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public)?.Invoke(null,counts);
            bool steady=SessionState.GetBool(SteadyKey,false);
            var auditFolder=SessionState.GetBool(QueryKey,false)?Folder+(steady?"/QueryOnlySteady":"/QueryOnlyChurn"):(steady?Folder+"/Steady":Folder);
            File.WriteAllLines(auditFolder+"/FinalAudit.txt",new[]{"before="+SessionState.GetString(AuditKey,""),"after="+scene.path+"|"+scene.isDirty+"|"+Time.timeScale+"|"+Application.runInBackground+"|"+QualitySettings.vSyncCount+"|"+Application.targetFrameRate,"playing="+EditorApplication.isPlaying+";compiling="+EditorApplication.isCompiling+";updating="+EditorApplication.isUpdating+";benchmarkObjects="+Resources.FindObjectsOfTypeAll<PNProjectileBenchmarkBody>().Length,"console errors="+counts[0]+";warnings="+counts[1]+";logs="+counts[2]+" (not cleared)","Game/Simulator selection and Play Maximized were not changed. Original scene was not saved or explicitly reloaded."});
        }
    }
#endif
}
