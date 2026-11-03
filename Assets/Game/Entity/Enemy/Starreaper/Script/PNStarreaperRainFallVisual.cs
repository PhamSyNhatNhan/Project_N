#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Two retained textured batches per landing, grown only at area Begin. Ages are
// analytic so cancellation, frozen clocks and pooled reuse have no particle debt.
public sealed class PNStarreaperRainFallVisual : IDisposable
{
    private const float FallSeconds=.2f, Tail=.42f;
    private readonly PNStarreaperEnergyMesh mesh;private readonly int seed;
    public PNStarreaperRainFallVisual(Transform parent,int index){seed=index;mesh=new PNStarreaperEnergyMesh(parent,"Rain landing "+index);}
    public void Render(Vector3 point,float radius,float age,float land,Camera camera)
    {
        mesh.Begin(age);
        float after=age-land;
        if(after>=Tail){mesh.End();return;}
        var viewUp=camera.transform.up;var viewRight=camera.transform.right;
        var front=-camera.transform.forward*.004f;
        var ground=point+Vector3.up*.024f;
        if(after<0)
        {
            float squeeze=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.13f,0,after));
            // Outer extent remains the physical radius. The faint inner mark
            // compresses before contact, while the boundary stays readable.
            mesh.Quad(false,ground,Vector3.right,Vector3.forward,radius*2,radius*2,new Color(.018f,.006f,.030f,.36f),17);
            mesh.Quad(true,ground+Vector3.up*.003f,Vector3.right,Vector3.forward,radius*2/.82f,radius*2/.82f,new Color(.68f,.22f,1.30f,.55f+squeeze*.30f),16);
            float inner=radius*(.60f-.34f*squeeze);
            mesh.Quad(true,ground+Vector3.up*.006f,Vector3.right,Vector3.forward,inner,inner,new Color(.69f,.23f,1.40f,.15f+squeeze*.30f),17);
            for(int i=0;i<7;i++)
            {
                float a=i*2.39996f+seed;float u=Mathf.Repeat(age*.62f+i*.173f,1);
                var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                var p=ground+radial*radius*(.44f+.20f*Mathf.Sin(i))+Vector3.up*(u*.23f);
                mesh.Quad(true,p+front,viewRight,viewUp,.012f,.012f,new Color(1.20f,.79f,.24f,Mathf.Sin(u*Mathf.PI)*.65f),3);
            }
            float fall=(after+FallSeconds)/FallSeconds;
            if(fall>=0)
            {
                float h=4*(1-fall*fall);
                var head=point+Vector3.up*h;
                var down=Vector3.down;var across=Vector3.Cross(camera.transform.forward,down).normalized;
                mesh.Body(head,down,across,front,1,.28f,age,1.15f);
                float length=.60f+fall*.75f;
                mesh.Quad(false,head+Vector3.up*(length*.5f+.10f)+front,down,across,length,.36f,new Color(.008f,.002f,.020f,.94f),0);
                mesh.Quad(true,head+Vector3.up*(length*.5f+.10f)+front*2,down,across,length,.29f,new Color(.52f,.08f,1.15f,.40f),6);
                // A small upper tear opens then is consumed by the fall.
                float tear=Mathf.Sin(Mathf.Clamp01(fall/.65f)*Mathf.PI);
                mesh.Quad(false,point+Vector3.up*4+front,Vector3.up,across,.38f,.10f,new Color(.008f,.002f,.02f,tear*.9f),9);
                mesh.Quad(true,point+Vector3.up*4+front*2,Vector3.up,across,.38f,.08f,new Color(.65f,.14f,1.3f,tear*.5f),9);
                for(int i=0;i<2;i++)mesh.BlackStar(head+Vector3.up*(.18f+i*.29f)+across*((i==0?-1:1)*.09f),across,Vector3.up,front,.032f,.8f,seed+i);
            }
        }
        else
        {
            float q=after/Tail,fade=1-Mathf.SmoothStep(0,1,q);
            float snap=Mathf.Exp(-after*65);
            // The last incoming wake is consumed from above into the contact,
            // rather than disappearing and being replaced by upward ribbons.
            float incoming=Mathf.Clamp01(1-after/.10f);
            if(incoming>0)
            {
                float length=1.35f*incoming;
                var at=point+Vector3.up*(length*.5f+.025f);
                mesh.Quad(false,at+front,Vector3.down,viewRight,length,.48f*incoming,new Color(.008f,.002f,.020f,incoming*.94f),0);
                mesh.Quad(true,at+front*2,Vector3.down,viewRight,length,.29f*incoming,new Color(.46f,.055f,1.0f,incoming*.24f),6);
            }
            mesh.Quad(false,ground,Vector3.right,Vector3.forward,radius*1.9f,radius*1.9f,new Color(.009f,.003f,.018f,fade*.76f),17);
            mesh.Quad(true,ground+Vector3.up*.004f,Vector3.right,Vector3.forward,radius*1.55f,radius*1.55f,new Color(.65f,.11f,1.35f,fade*.24f),17);
            mesh.Quad(true,point+Vector3.up*.055f+front*5,viewRight,viewUp,.40f,.26f,new Color(1.5f,.64f,2.3f,snap*.65f),2);
            // Namu texture sequences provide changing silhouettes, not static
            // procedural lobes. Four-frame impact row punches first, then the
            // expanding dark shell and smoke tear apart over their own frames.
            float hit=Mathf.Clamp01(after/.14f);
            if(after<.14f)
            {
                float size=radius*(1.1f+.65f*(1-Mathf.Exp(-after*40)));
                var at=point+Vector3.up*(size*.27f);
                mesh.Quad(false,at+front,viewRight,viewUp,size,size,new Color(.005f,.001f,.012f,1-hit*.5f),20,frame:4+hit*3);
                mesh.Quad(true,at+front*2,viewRight,viewUp,size*.94f,size*.94f,new Color(.75f,.13f,1.65f,(1-hit)*.38f),20,frame:4+hit*3);
            }
            float shell=Mathf.Clamp01(after/.24f);
            if(after<.24f)
            {
                float size=radius*(.8f+shell*1.4f);
                var at=point+Vector3.up*(size*.30f);
                mesh.Quad(false,at+front,viewRight,viewUp,size,size*.85f,new Color(.003f,.001f,.009f,(1-shell)*.95f),21,frame:8+shell*7);
                mesh.Quad(true,at+front*2,viewRight,viewUp,size*.96f,size*.82f,new Color(.48f,.06f,1.0f,(1-shell)*.24f),22,frame:8+shell*7);
            }
            for(int puff=0;puff<4;puff++)
            {
                float delay=puff*.012f;
                if(after<delay)continue;
                float t=Mathf.Clamp01((after-delay)/(.42f-delay));
                float a=puff*2.39996f+seed*.73f;
                var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                var at=point+radial*radius*(.06f+(1-Mathf.Exp(-t*6))*.37f)+Vector3.up*(.08f+t*.20f);
                float size=radius*(1.2f+t*.55f);
                float opacity=(1-Mathf.SmoothStep(0,1,t))*.90f;
                mesh.Quad(false,at+front,viewRight,viewUp,size,size,new Color(.008f,.002f,.018f,opacity),23,frame:t*10);
                mesh.Quad(true,at+front*2,viewRight,viewUp,size,size,new Color(.50f,.065f,1.0f,opacity*.24f),24,frame:t*10);
            }
            for(int i=0;i<7;i++)
            {
                float a=i*2.39996f+seed*.79f;
                var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                float travel=radius*(.06f+.86f*(1-Mathf.Exp(-after*23)));
                float height=(1.15f+i%3*.36f)*after-4.0f*after*after;
                var p=point+radial*travel+Vector3.up*(.04f+Mathf.Max(0,height));
                var streak=(radial+Vector3.up*.28f).normalized;
                var across=Vector3.Cross(camera.transform.forward,streak).normalized;
                float gold=i%4==0?1:0;
                mesh.Quad(true,p+front*2,streak,across,(.15f+i%3*.05f)*fade,.023f,new Color(gold>0?1.35f:.82f,gold>0?.85f:.18f,gold>0?.28f:1.6f,fade*.8f),1);
                if(i%2==0)mesh.BlackStar(p+front,viewRight,viewUp,front,(.095f+i*.004f)*fade,fade,i+after*2);

            }
        }
        mesh.End();
    }
    public void Clear(){mesh.Clear();}
    public void Dispose(){mesh.Dispose();}
}
#endif
