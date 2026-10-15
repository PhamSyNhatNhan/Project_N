#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public sealed class PNVfxFloorFrameData : ContextItem
{
    public TextureHandle Texture;
    public override void Reset() { Texture = default; }
}
public sealed class PNVfxFloorDepthPass : ScriptableRenderPass
{
    private sealed class Data { internal Mesh[] Meshes; internal Matrix4x4[] Matrices; internal Material[] Materials; internal Matrix4x4 ViewProjection; }
    private PNVfxFloorDepth[] sources;
    public PNVfxFloorDepthPass() { renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }
    public void Setup(PNVfxFloorDepth[] value) { sources = value; }
    public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
    {
        var camera = frameData.Get<UniversalCameraData>();
        var descriptor = camera.cameraTargetDescriptor;
        var color = graph.CreateTexture(new TextureDesc(descriptor.width, descriptor.height) {
            name = "Project_N VFX Floor Depth", colorFormat = GraphicsFormat.R32_SFloat,
            clearBuffer = true, clearColor = SystemInfo.usesReversedZBuffer ? Color.black : Color.white
        });
        var z = graph.CreateTexture(new TextureDesc(descriptor.width, descriptor.height) {
            name = "Project_N VFX Floor Z", depthBufferBits = DepthBits.Depth32, clearBuffer = true
        });
        using (var builder = graph.AddRasterRenderPass<Data>("Project_N VFX-only Floor", out var data))
        {
            int count = sources.Length; data.Meshes = new Mesh[count]; data.Matrices = new Matrix4x4[count]; data.Materials = new Material[count];
            data.ViewProjection=GL.GetGPUProjectionMatrix(camera.camera.projectionMatrix,true)*camera.camera.worldToCameraMatrix;
            for (int i=0;i<count;i++) { data.Meshes[i]=sources[i].Mesh; data.Matrices[i]=sources[i].transform.localToWorldMatrix; data.Materials[i]=sources[i].Material; }
            builder.SetRenderAttachment(color,0,AccessFlags.Write); builder.SetRenderAttachmentDepth(z,AccessFlags.Write);
            builder.SetGlobalTextureAfterPass(color,Shader.PropertyToID("_PNVfxFloorDepth"));
            builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
            builder.SetRenderFunc((Data pass, RasterGraphContext context) => {
                context.cmd.SetGlobalFloat("_PNVfxFloorDepthAvailable",1);
                context.cmd.SetGlobalMatrix("_PNVfxFloorViewProjection",pass.ViewProjection);
                for(int i=0;i<pass.Meshes.Length;i++) if(pass.Meshes[i]!=null && pass.Materials[i]!=null)
                    context.cmd.DrawMesh(pass.Meshes[i],pass.Matrices[i],pass.Materials[i],0,0);
            });
        }
        frameData.GetOrCreate<PNVfxFloorFrameData>().Texture = color;
    }
}
#endif
