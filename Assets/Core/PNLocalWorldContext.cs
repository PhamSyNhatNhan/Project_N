#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// A local view is independent of authority, default entry world and subscription count.
public sealed class PNLocalWorldContext
{
    public static PNLocalWorldContext Current { get; private set; }
    public EveSessionContext Session { get; }
    public EveWorldContext World { get; private set; }
    public EveEntityLoader Actor { get; private set; }
    public ulong Membership { get; private set; }
    public ulong SelectionRevision { get; private set; }
    private IDisposable viewLease;
    public event Action Changed;
    public PNLocalWorldContext(EveSessionContext session)
    {if(Current?.Session.IsActive==true)throw new InvalidOperationException("Local presentation already attached.");Session=session;Current=this;session.RegisterCleanup(Clear);}
    public void Select(EveWorldContext world,EveEntityLoader actor=null)
    {
        if(world!=null&&(!ReferenceEquals(world.Session,Session)||world.State!=EveWorldState.Ready&&world.State!=EveWorldState.Paused)||actor!=null&&(!actor.IsReady||actor.LoadContext.Identity.World!=world))throw new InvalidOperationException("Resident local world/presence required.");
        var membership=actor?.LoadContext.Identity.MembershipRevision??0;
        if(World==world&&Actor==actor&&Membership==membership)return;
        var revision=checked(SelectionRevision+1);var next=world?.AcquireLease("local view");var previous=viewLease;viewLease=next;World=world;Actor=actor;Membership=membership;SelectionRevision=revision;previous?.Dispose();
        foreach(Action observer in Changed?.GetInvocationList()??Array.Empty<Delegate>())try{observer();}catch(Exception error){Debug.LogException(error);}
        foreach(var error in EveEventManager.Global.PublishSafely(PNLocalWorldBound.Event,new PNLocalWorldBound(this)))Debug.LogException(error);
    }
    public bool Contains(Component component)=>component!=null&&World!=null&&PNWorldGameplay.ForScene(Session,component.gameObject.scene)==World;
    public bool Accepts(EveEntityLoader loader)=>loader!=null&&loader.IsReady&&ReferenceEquals(loader.LoadContext.Session,Session)&&loader.LoadContext.Identity.World==World;
    public void Clear(){Select(null);Changed=null;if(ReferenceEquals(Current,this))Current=null;}
}
public readonly struct PNLocalWorldBound
{
    public readonly Guid Session,World;
    public readonly ulong Incarnation,Membership,Selection;
    public readonly EveEntityId Entity;
    public readonly long Generation,Load;
    public readonly EveEntityLoader Actor;
    public static readonly EveId Event=EveId.Parse("project_n:local_world_bound");
    public PNLocalWorldBound(PNLocalWorldContext context)
    {Session=context.Session.SessionId;World=context.World?.InstanceId??Guid.Empty;Incarnation=context.World?.Incarnation??0;Membership=context.Membership;Selection=context.SelectionRevision;Actor=context.Actor;Entity=Actor!=null?Actor.LoadContext.Identity.Id:default;Generation=Actor?.LoadContext.Generation??0;Load=Actor?.LoadContext.LoadId??0;}
}
public readonly struct PNWorldPresentationAdded
{
    public readonly EveWorldContext World;
    public readonly GameObject Root;
    public static readonly EveId Event=EveId.Parse("project_n:world_presentation_added");
    public PNWorldPresentationAdded(EveWorldContext world,GameObject root){World=world;Root=root;}
}
public static class PNWorldPresentationRegistration
{
    public static void Register(EveWorldContext world,GameObject root)
    {
        if(world==null||root==null)return;
        if(root.scene!=world.PhysicsOwner)throw new InvalidOperationException("World presentation must live in its physics/content owner.");
        foreach(var error in EveEventManager.Global.PublishSafely(PNWorldPresentationAdded.Event,new PNWorldPresentationAdded(world,root)))Debug.LogException(error);
    }
}
#endif
