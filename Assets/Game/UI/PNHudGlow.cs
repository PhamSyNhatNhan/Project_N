#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.UIElements;

// A small feathered white outline, drawn behind the HUD; no textures or materials owned.
internal sealed class PNHudGlow : VisualElement
{
    private readonly bool circle;
    private float fill=1;
    public float Fill
    {
        set { float next=Mathf.Clamp01(value); if(next==fill)return; fill=next; MarkDirtyRepaint(); }
    }
    public PNHudGlow(bool circle)
    {
        this.circle=circle;
        name=circle?"portrait-glow":"health-glow";
        pickingMode=PickingMode.Ignore;
        float inset=circle?-9:-6;
        style.position=Position.Absolute; style.left=inset; style.right=inset; style.top=inset; style.bottom=inset;
        generateVisualContent+=Draw;
    }
    private void Draw(MeshGenerationContext context)
    {
        float width=contentRect.width-12, height=contentRect.height-12;
        if(!circle)width*=fill;
        if(width<=0 || height<=0)return;
        var painter=context.painter2D;
        for(int layer=0;layer<3;layer++)
        {
            painter.lineWidth=12-4*layer;
            painter.strokeColor=new Color(1,1,1,circle?.012f+.008f*layer:.015f+.01f*layer);
            painter.BeginPath();
            if(circle)
                painter.Arc(contentRect.center,Mathf.Min(width,height)*.5f,Angle.Degrees(0),Angle.Degrees(360));
            else
            {
                float radius=Mathf.Min(height,width)*.5f;
                painter.MoveTo(new Vector2(6+radius,6));
                painter.LineTo(new Vector2(6+width-radius,6));
                painter.Arc(new Vector2(6+width-radius,6+radius),radius,Angle.Degrees(-90),Angle.Degrees(0));
                painter.LineTo(new Vector2(6+width,6+height-radius));
                painter.Arc(new Vector2(6+width-radius,6+height-radius),radius,Angle.Degrees(0),Angle.Degrees(90));
                painter.LineTo(new Vector2(6+radius,6+height));
                painter.Arc(new Vector2(6+radius,6+height-radius),radius,Angle.Degrees(90),Angle.Degrees(180));
                painter.LineTo(new Vector2(6,6+radius));
                painter.Arc(new Vector2(6+radius,6+radius),radius,Angle.Degrees(180),Angle.Degrees(270));
            }
            painter.ClosePath(); painter.Stroke();
        }
    }
}
#endif
