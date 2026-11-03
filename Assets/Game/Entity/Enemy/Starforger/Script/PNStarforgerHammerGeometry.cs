#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;

// Closed, flat-normal chamfered volumes. No outline planes or pack mesh/code.
public static class PNStarforgerHammerGeometry
{
    public const float HeadY=.13f;
    public const float StrikeFaceX=.126f+.047f*.5f;
    public static Mesh Build()
    {
        var v=new List<Vector3>();var c=new List<Color>();var t=new List<int>();
        var gold=new Color(.86f,.54f,.15f);var pale=new Color(1.05f,.78f,.32f);var dark=new Color(.44f,.24f,.07f);
        Box(v,c,t,new Vector3(0,-.015f,0),new Vector3(.041f,.25f,.041f),.005f,dark,gold);
        Box(v,c,t,new Vector3(0,.055f,0),new Vector3(.065f,.065f,.064f),.008f,gold,pale);
        Box(v,c,t,new Vector3(0,HeadY,0),new Vector3(.27f,.15f,.135f),.014f,gold,pale);
        Box(v,c,t,new Vector3(-.126f,HeadY,0),new Vector3(.047f,.162f,.146f),.009f,gold,pale);
        Box(v,c,t,new Vector3(.126f,HeadY,0),new Vector3(.047f,.162f,.146f),.009f,gold,pale);
        Box(v,c,t,new Vector3(0,HeadY,-.069f),new Vector3(.025f,.122f,.008f),.002f,dark,gold);
        var m=new Mesh{name="Starforger heavy chamfered hammer",hideFlags=HideFlags.HideAndDontSave};
        m.SetVertices(v);m.SetColors(c);m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();return m;
    }
    private static void Box(List<Vector3> v,List<Color> colors,List<int> triangles,Vector3 centre,Vector3 size,float bevel,Color side,Color top)
    {
        float y=size.y*.5f,z=size.z*.5f,b=Mathf.Min(bevel,Mathf.Min(y,z)*.4f);
        var cross=new[]{new Vector2(-y+b,-z),new Vector2(y-b,-z),new Vector2(y,-z+b),new Vector2(y,z-b),new Vector2(y-b,z),new Vector2(-y+b,z),new Vector2(-y,z-b),new Vector2(-y,-z+b)};
        var rings=new[]{-size.x*.5f,-size.x*.5f+b,size.x*.5f-b,size.x*.5f};
        Vector3 P(int r,int i){var q=cross[i%8]*(r==0||r==3?.88f:1);return centre+new Vector3(rings[r],q.x,q.y);}
        void Face(Vector3 a,Vector3 b0,Vector3 c0,Vector3 d,Color color)
        {int k=v.Count;v.Add(a);v.Add(b0);v.Add(c0);v.Add(d);for(int j=0;j<4;j++)colors.Add(color);triangles.Add(k);triangles.Add(k+1);triangles.Add(k+2);triangles.Add(k);triangles.Add(k+2);triangles.Add(k+3);}
        for(int r=0;r<3;r++)for(int i=0;i<8;i++)Face(P(r,i),P(r+1,i),P(r+1,i+1),P(r,i+1),cross[i].x>0?top:side);
        for(int i=0;i<8;i++){Face(centre+Vector3.left*size.x*.5f,P(0,i),P(0,i+1),centre+Vector3.left*size.x*.5f,side);Face(centre+Vector3.right*size.x*.5f,P(3,i+1),P(3,i),centre+Vector3.right*size.x*.5f,top);}
    }
}
#endif
