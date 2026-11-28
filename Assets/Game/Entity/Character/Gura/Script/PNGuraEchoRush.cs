#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// EN: The picture of attack 3 (Docs/GuraCharacter.md 4.1). Gura melts into water while the body goes in; five water doubles appear one after another, each dashes straight through the place of
//     the enemy and stays at the end of its cut, so that they end evenly around it; the cuts draw a star that stays open in the middle. After the last one the cuts and the doubles burst
//     together, and Gura forms again where the step back ends. Everything is placed from the Action age; the body motions and the damage belong to the registered Action, not to this view:
//     the first and the last cut fall on its first and last hit, the others are spread evenly between them.
//     The cuts are chords of the hit circle (centre = the caster while the hits are open), so on the ground they never reach past the hit radius.
// VI: Hinh cua don 3: Gura tan thanh nuoc, nam bong nuoc hien lan luot va lao xuyen qua cho dich, vet chem ve thanh ngoi sao; xong cung vo thanh nuoc va Gura hien lai.
internal sealed class PNGuraEchoRush : IDisposable
{
    public const int Doubles = 5;
    // Each cut turns this much from the one before (a star, not a ring); the last one runs straight back toward where Gura forms again.
    private const float StarStep = -144f;
    // Gura: the body is gone from HideAt to ShowAt; the water coat melts it away and builds it again around those two moments.
    private const float HideAt = .035f, MeltEnd = .125f, FormAt = .74f, ShowAt = .84f, CoatEnd = 1f;
    // A double, in seconds around its hit: it forms, dashes through, then holds at the end of its cut.
    private const float AppearLead = .10f, FormSeconds = .05f, DashLead = .045f, DashFollow = .015f;
    // All three burst together this long after the last hit.
    private const float BurstAfter = .18f, BurstSeconds = .12f;
    private const float EnemyAhead = .55f, CrossOffset = .20f, ChordReach = .98f;

    private readonly Transform owner;
    private readonly PNVisualBody body;
    private readonly Vector3 forward;
    private readonly float[] times = new float[Doubles];
    private readonly Vector3[] directions = new Vector3[Doubles];
    private readonly float radius, approachEnd;
    private readonly PNGuraSlashArc[] cuts = new PNGuraSlashArc[Doubles];
    private readonly Double[] doubles = new Double[Doubles];
    private readonly PNGuraWaterFx water;
    private readonly PNGuraAttackWeapon weapon;
    private readonly PNGuraWaterParticles melt, form;
    private Material material;
    private Silhouette coat;
    private MeshRenderer hiddenBody;
    private PNGroundShadow shadow;
    private Vector3 anchor, formAnchor;
    private bool anchored, disposed;

    public PNGuraEchoRush(Transform owner, PNVisualBody body, Vector3 forward, float[] hits, float radius, float approachEnd)
    {
        this.owner = owner;
        this.body = body;
        this.forward = forward;
        float first = hits.Length > 0 ? hits[0] : approachEnd + AppearLead, final = hits.Length > 0 ? hits[hits.Length - 1] : first;
        for (int i = 0; i < Doubles; i++)
        {
            times[i] = Mathf.Lerp(first, final, i / (Doubles - 1f));
            directions[i] = Quaternion.AngleAxis(180f + (i - (Doubles - 1)) * StarStep, Vector3.up) * forward;
        }
        this.radius = radius;
        this.approachEnd = approachEnd;
        water = new PNGuraWaterFx(owner, "Gura echo water");
        weapon = new PNGuraAttackWeapon(owner, "Gura attack trident");
        for (int i = 0; i < Doubles; i++) cuts[i] = PNGuraSlashArc.Create(owner, false, 1f);
        melt = PNGuraWaterParticles.Create(owner, "surge");
        form = PNGuraWaterParticles.Create(owner, "surge");
        material = CreateMaterial("Gura water echo");
    }

