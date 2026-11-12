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
        return done;
    }

    private void Update()
    {
        if (world == null || !world.CanExecute) return;
        Pump();
    }
}
#endif
