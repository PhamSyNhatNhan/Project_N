#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// EN: The strings the stars hang puppets by (Docs/VisualInkStringsPlan.md). PNVisualStringSet is what ONE actor owns: up to four PNVisualStringState and the API that adds, changes, cuts, lowers
//     and removes strings at run time (the same resolver as the content uses). PNVisualStringSystem draws EVERY string of EVERY actor in view as ONE mesh with ONE material and one draw:
//     a camera-facing ribbon per string, rebuilt each frame into preallocated arrays (no allocation). Strings are presentation only: no collision, no gameplay state, nothing replicated.
// VI: Dây rối: mỗi actor có một PNVisualStringSet (tối đa 4 dây, API thêm/đổi/đứt/thả/gỡ lúc chạy); PNVisualStringSystem vẽ MỌI dây của mọi actor trong khung nhìn bằng MỘT mesh, MỘT material.
public sealed class PNVisualStringSet
{
    public const int Max = PNVisualStringLimits.MaxPerPuppet;
    private readonly PNVisualStringState[] states = new PNVisualStringState[Max];
    private readonly PNVisualStringEffects[] effects=new PNVisualStringEffects[Max];
    private Vector3 previousPosition;private bool positionKnown;
    internal PNVisualStringEffects Effects(int handle)=>effects[handle];
    internal void FeedPosition(Vector3 position,float dt)
    {
        Vector3 velocity=positionKnown&&dt>1e-5f?(position-previousPosition)/dt:Vector3.zero;
        bool teleport=positionKnown&&(position-previousPosition).sqrMagnitude>9;
        previousPosition=position;positionKnown=true;
        for(int i=0;i<Max;i++){if(teleport)effects[i].Reset();effects[i].Velocity(teleport?0:velocity.x,teleport?0:velocity.z);}
    }
    internal void Hit(){for(int i=0;i<Max;i++)if(states[i].InUse&&states[i].Phase==PNVisualStringPhase.Attached)effects[i].Hit();}
    private readonly EveRegistryHub hub; private readonly string owner; private int seeds;
    private static readonly HashSet<string> warned = new HashSet<string>();
    public static void ResetWarnings() => warned.Clear();          // checks: warn once per owner and kind, so a second run needs a clean slate
    public PNVisualStringSet(EveRegistryHub hub, string owner, IReadOnlyList<PNVisualStringEntry> entries)
    {
        this.hub = hub ?? throw new ArgumentNullException(nameof(hub)); this.owner = owner ?? "";
        for (int i = 0; i < Max; i++){states[i] = new PNVisualStringState();effects[i]=new PNVisualStringEffects((uint)(i*7919+17));}
        if (entries != null) foreach (var entry in entries) Add(entry, 0f);
    }
    public PNVisualStringState State(int handle) => handle >= 0 && handle < Max ? states[handle] : null;
    public int Count { get { int n = 0; for (int i = 0; i < Max; i++) if (states[i].InUse) n++; return n; } }
    public bool Any { get { for (int i = 0; i < Max; i++) if (states[i].InUse && (states[i].Phase != PNVisualStringPhase.Hidden||effects[i].DustCount>0)) return true; return false; } }
    public bool AnyLowering { get { for (int i = 0; i < Max; i++) if (states[i].InUse && states[i].Phase == PNVisualStringPhase.Lowering) return true; return false; } }
    public bool HasStrings { get { for (int i = 0; i < Max; i++) if (states[i].InUse) return true; return false; } }

