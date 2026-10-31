#if UNITY_5_3_OR_NEWER
using UnityEngine;
using EveEngine;

[DefaultExecutionOrder(-50)]
public sealed class PNLoricaPresentation:MonoBehaviour
{
    private PNEnemyStat stat;private bool known;private double previous;
    public static void Ensure(GameObject owner)
    {var p=owner.GetComponent<PNLoricaPresentation>();if(p==null)p=owner.AddComponent<PNLoricaPresentation>();p.ResetBaseline();}
    public void ResetBaseline(){stat=GetComponent<PNEnemyStat>();known=false;}
    private void OnDisable(){known=false;}
    private void LateUpdate(){Present(PNViewCamera.Current);}
    public void Present(Camera camera)
    {
        if(stat==null)stat=GetComponent<PNEnemyStat>();
        if(stat==null || !PNLoricaView.TryRead(stat,out var ratio,out _)){known=false;return;}
        double value=ratio;
        bool emit=known && previous>0 && value<=0 && !stat.IsDead && camera!=null;previous=value;known=true;
        if(emit)
        {
            var body=GetComponent<PNVisualBody>();
            if(body!=null && body.Actor!=null)PNLoricaShatter.PlayRig(gameObject.scene,body.Actor,GetComponent<EveTimeScale>(),camera);
            else
            {
                var sprite=GetComponentInChildren<SpriteRenderer>();
                if(sprite!=null && sprite.enabled && sprite.sprite!=null)
                {var bounds=sprite.bounds;PNLoricaShatter.PlayInWorld(GetComponent<EveEntityIdentity>()?.World,gameObject.scene,bounds.center,bounds.size,GetComponent<EveTimeScale>(),sprite.sortingLayerID,sprite.sortingOrder+5,-camera.transform.forward*.03f,sprite.sprite,sprite.flipX);}
            }
        }
    }
}
#endif
