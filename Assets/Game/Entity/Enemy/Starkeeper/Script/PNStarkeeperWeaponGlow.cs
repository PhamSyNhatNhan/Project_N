#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Cast-local full-weapon light. Borrows the authored mesh; owns only overlay materials/aura mesh.
public sealed class PNStarkeeperWeaponGlow : IDisposable
{
    private readonly Transform spear, overlay, aura;
    private readonly Material surface, halo;
    private readonly Mesh auraMesh;
    private readonly Vector3[] vertices=new Vector3[4];
    private bool disposed;
    public PNStarkeeperWeaponGlow(Transform parent,Transform spear,string resourcePrefix)
    {
        this.spear=spear;
        var shader=Resources.Load<Shader>("PNStarweaverEffects");
        if(shader==null || spear==null || spear.GetComponent<MeshFilter>()==null) throw new InvalidOperationException("Authored spear/shader required.");
        try
        {
            surface=new Material(shader){name=resourcePrefix+"weapon surface",hideFlags=HideFlags.HideAndDontSave};
            surface.SetFloat("_Mode",17);surface.SetFloat("_DstBlend",10);surface.SetColor("_Tint",new Color(.66f,.075f,1.25f,1));surface.SetFloat("_Opacity",0);
            halo=new Material(shader){name=resourcePrefix+"weapon aura",hideFlags=HideFlags.HideAndDontSave};
            halo.SetFloat("_Mode",18);halo.SetFloat("_DstBlend",1);halo.SetColor("_Tint",new Color(.8f,.16f,1.5f,1));halo.SetFloat("_Opacity",0);
            auraMesh=new Mesh{name=resourcePrefix+"weapon aura",hideFlags=HideFlags.HideAndDontSave};auraMesh.MarkDynamic();
            auraMesh.vertices=vertices;auraMesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};auraMesh.triangles=new[]{0,1,2,0,2,3};
            auraMesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            overlay=Draw(parent,"Spear purple cast glow",spear.GetComponent<MeshFilter>().sharedMesh,surface);
            aura=Draw(parent,"Spear whole weapon aura",auraMesh,halo);
        }
        catch{Dispose();throw;}
    }
    private static Transform Draw(Transform parent,string name,Mesh mesh,Material material)
    {
        var go=new GameObject(name);go.layer=parent.gameObject.layer;go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;
        r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;return go.transform;
    }
    public void Set(float strength,float age,Camera camera,float surfaceStrength=-1)
    {
        if(disposed || spear==null || camera==null)return;
        strength=Mathf.Clamp01(strength);
        overlay.SetPositionAndRotation(spear.position,spear.rotation);overlay.localScale=spear.lossyScale*1.025f;
        surface.SetFloat("_Opacity",(surfaceStrength<0?strength:Mathf.Clamp01(surfaceStrength))*.86f);surface.SetFloat("_FxTime",age);
        halo.SetFloat("_Opacity",strength*.75f);halo.SetFloat("_FxTime",age);
        var bottom=spear.TransformPoint(new Vector3(0,-.25f,0));var top=spear.TransformPoint(new Vector3(0,.29f,0));
        var projected=Vector3.ProjectOnPlane(top-bottom,camera.transform.forward);var direction=projected.sqrMagnitude>.00001f?projected.normalized:camera.transform.up;
        var side=Vector3.Cross(direction,camera.transform.forward).normalized*.075f;
        bottom-=direction*.055f;top+=direction*.055f;
        vertices[0]=bottom-side;vertices[1]=bottom+side;vertices[2]=top+side;vertices[3]=top-side;
        auraMesh.vertices=vertices;auraMesh.RecalculateBounds();
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(overlay!=null)Destroy(overlay.gameObject);if(aura!=null)Destroy(aura.gameObject);
        Destroy(surface);Destroy(halo);Destroy(auraMesh);
    }
    private static void Destroy(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
}
#endif
