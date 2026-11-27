#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps Gura's puppet out of sight while a picture stands in for her. Every holder lets go on its own; she shows again when the last one has.
internal static class PNGuraSubmerge
{
    private static readonly ConditionalWeakTable<PNVisualBody, HashSet<object>> Holders = new();

    public static bool Held(PNVisualBody body) => body != null && Holders.TryGetValue(body, out var set) && set.Count > 0;

    public static void Hold(PNVisualBody body, object holder, float fade)
    {
        if (body == null) return;
        Holders.GetValue(body, _ => new HashSet<object>()).Add(holder);
        Apply(body, fade);
    }

    public static void Release(PNVisualBody body, object holder)
    {
        if (body == null || !Holders.TryGetValue(body, out var set) || !set.Remove(holder) || set.Count > 0) return;
        Apply(body, 1f);
    }

    private static void Apply(PNVisualBody body, float fade)
    {
        var actor = body.Actor;
        if (actor == null) return;
        actor.SetFade(fade);
        actor.Tick(0f);
    }
}

// The dive: Gura goes under and a water shark's fin cuts the surface where she is. It trails a wake while she moves, circles while she waits,
// and churns when her time is nearly up. View only: the immunity and the movement belong to the Action.
public sealed class PNGuraDiveVisual : MonoBehaviour, IDisposable
{
    private const float PlungeFrom = .03f, Gone = .14f, WarnSeconds = 1f, TailSeconds = .18f;
    private const float FinScale = 2.8f, OrbitRadius = .34f, SurfaceLift = .03f;

    private struct Mark
    {
        public Vector3 At;
        public float Age, Strength;
    }

    private PNGuraSharkModel fin;
    private PNGuraWaterFx water, spray;
    private PNVisualBody body;
    private EveTimeScale clock;
    private Transform caster;
    private readonly List<Mark> wake = new(64);
    private readonly List<Vector3> path = new(64);
    private readonly List<Vector2> strength = new(64);
    private Vector3 origin, heading = Vector3.right, finAt;
    private float age, duration, previousAge, orbit, orbiting, tail;
    private bool placed, released, disposed;

    internal static PNGuraDiveVisual Create(Transform owner, Vector3 facing)
    {
        var root = new GameObject("Gura dive surface") { layer = owner.gameObject.layer };
        SceneManager.MoveGameObjectToScene(root, owner.gameObject.scene);
        var dive = root.AddComponent<PNGuraDiveVisual>();
        dive.caster = owner;
        dive.body = owner.GetComponent<PNVisualBody>();
        dive.clock = owner.GetComponent<EveTimeScale>();
        dive.heading = facing;
        dive.fin = new PNGuraSharkModel(root.transform);
        dive.water = new PNGuraWaterFx(root.transform, "Gura dive water");
        dive.spray = new PNGuraWaterFx(root.transform, "Gura dive spray");
        return dive;
    }

    internal void Draw(Vector3 point, float seconds, float length, bool seeking)
    {
        if (disposed || released) return;
        if (!placed || seeking)
        {
            wake.Clear();
            previousAge = seconds;
            origin = point;
            orbiting = 1f;
            placed = true;
        }
        float step = Mathf.Max(0f, seconds - previousAge);
        previousAge = seconds;
        age = seconds;
        duration = length;
        Move(point, step);
        PNGuraSubmerge.Hold(body, this, 1f - Ease(PlungeFrom, Gone, age));
        DrawFrame(1f);
    }

    // The fin goes along with her when she moves and circles the spot when she does not.
    private void Move(Vector3 point, float step)
    {
        Vector3 moved = point - origin;
        moved.y = 0f;
        origin = point;
        float speed = step > 1e-4f ? moved.magnitude / step : 0f;
        float still = speed < .35f ? 1f : 0f;
        orbiting = Mathf.MoveTowards(orbiting, still, step * (still > .5f ? 1.6f : 5f));
        float warn = Warning();
        orbit += step * Mathf.Lerp(3.2f, 8f, warn);
        Vector3 around = new(Mathf.Cos(orbit), 0f, Mathf.Sin(orbit));
        Vector3 tangent = new(-around.z, 0f, around.x);
        finAt = origin + around * (OrbitRadius * orbiting * (1f - .35f * warn));
        Vector3 wanted = orbiting > .5f || moved.sqrMagnitude < 1e-8f ? tangent : moved.normalized;
        if (step > 0f) heading = Vector3.Slerp(heading, wanted, 1f - Mathf.Exp(-step * 14f)).normalized;
        // Circling on the spot leaves no wake: the rings tell where she is.
        wake.Add(new Mark { At = finAt, Age = age, Strength = 1f - orbiting });
        while (wake.Count > 0 && (age - wake[0].Age > .75f || wake.Count > 60)) wake.RemoveAt(0);
    }

