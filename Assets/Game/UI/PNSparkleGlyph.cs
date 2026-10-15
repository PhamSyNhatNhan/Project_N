#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.UIElements;

// Filled glints and the plump hollow pause toggle share core/halo ownership.
internal class PNSparkleGlyph : VisualElement
{
    private readonly VisualElement halo;
    private sealed class Core : VisualElement
    {
        private readonly bool hollow;
        internal Core(bool hollow)
        {
            this.hollow=hollow;pickingMode=PickingMode.Ignore;
            style.position=Position.Absolute;style.left=0;style.right=0;style.top=0;style.bottom=0;
            generateVisualContent+=Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D;p.lineJoin=LineJoin.Round;
            PNSparklePath.Draw(p,contentRect.center,new Vector2(contentRect.width*.5f- (hollow?.8f:0),contentRect.height*.5f-(hollow?.8f:0)),hollow);
            if(hollow){p.lineWidth=Mathf.Clamp(contentRect.width*.047f,.8f,1.6f);p.strokeColor=Color.white;p.Stroke();}
            else{p.fillColor=Color.white;p.Fill();}
        }
    }
    internal PNSparkleGlyph(bool hollow)
    {
        pickingMode=PickingMode.Ignore;style.overflow=Overflow.Visible;
        halo=new VisualElement {name="sparkle-soft-halo",pickingMode=PickingMode.Ignore};
        halo.style.position=Position.Absolute;halo.style.left=Length.Percent(-50);halo.style.top=Length.Percent(-50);
        halo.style.width=Length.Percent(200);halo.style.height=Length.Percent(200);
        halo.style.backgroundImage=PNSparkleArtwork.Texture(hollow);halo.style.opacity=hollow?.3f:.5f;
        Add(halo);Add(new Core(hollow));
    }
    internal void SetGlow(Color glow)=>halo.style.unityBackgroundImageTintColor=glow;
}

internal static class PNSparklePath
{
    // Demo polygon: 50,0 / 59,40 / 100,50 / 59,60 / ...
    private static readonly Vector2[] Points={new Vector2(0,-1),new Vector2(.18f,-.2f),new Vector2(1,0),new Vector2(.18f,.2f),new Vector2(0,1),new Vector2(-.18f,.2f),new Vector2(-1,0),new Vector2(-.18f,-.2f)};
    private static readonly Vector2[] PausePoints={new Vector2(0,-.92f),new Vector2(.3f,-.3f),new Vector2(.92f,0),new Vector2(.3f,.3f),new Vector2(0,.92f),new Vector2(-.3f,.3f),new Vector2(-.92f,0),new Vector2(-.3f,-.3f)};
    internal static Vector2 Point(int index,bool pause=false)=>(pause?PausePoints:Points)[index&7];
    internal static void Draw(Painter2D painter,Vector2 center,Vector2 half,bool pause=false)
    {
        painter.BeginPath();
        for(int i=0;i<8;i++)
        {
            var point=center+Vector2.Scale(Point(i,pause),half);
            float round=pause?((i&1)==0?.36f:.3f):((i&1)==0?.025f:.1f);
            var enter=Vector2.Lerp(point,center+Vector2.Scale(Point(i+7,pause),half),round);
            var leave=Vector2.Lerp(point,center+Vector2.Scale(Point(i+1,pause),half),round);
            if(i==0)painter.MoveTo(enter);else painter.LineTo(enter);
            if(pause)painter.BezierCurveTo(Vector2.Lerp(enter,point,2f/3),Vector2.Lerp(leave,point,2f/3),leave);
            else painter.BezierCurveTo(point,point,leave);
        }
        painter.ClosePath();
    }
}

// Ref-counted UI-owned blur masks, constructed once and released with the last document.
internal sealed class PNSparkleArtwork : IDisposable
{
    private static Texture2D solid,hollow;
    private static int users;
    private bool disposed;
    internal PNSparkleArtwork()
    {
        if(users==0)
        {
            try{solid=Create(false);hollow=Create(true);}
            catch{ReleaseTextures();throw;}
        }
        users++;
    }
    internal static Texture2D Texture(bool outline)
    {
        if(users==0)throw new InvalidOperationException("Sparkle artwork requires an owning UI lease.");
        return outline?hollow:solid;
    }
    private static Texture2D Create(bool outline)
    {
        const int width=128,height=160;var mask=new float[width*height];var temp=new float[mask.Length];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var point=new Vector2(x+.5f-width*.5f,y+.5f-height*.5f);bool inside=false;float distance=float.MaxValue;
            for(int i=0;i<8;i++)
            {
                var a=Vector2.Scale(PNSparklePath.Point(i,outline),new Vector2(32,40));var b=Vector2.Scale(PNSparklePath.Point(i+1,outline),new Vector2(32,40));
                if((a.y>point.y)!=(b.y>point.y) && point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
                var edge=b-a;distance=Mathf.Min(distance,Vector2.Distance(point,a+edge*Mathf.Clamp01(Vector2.Dot(point-a,edge)/edge.sqrMagnitude)));
            }
            mask[y*width+x]=outline?Mathf.Clamp01(2-distance):(inside?1:0);
        }
        float sigma=outline?4:9;int radius=Mathf.CeilToInt(sigma*3);var kernel=new float[radius*2+1];float total=0;
        for(int i=-radius;i<=radius;i++){kernel[i+radius]=Mathf.Exp(-i*i/(2*sigma*sigma));total+=kernel[i+radius];}
        for(int i=0;i<kernel.Length;i++)kernel[i]/=total;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {float value=0;for(int k=-radius;k<=radius;k++)if(x+k>=0 && x+k<width)value+=mask[y*width+x+k]*kernel[k+radius];temp[y*width+x]=value;}
        var pixels=new Color[mask.Length];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {float value=0;for(int k=-radius;k<=radius;k++)if(y+k>=0 && y+k<height)value+=temp[(y+k)*width+x]*kernel[k+radius];pixels[y*width+x]=new Color(1,1,1,Mathf.Clamp01(value*2));}
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){name=outline?"Sparkle hollow blur":"Sparkle filled blur",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        try{texture.SetPixels(pixels);texture.Apply(false,true);return texture;}
        catch{PNUiRuntime.Retire(texture);throw;}
    }
    private static void ReleaseTextures(){if(solid!=null)PNUiRuntime.Retire(solid);if(hollow!=null)PNUiRuntime.Retire(hollow);solid=null;hollow=null;}
    public void Dispose(){if(disposed)return;disposed=true;if(--users==0)ReleaseTextures();}
}
#endif
