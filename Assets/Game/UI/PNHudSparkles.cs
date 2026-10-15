#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.UIElements;

// Retained decorative pool. Its random stream never consumes gameplay Random state.
internal sealed class PNHudSparkles
{
    private sealed class Glint : PNSparkleGlyph
    {
        internal float Start, Life;
        internal bool Active;
        internal Glint() : base(false)
        {
            style.position=Position.Absolute;style.width=8;style.height=10.4f;style.opacity=0;
        }
    }
    private readonly Glint[] pool;
    private readonly System.Random random=new System.Random();
    private int cursor;
    internal readonly VisualElement Layer;
    internal int ActiveCount { get {int count=0;foreach(var star in pool)if(star.Active)count++;return count;} }
    internal Color LastGlow { get; private set; }
    internal PNHudSparkles(VisualElement host,string name,int capacity)
    {
        Layer=new VisualElement {name=name,pickingMode=PickingMode.Ignore};
        Layer.style.position=Position.Absolute;Layer.style.left=0;Layer.style.right=0;Layer.style.top=0;Layer.style.bottom=0;Layer.style.overflow=Overflow.Visible;
        host.Add(Layer);pool=new Glint[capacity];
        for(int i=0;i<pool.Length;i++){pool[i]=new Glint();Layer.Add(pool[i]);}
    }
    private float Random()=> (float)random.NextDouble();
    private void Spawn(float x,float y,Color glow,float delay,float now)
    {
        var star=pool[cursor++%pool.Length];star.Active=true;star.Start=now+delay;star.Life=.65f+Random()*.5f;
        star.SetGlow(glow);LastGlow=glow;
        star.style.left=Length.Percent(x);star.style.top=Length.Percent(y);
        star.style.translate=new Translate(-4,-5.2f);star.style.opacity=0;
    }
    internal void Ring(int count,Color glow,float now,bool held)
    {
        for(int i=0;i<count;i++)
        {float angle=Random()*Mathf.PI*2,radius=held?49:50;Spawn(50+Mathf.Cos(angle)*radius,50+Mathf.Sin(angle)*radius,glow,held?i*.09f:Random()*.6f,now);}
    }
    internal void Health(Color glow,float now)
    {for(int i=0;i<10;i++)Spawn(5+Random()*90,Random()<.5f?-10:110,glow,Random()*.65f,now);}
    internal void CancelPending(float now)
    {foreach(var star in pool)if(star.Active && star.Start>now){star.Active=false;star.style.opacity=0;}}
    internal void Clear()
    {foreach(var star in pool){star.Active=false;star.style.opacity=0;}}
    internal void Tick(float now)
    {
        float width=Layer.resolvedStyle.width;
        float inheritedScale=float.IsFinite(width) && width>0?Layer.worldBound.width/width:1;
        float compensation=Mathf.Max(1,6/(8*Mathf.Max(.2f,inheritedScale)));
        foreach(var star in pool)
        {
            if(!star.Active)continue;
            float t=(now-star.Start)/star.Life;if(t<0)continue;
            if(t>=1){star.Active=false;star.style.opacity=0;continue;}
            // Demo glint keys: 0/.24/.48/1 -> alpha0/.95/.7/0, scale.45/1/.9/.3.
            float from=t<.24f?0:t<.48f?.24f:.48f,to=t<.24f?.24f:t<.48f?.48f:1;
            float p=PNPauseArtwork.Curve((t-from)/(to-from),.25f,.5f,.4f,1);
            float alpha=t<.24f?Mathf.Lerp(0,.95f,p):t<.48f?Mathf.Lerp(.95f,.7f,p):Mathf.Lerp(.7f,0,p);
            float scale=t<.24f?Mathf.Lerp(.45f,1,p):t<.48f?Mathf.Lerp(1,.9f,p):Mathf.Lerp(.9f,.3f,p);
            star.style.opacity=alpha;star.style.scale=new Scale(new Vector2(scale,scale)*compensation);
        }
    }
}
#endif
