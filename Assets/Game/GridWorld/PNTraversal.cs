#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: What a body can stand on (Docs/SandboxPlan.md, step G12). It holds the body's locomotion (registry `locomotions`: which terrains it can stand on) and the pushes on top of it, and keeps the physics
//     of the body in line: the barriers a grid world builds between terrains sit on layers of their own, and the body ignores the layers of every terrain its locomotion stands on (Rigidbody.excludeLayers).
//     The locomotion never changes by itself: a body that jumps high is still a walker and is still stopped by a barrier. Only `Push(id)` changes it, for as long as the handle is not `Pop`ped; the one in force is the
//     pushed locomotion with the highest priority (the latest wins a tie), or the body's own when nothing is pushed. A registered Action (PNLocomotionAction) pushes and pops for a skill.
//     The fall rule: in a grid world, a body whose cell is a terrain its locomotion cannot stand on (something carried it over a hole, a push ended over one) is put back on the last cell its OWN locomotion could stand on,
//     with no damage. It reads the cells, never casts a ray, and only the machine that moves the body applies it. A world that is not a grid world has no rule.
// VI: Thân đứng được trên terrain nào: locomotion gốc cộng các Push chồng lên; giữ vật lý khớp (bỏ qua layer rào của terrain đứng được). Locomotion không tự đổi, chỉ Push mới đổi. Luật rơi: thân đứng trên terrain mà
//     locomotion không đứng được thì được đưa về ô an toàn cuối (ô mà locomotion GỐC đứng được), không mất máu.
public sealed class PNTraversal : MonoBehaviour, IPNLocomotionSource
{
    public const int MaxPushes = 16, SearchRings = 16;

    private struct Pushed { public int Handle; public PNLocomotionDefinition Locomotion; }

    private EveRegistryHub registry;
    private PNLocomotionDefinition own, effective;
    private IReadOnlyDictionary<EveId, int> layers;
    private readonly List<Pushed> pushes = new List<Pushed>();
    private int nextHandle;
    private Rigidbody body;
    private LayerMask ownExclude;
    private bool began, hasSafe;
    private PNLocomotionDefinition replicated;
    private int revision;
    private Vector3 safePoint;

    // The locomotion in force and the body's own.
    public EveId Effective => effective != null ? effective.Id : default;
    public EveId Own => own != null ? own.Id : default;
    // IPNLocomotionSource: what the navigation reads (a pathfinding body and the Move mirror), and a revision that changes with every change of the locomotion in force.
    public PNLocomotionDefinition Locomotion => effective;
    public int LocomotionRevision => revision;
    // G12b: pushes are the gameplay authority's (host, or offline). A client owner only mirrors the locomotion the host sends with its Move state; observers and clients never push.
    public bool CanPush
    {
        get
        {
            var identity = GetComponent<EveEntityIdentity>(); var world = identity != null ? identity.World : null;
            return began && world != null && world.Session.HasAuthority;
        }
    }
    public int PushCount => pushes.Count;
    // How many times the fall rule put the body back (for checks).
    public int Falls { get; private set; }
    // The last cell centre the body's own locomotion could stand on (valid after the first fixed step in a grid world).
    public bool HasSafePoint => hasSafe;
    public Vector3 SafePoint => safePoint;

    // Finds the component of a spawned body or adds it, and starts it with the body's own locomotion. Safe to call for a pooled body again.
    public static PNTraversal Begin(GameObject root, EveRegistryHub registry, EveId locomotion)
    {
        var traversal = root.GetComponent<PNTraversal>() ?? root.AddComponent<PNTraversal>();
        traversal.Setup(registry, locomotion);
        return traversal;
    }

