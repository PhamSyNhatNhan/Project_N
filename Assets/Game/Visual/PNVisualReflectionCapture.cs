#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PNVisualReflectionCapture:IDisposable
{
    public RenderTexture Texture {get;private set;}
    public Vector3 Center {get;private set;} public Vector3 Size {get;private set;}
    public static int ActiveCount {get;private set;}
    private bool counted;
    private PNVisualReflectionCapture(){}
    public static PNVisualReflectionCapture Capture(PNVisualActor actor,Camera camera)
    {
        if(actor==null || camera==null || actor.Mesh==null || actor.Renderer==null)return null;
        actor.Tick(0); // write the current pose, including an actor culled last frame; never advance time
        var vertices=actor.Mesh.vertices;var colors=actor.Mesh.colors32;
        var right=camera.transform.right;var up=camera.transform.up;var forward=camera.transform.forward;
        float minX=float.PositiveInfinity,minY=minX,maxX=float.NegativeInfinity,maxY=maxX,depth=0;int count=0;
        for(int i=0;i<vertices.Length;i++)
        {
            if(colors.Length==vertices.Length && colors[i].a==0)continue;
            var point=actor.transform.TransformPoint(vertices[i]);float x=Vector3.Dot(point,right),y=Vector3.Dot(point,up);
            minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);depth+=Vector3.Dot(point,forward);count++;
        }
        if(count==0 || !float.IsFinite(minX) || maxX-minX<.0001f || maxY-minY<.0001f)return null;
        float width=(maxX-minX)*1.06f,height=(maxY-minY)*1.06f;
        var result=new PNVisualReflectionCapture{Center=right*((minX+maxX)*.5f)+up*((minY+maxY)*.5f)+forward*(depth/count),Size=new Vector3(width,height,.01f)};
        int w=width>=height?256:Mathf.Max(16,Mathf.RoundToInt(256*width/height));int h=height>=width?256:Mathf.Max(16,Mathf.RoundToInt(256*height/width));
        var material=actor.Renderer.sharedMaterial;int pass=material.FindPass("LoricaSnapshot");
        if(pass<0)throw new InvalidOperationException("VisualActor LoricaSnapshot pass missing.");
        var previous=RenderTexture.active;CommandBuffer commands=null;
        try
        {
            result.Texture=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){name="Lorica rig reflection",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};if(!result.Texture.Create())throw new InvalidOperationException("Unable to create Lorica reflection target.");ActiveCount++;result.counted=true;
            var view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(result.Center-forward*2,camera.transform.rotation,Vector3.one).inverse;
            var projection=GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-width*.5f,width*.5f,-height*.5f,height*.5f,.01f,4),false);
            var properties=new MaterialPropertyBlock();actor.Renderer.GetPropertyBlock(properties);
            commands=new CommandBuffer{name="Lorica rig snapshot"};commands.SetRenderTarget(result.Texture);commands.SetViewport(new Rect(0,0,w,h));commands.ClearRenderTarget(false,true,Color.clear);commands.SetViewProjectionMatrices(view,projection);
            commands.DrawMesh(actor.Mesh,actor.transform.localToWorldMatrix,material,0,pass,properties);
            commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,camera.targetTexture!=null));Graphics.ExecuteCommandBuffer(commands);
            return result;
        }
        catch{result.Dispose();throw;}
        finally{commands?.Release();RenderTexture.active=previous;}
    }
    public void Dispose()
    {
        var texture=Texture;Texture=null;if(texture==null)return;texture.Release();if(Application.isPlaying)UnityEngine.Object.Destroy(texture);else UnityEngine.Object.DestroyImmediate(texture);if(counted)ActiveCount--;counted=false;
    }
}
#endif
