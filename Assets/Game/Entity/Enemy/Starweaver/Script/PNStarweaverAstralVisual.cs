using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Visual-only assembly: cloth model, moving cosmic layers and a split crystal focus.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class PNStarweaverAstralVisual : MonoBehaviour
{
    [SerializeField] private Shader astralShader;
    [SerializeField] private Shader energyShader;
    [SerializeField] private Shader modelShader;
    [SerializeField] private Texture2D flowNoise;
    [SerializeField] private Mesh hoodModel;
    [SerializeField] private Mesh hoodBrimModel;
    [SerializeField] private Mesh hoodDecorationModel;
    [SerializeField] private Mesh bootClothModel;
    [SerializeField] private Mesh bootToeModel;
    [SerializeField] private Mesh focusHalfModel;
    [SerializeField] private Transform lowerBodyAnchor;
    [SerializeField] private Vector2 hoodSize=new Vector2(1.65f,1.65f);
    [SerializeField] private Vector3 hoodOffset=new Vector3(-.05f,.08f,.08f);
    [SerializeField] private Vector3 focusOffset=new Vector3(1.28f,-.30f,-.05f);
    [SerializeField,Min(.1f)] private float focusSize=.8f;
    [SerializeField,Min(0)] private float animationSpeed=1;
    [SerializeField,ColorUsage(false,true)] private Color energyColor=new Color(.62f,.22f,1.2f,.8f);
    [SerializeField] private Vector3 bootsOffset=new Vector3(0,-.02f,-.22f);

    readonly List<Object> owned=new List<Object>();
    Transform generated,hood,focus,upper,lower,boots,orbit;
    readonly Transform[] sparks=new Transform[5];
    readonly Mesh[] orbitMeshes=new Mesh[2];
    readonly Vector3[] orbitVertices=new Vector3[130];
    Material cosmos,overlay,energy,starMaterial,coreMaterial;
    double epoch;
#if UNITY_EDITOR
    double nextEditorFrame;
#endif
    static double Clock
    {
        get
        {
#if UNITY_EDITOR
            if(!Application.isPlaying)return UnityEditor.EditorApplication.timeSinceStartup;
#endif
            return Time.timeAsDouble;
        }
    }
    void OnEnable()
    {
        epoch=Clock;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update+=EditorTick;
#endif
        if(Ready)Build();
    }
    bool Ready=>astralShader!=null&&energyShader!=null&&modelShader!=null;
#if UNITY_EDITOR
    void EditorTick()
    {
        if(Application.isPlaying||!isActiveAndEnabled||Clock<nextEditorFrame)return;
        nextEditorFrame=Clock+1d/30;
        LateUpdate();UnityEditor.SceneView.RepaintAll();
    }
#endif
    void LateUpdate(){RenderAt((float)(Clock-epoch)*animationSpeed);}
    public void RenderAt(float seconds)
    {
        if(generated==null){if(!Ready)return;Build();}
        cosmos.SetFloat("_Phase",seconds);overlay.SetFloat("_Phase",seconds);
        energy.SetColor("_Tint",energyColor);starMaterial.SetColor("_Tint",energyColor);coreMaterial.SetColor("_Tint",energyColor);
        hood.localPosition=hoodOffset;
        hood.localScale=new Vector3(hoodSize.x/1.65f,hoodSize.y/1.65f,hoodSize.x/1.65f);
        hood.localRotation=Quaternion.Euler(0,Mathf.Sin(seconds*.4f)*2,Mathf.Sin(seconds*.55f)*1.2f);
        focus.localPosition=focusOffset+Vector3.up*(.045f*Mathf.Sin(seconds*.8f));
        focus.localScale=Vector3.one*focusSize;
        focus.localRotation=Quaternion.Euler(0,18+seconds*12,0);
        upper.localPosition=Vector3.up*(.045f+.012f*Mathf.Sin(seconds));
        lower.localPosition=-upper.localPosition;
        Vector3 foot=lowerBodyAnchor!=null?transform.InverseTransformPoint(lowerBodyAnchor.TransformPoint(new Vector3(0,-.37f,-.02f))):new Vector3(-.12f,-1.3f,.12f);
        boots.localPosition=foot+bootsOffset;
        for(int i=0;i<sparks.Length;i++)
        {
            float a=seconds*.65f+i*2.39996f;
            float radius=.32f;
            sparks[i].localPosition=focus.localPosition+new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius*.75f,-.16f+Mathf.Sin(a)*.12f);
            float size=.075f*(.6f+.4f*Mathf.Sin(a*1.7f)*Mathf.Sin(a*1.7f));
            sparks[i].localScale=new Vector3(size,size*1.4f,size);
        }
        for(int link=0;link<2;link++)
        {
            for(int j=0;j<=64;j++)
            {
                float u=j/64f;
                float a=u*Mathf.PI*2+seconds*.7f+link*Mathf.PI;
                Vector3 p=new Vector3(Mathf.Cos(a)*.72f,Mathf.Sin(a)*.40f,Mathf.Sin(a)*.28f);
                p=Quaternion.Euler(0,0,32)*p;
                Vector3 side=new Vector3(-Mathf.Sin(a),Mathf.Cos(a),0)*(.022f*(link==0?1:.55f));
                orbitVertices[j*2]=p+side;orbitVertices[j*2+1]=p-side;
            }
            orbitMeshes[link].vertices=orbitVertices;orbitMeshes[link].RecalculateBounds();
        }
    }
    T Own<T>(T value)where T:Object{value.hideFlags=HideFlags.HideAndDontSave;owned.Add(value);return value;}
    Transform Empty(string label,Transform parent)
    {
        var go=Own(new GameObject(label));go.layer=gameObject.layer;go.transform.SetParent(parent,false);return go.transform;
    }
    Transform Part(string label,Mesh mesh,Material material,int order,Transform parent)
    {
        var t=Empty(label,parent);
        t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.sortingOrder=order;
        r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return t;
    }
    Material Model(string label,Color color)
    {
        var m=Own(new Material(modelShader){name=label});m.SetColor("_BaseColor",color);return m;
    }
    Material Cloud(string label,float mode)
    {
        var m=Own(new Material(astralShader){name=label});m.SetFloat("_Mode",mode);
        if(flowNoise!=null)m.SetTexture("_NoiseTex",flowNoise);return m;
    }
    Material Light(string label,float mode)
    {
        var m=Own(new Material(energyShader){name=label});m.SetFloat("_Glow",mode);m.SetColor("_Tint",energyColor);return m;
    }
    void Build()
    {
        Release();generated=Empty("Astral generated presentation",transform);
        cosmos=Cloud("Cosmos behind mask",0);overlay=Cloud("Nebula over cloth model",1);
        energy=Light("Violet orbital ribbons",0);starMaterial=Light("Soft four-point sparks",2);coreMaterial=Light("Split crystal core glow",3);
        var cloth=Model("Purple-black low-poly cloth",new Color(.075f,.06f,.14f,1));
        var ivory=Model("Ivory hood lining",new Color(.94f,.85f,.74f,1));
        var gold=Model("Faceted pale gold crystal",new Color(.91f,.83f,.70f,1));
        var quad=Own(PNStarweaverAstralGeometry.Quad());
        var cap=hoodModel!=null?hoodModel:Own(PNStarweaverAstralGeometry.Hood());
        var brim=hoodBrimModel!=null?hoodBrimModel:Own(PNStarweaverAstralGeometry.Brim());
        var crystal=focusHalfModel!=null?focusHalfModel:Own(PNStarweaverAstralGeometry.HalfFocus());
        var star=Own(PNStarweaverAstralGeometry.Star());
        hood=Empty("3D hood model",generated);
        Part("Bent cloth shell",cap,cloth,0,hood);
        Part("Cosmic surface overlay",cap,overlay,1,hood);
        Part("Ivory opening rim",brim,ivory,3,hood);
        if(hoodDecorationModel!=null)Part("Blender hood embroidery",hoodDecorationModel,gold,3,hood);
        var inner=Part("Inner cosmic darkness",quad,cosmos,1,hood);
        inner.localPosition=new Vector3(0,0,.15f);inner.localScale=new Vector3(1.16f,1.22f,1);
        if(hoodDecorationModel==null)
        {
            Part("Hood constellation thread",Own(PNStarweaverAstralGeometry.Constellation()),gold,3,hood);
            Ornament("Hood star",star,gold,new Vector3(-.47f,.84f,.01f),.20f,hood);
            Ornament("Upper hood star",star,gold,new Vector3(.12f,1.04f,.13f),.12f,hood);
            Ornament("Hanging tip star",star,gold,new Vector3(-1.16f,-.22f,.05f),.12f,hood);
        }
        boots=Empty("3D shoes",generated);
        if(bootClothModel!=null)Part("Indigo shoe cloth",bootClothModel,cloth,0,boots);
        if(bootToeModel!=null)Part("Ivory pointed toes",bootToeModel,ivory,0,boots);
        var eye=Part("Left eye light",quad,starMaterial,1,generated);eye.localPosition=new Vector3(-.12f,-.10f,.012f);eye.localScale=new Vector3(.09f,.20f,1);
        eye=Part("Right eye light",quad,starMaterial,1,generated);eye.localPosition=new Vector3(.315f,-.08f,.012f);eye.localScale=new Vector3(.075f,.18f,1);
        focus=Empty("Split diamond focus",generated);
        upper=Part("Upper half diamond",crystal,gold,3,focus);
        lower=Part("Lower half diamond",crystal,gold,3,focus);lower.localRotation=Quaternion.Euler(0,0,180);
        var core=Part("Core glow",quad,coreMaterial,4,focus);core.localScale=Vector3.one*.55f;
        var coreStar=Part("Core star",quad,starMaterial,5,focus);coreStar.localScale=new Vector3(.18f,.30f,1);
        orbit=Empty("Orbiting light",focus);
        for(int i=0;i<2;i++)
        {
            var mesh=Own(new Mesh{name="Curved focus orbit "+i});mesh.MarkDynamic();
            var uv=new Vector2[130];var normals=new Vector3[130];var indices=new int[384];
            for(int j=0;j<=64;j++){uv[j*2]=new Vector2(j/64f,0);uv[j*2+1]=new Vector2(j/64f,1);normals[j*2]=normals[j*2+1]=Vector3.back;}
            for(int j=0;j<64;j++){int v=j*2,k=j*6;indices[k]=v;indices[k+1]=v+2;indices[k+2]=v+1;indices[k+3]=v+1;indices[k+4]=v+2;indices[k+5]=v+3;}
            mesh.vertices=orbitVertices;mesh.uv=uv;mesh.normals=normals;mesh.triangles=indices;orbitMeshes[i]=mesh;
            Part("Orbital ribbon "+i,mesh,energy,4,orbit);
        }
        for(int i=0;i<sparks.Length;i++)sparks[i]=Part("Focus mote "+i,quad,starMaterial,4,generated);
        RenderAt(0);
    }
    void Ornament(string name,Mesh mesh,Material material,Vector3 position,float size,Transform parent)
    {
        var t=Part(name,mesh,material,3,parent);t.localPosition=position;t.localScale=Vector3.one*size;
    }
    void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update-=EditorTick;
#endif
        Release();
    }
    void OnDestroy()=>Release();
    void Release()
    {
        for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null){if(Application.isPlaying)Destroy(owned[i]);else DestroyImmediate(owned[i]);}
        owned.Clear();generated=null;hood=null;focus=null;upper=null;lower=null;boots=null;orbit=null;
        cosmos=null;overlay=null;energy=null;starMaterial=null;coreMaterial=null;
        for(int i=0;i<sparks.Length;i++)sparks[i]=null;for(int i=0;i<2;i++)orbitMeshes[i]=null;
    }
}
