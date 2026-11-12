#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The world provider of the grid worlds (Docs/SandboxPlan.md, step G1). It builds the cells from a descriptor (size, cell size, a built-in map, a seed), makes the chunk objects, hands the cells and the navigation
//     source to the build context and adds ONE slot, `main`, whose root is the grid host. It is portable: the descriptor is what a save would keep. A world definition only differs by its default descriptor; a restored world
//     brings its own through the build context.
// VI: Provider của world lưới: dựng ô từ descriptor, tạo các đối tượng mảnh, đưa ô và nguồn điều hướng cho build context, thêm một slot `main`. Portable: descriptor là thứ một bản lưu sẽ giữ.
public sealed class PNGridWorldProvider : IPNWorldProvider, IPNPortableWorldProvider
{
    private readonly PNGridWorldDescriptor defaultDescriptor;
    private PNGridWorldDescriptor used;

    public PNGridWorldProvider(PNGridWorldDescriptor defaultDescriptor)
    { this.defaultDescriptor = defaultDescriptor ?? throw new ArgumentNullException(nameof(defaultDescriptor)); }

    public JToken CaptureLoadDescriptor(PNWorldBuildContext context) => (used ?? defaultDescriptor).ToJson();

    public void ValidateLoadDescriptor(JToken descriptor)
    {
        var parsed = PNGridWorldDescriptor.Parse(descriptor);
        if (!PNGridTestMaps.Knows(parsed.Map)) throw new FormatException("Unknown grid map: " + parsed.Map);
    }

    public IEnumerator Load(PNWorldBuildContext context)
    {
        // A client's world is a replica (Docs/SandboxPlan.md G8): it never generates or restores the ground. It builds an empty world of the shape the host offered and receives the chunks the host chooses for it.
        bool replica = !context.World.Session.HasAuthority;
        PNGridChunkInbox inbox = null;
        PNGridWorldDescriptor descriptor;
        if (replica)
        {
            inbox = PNGridChunkInboxes.Find(context.World.Session, context.World.InstanceId) ?? throw new InvalidOperationException("The host offered this grid world without a descriptor.");
            descriptor = inbox.Shape;
        }
        else if (context.LoadDescriptor != null) { ValidateLoadDescriptor(context.LoadDescriptor); descriptor = PNGridWorldDescriptor.Parse(context.LoadDescriptor); }
        else descriptor = defaultDescriptor;
        // A restored world carries a one-time token; the save it names is taken out of the staging area now, and the world is filled from it instead of from its map (Docs/SandboxPlan.md G7).
        PNGridWorldSnapshot restored = null;
        if (descriptor.Restore != null)
        {
            restored = PNGridRestoreStaging.Take(descriptor.Restore) ?? throw new InvalidOperationException("The save this world should be restored from is no longer waiting.");
            if (!context.Features.Has(PNGridSaveIds.Feature)) throw new InvalidOperationException("Only a world with the feature " + PNGridSaveIds.Feature + " is restored from a save.");
        }
        descriptor = descriptor.WithoutRestore();
        used = descriptor;

        var grounds = context.World.Session.Registry.Get<PNGroundDefinition>();
        var definitions = grounds.Snapshot().Values.OrderBy(x => x.Id.ToString(), StringComparer.Ordinal).Take(PNGridWorldLimits.MaxPalette).ToArray();
        if (definitions.Length == 0) throw new InvalidOperationException("A grid world needs at least one registered ground type.");
        var data = new PNGridWorldData(descriptor, definitions.Select(x => x.Id.ToString()).ToArray());
        if (replica) { }
        else if (restored == null) PNGridTestMaps.Fill(data, descriptor.Map, descriptor.Seed);
        else
        {
            // A few chunks per frame, so a large world does not stall a frame; the clearance field is computed once at the end.
            restored.CheckAgainst(data);
            for (int next = 0; next < restored.ChunkCount;) { next = restored.ApplyChunks(data, next, 32); yield return null; }
            restored.Finish(data);
        }

        var host = PNGridWorldHost.Create(data, definitions.Select(x => new Color32(x.Red, x.Green, x.Blue, 255)).ToArray());
        context.Own(host.Dispose);   // registered before the chunk objects exist, so a failed or cancelled load cannot leak them
        context.SetGridWorld(data, descriptor);
        if (!replica) context.SetNavigation(new PNGridNavigationSource(data, definitions.Select(x => x.Walkable).ToArray(), PNNavigationProfile.GroundSmall));

        var build = host.BuildAll(16);
        while (build.MoveNext()) yield return null;
        // The edit gateway and the placed things (step G3); the root is also the world object `grid` that the place and break commands are sent to.
        var controller = PNGridWorldController.Create(context.World, host, definitions.Select(x => x.Walkable).ToArray());
        context.Own(controller.Dispose);
        host.gameObject.AddComponent<PNWorldObject>().Configure(PNGridCommands.ObjectKey);
        if (restored != null)
        {
            var placeables = context.World.Session.Registry.Instances();
            foreach (var item in restored.Placed)
            {
                var definition = placeables.GetEffectiveKind<PNPlaceableDefinition>(EveId.Parse(item.Definition)) ?? throw new FormatException("The save places " + item.Definition + ", which this game does not register.");
                controller.Placements.Restore(definition, item.Id, item.X, item.Z, item.Direction, item.Health);
            }
        }
        if (replica)
        {
            // The world is ready when the host has sent the first set (the chunks around this client's players), they are in the cells and their meshes and colliders are built (step G8).
            var receiver = PNGridChunkReceiver.Create(host.gameObject, context.World, data, inbox);
            context.Own(() => PNGridChunkInboxes.Remove(context.World.Session, context.World.InstanceId));
            while (!inbox.Ready) { receiver.Pump(); yield return null; }
            while (host.RebuildDirty(8) > 0) yield return null;
        }
        else
        {
            // The save service (step G7) for worlds that have the feature; it stays idle until a mode configures a folder and a key.
            if (context.Features.Has(PNGridSaveIds.Feature)) context.Own(PNGridWorldSaveService.Create(context.World, context.PhysicsScene, controller, descriptor, restored).Dispose);
            // The despawn service (step G4) for worlds that have the feature; a world without it keeps every enemy.
            if (context.Features.Has(PNDespawnIds.Feature)) context.Own(PNDespawnService.Create(context.World, context.PhysicsScene).Dispose);
        }
        // AddSlot wants the root to be in the scene it names as the source: the world's own physics scene.
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host.gameObject, context.PhysicsScene);
        context.AddSlot("main", context.PhysicsScene, new[] { host.gameObject });
        host.gameObject.SetActive(true);
    }
}
#endif
