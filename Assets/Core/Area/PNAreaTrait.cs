#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The behavior of an area trait (Docs/InstanceLifecyclePlan.md 3.4). Hooks run on the AUTHORITY only, never for a copy or a guess, through the manager's queue, and write nothing for the network:
//     the area's own sync record already carries what the others need. A trait keeps no state of its own (one behavior object per definition); what it must remember it asks the manager.
//     `OnCreated`: after the new area was made and recorded (a failed creation calls nothing). `OnEnded`: after the area's own `OnEnd`, only for a real finish (END reason), never for a Cancel.
// VI: Hanh vi cua trait area. Hook chi chay o AUTHORITY, khong bao gio cho ban sao/ban doan, qua hang doi cua manager, khong viet gi cho mang. Trait khong giu trang thai.
//     `OnCreated`: sau khi area moi da tao va ghi (tao that bai khong goi). `OnEnded`: sau `OnEnd` cua chinh area, chi khi ket thuc that (ly do END), khong khi Cancel.
public abstract class PNAreaTrait
{
    public virtual void OnCreated(PNAreaTraitContext context) { }
    public virtual void OnEnded(PNAreaTraitContext context, PNAreaEndReason reason) { }
}

// What a trait hook may know and do: the area it was called for, and the manager's queries and commands (every change goes through the manager's queue).
public sealed class PNAreaTraitContext
{
    private readonly PNAreaManager manager;
    internal PNAreaTraitContext(PNAreaManager manager, PNAreaHandle area, EveId areaId, EveId traitId, EveWorldContext world, Vector3 position, PNStat owner, JToken payload)
    { this.manager = manager; Area = area; AreaId = areaId; TraitId = traitId; World = world; Position = position; Owner = owner; this.payload = payload; }
    private readonly JToken payload;
    public PNAreaHandle Area { get; }
    public EveId AreaId { get; }
    // The effective id of the trait this hook belongs to (the same for an alias and the original).
    public EveId TraitId { get; }
    public EveWorldContext World { get; }
    public Vector3 Position { get; }
    public PNStat Owner { get; }
    public JToken Payload => payload.DeepClone();
    // Adds every area RUNNING in this world (an area in its Ending is not counted) that carries this trait, this one included, to `into`; returns how many were added.
    public int CollectRunning(List<PNAreaHandle> into) => manager.CollectWithTrait(World, TraitId, into);
    // Ends an area with a reason (queued; an area already ending or gone is left as it is).
    public bool End(PNAreaHandle handle, PNAreaEndReason reason) => manager.End(handle, reason);
}
#endif
