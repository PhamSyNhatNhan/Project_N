#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// Water after-images: frozen copies of poses left where they were, melting from the head down (her Dash and the held coat). At most `limit` at a time. Display only.
internal sealed class PNGuraWaterGhosts : IDisposable
{
    private sealed class Ghost { public PNGuraEchoRush.Silhouette Shape; public float Age; }
    private readonly List<Ghost> live = new();
    private readonly Transform host;
    private readonly Material material;
    private readonly int limit;
    private readonly float life;

    public PNGuraWaterGhosts(Transform host, int limit, float life)
    {
        this.host = host; this.limit = limit; this.life = life;
        material = PNGuraEchoRush.CreateMaterial("Gura Dash ghost", true);
    }

    public int Count => live.Count;

    // `offset`: where from the puppet it is left (a point she already passed); `age`: how long it has been there.
    public void Spawn(PNVisualActor actor, Vector3 offset = default, float age = 0f)
    {
        if (material == null || actor == null || actor.Mesh == null || actor.Renderer == null) return;
        while (live.Count >= limit) Drop(0);
        live.Add(new Ghost { Shape = PNGuraEchoRush.Silhouette.Freeze(actor, material, "Gura dash ghost", host, offset), Age = Mathf.Max(0f, age) });
    }

    public void Advance(float delta)
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var ghost = live[i];
            ghost.Age += delta;
            float k = ghost.Age / life;
            if (k >= 1f) { Drop(i); continue; }
            ghost.Shape.DrawFrozen(ghost.Age, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.15f, 1f, k)), true, .62f * (1f - .55f * k));
        }
    }

    public void Hide() { foreach (var ghost in live) ghost.Shape.Hide(); }

    public void Clear() { while (live.Count > 0) Drop(live.Count - 1); }

    private void Drop(int index) { live[index].Shape.Dispose(); live.RemoveAt(index); }

    public void Dispose()
    {
        Clear();
        if (material == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material);
    }
}

// EN: The picture of Gura's Dash (Docs/GuraCharacter.md 4.5): she flashes into water and slides, leaving up to three water after-images and one thin speed line from where she set off, with a ring
//     there; then the water runs off her (face first) and the line breaks into drops. While the Action runs its view drives this by the run's age; after it the picture goes on by itself.
//     Display only: it reads where the puppet is and nothing else.
// VI: Hinh Dash cua Gura: hoa nuoc luot di, du anh va vet manh, sau do nuoc rut. Chi la hinh.
public sealed class PNGuraDashVisual : MonoBehaviour
{
    private const float Melt = .15f, Tail = .34f, GhostEvery = .45f, Hip = .13f;
    private const int Ghosts = 3;
    private Transform owner;
    private EveTimeScale clock;
    private PNVisualBody body;
    private PNVisualActor covered;
    private PNGuraEchoRush.Silhouette flash;
    private PNGuraWaterGhosts ghosts;
    private PNGuraWaterFx water;
    private Material material;
    private Vector3 direction, start, head;
    private float duration, distance, age;
    private int spawned;
    private bool placed, released, removed;

    public bool Released => released;
    public int GhostCount => ghosts != null ? ghosts.Count : 0;

    internal static PNGuraDashVisual Create(Transform owner, Vector3 direction, float duration, float distance)
    {
        var node = new GameObject("Gura Dash Water") { layer = owner.gameObject.layer };
        node.transform.SetParent(owner, false);
        var visual = node.AddComponent<PNGuraDashVisual>();
        visual.owner = owner;
        visual.direction = direction;
        visual.duration = Mathf.Max(.01f, duration);
        visual.distance = Mathf.Max(0f, distance);
        visual.clock = owner.GetComponent<EveTimeScale>();
        visual.body = owner.GetComponentInChildren<PNVisualBody>();
        visual.water = new PNGuraWaterFx(node.transform, "Gura dash streak");
        visual.material = PNGuraEchoRush.CreateMaterial("Gura Dash flash");
        visual.ghosts = new PNGuraWaterGhosts(node.transform, Ghosts, .30f);
        return visual;
    }