    private PNVisualStringSpec Resolve(PNVisualStringEntry entry)
    {
        var styles = hub.Get<PNVisualStringStyleDefinition>(); var shapes = hub.Get<PNVisualStringShapeDefinition>(); var colors = hub.Get<PNVisualStringColorDefinition>();
        return PNVisualStringResolver.Resolve(entry, id => styles.TryGet(id, out var s) ? s : null, id => shapes.TryGet(id, out var s) ? s : null, id => colors.TryGet(id, out var s) ? s : null);
    }
    private bool Valid(int handle, string what)
    {
        if (handle >= 0 && handle < Max && states[handle].InUse) return true;
        if (warned.Add(owner + what)) Debug.LogWarning("Visual strings of '" + owner + "': " + what + " ignored for the invalid handle " + handle + ".");
        return false;
    }
    // Adds a string; `lowerSeconds` > 0 lowers it from the top (the entrance), 0 shows it attached. Returns the handle, or -1 when the puppet already has four.
    public int Add(PNVisualStringEntry entry, float lowerSeconds = 0f)
    {
        var spec = Resolve(entry);
        for (int i = 0; i < Max; i++)
            if (!states[i].InUse)
            {
                effects[i].Reset();states[i].Begin(spec, (seeds++ * 1.618f) % 6.2832f, PNVisualStringPhase.Attached); if (lowerSeconds > 0f) states[i].Lower(lowerSeconds); return i;
            }
        if (warned.Add(owner + "full")) Debug.LogWarning("Visual strings of '" + owner + "': a puppet has at most " + Max + " strings; the extra one is ignored.");
        return -1;
    }
    // Removes a string: cut = it falls away first, otherwise it is gone at once. The handle is free again afterwards.
    public void Remove(int handle, bool cut = true)
    {
        if (!Valid(handle, "remove")) return;
        if (cut && states[handle].Phase != PNVisualStringPhase.Hidden) { effects[handle].Burst(3);states[handle].Cut(); removing |= 1 << handle; } else { states[handle].Release();effects[handle].Reset(); }
    }
    private int removing;
    // Replaces what the string is (style, shape, colour, socket, offset, lean) and blends there over `blendSeconds`.
    public void Set(int handle, PNVisualStringEntry entry, float blendSeconds = 0f) { if (Valid(handle, "set")) states[handle].Retarget(Resolve(entry), blendSeconds); }
    public void SetColor(int handle, EveId color, float blendSeconds = 0f)
    {
        if (!Valid(handle, "set colour")) return;
        var colors = hub.Get<PNVisualStringColorDefinition>(); var c = colors.Get(color); var t = states[handle].Target;
        t.Core = c.Core; t.Halo = c.Halo; t.Knot = c.Knot; t.Alpha = c.Alpha; t.FadeTop = c.FadeTop; states[handle].Retarget(t, blendSeconds);
    }
    public void SetShape(int handle, EveId shape, float blendSeconds = 0f)
    {
        if (!Valid(handle, "set shape")) return;
        var s = hub.Get<PNVisualStringShapeDefinition>().Get(shape); var t = states[handle].Target;
        t.LeaveX = s.LeaveX; t.LeaveY = s.LeaveY; t.LeaveDistance = s.LeaveDistance; t.Curve = s.Curve; t.Slack = s.Slack; t.SwayAmplitude = s.SwayAmplitude; t.SwayFrequency = s.SwayFrequency;
        t.WidthRoot = s.WidthRoot; t.WidthTop = s.WidthTop; t.Length = s.Length; t.Segments = s.Segments; states[handle].Retarget(t, blendSeconds);
    }
    public void SetAllColors(EveId color, float blendSeconds = 0f) { for (int i = 0; i < Max; i++) if (states[i].InUse) SetColor(i, color, blendSeconds); }
    public void CutAll() { for (int i = 0; i < Max; i++){if(states[i].InUse&&states[i].Phase!=PNVisualStringPhase.Cut&&states[i].Phase!=PNVisualStringPhase.Hidden)effects[i].Burst(3);states[i].Cut();} }
    public void LowerAll(float seconds) { for (int i = 0; i < Max; i++){effects[i].Reset();states[i].Lower(seconds);} }
    public void ShowAll() { for (int i = 0; i < Max; i++){effects[i].Reset();states[i].Show();} }
    public void HideAll() { for (int i = 0; i < Max; i++){states[i].Hide();effects[i].Reset();} }
    public void Advance(float dt)
    {
        for (int i = 0; i < Max; i++)
        {
            var s = states[i]; if (!s.InUse) continue;var previous=s.Phase; s.Advance(dt);effects[i].Advance(dt,s.Phase==PNVisualStringPhase.Attached);
            if(previous==PNVisualStringPhase.Lowering&&s.Phase==PNVisualStringPhase.Attached)effects[i].Burst(1);
            if ((removing & (1 << i)) != 0 && s.Phase == PNVisualStringPhase.Hidden) { s.Release();effects[i].Reset(); removing &= ~(1 << i); }
        }
    }
    // The first string's entrance ("none", "slumped", "lowered"), or "none".
    public string Entrance(EveRegistryHub hubFor, PNVisualAppearanceDefinition appearance)
    {
        if (appearance.Strings.Count == 0) return PNVisualStringStyleDefinition.EntranceNone;
        try { return Resolve(appearance.Strings[0]).Entrance; } catch (Exception) { return PNVisualStringStyleDefinition.EntranceNone; }
    }
}