    private float Warning() => duration > 0f ? Ease(duration - WarnSeconds, duration - .15f, age) : 0f;

    private void DrawFrame(float stay)
    {
        Vector3 surface = origin + Vector3.up * SurfaceLift;
        Vector3 at = new(finAt.x, surface.y, finAt.z);
        Quaternion view = Quaternion.Euler(31f, PNGuraSharkBite.CameraYaw(), 0f);
        Vector3 right = view * Vector3.right, up = view * Vector3.up;
        float opened = Ease(.02f, .20f, age) * stay;
        float warn = Warning();

        water.Begin();
        water.Pool(surface, .66f * Mathf.Lerp(.35f, 1f, opened), age, opened);
        water.Silhouette(surface + Vector3.up * .002f, new Vector3(at.x - surface.x, 0f, at.z - surface.z), heading, 1.15f, Mathf.Sin(age * 9f), opened * .75f);
        path.Clear();
        strength.Clear();
        foreach (var mark in wake)
        {
            path.Add(new Vector3(mark.At.x, surface.y + .006f, mark.At.z));
            strength.Add(new Vector2(age - mark.Age, mark.Strength * stay));
        }
        water.Wake(path, strength);
        // Rings spreading from where she is; quicker as the time runs out.
        for (int i = 0; i < 2; i++)
        {
            float ring = Mathf.Repeat(age * Mathf.Lerp(1.3f, 3f, warn) + i * .5f, 1f);
            water.Ripple(surface + Vector3.up * .004f, .20f + .62f * ring, .045f, (1f - ring) * opened);
        }
        water.Draw();

        spray.Begin();
        spray.Mist(surface, .55f, age - .05f, right, up);
        spray.Splash(surface, .16f, .55f, age - .06f, 34, .75f, .036f, right, up);
        if (opened > .5f) spray.Bow(at + heading * .10f, heading, age, stay, right, up);
        if (warn > 0f) spray.Boil(surface, .55f, age, warn * stay, right, up);
        spray.Draw();

        // The fin comes up after she has gone under, and slips back under at the end.
        float rise = Ease(.10f, .30f, age) * stay;
        if (rise > .01f)
            fin.DrawFin(at + Vector3.down * (.48f * (1f - rise)), Quaternion.LookRotation(heading, Vector3.up), FinScale, surface.y, age * Mathf.Lerp(1.4f, 2.6f, warn));
        else fin.Hide();
    }

    private static float Ease(float from, float to, float time) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, time));

    internal void Hide()
    {
        placed = false;
        PNGuraSubmerge.Release(body, this);
        fin?.Hide();
        water?.Hide();
        spray?.Hide();
    }

    // The Action is over: the fin slips under for a moment more. Whatever follows may keep her hidden in the meantime.
    internal bool Release()
    {
        if (!Application.isPlaying || disposed || !placed) return false;
        released = true;
        return true;
    }

    private void LateUpdate()
    {
        if (!released || disposed) return;
        float delta = clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime;
        age += delta;
        tail += delta;
        if (caster == null || tail >= TailSeconds) { Dispose(); return; }
        DrawFrame(1f - Mathf.Clamp01(tail / TailSeconds));
    }

    public void Dispose()
    {
        Cleanup();
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }

    private void OnDestroy() => Cleanup();

    private void Cleanup()
    {
        if (disposed) return;
        disposed = true;
        PNGuraSubmerge.Release(body, this);
        fin?.Dispose();
        water?.Dispose();
        spray?.Dispose();
    }
}
#endif