    private void Setup(EveRegistryHub hub, EveId locomotion)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        Clear();
        registry = hub;
        own = hub.Locomotions().Get(locomotion);
        layers = PNTerrainLayers.Assign(hub.Terrains().Snapshot().Values.Select(x => x.Id));
        body = GetComponent<Rigidbody>(); ownExclude = body != null ? body.excludeLayers : default;
        began = true; Refresh();
    }

    // Puts the body back as it was before Begin (pool reset or removal): no pushes, the physics layers it ignored, no safe point.
    public void Clear()
    {
        if (body != null) body.excludeLayers = ownExclude;
        pushes.Clear(); registry = null; own = effective = replicated = null; layers = null; body = null; began = false; hasSafe = false; Falls = 0; nextHandle = 0; revision = 0;
    }

    // Puts a locomotion on the body until it is popped; returns the handle to pop. The id must be registered.
    public int Push(EveId locomotion)
    {
        if (!began) throw new InvalidOperationException("The traversal has not begun.");
        if (!CanPush) throw new InvalidOperationException("Only the gameplay authority (the host, or an offline game) pushes a locomotion; a client mirrors the one the host sends.");
        if (pushes.Count >= MaxPushes) throw new InvalidOperationException("A body carries at most " + MaxPushes + " pushed locomotions.");
        var definition = registry.Locomotions().Get(locomotion);
        int handle = ++nextHandle;
        pushes.Add(new Pushed { Handle = handle, Locomotion = definition });
        Refresh();
        return handle;
    }

    // Takes a push off; false when the handle is not (or no longer) there.
    public bool Pop(int handle)
    {
        int index = pushes.FindIndex(x => x.Handle == handle);
        if (index < 0) return false;
        pushes.RemoveAt(index);
        Refresh();
        return true;
    }

    public bool CanStandOn(EveId terrain) => effective != null && effective.CanStandOn(terrain);

    // The pushed locomotion with the highest priority (the latest push wins a tie), else the body's own.
    private void Refresh()
    {
        PNLocomotionDefinition chosen = own; int best = int.MinValue;
        foreach (var push in pushes) if (push.Locomotion.Priority >= best) { best = push.Locomotion.Priority; chosen = push.Locomotion; }
        if (replicated != null) chosen = replicated;   // a client owner: the host decides
        if (!ReferenceEquals(effective, chosen)) revision++;
        effective = chosen;
        if (body != null) body.excludeLayers = ownExclude.value | PNTerrainLayers.ExcludeMask(effective, layers);
    }

    private void OnDisable() { if (began && body != null) body.excludeLayers = ownExclude; }
    private void OnEnable() { if (began) Refresh(); }

    private void FixedUpdate()
    {
        if (!began || effective == null) return;
        var identity = GetComponent<EveEntityIdentity>(); var world = identity != null ? identity.World : null;
        if (world == null || !world.CanExecute) return;
        var move = GetComponent<PNMove>();
        var role = move != null ? move.NetworkRole : PNMoveNetworkRole.Local;
        // A client owner takes the locomotion the host sent with its Move state (G12b); an observer is moved by poses and needs neither the locomotion nor the fall rule.
        if (role == PNMoveNetworkRole.Owner) { var mirror = move.OwnerMovement != null ? move.OwnerMovement.Mirror : null; if (mirror != null) MirrorLocomotion(mirror.Locomotion); }
        if (role == PNMoveNetworkRole.Observer || role == PNMoveNetworkRole.RemoteOwnerHost) return;   // a body a client owns is that client's to put back
        if (role != PNMoveNetworkRole.Owner && !world.Session.HasAuthority) return;
        var controller = PNGridWorldController.Find(world);
        if (controller == null || controller.Host == null) return;
        var data = controller.Data; var host = controller.Host;
        int cx = Mathf.FloorToInt(transform.position.x / data.CellSize), cz = Mathf.FloorToInt(transform.position.z / data.CellSize);
        if (!data.InBounds(cx, cz)) return;
        var terrain = host.TerrainAt(cx, cz);
        if (effective.CanStandOn(terrain))
        {
            if (own.CanStandOn(terrain)) { safePoint = new Vector3((cx + .5f) * data.CellSize, transform.position.y, (cz + .5f) * data.CellSize); hasSafe = true; }
            return;
        }
        // Over a terrain the body cannot stand on: back to the last safe cell, else the nearest cell of the body's own locomotion.
        Vector3 target;
        if (hasSafe) target = safePoint;
        else if (!FindStandable(host, data, cx, cz, out target)) return;
        Falls++;
        if (move != null) move.RelocateToPoint(new Vector2(target.x, target.z));
        else if (body != null) { body.position = new Vector3(target.x, body.position.y, target.z); body.linearVelocity = Vector3.zero; }
        else transform.position = new Vector3(target.x, transform.position.y, target.z);
    }

    // The locomotion the host says this client-owned body has ("" = nothing declared, the body's own).
    public void MirrorLocomotion(string id)
    {
        PNLocomotionDefinition next = null;
        if (id.Length > 0 && EveId.TryParse(id, out var parsed)) registry.Locomotions().TryGet(parsed, out next);
        if (ReferenceEquals(next, replicated)) return;
        replicated = next; Refresh();
    }

    // The nearest cell (rings around the start) of a terrain the own locomotion stands on, not solid.
    private bool FindStandable(PNGridWorldHost host, PNGridWorldData data, int cx, int cz, out Vector3 point)
    {
        for (int ring = 1; ring <= SearchRings; ring++)
            for (int dz = -ring; dz <= ring; dz++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                    int x = cx + dx, z = cz + dz;
                    if (!data.InBounds(x, z) || data.IsSolid(x, z) || !own.CanStandOn(host.TerrainAt(x, z))) continue;
                    point = new Vector3((x + .5f) * data.CellSize, transform.position.y, (z + .5f) * data.CellSize); return true;
                }
        point = default; return false;
    }
}
#endif
