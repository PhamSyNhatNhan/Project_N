#if UNITY_5_3_OR_NEWER
using EveEngine;
using UnityEngine;

// Run after PNMove.FixedUpdate. Update-driven motion keeps its existing cadence.
// P4 reports still sample the completed preceding step plus intervening producers.
[DefaultExecutionOrder(32000)]
public sealed class PNWorldPhysicsLoop : MonoBehaviour
{
    private struct HeldBody
    {public Rigidbody Body;public RigidbodyConstraints Constraints;public Vector3 Velocity,Angular;public bool Gravity;}
    private readonly System.Collections.Generic.List<HeldBody> heldBodies=new System.Collections.Generic.List<HeldBody>();
    public EveSessionContext Session { get; private set; }
    private ulong boundary;
    public event System.Action<ulong> BeforeSimulation;
    public ulong Boundary => boundary;
    public void Bind(EveSessionContext session){Session=session;boundary=0;BeforeSimulation=null;}
    private void Update()
    {if(Session?.IsActive==true)foreach(var world in Session.Worlds.Snapshot())world.AdvanceClock(Time.deltaTime);}
    private void FixedUpdate()
    {
        if(Session?.IsActive!=true)return;
        boundary=checked(boundary+1);
        BeforeSimulation?.Invoke(boundary);
        foreach(var world in Session.Worlds.Snapshot())
        {
            try
            {
                // Hold also covers bodies without an enabled PNMove. Snapshot around
                // this step preserves each existing owner (including P4's own hold).
                if(world.CanExecute)
                foreach(var loader in Session.Entities.Snapshot(world))
                {
                    var identity=loader.LoadContext.Identity;if(!identity.IsGameplayHeld)continue;
                    foreach(var body in loader.GetComponentsInChildren<Rigidbody>())
                    {
                        if(body.isKinematic||body.GetComponentInParent<EveEntityIdentity>()!=identity)continue;
                        heldBodies.Add(new HeldBody{Body=body,Constraints=body.constraints,Velocity=body.linearVelocity,Angular=body.angularVelocity,Gravity=body.useGravity});
                        body.constraints=RigidbodyConstraints.FreezeAll;body.useGravity=false;body.linearVelocity=body.angularVelocity=Vector3.zero;
                    }
                }
                world.SimulatePhysics(boundary,Time.fixedDeltaTime);
            }
            catch(System.Exception error){world.Fail(error);Debug.LogException(error,this);}
            finally
            {
                foreach(var saved in heldBodies)if(saved.Body!=null)
                {saved.Body.constraints=saved.Constraints;saved.Body.useGravity=saved.Gravity;if(!saved.Body.isKinematic){saved.Body.linearVelocity=saved.Velocity;saved.Body.angularVelocity=saved.Angular;}}
                heldBodies.Clear();
            }
        }
    }
}
#endif
