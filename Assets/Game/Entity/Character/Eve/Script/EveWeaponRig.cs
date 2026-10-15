using System;
using Newtonsoft.Json.Linq;

public sealed class EveWeaponSettings
{
    public float HandHeight, HandOffset, HandSideOffset, ChargeLength, ChargeAngle, BladeWidth, BladeDepth, BaseLength, GuardLength, GuardWidth, GuardDepth, GripLength, GripWidth, GripDepth, JunctionSize, JunctionGlow, NormalGlow, BurstGlow;
    public static EveWeaponSettings Parse(JToken token)
    {
        if (!(token is JObject obj)) throw new FormatException("Eve energy blade settings are required.");
        var result = new EveWeaponSettings();
        foreach (var field in typeof(EveWeaponSettings).GetFields())
        {
            string key=char.ToLowerInvariant(field.Name[0])+field.Name.Substring(1);
            if(obj[key]==null || (obj[key].Type!=JTokenType.Integer && obj[key].Type!=JTokenType.Float))
                throw new FormatException("Missing weapon field: "+key);
            float value=obj[key].Value<float>();
            if(float.IsNaN(value)||float.IsInfinity(value)||Math.Abs(value)>1000)throw new FormatException("Invalid weapon field: "+key);
            field.SetValue(result,value);
        }
        if(result.GripLength<=0 || result.GripWidth<=0 || result.GripDepth<=0 || result.JunctionSize<=0 || result.JunctionGlow<0 || result.JunctionGlow>1 || result.GuardLength<=0 || result.GuardWidth<=0 || result.GuardDepth<=0 || result.BaseLength<=0 || result.ChargeLength<=0 || result.BladeWidth<=0 || result.BladeDepth<=0 || result.NormalGlow<0 || result.BurstGlow<0)
            throw new FormatException("Invalid energy blade dimensions.");
        foreach(var p in obj.Properties())
            if(p.Name.Length==0 || typeof(EveWeaponSettings).GetField(char.ToUpperInvariant(p.Name[0])+p.Name.Substring(1))==null)
                throw new FormatException("Unknown weapon field: "+p.Name);
        return result;
    }
}
#if UNITY_5_3_OR_NEWER
// EN: A world-space, faceted energy blade; its pose never depends on camera projection.
// VI: Lưỡi năng lượng có mặt và tiết diện trong thế giới; tư thế không phụ thuộc camera.
public sealed class EveWeaponRig : UnityEngine.MonoBehaviour
{
    private EveWeaponSettings settings;
    private UnityEngine.Transform owner, blade, extension, halo, junction;
    private UnityEngine.MeshRenderer renderer;
    private UnityEngine.Material material, energyMaterial, haloMaterial, junctionMaterial;
    private UnityEngine.Mesh mesh, energyMesh, junctionMesh;
    private UnityEngine.Vector3 pivot, direction, planeNormal;
    private float length, visibility=1, charging;
    private bool striking, burst;
    public UnityEngine.Vector3 GripPosition=>blade.position;
    public UnityEngine.Vector3 BladeTip=>blade.position+direction.normalized*length;
    public UnityEngine.MeshRenderer BladeRenderer=>renderer;
    public UnityEngine.Mesh BladeMesh=>mesh;
    public bool IsStriking=>striking;
    public UnityEngine.Transform EnergyBlade=>extension;
    public static EveWeaponRig Create(UnityEngine.Transform owner,EveWeaponSettings settings)
    {
        var root=new UnityEngine.GameObject("Eve energy blade");root.layer=owner.gameObject.layer;root.transform.SetParent(owner,false);
        var rig=root.AddComponent<EveWeaponRig>();rig.owner=owner;rig.settings=settings;
        rig.blade=new UnityEngine.GameObject("Faceted blade mesh").transform;rig.blade.SetParent(root.transform,false);rig.blade.gameObject.layer=root.layer;
        rig.mesh=BuildBlade(settings);rig.blade.gameObject.AddComponent<UnityEngine.MeshFilter>().sharedMesh=rig.mesh;
        rig.renderer=rig.blade.gameObject.AddComponent<UnityEngine.MeshRenderer>();
        var shader=UnityEngine.Shader.Find("Project_N/VFX/EveBlade");
        if(shader==null){EveVfxUtility.DestroyResource(root);throw new InvalidOperationException("Missing energy blade shader.");}
        rig.material=new UnityEngine.Material(shader){hideFlags=UnityEngine.HideFlags.HideAndDontSave};
        rig.renderer.sharedMaterial=rig.material;rig.renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        rig.energyMesh=BuildEnergy();
        rig.energyMaterial=EveVfxUtility.Material(false);rig.energyMaterial.SetFloat("_Volume",1);
        rig.haloMaterial=EveVfxUtility.Material(false);rig.haloMaterial.SetFloat("_Volume",1);
        rig.extension=rig.Layer("Extended energy edge",rig.energyMaterial);
        rig.halo=rig.Layer("Blade shaped glow",rig.haloMaterial);
        rig.junctionMesh=BuildJunction();rig.junctionMaterial=EveVfxUtility.Material(true);
        rig.junction=rig.Layer("Soft four-way light junction",rig.junctionMaterial,rig.junctionMesh);
        rig.junction.localScale=UnityEngine.Vector3.one*settings.JunctionSize;
        rig.UpdatePose();return rig;
    }
    private UnityEngine.Transform Layer(string name,UnityEngine.Material layerMaterial,UnityEngine.Mesh content=null)
    {
        var child=new UnityEngine.GameObject(name);child.layer=gameObject.layer;child.transform.SetParent(blade,false);
        child.AddComponent<UnityEngine.MeshFilter>().sharedMesh=content??energyMesh;
        var surface=child.AddComponent<UnityEngine.MeshRenderer>();surface.sharedMaterial=layerMaterial;
        surface.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return child.transform;
    }
    private sealed class Geometry
    {
        private readonly System.Collections.Generic.List<UnityEngine.Vector3> vertices=new System.Collections.Generic.List<UnityEngine.Vector3>();
        private readonly System.Collections.Generic.List<UnityEngine.Color> colors=new System.Collections.Generic.List<UnityEngine.Color>();
        private readonly System.Collections.Generic.List<UnityEngine.Vector2> uv=new System.Collections.Generic.List<UnityEngine.Vector2>();
        private readonly System.Collections.Generic.List<int> triangles=new System.Collections.Generic.List<int>();
        public void Quad(UnityEngine.Vector3 a,UnityEngine.Vector3 b,UnityEngine.Vector3 c,UnityEngine.Vector3 d,UnityEngine.Color color)
        {
            int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
            for(int j=0;j<4;j++){colors.Add(color);uv.Add(new UnityEngine.Vector2(j<2?0:1,j%2));}
            triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);triangles.Add(i);triangles.Add(i+2);triangles.Add(i+3);
        }
        public void Diamond(float[] stations,float[] radii,float width,float depth,UnityEngine.Color[] faces)
        {
            var corners=new[]{UnityEngine.Vector3.up*width*.5f,UnityEngine.Vector3.forward*depth*.5f,UnityEngine.Vector3.down*width*.5f,UnityEngine.Vector3.back*depth*.5f};
            UnityEngine.Vector3 Point(int ring,int corner)=>UnityEngine.Vector3.right*stations[ring]+corners[corner]*radii[ring];
            for(int ring=0;ring<stations.Length-1;ring++)for(int side=0;side<4;side++)
                Quad(Point(ring,side),Point(ring,(side+1)%4),Point(ring+1,(side+1)%4),Point(ring+1,side),faces[side]);
            Quad(Point(0,3),Point(0,2),Point(0,1),Point(0,0),faces[2]);
        }
        public void Triangle(UnityEngine.Vector3 a,UnityEngine.Vector3 b,UnityEngine.Vector3 c,UnityEngine.Color color)
        {
            int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
            for(int j=0;j<3;j++){colors.Add(color);uv.Add(new UnityEngine.Vector2(.5f,.5f));triangles.Add(i+j);}
        }
        public void GuardDiamond(UnityEngine.Vector3 center,float length,float width,float depth,float opacity=1)
        {
            var tips=new[]{center+UnityEngine.Vector3.up*length*.5f,center-UnityEngine.Vector3.up*length*.5f};
            var ring=new[]{center+UnityEngine.Vector3.right*width*.5f,center+UnityEngine.Vector3.forward*depth*.5f,
                center-UnityEngine.Vector3.right*width*.5f,center-UnityEngine.Vector3.forward*depth*.5f};
            for(int i=0;i<4;i++)
            {
                var light=new UnityEngine.Color(1.8f,1.72f,1.25f,i%2==0?.65f:.4f);
                light.a*=opacity;
                Triangle(tips[0],ring[i],ring[(i+1)%4],light);
                Triangle(tips[1],ring[(i+1)%4],ring[i],light);
            }
        }
        public UnityEngine.Mesh Finish(string name)
        {
            var result=new UnityEngine.Mesh{name=name,hideFlags=UnityEngine.HideFlags.HideAndDontSave};
            result.SetVertices(vertices);result.SetColors(colors);result.SetUVs(0,uv);result.SetTriangles(triangles,0);result.RecalculateNormals();result.RecalculateBounds();return result;
        }
    }
    private static UnityEngine.Mesh BuildBlade(EveWeaponSettings config)
    {
        var g=new Geometry();
        g.Diamond(new[]{-.06f,0f,config.BaseLength*.13f,config.BaseLength*.78f,config.BaseLength},new[]{.08f,.3f,1f,.65f,0f},config.BladeWidth,config.BladeDepth,
            new[]{new UnityEngine.Color(1.7f,1.65f,1.3f,.65f),new UnityEngine.Color(1.4f,1.25f,.65f,.38f),new UnityEngine.Color(1.3f,1.16f,.6f,.3f),new UnityEngine.Color(1.6f,1.5f,1,.5f)});
        // Two fixed-size light diamonds project from opposite sides in the blade plane.
        for(int side=-1;side<=1;side+=2)
            g.GuardDiamond(new UnityEngine.Vector3(-.012f,side*config.GuardLength*.6f,0),config.GuardLength,config.GuardWidth,config.GuardDepth);
        // A faint, narrow light joint connects the two diamonds through the blade root.
        g.GuardDiamond(UnityEngine.Vector3.zero,config.GuardLength*.3f,config.GuardWidth*.45f,config.GuardDepth*.6f,.25f);
        g.Diamond(new[]{-config.GripLength,-config.GripLength*.52f,0f},new[]{0f,1f,0f},config.GripWidth,config.GripDepth,
            new[]{new UnityEngine.Color(1.8f,1.72f,1.25f,.65f),new UnityEngine.Color(1.5f,1.4f,.8f,.4f),new UnityEngine.Color(1.5f,1.4f,.8f,.4f),new UnityEngine.Color(1.8f,1.72f,1.25f,.65f)});
        return g.Finish("White gold blade twin guard and diamond grip");
    }
    private static UnityEngine.Mesh BuildEnergy()
    {
        var g=new Geometry();g.Diamond(new[]{0f,.08f,.82f,1f},new[]{.9f,1f,.6f,0f},1,1,
            new[]{new UnityEngine.Color(1.7f,1.65f,1.25f,.4f),new UnityEngine.Color(1.5f,1.3f,.65f,.25f),new UnityEngine.Color(1.4f,1.2f,.5f,.2f),new UnityEngine.Color(1.65f,1.5f,.9f,.32f)});
        return g.Finish("Pointed energy blade overlay");
    }
    private static UnityEngine.Mesh BuildJunction()
    {
        // Three crossed radial sheets give the joint a soft glow from different angles.
        var vertices=new System.Collections.Generic.List<UnityEngine.Vector3>();
        var uv=new System.Collections.Generic.List<UnityEngine.Vector2>();
        var colors=new System.Collections.Generic.List<UnityEngine.Color>();
        var triangles=new System.Collections.Generic.List<int>();
        void Plane(UnityEngine.Vector3 right,UnityEngine.Vector3 up)
        {
            int i=vertices.Count;vertices.Add((-right-up)*.5f);vertices.Add((right-up)*.5f);
            vertices.Add((right+up)*.5f);vertices.Add((-right+up)*.5f);
            uv.Add(new UnityEngine.Vector2(0,0));uv.Add(new UnityEngine.Vector2(1,0));uv.Add(new UnityEngine.Vector2(1,1));uv.Add(new UnityEngine.Vector2(0,1));
            for(int j=0;j<4;j++)colors.Add(new UnityEngine.Color(1.6f,1.5f,1.05f,1));
            triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);triangles.Add(i);triangles.Add(i+2);triangles.Add(i+3);
        }
        Plane(UnityEngine.Vector3.right,UnityEngine.Vector3.up);
        Plane(UnityEngine.Vector3.right,UnityEngine.Vector3.forward);
        Plane(UnityEngine.Vector3.up,UnityEngine.Vector3.forward);
        var mesh=new UnityEngine.Mesh{name="Soft radial light junction",hideFlags=UnityEngine.HideFlags.HideAndDontSave};
        mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    public UnityEngine.Vector3 HandOffset(UnityEngine.Vector3 aim)
    {
        aim.y=0;aim.Normalize();
        return aim*settings.HandOffset+UnityEngine.Vector3.Cross(aim,UnityEngine.Vector3.up)*settings.HandSideOffset;
    }
    public void Strike(UnityEngine.Vector3 pivot,UnityEngine.Vector3 direction,float length,float alpha,UnityEngine.Vector3 planeNormal)
    {striking=true;this.pivot=pivot;this.direction=direction;this.length=length;this.planeNormal=planeNormal;visibility=alpha;UpdatePose();}
    public void EndStrike(){striking=false;visibility=1;UpdatePose();}
    public void SetBurst(bool active){burst=active;UpdatePose();}
    public void Charge(float progress){charging=UnityEngine.Mathf.Clamp01(progress);if(!striking)UpdatePose();}
    private void LateUpdate()=>UpdatePose();
    private void UpdatePose()
    {
        if(renderer==null || owner==null)return;
        renderer.enabled=striking || charging>0;
        if(!striking)
        {
            var move=owner.GetComponent<PNMove>();var aim=move!=null?new UnityEngine.Vector3(move.LastMoveDirection.x,0,move.LastMoveDirection.y):UnityEngine.Vector3.right;
            if(aim.sqrMagnitude<.0001f)aim=UnityEngine.Vector3.right;aim.Normalize();
            pivot=owner.position+UnityEngine.Vector3.up*settings.HandHeight+HandOffset(aim);
            float angle=settings.ChargeAngle*UnityEngine.Mathf.Deg2Rad;
            direction=aim*UnityEngine.Mathf.Cos(angle)+UnityEngine.Vector3.up*UnityEngine.Mathf.Sin(angle);
            planeNormal=UnityEngine.Vector3.Cross(aim,UnityEngine.Vector3.up);length=settings.ChargeLength;
        }
        blade.position=pivot;
        blade.rotation=UnityEngine.Quaternion.LookRotation(planeNormal.normalized,UnityEngine.Vector3.Cross(planeNormal,direction).normalized);
        blade.localScale=UnityEngine.Vector3.one;
        bool visible=renderer.enabled && visibility>.01f;
        extension.gameObject.SetActive(visible);halo.gameObject.SetActive(visible);junction.gameObject.SetActive(visible);
        junctionMaterial.SetColor("_Tint",new UnityEngine.Color(1,1,1,settings.JunctionGlow*visibility));
        float overlap=settings.BaseLength*.42f;
        float energyLength=UnityEngine.Mathf.Max(.01f,length-overlap);
        extension.localPosition=halo.localPosition=UnityEngine.Vector3.right*overlap;
        extension.localScale=new UnityEngine.Vector3(energyLength,settings.BladeWidth*.8f,settings.BladeDepth*.8f);
        halo.localScale=new UnityEngine.Vector3(energyLength,settings.BladeWidth*1.55f,settings.BladeDepth*1.55f);
        // Glow is shaped by the tapered mesh, not by scaling the physical sword.
        energyMaterial.SetColor("_Tint",new UnityEngine.Color(1,1,1,visibility*(burst?1.25f:1)));
        haloMaterial.SetColor("_Tint",new UnityEngine.Color(1,1,1,visibility*.18f));
        material.SetFloat("_Intensity",1+(burst?settings.BurstGlow:settings.NormalGlow)+charging*.6f);
        material.SetFloat("_Opacity",visibility);
    }
    private void OnDestroy(){EveVfxUtility.DestroyResource(mesh);EveVfxUtility.DestroyResource(material);EveVfxUtility.DestroyResource(energyMesh);EveVfxUtility.DestroyResource(energyMaterial);EveVfxUtility.DestroyResource(haloMaterial);EveVfxUtility.DestroyResource(junctionMesh);EveVfxUtility.DestroyResource(junctionMaterial);}
}
#endif