    // One step of the running Action. `seeking`: the age was set, not advanced (first frame, late join, shown again): nothing of the skipped time is replayed.
    public void Run(float runAge, bool seeking)
    {
        if (removed || released || owner == null) return;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        float delta = placed && !seeking ? Mathf.Max(0f, runAge - age) : 0f;
        if (!placed)
        {
            // The first look at the run comes a step into it (a long one on a slow frame): she set off that much behind.
            start = owner.position - direction * (distance * Mathf.Clamp01(runAge / duration));
            placed = true;
        }
        else if (seeking)
        {
            // Shown again: nothing of the skipped time is replayed.
            ghosts.Clear();
            start = owner.position;
            spawned = Ghosts;
        }
        age = runAge;
        head = owner.position;
        ghosts.Advance(delta);
        float travelled = Travelled(head);
        var actor = Actor();
        // An image every stretch of the way, left where she passed it (several at once when one step covers several).
        bool left = false;
        while (actor != null && spawned < Ghosts && travelled >= spawned * GhostEvery)
        {
            float passed = travelled - spawned * GhostEvery;
            Vector3 back = start - head;
            back.y = 0f;
            ghosts.Spawn(actor, back.normalized * passed, travelled > .001f ? age * passed / travelled : 0f);
            spawned++;
            left = true;
        }
        if (left) ghosts.Advance(0f);
        Draw();
    }

    // The Action ended on its own: the line stays where she stopped.
    public void Release()
    {
        if (removed || released) return;
        released = true;
        age = Mathf.Max(age, duration);
    }

    private void LateUpdate()
    {
        if (released) Tick(clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime);
    }

    public void Tick(float delta)
    {
        if (removed || !released || owner == null) return;
        age += delta;
        ghosts.Advance(delta);
        if (age >= duration + Tail && ghosts.Count == 0) { Remove(); return; }
        Draw();
    }

    private float Travelled(Vector3 at)
    {
        Vector3 step = at - start;
        step.y = 0f;
        return step.magnitude;
    }

    // Her puppet, or nothing while it is away (a strike that hides it).
    private PNVisualActor Actor()
    {
        if (body == null && owner != null) body = owner.GetComponentInChildren<PNVisualBody>();
        var actor = body != null ? body.Actor : null;
        return actor != null && actor.Renderer != null && actor.Renderer.enabled ? actor : null;
    }

    private void Draw()
    {
        float after = age - duration;
        var actor = Actor();
        if (actor != null && material != null)
        {
            if (covered != actor) { flash?.Dispose(); flash = new PNGuraEchoRush.Silhouette(actor, material, "Gura dash flash", lift: .001f); covered = actor; }
            // Nearer than the sheet of the held coat, so the flash lies over it while both are on.
            flash.Draw(actor, age, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, Melt, after)), true, Mathf.Clamp01((age + .012f) / .03f) * .96f);
        }
        else flash?.Hide();

        water.Begin();
        Vector3 hip = Vector3.up * Hip;
        float travelled = Travelled(head);
        if (travelled > .2f)
        {
            float shrink = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .14f, after));
            Vector3 front = head - direction * .14f + hip, back = start + hip;
            var camera = PNViewCamera.Current;
            Vector3 view = camera != null ? camera.transform.forward : new Vector3(0f, -.5f, .866f);
            water.Streak(Vector3.Lerp(back, front, shrink), front, .042f, .9f * (1f - shrink * .55f), view);
            water.LineSpray(back, front, after - .05f, 12, .13f);
        }
        float ring = age / .26f;
        if (ring < 1f)
        {
            water.Ripple(start + Vector3.up * .04f, Mathf.Lerp(.10f, .38f, ring), .024f, 1f - ring);
            water.EdgeSpray(start + Vector3.up * .05f, -direction, .24f, 150f, age, 10, .22f);
        }
        water.Draw();
    }

    public void Hide()
    {
        if (removed) return;
        flash?.Hide();
        ghosts?.Hide();
        water?.Hide();
        gameObject.SetActive(false);
    }

    public void Remove()
    {
        Clear();
        if (this == null) return;
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }

    private void Clear()
    {
        if (removed) return;
        removed = true;
        flash?.Dispose(); flash = null; covered = null;
        ghosts?.Dispose(); ghosts = null;
        water?.Dispose(); water = null;
        if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        material = null;
    }

    private void OnDestroy() => Clear();
}
#endif
