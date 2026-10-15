#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

// Runtime pass injection follows URP's supplied BlitAndSwapColor sample.
// No authored renderer asset or global Volume is modified.
public static class PNTimeScaleAreaRendering
{
    private static readonly HashSet<PNTimeScaleArea> areas = new HashSet<PNTimeScaleArea>();
    private static readonly Dictionary<PNTimeScaleArea, long> dustOrder = new Dictionary<PNTimeScaleArea, long>();
    private static long sequence;
    private static readonly Dictionary<Camera, AreaPass> passes = new Dictionary<Camera, AreaPass>();
    private static bool subscribed;
    public static int RegisteredCount => areas.Count;
    public static void Register(PNTimeScaleArea area)
    {
        areas.Add(area);
        dustOrder[area] = ++sequence;
        if (!subscribed) { RenderPipelineManager.beginCameraRendering += BeginCamera; subscribed = true; }
    }
    public static void Unregister(PNTimeScaleArea area)
    {
        areas.Remove(area);
        dustOrder.Remove(area);
        if (areas.Count != 0) return;
        if (subscribed) RenderPipelineManager.beginCameraRendering -= BeginCamera;
        subscribed = false;
        foreach (var pass in passes.Values) pass.Dispose(); passes.Clear();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    { areas.Clear(); dustOrder.Clear(); sequence = 0; if (subscribed) RenderPipelineManager.beginCameraRendering -= BeginCamera; subscribed = false; foreach (var p in passes.Values) p.Dispose(); passes.Clear(); }
    public static bool HasEarlierDustOwner(PNTimeScaleArea owner, Vector3 point)
    {
        if (!dustOrder.TryGetValue(owner, out var order)) return false;
        foreach (var area in areas)
            if (area != null && area != owner && area.gameObject.activeInHierarchy && area.gameObject.scene == owner.gameObject.scene && area.VisualStrength > 0 &&
                dustOrder[area] < order && (point - area.transform.position).sqrMagnitude <= area.VisualRadius * area.VisualRadius) return true;
        return false;
    }
    private static void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        Shader.SetGlobalFloat("_PNVfxFloorDepthAvailable", 0);
        if (camera.cameraType != CameraType.Game || !camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional) || additional.renderType != CameraRenderType.Base) return;
        foreach (var dead in passes.Keys.Where(x => x == null).ToArray()) { passes[dead].Dispose(); passes.Remove(dead); }
        var visible = areas.Where(x => x != null && x.gameObject.activeInHierarchy && x.VisualStrength > 0 &&
            (camera.cullingMask & (1 << x.gameObject.layer)) != 0 && (!camera.scene.IsValid() || camera.scene == x.gameObject.scene)).ToArray();
        if (visible.Length == 0) return;
        if (!passes.TryGetValue(camera, out var pass)) passes.Add(camera, pass = new AreaPass());
        var floors = PNVfxFloorDepth.ForCamera(camera);
        if (floors.Length > 0) { pass.FloorPass.Setup(floors); additional.scriptableRenderer.EnqueuePass(pass.FloorPass); }
        pass.Setup(visible, camera);
        additional.scriptableRenderer.EnqueuePass(pass);
    }
    private sealed class AreaPass : ScriptableRenderPass
    {
        private sealed class PassData { internal TextureHandle Source, Depth, Floor; internal Material Material; }
        private Material material;
        internal readonly PNVfxFloorDepthPass FloorPass = new PNVfxFloorDepthPass();
        private GraphicsBuffer spheres;
        private Vector4[] data;
        public AreaPass()
        {
            var shader = Resources.Load<Shader>("PNVfx/TimeScaleArea");
            material = CoreUtils.CreateEngineMaterial(shader);
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        public void Setup(PNTimeScaleArea[] areas, Camera camera)
        {
            material.SetMatrix("_AreaViewProjection", GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);
            material.SetMatrix("_AreaInvViewProjection", (GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix).inverse);
            int count = areas.Length * 7;
            if (data == null || data.Length < count)
            { spheres?.Dispose(); data = new Vector4[Mathf.NextPowerOfTwo(count)]; spheres = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, 16); }
            for (int i = 0; i < areas.Length; i++)
            {
                var p = areas[i].transform.position;
                var visual = areas[i].VisualSettings;
                var filter = visual.Filter; var vignette = visual.VignetteColor;
                data[i * 7] = new Vector4(p.x, p.y, p.z, areas[i].VisualRadius);
                data[i * 7 + 1] = new Vector4(areas[i].VisualStrength, visual.Saturation / 100f, visual.Chromatic * .05f, visual.Vignette);
                data[i * 7 + 2] = new Vector4(filter.r, filter.g, filter.b, visual.Smoothness * 5);
                data[i * 7 + 3] = new Vector4(vignette.r, vignette.g, vignette.b, 0);
                data[i * 7 + 4] = new Vector4(visual.TintStrength, visual.EdgeDistortion, areas[i].DustTime, 0);
                data[i * 7 + 5] = new Vector4(visual.RimIntensity, visual.RimWidth, visual.RimPulse, 0);
                data[i * 7 + 6] = new Vector4(visual.TwistStrength, visual.TwistSpeed, 0, 0);
            }
            var textures = Resources.Load<PNTimeScaleTextureReferences>("PNVfx/TimeScaleTextures");
            material.SetTexture("_AreaNoise", textures != null ? textures.Noise : Texture2D.grayTexture);
            spheres.SetData(data); material.SetBuffer("_AreaSpheres", spheres); material.SetInt("_AreaCount", areas.Length);
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>(); if (resources.isActiveTargetBackBuffer) return;
            var descriptor = graph.GetTextureDesc(resources.activeColorTexture); descriptor.name = "Project_N Timescale Sphere Union"; descriptor.clearBuffer = false;
            var destination = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<PassData>("Project_N Timescale Area", out var data))
            {
                data.Source = resources.activeColorTexture; data.Depth = resources.cameraDepthTexture; data.Material = material;
                data.Floor = frameData.GetOrCreate<PNVfxFloorFrameData>().Texture;
                if (data.Floor.IsValid()) builder.UseTexture(data.Floor, AccessFlags.Read);
                builder.UseTexture(data.Source, AccessFlags.Read);
                builder.UseTexture(data.Depth, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData pass, RasterGraphContext context) => {
                    context.cmd.SetGlobalTexture(Shader.PropertyToID("_CameraDepthTexture"), pass.Depth);
                    context.cmd.SetGlobalFloat("_PNVfxFloorDepthAvailable", pass.Floor.IsValid() ? 1 : 0);
                    if (pass.Floor.IsValid()) context.cmd.SetGlobalTexture(Shader.PropertyToID("_PNVfxFloorDepth"), pass.Floor);
                    Blitter.BlitTexture(context.cmd, pass.Source, new Vector4(1,1,0,0), pass.Material, 0);
                });
            }
            resources.cameraColor = destination;
        }
        public void Dispose() { spheres?.Dispose(); spheres = null; CoreUtils.Destroy(material); material = null; }
    }
}
#endif
