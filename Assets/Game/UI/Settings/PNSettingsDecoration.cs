#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.UIElements;

// A single position buffer drives both nodes and threads: ribbon idle, center unfold in/out.
internal sealed class PNSettingsDecoration : VisualElement
{
    private static readonly Vector2[] Basis = { new Vector2(920,135),new Vector2(1090,252),new Vector2(970,390),new Vector2(1125,532),new Vector2(945,630),new Vector2(1175,352),new Vector2(845,473) };
    private static readonly int[] Edges = { 0,1,1,2,2,3,3,4,1,5,5,3,2,6,6,4,0,2,2,4 };
    private readonly Vector2[] points = new Vector2[7];
    private readonly VisualElement[] nodes = new VisualElement[7];
    public PNSettingsDecoration()
    {
        name="pn-settings-decoration";pickingMode=PickingMode.Ignore;PNSettingsView.Stretch(this);
        for(int i=0;i<nodes.Length;i++)
        {
            bool star=i==1||i==2||i==4;
            VisualElement node=star ? (VisualElement)new PNPauseDrawing(PNPauseDrawing.Shape.Star) : new VisualElement();
            node.pickingMode=PickingMode.Ignore;node.style.position=Position.Absolute;
            float size=star?22:5;node.style.width=size;node.style.height=star?26:5;
            if(!star){node.style.backgroundColor=Color.white;node.style.borderTopLeftRadius=node.style.borderTopRightRadius=node.style.borderBottomLeftRadius=node.style.borderBottomRightRadius=5;}
            nodes[i]=node;Add(node);
        }
        generateVisualContent+=Draw;
    }
    public void Animate(float clock,float progress,float width,float height)
    {
        float scale=Mathf.Min(width/1280,height/720);
        float cx=width*.79f,cy=height*.535f;
        // On narrow windows keep the decoration to the right of the control block.
        if(width<820){cx=width*.84f;scale*=.62f;cy=height*.66f;}
        for(int i=0;i<points.Length;i++)
        {
            float dx=Basis[i].x-1000,dy=Basis[i].y-385;
            dx+=Mathf.Sin(dy*.012f-clock*.65f)*14;dy+=Mathf.Sin(dy*.008f+clock*.45f)*7;
            points[i]=new Vector2(cx+dx*(.4f+.6f*progress)*scale,cy+dy*(.04f+.96f*progress)*scale);
            nodes[i].style.left=points[i].x-(i==1||i==2||i==4?11:2.5f);nodes[i].style.top=points[i].y-(i==1||i==2||i==4?13:2.5f);
        }
        style.opacity=progress;MarkDirtyRepaint();
    }
    private void Draw(MeshGenerationContext context)
    {
        var p=context.painter2D;p.strokeColor=new Color(1,1,1,.38f);p.lineWidth=1;p.lineCap=LineCap.Round;
        for(int i=0;i<Edges.Length;i+=2){p.BeginPath();p.MoveTo(points[Edges[i]]);p.LineTo(points[Edges[i+1]]);p.Stroke();}
    }
}
#endif
