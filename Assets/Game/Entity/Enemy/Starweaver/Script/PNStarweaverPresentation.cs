#if UNITY_5_3_OR_NEWER
using UnityEngine;
[DefaultExecutionOrder(200)]
public sealed class PNStarweaverPresentation:MonoBehaviour
{
    [SerializeField] private Transform weapon;
    [SerializeField, Min(0f)] private float hoverHeight=.025f, hoverWidth=.012f;
    [SerializeField, Min(.1f)] private float hoverPeriod=2.8f;
    [SerializeField, Min(0f)] private float rockDegrees=9f;
    private PNVisualBody body;
    private PNVisualActor owner;
    private float phase,castAge,castWindup;
    private bool casting,triangle;
    private Vector3 castDirection;
    public bool HasWeapon=>weapon!=null;
    public Vector3 EyePosition=>weapon!=null?weapon.TransformPoint(new Vector3(0,.075f,0)):transform.position+Vector3.up*.3f;
    public Vector3 TipPosition=>weapon!=null?weapon.TransformPoint(new Vector3(0,-.16f,0)):EyePosition;
    public void SetCastVisual(bool constellation,float age,float windup,Vector3 direction)
    {casting=true;triangle=constellation;castAge=age;castWindup=windup;castDirection=direction;}
    public void EndCastVisual(){casting=false;if(isActiveAndEnabled)TickWeapon(0);}
    private void Awake(){body=GetComponent<PNVisualBody>();}
    private void LateUpdate()
    {
        if(body==null)body=GetComponent<PNVisualBody>();
        if(body!=null&&body.Actor!=null)TickWeapon(body.Actor.ClockDelta());
    }
    private void OnDisable(){owner=null;phase=0f;casting=false;}
    public static float WeaveThrust(float progress)
    {
        if(progress<.76f)return -.075f*Mathf.SmoothStep(0,1,Mathf.Clamp01(progress/.76f));
        if(progress<1)return Mathf.Lerp(-.075f,.10f,Mathf.Pow((progress-.76f)/.24f,.7f));
        return .10f*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((progress-1)/.55f)));
    }
    // Presentation only: use the same entity clock as the puppet; no wall-time drift on pause.
    public void TickWeapon(float dt)
    {
        if(body==null)body=GetComponent<PNVisualBody>();var actor=body!=null?body.Actor:null;
        if(weapon==null||actor==null)return;
        if(owner!=actor){owner=actor;phase=0f;}
        phase=Mathf.Repeat(phase+Mathf.Max(0f,dt)*Mathf.PI*2f/Mathf.Max(.1f,hoverPeriod),Mathf.PI*2f);
        float side=actor.FacingLeft?-1:1;
        float lift=Mathf.Sin(phase),drift=Mathf.Sin(phase*2f);
        weapon.position=actor.transform.TransformPoint(new Vector3(side*(.23f+hoverWidth*drift),.22f+hoverHeight*lift,-.025f));
        float angle=side*(-25f+rockDegrees*Mathf.Sin(phase+.45f));
        if(casting)
        {
            float progress=castAge/castWindup;var local=actor.transform.InverseTransformDirection(castDirection);
            float aim=triangle?-side*12:Mathf.Atan2(local.x,-local.y)*Mathf.Rad2Deg;
            float aimBlend=Mathf.SmoothStep(0,1,Mathf.Clamp01(progress/.85f));
            if(!triangle&&progress>1)aimBlend*=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((progress-1)/.55f));
            angle=Mathf.LerpAngle(angle,aim,aimBlend);
            float twitch=0;if(triangle)for(int i=0;i<3;i++)if(progress>=.86f+i*.04f)twitch+=Mathf.Exp(-(progress-(.86f+i*.04f))*100);
            angle+=side*twitch*5;weapon.position+=actor.transform.up*twitch*.008f;
            if(!triangle)
            {
                // Cock backwards, then thrust to the release pose at exactly the registered windup.
                float thrust=WeaveThrust(progress);
                weapon.position+=castDirection.normalized*thrust;
            }
        }
        weapon.rotation=actor.transform.rotation*Quaternion.Euler(0,0,angle);
    }
}
#endif
