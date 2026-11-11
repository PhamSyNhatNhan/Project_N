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
        PNGridWorldDescriptor descriptor;
        if (context.LoadDescriptor != null) { ValidateLoadDescriptor(context.LoadDescriptor); descriptor = PNGridWorldDescriptor.Parse(context.LoadDescriptor); }
        else descriptor = defaultDescriptor;
        used = descriptor;

        var grounds = context.World.Session.Registry.Get<PNGroundDefinition>();
        var definitions = grounds.Snapshot().Values.OrderBy(x => x.Id.ToString(), StringComparer.Ordinal).Take(PNGridWorldLimits.MaxPalette).ToArray();
        if (definitions.Length == 0) throw new InvalidOperationException("A grid world needs at least one registered ground type.");
        var data = new PNGridWorldData(descriptor, definitions.Select(x => x.Id.ToString()).ToArray());
        PNGridTestMaps.Fill(data, descriptor.Map, descriptor.Seed);

        var host = PNGridWorldHost.Create(data, definitions.Select(x => new Color32(x.Red, x.Green, x.Blue, 255)).ToArray());
        context.Own(host.Dispose);   // registered before the chunk objects exist, so a failed or cancelled load cannot leak them
        context.SetGridWorld(data);
        context.SetNavigation(new PNGridNavigationSource(data, definitions.Select(x => x.Walkable).ToArray(), PNNavigationProfile.GroundSmall));

        var build = host.BuildAll(16);
        while (build.MoveNext()) yield return null;
        // The edit gateway and the placed things (step G3); the root is also the world object `grid` that the place and break commands are sent to.
        var controller = PNGridWorldController.Create(context.World, host, definitions.Select(x => x.Walkable).ToArray());
        context.Own(controller.Dispose);
        host.gameObject.AddComponent<PNWorldObject>().Configure(PNGridCommands.ObjectKey);
        // AddSlot wants the root to be in the scene it names as the source: the world's own physics scene.
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host.gameObject, context.PhysicsScene);
        context.AddSlot("main", context.PhysicsScene, new[] { host.gameObject });
        host.gameObject.SetActive(true);
    }
}
#endif
