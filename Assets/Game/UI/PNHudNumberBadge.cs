#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.UIElements;

// One interpolated alpha mesh behind a separate sharp text child; no owned resources.
internal sealed class PNHudNumberBadge : VisualElement
{
    public PNHudNumberBadge()
    {
        pickingMode=PickingMode.Ignore;
        AddToClassList("ability-number-badge");
        generateVisualContent+=Draw;
    }
    private void Draw(MeshGenerationContext context)
    {
        float width=layout.width,height=layout.height;
        if(width<=0 || height<=0)return;
        int columns=Mathf.Clamp(Mathf.CeilToInt(width),1,128);
        int rows=Mathf.Clamp(Mathf.CeilToInt(height),1,64);
        var mesh=context.Allocate((columns+1)*(rows+1),columns*rows*6);
        float radius=Mathf.Min(width,height)*.5f;
        var half=new Vector2(width*.5f,height*.5f);
        var straight=half-Vector2.one*radius;
        for(int y=0;y<=rows;y++)
            for(int x=0;x<=columns;x++)
            {
                var point=new Vector2(width*x/columns,height*y/rows);
                var offset=point-half;
                var q=new Vector2(Mathf.Abs(offset.x),Mathf.Abs(offset.y))-straight;
                float distance=new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0)-radius;
                float alpha=.30f*Mathf.SmoothStep(0,1,Mathf.Clamp01(-distance/5));
                mesh.SetNextVertex(new Vertex { position=new Vector3(point.x,point.y,Vertex.nearZ),tint=new Color(184/255f,192/255f,196/255f,alpha) });
            }
        for(int y=0;y<rows;y++)
            for(int x=0;x<columns;x++)
            {
                int a=y*(columns+1)+x,b=a+columns+1;
                mesh.SetNextIndex((ushort)a); mesh.SetNextIndex((ushort)b); mesh.SetNextIndex((ushort)(a+1));
                mesh.SetNextIndex((ushort)(a+1)); mesh.SetNextIndex((ushort)b); mesh.SetNextIndex((ushort)(b+1));
            }
    }
}
#endif