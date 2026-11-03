#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Owned by the hammer presentation, driven only by the cast age.
public sealed class PNStarforgerForgeVisual : IDisposable
{
    private readonly PNStarreaperEnergyMesh mesh;
    private PNVisualActor actor;
    private float age,cast;private Vector3 localPoint;
    private bool running,disposed;
    public PNStarforgerForgeVisual(Transform parent){mesh=new PNStarreaperEnergyMesh(parent,"Starforger forge");PNViewCamera.Updated+=OnView;}
    public void Advance(PNVisualActor value,float seconds,float duration,Vector3 point){actor=value;localPoint=point;age=seconds;cast=duration;running=true;OnView();}
    private void OnView(){Render(PNViewCamera.Current);}
    public void Render(Camera camera)
    {
        if(!running||disposed||camera==null||actor==null)return;
        float q=Mathf.Clamp01(age/cast),post=Mathf.Max(0,age-cast),tail=1-Mathf.Clamp01(post/.2f);
        var at=actor.transform.TransformPoint(localPoint);
        var right=camera.transform.right;var up=camera.transform.up;var front=-camera.transform.forward*.006f;
        mesh.Begin(age);
        if(age<cast)
        {
            float grow=Mathf.SmoothStep(0,1,q);
            mesh.Quad(false,at,right,up,.22f*grow,.20f*grow,new Color(.06f,.012f,.10f,.9f),9);
            mesh.Quad(true,at+front,right,up,.30f*grow,.26f*grow,new Color(1.9f,.90f,.20f,.85f),2);
            mesh.Quad(true,at+front*2,right,up,.13f*grow,.11f*grow,new Color(2.8f,2.1f,.85f,1),3);
            for(int i=0;i<18;i++)
            {
                float u=Mathf.Repeat(age*1.8f+i*.618034f,1),a=i*2.39996f;var radial=right*Mathf.Cos(a)+up*Mathf.Sin(a)*.7f;
                var point=at+radial*(.035f+.28f*(1-u));float alpha=Mathf.Sin(u*Mathf.PI)*grow;
                mesh.Quad(true,point+front,radial.normalized,Vector3.Cross(camera.transform.forward,radial).normalized,.065f,.024f,new Color(1.9f,1.10f,.30f,alpha),1);
                if(i%4==0)mesh.Quad(true,point,right,up,.03f,.03f,new Color(.6f,.12f,1.1f,alpha),3);
            }
        }
        else if(tail>0)
        {
            mesh.Quad(true,at+front*3,right,up,.62f,.42f,new Color(4,3.0f,1.3f,Mathf.Exp(-Mathf.Max(0,post-.025f)*40)),2);
            mesh.Quad(true,at+front*4,right,up,.56f,.34f,new Color(1.8f,.45f,1.4f,tail),21,frame:post/.2f*3);
            mesh.Quad(true,at+front*5,right,up,.40f,.06f,new Color(2.8f,2.1f,.8f,tail*tail),3);
            for(int i=0;i<24;i++)
            {
                float a=i*2.39996f;var radial=right*Mathf.Cos(a)+up*Mathf.Sin(a)*.7f;var point=at+radial*(post*(1.5f+i%3*.35f))+Vector3.up*(-post*post*2);
                mesh.Quad(true,point+front,radial.normalized,Vector3.Cross(camera.transform.forward,radial).normalized,(.09f+i%3*.02f)*tail,.032f,new Color(2.4f,1.5f,.4f,tail),1);
                if(i%4==0)mesh.Quad(true,point+front*2,right,up,.05f*tail,.05f*tail,new Color(2.1f,1.6f,.55f,tail),3);
            }
        }
        mesh.End();
    }
    public void Clear(){running=false;actor=null;mesh.Clear();}
    public void Dispose(){if(disposed)return;disposed=true;PNViewCamera.Updated-=OnView;mesh.Dispose();}
}
#endif
