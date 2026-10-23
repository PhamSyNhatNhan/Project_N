#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class PNMobileIconView : VisualElement
{
    private PNMobileIconGeometry geometry;
    private Color glow;
    internal PNMobileIconView(){pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;}
    internal void Set(PNMobileIconGeometry value,PNMobileRgb color)
    {Color next=new Color(color.R,color.G,color.B);if(ReferenceEquals(geometry,value)&&glow==next)return;geometry=value;glow=next;MarkDirtyRepaint();}
    private void Draw(MeshGenerationContext context)
    {
        if(geometry==null)return;var p=context.painter2D;p.lineCap=LineCap.Round;p.lineJoin=LineJoin.Round;
        float size=Mathf.Min(contentRect.width,contentRect.height)*.95f;
        Vector2 origin=contentRect.center-Vector2.one*(size*.5f);
        // Composite the entire halo below the entire white silhouette. A later path's
        // halo must never tint an earlier white stroke at a letter junction (A/E).
        for(int layer=3;layer>=0;layer--)
        {
            foreach(var path in geometry.Paths)
            {
                Vector2 current=Vector2.zero,start=Vector2.zero;p.BeginPath();foreach(var c in path.Commands)
                {
                    Vector2 end=origin+new Vector2(c.X,c.Y)*size;
                    switch(c.Kind){case PNIconCommandKind.Move:p.MoveTo(end);current=start=end;break;case PNIconCommandKind.Line:p.LineTo(end);current=end;break;
                    // Exact quadratic-to-cubic conversion avoids the rounded-corner artefact
                    // seen with Painter2D's native quadratic path on the checked Windows Player.
                    case PNIconCommandKind.Quadratic:
                        Vector2 finish=origin+new Vector2(c.X2,c.Y2)*size;
                        p.BezierCurveTo(current+(end-current)*(2f/3),finish+(end-finish)*(2f/3),finish);current=finish;break;
                    case PNIconCommandKind.Cubic:current=origin+new Vector2(c.X3,c.Y3)*size;p.BezierCurveTo(end,origin+new Vector2(c.X2,c.Y2)*size,current);break;
                    case PNIconCommandKind.Close:p.ClosePath();current=start;break;}
                }
                if(layer>0){p.strokeColor=new Color(glow.r,glow.g,glow.b,.15f);p.lineWidth=path.StrokeWidth*size+layer*4;p.Stroke();}
                else {if(path.Fill){p.fillColor=Color.white;p.Fill(FillRule.NonZero);}if(path.StrokeWidth>0){p.strokeColor=Color.white;p.lineWidth=path.StrokeWidth*size;p.Stroke();}}
            }
        }
    }
}
#endif
