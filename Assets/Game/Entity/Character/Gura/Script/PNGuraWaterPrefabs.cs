#if UNITY_5_3_OR_NEWER
using UnityEngine;

public sealed class PNGuraWaterPrefabs : ScriptableObject
{
    public GameObject TridentImpact;
    public GameObject SharkBreach;
    public GameObject DiveBubbles;
    public GameObject DashSplash;
    public GameObject DashFoam;
    public GameObject Slash;
}

internal sealed class PNGuraWaterParticles : System.IDisposable
{
    private readonly GameObject node;
    private readonly ParticleSystem[] systems;
    private bool shown;
    private float previousAge;

    public static PNGuraWaterParticles Create(Transform owner, string effect)
    {
        var pack = Resources.Load<PNGuraWaterPrefabs>("PNVfx/GuraWaterPrefabs");
        if (pack == null) return null;
        switch (effect)
        {
            case "impact":
                return new PNGuraWaterParticles(owner, pack.TridentImpact, "water_ring", "splash_ground", "splash_long");
            case "shark":
                return new PNGuraWaterParticles(owner, pack.SharkBreach, "water_ring", "splash_ground", "splash_long");
            case "dive":
                return new PNGuraWaterParticles(owner, pack.DiveBubbles);
            case "dash":
                return new PNGuraWaterParticles(owner, pack.DashSplash, "ground_water", "splash_long");
            case "surge":
                return new PNGuraWaterParticles(owner, pack.DashFoam, "splash_mesh", "splash_long");
            case "cut":
                return new PNGuraWaterParticles(owner, pack.Slash, "slash_alp", "splash1", "splash2");
            default:
                return null;
        }
    }

    private PNGuraWaterParticles(Transform owner, GameObject source, params string[] layers)
    {
        if (source == null) { systems = System.Array.Empty<ParticleSystem>(); return; }
        node = UnityEngine.Object.Instantiate(source, owner, false);
        node.name = source.name + " (Gura)";
        node.SetActive(false);
        node.layer = owner.gameObject.layer;

        var selected = new System.Collections.Generic.List<ParticleSystem>();
        foreach (var system in node.GetComponentsInChildren<ParticleSystem>(true))
        {
            bool include = layers.Length == 0 || System.Array.IndexOf(layers, system.gameObject.name) >= 0;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.enabled = include;
            var main = system.main;
            main.playOnAwake = false;
            main.stopAction = ParticleSystemStopAction.None;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            system.gameObject.layer = node.layer;
            if (!include) continue;
            system.useAutoRandomSeed = false;
            system.randomSeed = (uint)(113 + selected.Count * 79);
            selected.Add(system);
        }
        systems = selected.ToArray();
    }

    // Sample the stock prefab at the Action/object age. It does not run on wall-clock time.
    public void Sample(Vector3 position, Vector3 direction, Vector3 scale, float age, bool seek = false, float yaw = 0f)
    {
        if (node == null) return;
        node.transform.SetPositionAndRotation(position,
            (direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity) * Quaternion.Euler(0f, yaw, 0f));
        node.transform.localScale = scale;
        bool restart = seek || !shown || age < previousAge;
        if (!shown) { shown = true; node.SetActive(true); }
        if (!restart && age <= previousAge) return;
        float step = restart ? age : age - previousAge;
        foreach (var system in systems) system.Simulate(step, false, restart, true);
        previousAge = age;
    }

    public void Hide()
    {
        if (!shown || node == null) return;
        shown = false;
        foreach (var system in systems) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        node.SetActive(false);
    }

    public void Dispose()
    {
        if (node == null) return;
        Hide();
        if (Application.isPlaying) UnityEngine.Object.Destroy(node);
        else UnityEngine.Object.DestroyImmediate(node);
    }
}
#endif