    // The water drawn from a puppet's own mesh (also her Dash and its coat). `behind` = drawn before the puppets, for what lies under or after them (outline, after-images).
    internal static Material CreateMaterial(string name, bool behind = false)
    {
        var shader = Resources.Load<Shader>("PNVfx/GuraEcho");
        if (shader == null) return null;
        var created = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        created.SetTexture("_NoiseTex", Resources.Load<Texture2D>("StarreaperVfx/RainNoise"));
        if (behind) created.renderQueue = (int)RenderQueue.Transparent - 1;
        return created;
    }

    private float BurstAt => times[Doubles - 1] + BurstAfter;

    public void Draw(float age, bool seeking)
    {
        if (disposed || owner == null) return;
        Vector3 origin = owner.position;
        // The circle of the cuts stays where the approach ended: the step back must not drag them along.
        if (!anchored || seeking) { anchor = origin; anchored = age >= approachEnd; }
        var actor = body != null ? body.Actor : null;

        water.Begin();
        DrawBody(actor, origin, age, seeking);
        for (int i = 0; i < Doubles; i++) DrawCut(actor, i, age);
        DrawRing(age);
        DrawBurst(age);
        water.Draw();
    }

    // Gura herself: melts into water, is gone, forms again on the spot the step back reaches.
    private void DrawBody(PNVisualActor actor, Vector3 origin, float age, bool seeking)
    {
        bool gone = age >= HideAt && age < ShowAt;
        if (actor != null && actor.Renderer != null)
        {
            if (gone) { hiddenBody = actor.Renderer; hiddenBody.enabled = false; }
            else RestoreBody();
        }
        SuppressShadow(age >= HideAt + .03f && age < ShowAt - .04f);

        if (actor != null && material != null)
        {
            coat ??= new Silhouette(actor, material, "Gura water coat");
            if (age < MeltEnd)
                coat.Draw(actor, age, Mathf.InverseLerp(HideAt, MeltEnd, age), true, Mathf.InverseLerp(0f, HideAt, age + .012f));
            else if (age >= FormAt && age < ShowAt)
                coat.Draw(actor, age, 1f - Mathf.InverseLerp(FormAt, ShowAt, age), true, 1f);
            else if (age >= ShowAt && age < CoatEnd)
                coat.Draw(actor, age, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ShowAt + .02f, CoatEnd, age)), true, .95f);
            else coat.Hide();
        }

        Vector3 surface = origin + Vector3.up * .045f;
        if (age < .20f)
        {
            if (age < approachEnd || seeking) formAnchor = origin;
            float p = Mathf.Clamp01(age / .20f);
            water.Ripple(formAnchor + Vector3.up * .045f, Mathf.Lerp(.10f, .46f, p), .028f, (1f - p) * .85f);
            water.EdgeSpray(formAnchor + Vector3.up * .05f, forward, .30f, 360f, age - .02f, 18, .42f);
            melt?.Sample(formAnchor - forward * .06f, -forward, new Vector3(.30f, .16f, .32f), .05f + 1.2f * age, seeking);
        }
        else melt?.Hide();
        if (age >= FormAt - .02f && age < CoatEnd)
        {
            float p = Mathf.InverseLerp(FormAt - .02f, CoatEnd, age);
            water.Ripple(surface, Mathf.Lerp(.42f, .16f, Mathf.Clamp01(p * 2.2f)), .026f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(p * 1.4f)) * .8f);
            water.EdgeSpray(surface, forward, .30f, 360f, age - (ShowAt - .02f), 14, .34f);
            form?.Sample(origin, forward, new Vector3(.24f, .17f, .24f), .05f + 1.8f * (age - FormAt + .02f), seeking);
        }
        else form?.Hide();

        float held = age < HideAt ? Mathf.InverseLerp(0f, HideAt, age + .01f)
            : Mathf.Min(Mathf.InverseLerp(ShowAt - .04f, ShowAt + .03f, age), 1f - Mathf.InverseLerp(CoatEnd - .09f, CoatEnd, age));
        Vector3 feet = actor != null ? actor.transform.position : origin;
        Vector3 axis = (forward + Vector3.up * .18f).normalized;
        weapon.Draw(new PNGuraAttackWeapon.Pose(feet + Vector3.up * .15f + forward * .16f, axis,
            Vector3.Cross(Vector3.up, axis).normalized, .66f, .22f, 0f), held);
    }

    // The two ends of one double's cut: the chord of the hit circle along its heading, passing a little to the side of the enemy's place.
    private void Chord(int index, out Vector3 from, out Vector3 to)
    {
        Vector3 heading = directions[index];
        Vector3 through = anchor + forward * EnemyAhead * (radius / 1.3f) + Vector3.Cross(Vector3.up, heading) * (CrossOffset * radius / 1.3f);
        Vector3 offset = through - anchor;
        offset.y = 0f;
        float along = Vector3.Dot(offset, heading);
        float reach = radius * ChordReach;
        float half = Mathf.Sqrt(Mathf.Max(.0001f, along * along - offset.sqrMagnitude + reach * reach));
        from = through + heading * (-along - half);
        to = through + heading * (-along + half);
    }

    private void DrawCut(PNVisualActor actor, int index, float age)
    {
        float hit = times[index];
        float appear = hit - AppearLead, dash = hit - DashLead, arrive = hit + DashFollow, burst = BurstAt;
        Vector3 heading = directions[index];
        Chord(index, out Vector3 from, out Vector3 to);
        float travel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(dash, arrive, age));
        bool last = index == Doubles - 1;

        // The streak: grows behind the double, holds so all three can be read together, then breaks up with the burst.
        float since = age < hit ? age - hit : age < burst ? Mathf.Min(age - hit, .016f) : .016f + (age - burst) * 1.9f;
        // One cut starts high, the next low: crossing planes.
        float high = .42f, low = .19f;
        Vector3 start = from + Vector3.up * (index % 2 == 0 ? high : low);
        Vector3 end = to + Vector3.up * (index % 2 == 0 ? low : high);
        float width = (last ? .58f : .42f) * (radius / 1.3f);
        float settle = Mathf.Lerp(1f, last ? .92f : .78f, Mathf.InverseLerp(arrive, arrive + .12f, age));
        cuts[index]?.DrawLine(start, end, width, since, age >= dash ? travel : 0f, settle, age - hit);
        if (age >= dash && age < burst + BurstSeconds)
        {
            float flash = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(arrive, arrive + .09f, age));
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(burst, burst + BurstSeconds * .8f, age));
            water.Cut(start, Vector3.Lerp(start, end, travel), Mathf.Lerp(.016f, last ? .06f : .045f, flash), fade * Mathf.Lerp(.62f, 1f, flash));
        }
        water.LineSpray(start, end, age - burst, last ? 22 : 14, .46f);
        // Where the double stood, it falls apart: a short column of water and drops thrown around it.
        float gone = age - burst;
        if (gone >= 0f && gone < .26f)
        {
            Vector3 stand = new Vector3(to.x, owner.position.y + .04f, to.z) + heading * .05f;
            float p = Mathf.Clamp01(gone / .16f);
            if (p < 1f) water.Crown(stand, Mathf.Lerp(.07f, .30f, Mathf.Sqrt(p)), .34f * Mathf.Sin(Mathf.PI * Mathf.Lerp(.15f, 1f, p)), age + index, (1f - p) * .9f);
            water.Fountain(stand, .38f, .06f + gone * 1.7f, 12);
        }

        // The double itself.
        var current = doubles[index];
        if (age < appear || age >= burst + BurstSeconds || actor == null || material == null) { current?.Hide(); return; }
        current ??= doubles[index] = new Double(owner, actor, material, index);
        Vector3 at = Vector3.Lerp(from, to, travel)
            + heading * (.05f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(arrive, burst, age)));
        float forming = 1f - Mathf.InverseLerp(appear, appear + FormSeconds, age);
        float breaking = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(burst, burst + BurstSeconds, age));
        at.y = owner.position.y;
        current.Draw(actor, at, heading, age, Mathf.InverseLerp(dash, arrive + .08f, age), Mathf.Max(forming, breaking), .96f);

        Vector3 ground = from + Vector3.up * .045f;
        float splash = Mathf.InverseLerp(appear, appear + .13f, age);
        if (splash < 1f)
        {
            water.Ripple(ground, Mathf.Lerp(.07f, .31f, splash), .022f, (1f - splash) * .9f);
            water.EdgeSpray(ground, heading, .24f, 360f, age - appear, 12, .30f);
        }
    }

    // A faint ring on the ground marks how far the cuts reach.
    private void DrawRing(float age)
    {
        float show = Mathf.InverseLerp(times[0] - .03f, times[0] + .04f, age) * (1f - Mathf.InverseLerp(BurstAt - .06f, BurstAt + .08f, age));
        if (show > 0f) water.Ripple(anchor + Vector3.up * .028f, radius, .024f * radius, show * .42f);
    }

    private void DrawBurst(float age)
    {
        float since = age - BurstAt;
        if (since < 0f || since >= .24f) return;
        Vector3 centre = anchor + forward * EnemyAhead * (radius / 1.3f) + Vector3.up * .035f;
        water.Fountain(centre, .50f * (radius / 1.3f), .06f + since * 1.7f, 20);
        float p = since / .24f;
        water.Ripple(anchor + Vector3.up * .035f, radius * Mathf.Lerp(.5f, 1f, p), .035f * radius, (1f - p) * .55f);
    }

    private void RestoreBody()
    {
        if (hiddenBody != null) hiddenBody.enabled = true;
        hiddenBody = null;
    }

    private void SuppressShadow(bool value)
    {
        if (shadow == null && value && owner != null) shadow = owner.GetComponentInChildren<PNGroundShadow>();
        if (shadow != null) shadow.Suppressed = value;
    }

    public void Hide()
    {
        RestoreBody();
        SuppressShadow(false);
        water.Hide();
        weapon.Hide();
        coat?.Hide();
        foreach (var cut in cuts) cut?.Hide();
        foreach (var item in doubles) item?.Hide();
        melt?.Hide();
        form?.Hide();
    }

    public void Dispose()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        water.Dispose();
        weapon.Dispose();
        coat?.Dispose();
        foreach (var cut in cuts) cut?.Dispose();
        foreach (var item in doubles) item?.Dispose();
        melt?.Dispose();
        form?.Dispose();
        Release(material);
        material = null;
    }

    private static void Release(Object item)
    {
        if (item == null) return;
        if (Application.isPlaying) Object.Destroy(item);
        else Object.DestroyImmediate(item);
    }

    // The posed mesh of a puppet drawn again as water (Resources/PNVfx/GuraEcho): live over (or just behind) the puppet, or a frozen copy of one pose left where it was. The atlas stays the puppet's.
    internal sealed class Silhouette : IDisposable
    {
        private static readonly int MainTex = Shader.PropertyToID("_MainTex"), Dissolve = Shader.PropertyToID("_Dissolve"), FromTop = Shader.PropertyToID("_FromTop"),
            Opacity = Shader.PropertyToID("_Opacity"), FxTime = Shader.PropertyToID("_FxTime"), Height = Shader.PropertyToID("_Height"),
            Waterline = Shader.PropertyToID("_Waterline"), EdgeLight = Shader.PropertyToID("_EdgeLight"), Fill = Shader.PropertyToID("_Fill"), Outline = Shader.PropertyToID("_Outline");
        private readonly GameObject node;
        private readonly MeshFilter filter;
        private readonly MeshRenderer renderer;
        private readonly MaterialPropertyBlock block = new();
        private readonly bool behind;
        private readonly Mesh frozen;
        private readonly Texture frozenTexture;
        private readonly float frozenHeight;
        private readonly Vector3 frozenPosition;
        private readonly Quaternion frozenRotation;

        public Silhouette(PNVisualActor actor, Material material, string name, bool behind = false, float lift = 0f)
        {
            this.behind = behind;
            node = Node(actor.transform, actor.gameObject.layer, material, name, out filter, out renderer);
            node.transform.localPosition = new Vector3(0f, 0f, behind ? .012f : -.009f - lift);
        }

        private Silhouette(PNVisualActor actor, Material material, string name, Transform host, Vector3 offset)
        {
            node = Node(host, actor.gameObject.layer, material, name, out filter, out renderer);
            frozen = Object.Instantiate(actor.Mesh);
            frozen.hideFlags = HideFlags.HideAndDontSave;
            filter.sharedMesh = frozen;
            frozenTexture = actor.Renderer.sharedMaterial.GetTexture(MainTex);
            frozenHeight = actor.BodyTopLocal;
            frozenPosition = actor.transform.position + offset;
            frozenRotation = actor.transform.rotation;
            Vector3 world = actor.transform.lossyScale, under = host.lossyScale;
            node.transform.localScale = new Vector3(world.x / under.x, world.y / under.y, world.z / under.z);
            renderer.sortingOrder = actor.Renderer.sortingOrder;
        }

        // This pose, copied and put `offset` away from the puppet: it stays at that place in the world while `host` moves (`DrawFrozen` puts it back every time).
        public static Silhouette Freeze(PNVisualActor actor, Material material, string name, Transform host, Vector3 offset = default) => new(actor, material, name, host, offset);

        private static GameObject Node(Transform parent, int layer, Material material, string name, out MeshFilter filter, out MeshRenderer renderer)
        {
            var created = new GameObject(name) { layer = layer };
            created.transform.SetParent(parent, false);
            filter = created.AddComponent<MeshFilter>();
            renderer = created.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            created.SetActive(false);
            return created;
        }

        // `waterline` makes the boundary a level by height alone (with a softer lit edge) instead of a melt that follows the drawing's shades.
        // `fill` thins the inside; `outline` draws only a ring of that width around the parts.
        public void Draw(PNVisualActor actor, float age, float dissolve, bool fromTop, float opacity, bool waterline = false, float fill = 1f, float outline = 0f)
        {
            if (node == null) return;
            if (actor.Mesh == null) { Hide(); return; }
            filter.sharedMesh = actor.Mesh;
            renderer.sortingOrder = actor.Renderer.sortingOrder + (behind ? 0 : 1);
            Paint(actor.Renderer.sharedMaterial.GetTexture(MainTex), actor.BodyTopLocal, age, dissolve, fromTop, opacity, waterline, fill, outline);
        }

        public void DrawFrozen(float age, float dissolve, bool fromTop, float opacity)
        {
            if (node == null) return;
            node.transform.SetPositionAndRotation(frozenPosition, frozenRotation);
            Paint(frozenTexture, frozenHeight, age, dissolve, fromTop, opacity, false, 1f, 0f);
        }

        private void Paint(Texture texture, float height, float age, float dissolve, bool fromTop, float opacity, bool waterline, float fill, float outline)
        {
            if (opacity <= .001f || dissolve >= .999f) { Hide(); return; }
            block.SetTexture(MainTex, texture);
            block.SetFloat(Dissolve, dissolve);
            block.SetFloat(FromTop, fromTop ? 1f : 0f);
            block.SetFloat(Opacity, opacity);
            block.SetFloat(FxTime, age);
            block.SetFloat(Height, Mathf.Max(.1f, height));
            block.SetFloat(Waterline, waterline ? 1f : 0f);
            block.SetFloat(EdgeLight, waterline ? .6f : 1f);
            block.SetFloat(Fill, fill);
            block.SetFloat(Outline, outline);
            renderer.SetPropertyBlock(block);
            node.SetActive(true);
        }

        public void Hide() { if (node != null) node.SetActive(false); }

        public void Dispose()
        {
            Hide();
            Release(node);
            Release(frozen);
        }
    }

    // One water double: a puppet of Gura's own appearance that only this view poses (it is not ticked by the visual system and never drawn as a character), with its own trident.
    internal sealed class Double : IDisposable
    {
        private readonly GameObject root;
        private readonly PNVisualActor actor;
        private readonly Silhouette silhouette;
        private readonly PNGuraAttackWeapon trident;
        private string playing = "echo_cut";

        public Double(Transform owner, PNVisualActor source, Material material, int index)
        {
            root = new GameObject("Gura water double " + index) { layer = source.gameObject.layer };
            root.transform.SetParent(owner, false);
            var child = new GameObject("Gura water puppet") { layer = source.gameObject.layer };
            child.transform.SetParent(root.transform, false);
            actor = child.AddComponent<PNVisualActor>();
            var facing = source.GetComponent<PNCameraFacingTarget>();
            actor.Bind(source.Appearance, source.Hub, facing != null ? facing.FootAnchor : Vector3.zero, source.BuildId);
            PNVisualSystem.Unregister(actor);
            var own = child.GetComponent<PNCameraFacingTarget>();
            if (own != null) own.enabled = false;      // it takes the turn of the live puppet instead
            actor.HideStrings();
            actor.SetRate(0f);
            actor.Play("echo_cut", true);
            silhouette = new Silhouette(actor, material, "Gura water silhouette");
            trident = new PNGuraAttackWeapon(owner, "Gura water double trident", true);
        }

        public void Draw(PNVisualActor source, Vector3 position, Vector3 heading, float age, float cut, float dissolve, float opacity)
        {
            root.SetActive(true);
            root.transform.SetPositionAndRotation(position, root.transform.parent.rotation);
            actor.transform.localPosition = source.transform.localPosition;
            actor.transform.localRotation = source.transform.localRotation;
            var camera = PNViewCamera.Current;
            actor.SetFacingLeft(Vector3.Dot(heading, camera != null ? camera.transform.right : Vector3.right) < 0f);
            actor.SetPhase(cut);
            actor.Tick(0f);
            actor.Renderer.enabled = false;
            silhouette.Draw(actor, age, dissolve, true, opacity);

            // The trident leads the dash, then drops a little with the follow through.
            Vector3 axis = (heading + Vector3.up * Mathf.Lerp(.05f, -.17f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1f, cut)))).normalized;
            Vector3 feet = actor.transform.position;
            trident.Draw(new PNGuraAttackWeapon.Pose(feet + Vector3.up * .21f + heading * .07f, axis,
                Vector3.Cross(Vector3.up, axis).normalized, .62f, .21f, 0f), opacity * .94f * (1f - dissolve) * (1f - dissolve));
        }

        // One pose of `motion` (0..1 of it), looking `heading`, the trident from `grip` (off the feet) along `axis`.
        public void Hold(PNVisualActor source, Vector3 position, Vector3 heading, string motion, float phase, float age, float dissolve, float opacity, Vector3 grip, Vector3 axis)
        {
            root.SetActive(true);
            root.transform.SetPositionAndRotation(position, root.transform.parent.rotation);
            actor.transform.localPosition = source.transform.localPosition;
            actor.transform.localRotation = source.transform.localRotation;
            var camera = PNViewCamera.Current;
            actor.SetFacingLeft(Vector3.Dot(heading, camera != null ? camera.transform.right : Vector3.right) < 0f);
            if (playing != motion && actor.Play(motion, true)) playing = motion;
            actor.SetPhase(phase);
            actor.Tick(0f);
            actor.Renderer.enabled = false;
            silhouette.Draw(actor, age, dissolve, true, Mathf.Clamp01(opacity));
            trident.Draw(new PNGuraAttackWeapon.Pose(actor.transform.position + grip, axis.normalized,
                Vector3.Cross(Vector3.up, axis).normalized, .72f, .23f, 0f), Mathf.Clamp01(opacity) * (1f - dissolve) * (1f - dissolve));
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
            trident.Hide();
        }

        public void Dispose()
        {
            Hide();
            silhouette.Dispose();
            trident.Dispose();
            Release(root);
        }
    }
}
#endif
