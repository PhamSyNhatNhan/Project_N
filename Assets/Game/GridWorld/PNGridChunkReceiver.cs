#if UNITY_5_3_OR_NEWER
using System;
using System.Diagnostics;
using EveEngine;
using UnityEngine;

// EN: The applying side of the chunk channel (Docs/SandboxPlan.md, step G8), on the root of a CLIENT's grid world. The network layer puts the chunks the host sent into the world's inbox; every frame this
//     decodes a few of them into the world's cells, within a time budget, and the chunk host rebuilds the meshes and colliders of the chunks that changed (it already does that for any version change).
//     A chunk that cannot be decoded (a damaged blob) fails the world: the client leaves it instead of playing on a hole.
// VI: Phía áp của kênh mảnh (G8), trên root world lưới của CLIENT. Tầng mạng bỏ mảnh host gửi vào hộp thư của world; mỗi frame lớp này giải mã một ít vào ô của world trong ngân sách thời gian, host mảnh tự dựng lại mesh.
public sealed class PNGridChunkReceiver : MonoBehaviour
{
    public const int ChunksPerFrame = 8;
    public const double BudgetMilliseconds = 2;
    private EveWorldContext world;
    private PNGridWorldData data;
    private PNGridChunkInbox inbox;
    // Bodies this machine owns are freed from solid cells here, when chunks have been applied and every so often after (a held body, which the host does not let move, is tried again later).
    public const float RecheckSeconds = .25f;
    private float nextRecheck;
    private readonly System.Collections.Generic.List<PNMove> owned = new System.Collections.Generic.List<PNMove>();
    public int PushedOut { get; private set; }

    public PNGridChunkInbox Inbox => inbox;
    public int Applied { get; private set; }

    public static PNGridChunkReceiver Create(GameObject root, EveWorldContext world, PNGridWorldData data, PNGridChunkInbox inbox)
    {
        var receiver = root.AddComponent<PNGridChunkReceiver>();
        receiver.world = world ?? throw new ArgumentNullException(nameof(world)); receiver.data = data ?? throw new ArgumentNullException(nameof(data)); receiver.inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        return receiver;
    }

    // One frame's worth; public so the provider can drive it while the world is still loading (the component is not updated before the root is active).
    public int Pump()
    {
        if (inbox == null || data == null) return 0;
        long deadline = Stopwatch.GetTimestamp() + (long)(BudgetMilliseconds * Stopwatch.Frequency / 1000.0);
        int done;
        try { done = inbox.Apply(data, ChunksPerFrame, deadline); }
        catch (FormatException error) { if (world.State != EveWorldState.Disposed) world.Fail(error); return 0; }
        Applied += done;
        if (done > 0) { PushOwnedOut(); nextRecheck = Time.unscaledTime + RecheckSeconds; }
        return done;
    }

    // A body owned by this machine whose footprint is inside solid cells of this copy of the world is moved to the nearest free spot (PNMove.RelocateToPoint; its owner report carries the new place to the host).
    public int PushOwnedOut()
    {
        if (world == null || world.State == EveWorldState.Disposed || !world.Session.LocalOwnerId.HasValue) return 0;
        long local = world.Session.LocalOwnerId.Value; int moved = 0;
        owned.Clear();
        foreach (var entity in world.Session.Entities.Snapshot(world))
        {
            if (entity == null || !entity.IsReady) continue;
            var identity = entity.GetComponent<EveEntityIdentity>(); var move = entity.GetComponent<PNMove>();
            if (identity != null && move != null && identity.OwnerId == local) owned.Add(move);
        }
        foreach (var move in owned)
        {
            var at = move.transform.position;
            if (PNGridBodies.FootprintFree(data, at.x, at.z)) continue;
            if (!PNGridBodies.FindFreeSpot(data, at.x, at.z, out float x, out float z)) continue;
            try { move.RelocateToPoint(new Vector2(x, z)); moved++; }
            catch (Exception error) { UnityEngine.Debug.LogWarning("Could not move " + move.name + " out of a wall: " + error.Message, move); }
        }
        PushedOut += moved; return moved;
    }

    private void Update()
    {
        if (world == null || !world.CanExecute) return;
        Pump();
        if (Time.unscaledTime >= nextRecheck) { nextRecheck = Time.unscaledTime + RecheckSeconds; PushOwnedOut(); }
    }
}
#endif
