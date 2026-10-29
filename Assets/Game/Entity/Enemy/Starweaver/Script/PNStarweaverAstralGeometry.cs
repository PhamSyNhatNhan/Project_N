using System.Collections.Generic;
using UnityEngine;

// Low-poly authored shapes for the Starweaver presentation. No gameplay or colliders.
internal static class PNStarweaverAstralGeometry
{
    private sealed class Builder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> triangles = new List<int>();
        public void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            int k=vertices.Count;
            vertices.Add(a);vertices.Add(b);vertices.Add(c);
            uv.Add(new Vector2(a.x*.4f+.5f,a.y*.4f+.3f));
            uv.Add(new Vector2(b.x*.4f+.5f,b.y*.4f+.3f));
            uv.Add(new Vector2(c.x*.4f+.5f,c.y*.4f+.3f));
            triangles.Add(k);triangles.Add(k+1);triangles.Add(k+2);
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        { Triangle(a,b,c);Triangle(c,b,d); }
        public Mesh Finish(string name)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);
            mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }

    public static Mesh Hood()
    {
        var centres=new[]{new Vector3(0,.02f,.12f),new Vector3(-.10f,.25f,.43f),
            new Vector3(-.35f,.59f,.47f),new Vector3(-.79f,.64f,.33f),
            new Vector3(-1.08f,.33f,.19f),new Vector3(-1.16f,-.02f,.10f)};
        var radii=new[]{new Vector2(.71f,.73f),new Vector2(.83f,.83f),
            new Vector2(.71f,.68f),new Vector2(.45f,.43f),new Vector2(.19f,.30f),new Vector2(.025f,.045f)};
        const int segments=24;
        var b=new Builder();
        for(int ring=0;ring<centres.Length-1;ring++)for(int j=0;j<segments;j++)
        {
            float a=j*Mathf.PI*2/segments,n=(j+1)*Mathf.PI*2/segments;
            Vector3 p=centres[ring]+new Vector3(Mathf.Cos(a)*radii[ring].x,Mathf.Sin(a)*radii[ring].y,0);
            Vector3 q=centres[ring]+new Vector3(Mathf.Cos(n)*radii[ring].x,Mathf.Sin(n)*radii[ring].y,0);
            Vector3 u=centres[ring+1]+new Vector3(Mathf.Cos(a)*radii[ring+1].x,Mathf.Sin(a)*radii[ring+1].y,0);
            Vector3 v=centres[ring+1]+new Vector3(Mathf.Cos(n)*radii[ring+1].x,Mathf.Sin(n)*radii[ring+1].y,0);
            b.Quad(p,q,u,v);
        }
        return b.Finish("Starweaver bent cloth hood 3D");
    }

    public static Mesh Brim()
    {
        var b=new Builder();
        const int segments=40;
        for(int j=0;j<segments;j++)
        {
            float a=j*Mathf.PI*2/segments,n=(j+1)*Mathf.PI*2/segments;
            b.Quad(Rim(a,false),Rim(n,false),Rim(a,true),Rim(n,true));
        }
        return b.Finish("Starweaver ivory hood opening");
    }
    static Vector3 Rim(float a,bool inner)
    {
        float x=Mathf.Cos(a),y=Mathf.Sin(a);
        return new Vector3(x*(inner?.63f:.72f),.02f+y*(inner?.65f:.74f)+x*.12f,
            inner?-.14f:.10f);
    }

    public static Mesh Constellation()
    {
        var b=new Builder();
        var points=new[]{new Vector3(-1.02f,.38f,-.015f),new Vector3(-.83f,.68f,-.015f),
            new Vector3(-.47f,.84f,-.015f),new Vector3(-.18f,1.02f,.07f),new Vector3(.12f,1.04f,.12f),new Vector3(.42f,.78f,.09f)};
        for(int j=0;j<points.Length-1;j++)
        {
            var delta=(points[j+1]-points[j]).normalized;
            var side=new Vector3(-delta.y,delta.x,0)*.004f;
            b.Quad(points[j]+side,points[j]-side,points[j+1]+side,points[j+1]-side);
        }
        return b.Finish("Starweaver hood constellation thread");
    }

    public static Mesh HalfFocus()
    {
        // A faceted square pyramid with a broad closed base and a narrow tip.
        var b=new Builder();
        var tip=new Vector3(0,-.02f,0);var centre=new Vector3(0,.72f,0);
        var ring=new[]{new Vector3(-.46f,.72f,-.32f),new Vector3(.46f,.72f,-.32f),
            new Vector3(.46f,.72f,.32f),new Vector3(-.46f,.72f,.32f)};
        for(int j=0;j<4;j++)
        {
            int n=(j+1)%4;
            Vector3 middle=Vector3.Lerp((ring[j]+ring[n])*.5f,tip,.34f);
            middle+=((ring[j]+ring[n])*.5f-centre)*.10f;
            b.Triangle(ring[j],ring[n],middle);b.Triangle(ring[n],tip,middle);b.Triangle(tip,ring[j],middle);
            b.Triangle(centre,ring[n],ring[j]);
        }
        return b.Finish("Starweaver faceted half diamond");
    }

    public static Mesh Star()
    {
        var b=new Builder();
        var p=new Vector3[8];
        for(int j=0;j<8;j++)
        {
            float a=j*Mathf.PI*.25f;
            float r=j%2==0?1:.22f;
            p[j]=new Vector3(Mathf.Sin(a)*r*.65f,Mathf.Cos(a)*r,0);
        }
        for(int j=0;j<8;j++)
        {
            b.Triangle(new Vector3(0,0,-.15f),p[j],p[(j+1)%8]);
            b.Triangle(new Vector3(0,0,.08f),p[(j+1)%8],p[j]);
        }
        return b.Finish("Starweaver four point faceted star");
    }

    public static Mesh Quad()
    {
        var mesh=new Mesh{name="Starweaver VFX plane"};
        mesh.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f)};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
        mesh.triangles=new[]{0,2,1,1,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
}
