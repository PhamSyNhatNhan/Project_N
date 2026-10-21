#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.UIElements;

// Toolkit-only pause artwork. Geometry stays local; callers animate transforms, not layout.
internal sealed class PNPauseDrawing : VisualElement
{
    internal enum Shape { Orbit, FrontOrbit, Dial, Star, Icon, ButtonHalo }
    private readonly Shape shape;
    // Corner radius of the ButtonHalo outline (the pause buttons' 19; other buttons set their own).
    internal float HaloRadius = 19, RingAlpha = .35f, RingWidth = 1f;
    public PNPauseDrawing(Shape shape)
    {
        this.shape=shape; pickingMode=PickingMode.Ignore; generateVisualContent+=Draw;
        if(shape==Shape.Star)AddStar(0,0,12,15.6f,false);
        if(shape==Shape.Icon)
        {AddStar(0,0,32,34,true);AddStar(-19,13,11,12,true);AddStar(20,-15,8,8.8f,true);}
    }
    private void AddStar(float x,float y,float width,float height,bool hollow)
    {
        var star=new PNSparkleGlyph(hollow);star.style.position=Position.Absolute;
        star.style.left=Length.Percent(50);star.style.top=Length.Percent(50);
        star.style.width=width;star.style.height=height;star.style.translate=new Translate(x-width*.5f,y-height*.5f);Add(star);
    }
    private void Draw(MeshGenerationContext context)
    {
        var p=context.painter2D;var c=contentRect.center;
        if(shape==Shape.Star || shape==Shape.Icon)return;
        if(shape==Shape.ButtonHalo)
        {
            for(int i=0;i<3;i++){p.strokeColor=new Color(1,1,1,.025f+i*.015f);p.lineWidth=12-i*4;RoundRect(p,contentRect,HaloRadius);p.Stroke();}
            p.strokeColor=new Color(1,1,1,RingAlpha);p.lineWidth=RingWidth;RoundRect(p,contentRect,HaloRadius);p.Stroke();return;
        }
        p.strokeColor=new Color(1,1,1,shape==Shape.Dial?.32f:.64f);p.lineWidth=1;
        if(shape==Shape.Dial)
        {
            // Long incomplete dial with feathered ends, distinct from the two ellipses.
            for(int segment=0;segment<48;segment++)
            {float fade=Mathf.Min(1,Mathf.Min(segment+1,48-segment)/6f);p.strokeColor=new Color(1,1,1,.32f*fade);p.BeginPath();p.Arc(c,248,Angle.Degrees(-110+segment*5),Angle.Degrees(-105+segment*5));p.Stroke();}
            return;
        }
        float x=contentRect.width*.5f-1,y=contentRect.height*.5f-1;const float k=.55228475f;
        p.BeginPath();p.MoveTo(c+new Vector2(x,0));
        p.BezierCurveTo(c+new Vector2(x,k*y),c+new Vector2(k*x,y),c+new Vector2(0,y));
        p.BezierCurveTo(c+new Vector2(-k*x,y),c+new Vector2(-x,k*y),c+new Vector2(-x,0));
        if(shape==Shape.Orbit)
        {
            p.BezierCurveTo(c+new Vector2(-x,-k*y),c+new Vector2(-k*x,-y),c+new Vector2(0,-y));
            p.BezierCurveTo(c+new Vector2(k*x,-y),c+new Vector2(x,-k*y),c+new Vector2(x,0));p.ClosePath();
        }
        p.Stroke();
    }
    private static void RoundRect(Painter2D p,Rect r,float radius)
    {
        float x=r.x,y=r.y,w=r.width,h=r.height;
        p.BeginPath();p.MoveTo(new Vector2(x+radius,y));p.LineTo(new Vector2(x+w-radius,y));
        p.Arc(new Vector2(x+w-radius,y+radius),radius,-90,0);p.LineTo(new Vector2(x+w,y+h-radius));
        p.Arc(new Vector2(x+w-radius,y+h-radius),radius,0,90);p.LineTo(new Vector2(x+radius,y+h));
        p.Arc(new Vector2(x+radius,y+h-radius),radius,90,180);p.LineTo(new Vector2(x,y+radius));
        p.Arc(new Vector2(x+radius,y+radius),radius,180,270);p.ClosePath();
    }
}

internal static class PNPauseArtwork
{
    // Small, shared-within-menu textures are created once, never during drawing/ticking.
    internal static Texture2D Gradient(bool sphere)
    {
        const int size=128;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
        texture.name=sphere?"Pause sphere gradient":"Pause soft light";texture.hideFlags=HideFlags.HideAndDontSave;
        texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
        var pixels=new Color[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            var point=new Vector2((x+.5f)/size,(y+.5f)/size);
            if(sphere)
            {
                float shade=Mathf.Clamp01(Vector2.Distance(point,new Vector2(.28f,.76f))/1.05f);
                pixels[y*size+x]=Color.Lerp(new Color(.906f,.925f,.933f),new Color(.722f,.773f,.796f),shade);
            }
            else
            {
                float distance=Vector2.Distance(point,new Vector2(.5f,.5f))*2;
                pixels[y*size+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-distance),2)*.8f);
            }
        }
        texture.SetPixels(pixels);texture.Apply(false,true);return texture;
    }
    internal static float Curve(float t,float x1,float y1,float x2,float y2)
    {
        t=Mathf.Clamp01(t);float lo=0,hi=1;
        for(int i=0;i<12;i++){float m=(lo+hi)*.5f;float x=Bezier(m,x1,x2);if(x<t)lo=m;else hi=m;}
        return Bezier((lo+hi)*.5f,y1,y2);
    }
    private static float Bezier(float t,float a,float b)=>3*(1-t)*(1-t)*t*a+3*(1-t)*t*t*b+t*t*t;
}
#endif
