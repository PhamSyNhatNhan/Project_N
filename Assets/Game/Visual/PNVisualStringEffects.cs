using System;

// Per-string presentation state. Pure numbers, bounded storage; no gameplay or wire state.
internal sealed class PNVisualStringEffects
{
    internal const int MaxDust = 6;
    internal struct Dust
    {
        internal float X,Y,Z,VX,VY,VZ,Age,Life,Size; internal bool Star;
        internal bool Alive => Life>0 && Age<Life;
    }
    internal readonly Dust[] Particles = new Dust[MaxDust];
    internal float Time,BendX,BendZ,Motion,TrailX,TrailY,TrailZ;
    internal float PulseAge=10,RootX,RootY,RootZ;
    internal bool RootKnown;
    private float springX,springZ,velocityX,velocityZ,dustTimer;
    private uint random;
    internal float Pulse => PulseAge<.65f ? 1-PulseAge/.65f : 0;
    internal float PulseFront => PulseAge/.65f;
    internal int DustCount { get { int n=0;for(int i=0;i<MaxDust;i++)if(Particles[i].Alive)n++;return n; } }
    internal PNVisualStringEffects(uint seed){random=seed+1;}
    private float Random(){random=random*1664525u+1013904223u;return (random>>8)*(1f/16777216f);}
    internal void ObserveRoot(float x,float y,float z)
    {
        if(!RootKnown){TrailX=x;TrailY=y;TrailZ=z;RootKnown=true;}
        RootX=x;RootY=y;RootZ=z;
    }
    internal void Velocity(float x,float z)
    {
        if(float.IsNaN(x)||float.IsInfinity(x)||float.IsNaN(z)||float.IsInfinity(z)){x=z=0;}
        velocityX=Math.Max(-6,Math.Min(6,x));velocityZ=Math.Max(-6,Math.Min(6,z));
    }
    internal void Hit()
    {
        PulseAge=0; Burst(2);
    }
    internal void Burst(int count)
    {
        if(!RootKnown)return;
        for(int k=0;k<count;k++)
        {
            int free=-1;for(int i=0;i<MaxDust;i++)if(!Particles[i].Alive){free=i;break;}
            if(free<0)return;
            Particles[free]=new Dust{X=RootX+(Random()-.5f)*.025f,Y=RootY+.012f+Random()*.06f,Z=RootZ,
                VX=(Random()-.5f)*.025f,VY=-.025f-Random()*.025f,VZ=(Random()-.5f)*.025f,
                Age=0,Life=.7f+Random()*.55f,Size=.0025f+Random()*.002f,Star=Random()>.82f};
        }
    }
    internal void Reset()
    {
        BendX=BendZ=springX=springZ=velocityX=velocityZ=Motion=dustTimer=0;PulseAge=10;RootKnown=false;
        for(int i=0;i<MaxDust;i++)Particles[i]=default;
    }
    internal void Advance(float dt,bool attached)
    {
        if(!(dt>0)||float.IsInfinity(dt))return;
        // Large gaps (e.g. offscreen catch-up) settle without a particle burst or unstable spring.
        if(dt>.25f){Time+=dt;Reset();return;}
        Time+=dt;PulseAge+=dt;
        int steps=(int)Math.Ceiling(dt/.016f);float h=dt/steps;
        float targetX=attached?-velocityX*.018f:0,targetZ=attached?-velocityZ*.018f:0;
        for(int i=0;i<steps;i++)
        {
            springX+=(75*(targetX-BendX)-13*springX)*h;springZ+=(75*(targetZ-BendZ)-13*springZ)*h;
            BendX+=springX*h;BendZ+=springZ*h;
        }
        float speed=(float)Math.Sqrt(velocityX*velocityX+velocityZ*velocityZ);
        float targetMotion=attached?Math.Min(1,speed/1.3f):0;
        Motion+=(targetMotion-Motion)*(1-(float)Math.Exp(-dt*7));
        float follow=1-(float)Math.Exp(-dt*11);
        if(RootKnown){TrailX+=(RootX-TrailX)*follow;TrailY+=(RootY-TrailY)*follow;TrailZ+=(RootZ-TrailZ)*follow;}
        if(attached&&(Motion>.18f||Pulse>.1f))
        {
            dustTimer+=dt*(.7f+Motion*.8f);
            if(dustTimer>=.6f){dustTimer-=.6f;Burst(1);}
        }
        else dustTimer=0;
        for(int i=0;i<MaxDust;i++)
        {
            ref var p=ref Particles[i];if(!p.Alive)continue;
            p.Age+=dt;p.VY-=.035f*dt;p.X+=p.VX*dt;p.Y+=p.VY*dt;p.Z+=p.VZ*dt;
        }
    }
}