public static class PNVisualStringSystem
{
    // Capacity of the one mesh: 128 puppets x 4 strings x 49 rows x 2 vertices stays inside the 16-bit index range.
    public const int MaxStrings = 128 * PNVisualStringLimits.MaxPerPuppet, RowsPerString = PNVisualStringLimits.MaxSegments + 1, MaxVertices = 65000;
    private static readonly Vector3[] positions = new Vector3[MaxVertices];
    private static readonly Color32[] colors = new Color32[MaxVertices];
    private static readonly Vector4[] uv0 = new Vector4[MaxVertices], uv1 = new Vector4[MaxVertices], uv2 = new Vector4[MaxVertices],uv3=new Vector4[MaxVertices];
    private static readonly int[] indices = new int[MaxVertices * 3];
    private static readonly float[] rowX = new float[RowsPerString], rowY = new float[RowsPerString], rowW = new float[RowsPerString];
    private static GameObject host; private static Mesh mesh; private static Material material; private static MeshRenderer meshRenderer;
    // Checks and previews: draw for this camera instead of the resolved game camera; a value >= 0 freezes the sway clock.
    public static Camera CameraOverride; public static float FixedTime = -1f;
    public static int LastStrings { get; private set; }
    public static int LastVertices { get; private set; }
    internal static int LastDust {get;private set;}
    internal static int LastTrails {get;private set;}
    public static MeshRenderer Renderer => meshRenderer;
    public static Material Material => material;
    public static float TopMargin = .35f;

