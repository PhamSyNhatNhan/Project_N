#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;

// Continuous angular bow frame, wedged crescent and separate raised armour shells.
public static class PNStarreaperBowGeometry
{
    private sealed class Builder
    {
        public readonly List<Vector3> V=new List<Vector3>(),N=new List<Vector3>();public readonly List<Color> C=new List<Color>();public readonly List<int> T=new List<int>();
        public void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color,Vector3 outward)
        {
            var n=Vector3.Cross(b-a,c-a).normalized;if(Vector3.Dot(n,outward)<0){var q=b;b=c;c=q;n=-n;}
            int k=V.Count;V.Add(a);V.Add(b);V.Add(c);for(int i=0;i<3;i++){N.Add(n);C.Add(color);}T.Add(k);T.Add(k+1);T.Add(k+2);
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color,Vector3 outward){Triangle(a,b,c,color,outward);Triangle(a,c,d,color,outward);}
        public Mesh Finish(string name){var m=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};m.SetVertices(V);m.SetNormals(N);m.SetColors(C);m.SetTriangles(T,0);m.RecalculateBounds();return m;}
    }
    private static Vector3 P(float x,float y,float scale)=>new Vector3((790-x)*scale,(1040-y)*scale,0);
    private static void JoinedLowerLimb(Builder b,float s)
    {
        // One uninterrupted loft from the upper mount through the grip into the
        // lower blade. Every neighbour shares its full section: there is no
        // hidden shaft, end cap or separate armour crossing the lower surface.
        var outer=new[]{P(810,800,s),P(815,835,s),P(800,872,s),P(783,915,s),P(774,963,s),P(773,1016,s),P(778,1050,s),P(773,1088,s),P(713,1100,s),P(728,1147,s),P(745,1175,s),P(771,1203,s),P(800,1220,s),P(840,1234,s)};
        var inner=new[]{P(850,800,s),P(858,835,s),P(848,872,s),P(831,915,s),P(800,963,s),P(789,1016,s),P(794,1050,s),P(789,1088,s),P(779,1123,s),P(765,1137,s),P(787,1180,s),P(808,1211,s),P(823,1227,s),P(840,1234,s)};
        var rings=new Vector3[outer.Length,5];
        for(int i=0;i<outer.Length;i++)
        {
            float depth=Mathf.Min(.012f,Vector3.Distance(outer[i],inner[i])*.32f);
            var ridge=Vector3.Lerp(outer[i],inner[i],.53f);
            rings[i,0]=outer[i];rings[i,1]=ridge+Vector3.back*depth;
            rings[i,2]=inner[i]+Vector3.back*depth*.35f;
            rings[i,3]=inner[i]+Vector3.forward*depth*.35f;
            rings[i,4]=ridge+Vector3.forward*depth;
        }
        for(int i=0;i<outer.Length-1;i++)for(int j=0;j<5;j++)
        {
            int k=(j+1)%5;var centre=(outer[i]+inner[i]+outer[i+1]+inner[i+1])*.25f;
            var outward=(rings[i,j]+rings[i,k]+rings[i+1,j]+rings[i+1,k])*.25f-centre;
            var color=j==0||j==4?new Color(.52f,.18f,.73f):new Color(.38f,.095f,.56f);
            b.Quad(rings[i,j],rings[i+1,j],rings[i+1,k],rings[i,k],color,outward);
        }
        var centreCap=(outer[0]+inner[0])*.5f;
        var capOut=(outer[0]+inner[0]-outer[1]-inner[1]).normalized;
        for(int j=0;j<5;j++)b.Triangle(centreCap,rings[0,j],rings[0,(j+1)%5],new Color(.38f,.095f,.56f),capOut);
    }
    private static void Armour(Builder b,Vector3[] outline,Color color)
    {
        Vector3 centre=Vector3.zero;foreach(var p in outline)centre+=p;centre/=outline.Length;
        for(int i=0;i<outline.Length;i++)
        {
            var a=outline[i];var d=outline[(i+1)%outline.Length];var ai=Vector3.Lerp(a,centre,.10f)+Vector3.back*.007f;var di=Vector3.Lerp(d,centre,.10f)+Vector3.back*.007f;
            var crest=centre+Vector3.back*.011f;
            b.Triangle(crest,ai,di,color,Vector3.back);
            b.Quad(a,d,di,ai,color*1.08f,Vector3.back);
            b.Quad(a+Vector3.forward*.014f,d+Vector3.forward*.014f,d,a,color*.60f,(a+d)*.5f-centre);
            b.Triangle(centre+Vector3.forward*.014f,d+Vector3.forward*.014f,a+Vector3.forward*.014f,color*.7f,Vector3.forward);
        }
    }
    public static Mesh Main(float height)
    {
        float s=height/362f;var b=new Builder();
        JoinedLowerLimb(b,s);
        var outer=new[]{P(752,800,s),P(791,755,s),P(835,720,s),P(888,682,s),P(944,667,s),P(1000,661,s),P(1054,672,s),P(1104,693,s),P(1140,725,s)};
        var inner=new[]{P(775,793,s),P(805,815,s),P(860,820,s),P(878,776,s),P(938,728,s),P(978,711,s),P(1047,701,s),P(1110,714,s),P(1140,725,s)};
        var rings=new Vector3[outer.Length,5];
        for(int i=0;i<outer.Length;i++)
        {
            float thick=Mathf.Min(.018f,Vector3.Distance(outer[i],inner[i])*.24f);var ridge=Vector3.Lerp(outer[i],inner[i],.54f);
            rings[i,0]=outer[i];rings[i,1]=ridge+Vector3.back*thick;rings[i,2]=inner[i]+Vector3.back*thick*.45f;rings[i,3]=inner[i]+Vector3.forward*thick*.45f;rings[i,4]=ridge+Vector3.forward*thick;
        }
        for(int i=0;i<outer.Length-1;i++)for(int j=0;j<5;j++)
        {
            int k=(j+1)%5;var centre=(outer[i]+inner[i]+outer[i+1]+inner[i+1])*.25f;
            var outward=(rings[i,j]+rings[i,k]+rings[i+1,j]+rings[i+1,k])*.25f-centre;
            var color=j==0||j==4?new Color(.65f,.26f,.85f):new Color(.40f,.10f,.60f);
            b.Quad(rings[i,j],rings[i+1,j],rings[i+1,k],rings[i,k],color,outward);
        }
        var cap=Vector3.zero;for(int j=0;j<5;j++)cap+=rings[0,j];cap/=5;
        var capOut=(outer[0]+inner[0]-outer[1]-inner[1]).normalized;
        for(int j=0;j<5;j++)b.Triangle(cap,rings[0,j],rings[0,(j+1)%5],new Color(.30f,.07f,.45f),capOut);
        return b.Finish("Starreaper continuous angular frame and pointed limbs");
    }
    public static Mesh Plate(float height,bool outer)
    {
        float s=height/362f;var b=new Builder();var points=outer?
            new[]{P(779,850,s),P(729,916,s),P(710,993,s),P(755,971,s),P(803,903,s)}:
            new[]{P(803,903,s),P(753,969,s),P(722,1030,s),P(774,997,s),P(812,953,s),P(840,872,s)};
        Armour(b,points,outer?new Color(.47f,.13f,.68f):new Color(.34f,.075f,.51f));return b.Finish(outer?"Raised outer shoulder armour":"Raised inner shoulder armour");
    }
}
#endif