    private static void Ensure(Camera camera)
    {
        if (host == null)
        {
            var shader = Resources.Load<Shader>("PNVisualStrings"); if (shader == null) shader = Shader.Find("PN/VisualStrings"); if (shader == null) throw new InvalidOperationException("Shader PN/VisualStrings is missing.");
            material = new Material(shader) { name = "PN Visual Strings", hideFlags = HideFlags.HideAndDontSave, enableInstancing = false };
            mesh = new Mesh { name = "PN Visual Strings", hideFlags = HideFlags.HideAndDontSave }; mesh.MarkDynamic();
            host = new GameObject("PN Visual Strings") { hideFlags = HideFlags.HideAndDontSave };
            host.AddComponent<MeshFilter>().sharedMesh = mesh; meshRenderer = host.AddComponent<MeshRenderer>(); meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off; meshRenderer.receiveShadows = false; meshRenderer.lightProbeUsage = LightProbeUsage.Off; meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(4000f, 4000f, 4000f));   // one renderer: no per-frame bounds work
        }
        if (camera != null && camera.scene.IsValid() && host.scene != camera.scene) SceneManager.MoveGameObjectToScene(host, camera.scene);   // a preview-scene camera only renders its own scene
    }
    public static void Release()
    {
        if (mesh != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh); }
        if (material != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material); }
        if (host != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(host); else UnityEngine.Object.DestroyImmediate(host); }
        host = null; mesh = null; material = null; meshRenderer = null; LastStrings = LastVertices = LastDust = LastTrails = 0;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset() { host = null; mesh = null; material = null; meshRenderer = null; CameraOverride = null; FixedTime = -1f; }

    // Rebuilds the one mesh from every string of every actor that is in view. Called once per frame by PNVisualSystem after the actors ticked.
    public static void Draw()
    {
        var camera=CameraOverride!=null?CameraOverride:PNCameraFacing.ResolveCamera(null,Application.isPlaying);
        int vertexCount=0,indexCount=0,strings=0;LastDust=LastTrails=0;
        if(camera!=null)
        {
            var actors=PNVisualSystem.Actors;
            var ct=camera.transform;Vector3 right=ct.right,up=ct.up,forward=ct.forward,camPos=ct.position;
            int baseRemaining=0;
            // Reserve every primary ribbon first; optional trails/dust must never hide a real string.
            for(int a=0;a<actors.Count;a++)
            {
                var actor=actors[a];if(actor==null||!actor.InView||actor.Strings==null)continue;
                for(int s=0;s<PNVisualStringSet.Max;s++)
                {
                    var state=actor.Strings.State(s);
                    if(state.InUse&&state.Visible(out _,out _,out _,out _))baseRemaining+=(state.Current.Segments+1)*2;
                }
            }
            for(int a=0;a<actors.Count;a++)
            {
                var actor=actors[a];if(actor==null||!actor.InView)continue;var set=actor.Strings;if(set==null||!set.Any)continue;
                for(int s=0;s<PNVisualStringSet.Max;s++)
                {
                    var state=set.State(s);if(!state.InUse)continue;
                    var effect=set.Effects(s);var p=state.Current;
                    if(!actor.TryGetSocketWorld(state.Socket,out var root))continue;
                    effect.ObserveRoot(root.x,root.y,root.z);
                    if(state.Visible(out float t0,out float t1,out float alphaMul,out bool knot)&&strings<MaxStrings)
                    {
                        int rows=Mathf.Clamp(p.Segments+1,2,RowsPerString);baseRemaining-=rows*2;
                        if(vertexCount+rows*2>MaxVertices)break;
                        float viewY=Vector3.Dot(root-camPos,up),depth=Vector3.Dot(root-camPos,forward);
                        float halfHeight=camera.orthographic?camera.orthographicSize:Mathf.Max(0,depth)*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad);
                        float height=halfHeight*1.06f+TopMargin-viewY;
                        float time=FixedTime>=0?FixedTime:effect.Time;
                        PNVisualStringPath.Sample(p,height,actor.FacingLeft,time,state.Seed,t0,t1,rows,rowX,rowY,rowW);
                        float bend=Vector3.Dot(new Vector3(effect.BendX,0,effect.BendZ),right)*(p.LeaveDistance>.001f?1.25f:.8f);
                        for(int k=0;k<rows;k++)
                        {
                            float along=t0+(t1-t0)*k/(rows-1),d=along-effect.PulseFront;
                            float wave=effect.Pulse>0?effect.Pulse*Mathf.Exp(-d*d*150)*Mathf.Sin(d*65)*.006f:0;
                            rowX[k]+=(Mathf.Abs(bend)>1e-6f?bend*Mathf.Sin(Mathf.PI*along)*(1-along)*2:0)+wave;
                        }
                        byte alpha=B(p.Alpha*alphaMul);
                        var core=new Color32(B(p.Core.R),B(p.Core.G),B(p.Core.B),alpha);
                        var halo=new Vector4(p.Halo.R,p.Halo.G,p.Halo.B,p.FadeTop);
                        var knotColor=new Vector4(p.Knot.R,p.Knot.G,p.Knot.B,0);
                        var pulse=new Vector4(effect.PulseFront,effect.Pulse,0,0);
                        int first=vertexCount;
                        for(int k=0;k<rows;k++)
                        {
                            float tx,ty;
                            if(k==0){tx=rowX[1]-rowX[0];ty=rowY[1]-rowY[0];}
                            else if(k==rows-1){tx=rowX[k]-rowX[k-1];ty=rowY[k]-rowY[k-1];}
                            else{tx=rowX[k+1]-rowX[k-1];ty=rowY[k+1]-rowY[k-1];}
                            float n=Mathf.Sqrt(tx*tx+ty*ty);if(n<1e-6f){tx=0;ty=1;n=1;}
                            float half=rowW[k]*.5f*(k==0&&knot?1.8f:1);
                            var centre=root+right*rowX[k]+up*rowY[k];var offset=right*(-ty/n*half)+up*(tx/n*half);
                            float along=t0+(t1-t0)*k/(rows-1);int v=vertexCount;
                            positions[v]=centre-offset;positions[v+1]=centre+offset;colors[v]=colors[v+1]=core;
                            uv0[v]=new Vector4(0,along,rowW[k],knot?1:0);uv0[v+1]=new Vector4(1,along,rowW[k],knot?1:0);
                            uv1[v]=uv1[v+1]=halo;uv2[v]=uv2[v+1]=knotColor;uv3[v]=uv3[v+1]=pulse;vertexCount+=2;
                        }
                        AddStripIndices(first,rows,ref indexCount);strings++;
                        if(effect.Motion>.2f&&state.Phase==PNVisualStringPhase.Attached&&vertexCount+baseRemaining+rows*2<=MaxVertices)
                        {
                            var lag=new Vector3(effect.TrailX,effect.TrailY,effect.TrailZ)-root;
                            lag=Vector3.ClampMagnitude(lag,.14f);
                            var trailColor=new Color32(B(p.Halo.R),B(p.Halo.G),B(p.Halo.B),B(p.Alpha*effect.Motion*.15f));
                            int trail=vertexCount;
                            for(int k=0;k<rows*2;k++)
                            {
                                int v=first+k;float along=uv0[v].y;
                                positions[vertexCount]=positions[v]+lag*(1-along);
                                colors[vertexCount]=trailColor;uv0[vertexCount]=uv0[v];uv0[vertexCount].w=0;
                                uv1[vertexCount]=halo;uv2[vertexCount]=knotColor;uv3[vertexCount]=Vector4.zero;vertexCount++;
                            }
                            AddStripIndices(trail,rows,ref indexCount);LastTrails++;
                        }
                    }
                    for(int d=0;d<PNVisualStringEffects.MaxDust;d++)
                    {
                        var particle=effect.Particles[d];if(!particle.Alive||vertexCount+baseRemaining+4>MaxVertices)continue;
                        float life=particle.Age/particle.Life;
                        float fade=Mathf.Clamp01(particle.Age/.06f)*(1-life)*(1-life)*.8f;
                        var centre=new Vector3(particle.X,particle.Y,particle.Z);
                        var r=right*particle.Size;var u=up*particle.Size;int v=vertexCount;
                        positions[v]=centre-r-u;positions[v+1]=centre+r-u;positions[v+2]=centre-r+u;positions[v+3]=centre+r+u;
                        var color=new Color32(B(p.Knot.R),B(p.Knot.G*.92f),B(p.Knot.B*.75f),B(p.Alpha*fade));
                        for(int k=0;k<4;k++)
                        {
                            colors[v+k]=color;uv0[v+k]=new Vector4(k%2,k/2,0,0);uv1[v+k]=Vector4.zero;
                            uv2[v+k]=new Vector4(0,0,0,particle.Star?2:1);uv3[v+k]=Vector4.zero;
                        }
                        AddStripIndices(v,2,ref indexCount);vertexCount+=4;LastDust++;
                    }
                }
            }
        }
        LastStrings=strings;LastVertices=vertexCount;
        if(vertexCount==0){if(meshRenderer!=null)meshRenderer.enabled=false;return;}
        Ensure(camera);meshRenderer.enabled=true;mesh.Clear(false);
        mesh.SetVertices(positions,0,vertexCount);mesh.SetColors(colors,0,vertexCount);
        mesh.SetUVs(0,uv0,0,vertexCount);mesh.SetUVs(1,uv1,0,vertexCount);mesh.SetUVs(2,uv2,0,vertexCount);mesh.SetUVs(3,uv3,0,vertexCount);
        mesh.SetIndices(indices,0,indexCount,MeshTopology.Triangles,0,false);mesh.bounds=new Bounds(Vector3.zero,new Vector3(4000,4000,4000));
    }
    private static void AddStripIndices(int first,int rows,ref int indexCount)
    {
        for(int k=0;k<rows-1;k++)
        {
            int v=first+k*2;indices[indexCount++]=v;indices[indexCount++]=v+1;indices[indexCount++]=v+2;
            indices[indexCount++]=v+1;indices[indexCount++]=v+3;indices[indexCount++]=v+2;
        }
    }
    private static byte B(float c) => (byte)Mathf.Clamp(Mathf.RoundToInt(c * 255f), 0, 255);
}
#endif
