#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class PNGuraSharkBite : MonoBehaviour, IDisposable
{
    internal const float Start = -.175f, End = 1.35f;
    // Up fast, then slow: the shut jaws stay in view, the body sinks back at leisure and the water takes longer still.
    private const float RiseStart = -.17f, SinkStart = .28f, SinkEnd = .88f;
    // The head comes up nearly straight and tips a little towards the enemy, more as the jaws shut.
    private const float RiseLean = 12f, SnapLean = 20f;
    // Jaw: 1 is the open pose of the model's own clip, 0 its rest, which still gapes; below 0 the jaws shut past it.
    private const float OpenGape = 1.3f, ShutGape = -.85f;
    // How far the mouth may turn from the camera, so that one side of the jaws stays readable on an attack up the screen.
    private const float MouthLimit = 100f;
    private PNGuraSharkModel model;
    private PNGuraWaterFx water, spray;
    private EveTimeScale clock;
    private Transform caster;
    private Vector3 origin, direction;
    private float radius, age;
    private float previousAge = float.NegativeInfinity;
    private bool placed, released, disposed, shook;
    private IPNCameraHandle shake;

    internal bool HasTail => released && !disposed;

    internal static PNGuraSharkBite Create(Transform owner)
    {
        var root = new GameObject("Gura shark surface") { layer = owner.gameObject.layer };
        SceneManager.MoveGameObjectToScene(root, owner.gameObject.scene);
        var bite = root.AddComponent<PNGuraSharkBite>();
        bite.caster = owner;
        bite.clock = owner.GetComponent<EveTimeScale>();
        bite.model = new PNGuraSharkModel(root.transform);
        bite.water = new PNGuraWaterFx(root.transform, "Gura shark ripples");
        bite.spray = new PNGuraWaterFx(root.transform, "Gura shark spray");
        return bite;
    }

    internal void Draw(Vector3 point, Vector3 facing, float reach, float since, bool seeking)
    {
        origin = point;
        direction = facing;
        radius = reach;
        age = since;
        placed = true;
        DrawFrame(seeking);
    }

    private void DrawFrame(bool seeking)
    {
        if (age >= RiseStart && age < SinkEnd)
        {
            float rise = Mathf.InverseLerp(RiseStart, -.075f, age);
            rise = 1f - (1f - rise) * (1f - rise) * (1f - rise);
            float hang = Ease(-.075f, 0f, age);
            float snap = Mathf.InverseLerp(-.035f, 0f, age);
            float sink = Mathf.InverseLerp(SinkStart, SinkEnd, age);
            float gape = snap <= 0f ? Mathf.Lerp(.7f, OpenGape, Ease(RiseStart, -.06f, age)) : Mathf.Lerp(OpenGape, ShutGape, snap * snap);
            float lean = Mathf.Lerp(Mathf.Lerp(RiseLean, SnapLean, snap), RiseLean * .5f, sink);
            float settle = Ease(0f, SinkStart, age);
            float height = -2.40f + 1.72f * rise + .12f * hang - .10f * settle - 1.75f * sink * sink;
            Vector3 centre = origin + Vector3.up * (radius * height) + direction * (radius * Mathf.Lerp(-.36f, -.10f, Ease(-.04f, .03f, age)));
            model.Draw(centre, Facing(lean), radius, origin.y, gape, age - Start);
        }
        else model.Hide();

        if (!shook && !seeking && previousAge > float.NegativeInfinity && previousAge < 0f && age >= 0f && Application.isPlaying)
        {
            shook = true;
            shake?.Dispose();
            // The bite stands on its own in the scene: it names whose it is, so only her player's camera shakes.
            shake = PNCameraServices.Current.RequestShake(gameObject, "gura.shark_bite",
                new PNShakeSpec(.13f, 24f, .24f, .18f, 0f, PNShakeShape.Recoil, .65f, 1f, .12f, 7f, PNShakeFalloff.Quadratic),
                PNCameraAudience.Auto, caster != null ? caster.GetComponent<PNStat>() : null);
        }
        previousAge = age;

        DrawWater(origin + Vector3.up * .032f);
        DrawSpray(origin + Vector3.up * .032f);
    }

    // All the water on the ground comes out of the one thing that happens: the head breaking the surface pushes it away,
    // wave after wave, and a sheet of it runs outward after them.
    private void DrawWater(Vector3 surface)
    {
        Vector3 viewer = Quaternion.AngleAxis(CameraYaw(), Vector3.up) * Vector3.back;
        water.Begin();
        // Before it breaks: the water darkens and swells where it will come up.
        float warn = Ease(Start, -.10f, age) * (1f - Ease(.05f, .30f, age));
        if (warn > .001f) water.Ripple(surface, radius * Mathf.Lerp(.40f, 1f, Ease(Start, -.04f, age)), radius * .40f, warn * .80f);

        float sheet = Ease(-.12f, -.03f, age) * (1f - Ease(.35f, 1.05f, age));
        water.Outflow(surface + Vector3.up * .002f, radius * .30f, radius * (Mathf.Lerp(.55f, 1.08f, Ease(-.12f, 0f, age)) + .18f * Out(0f, .8f, age)), age, sheet);

        // Inner waves first, so the outer ones lie over them.
        for (int i = Waves - 1; i >= 0; i--)
            water.Surge(surface, WaveRadius(i), WaveHeight(i), age, WaveFade(i), viewer, i * 2.1f);

        // A shock runs along the ground when the jaws shut.
        float shock = Mathf.InverseLerp(0f, .22f, age);
        if (shock > 0f && shock < 1f)
            water.Ripple(surface + Vector3.up * .006f, radius * Mathf.Lerp(.35f, 1.25f, 1f - (1f - shock) * (1f - shock)), radius * .10f, 1f - shock);

        // The last of it: rings closing over where it went down.
        for (int i = 0; i < 3; i++)
        {
            float ring = Mathf.InverseLerp(SinkEnd - .22f + i * .13f, SinkEnd + .18f + i * .13f, age);
            if (ring > 0f && ring < 1f)
                water.Ripple(surface + Vector3.up * .008f, radius * Mathf.Lerp(.16f, .62f + i * .12f, ring), radius * .045f, (1f - ring) * .8f);
        }
        water.Draw();
    }

    private const int Waves = 4;
    // Launch time, life, start and end radius, height (radius parts). The first one is the breach itself.
    private static readonly float[,] Wave =
    {
        { -.13f, .68f, .62f, 1.27f, .52f },
        { -.01f, .62f, .45f, 1.16f, .36f },
        { .13f, .64f, .40f, 1.04f, .25f },
        { .30f, .64f, .34f, .90f, .16f },
    };

    private float WaveRadius(int i)
    {
        // The first crest stands on the hit radius at the bite and only then runs on.
        if (i == 0) return radius * (Mathf.Lerp(Wave[0, 2], 1f, Ease(Wave[0, 0], 0f, age)) + (Wave[0, 3] - 1f) * Out(0f, Wave[0, 0] + Wave[0, 1], age));
        return radius * Mathf.Lerp(Wave[i, 2], Wave[i, 3], Out(Wave[i, 0], Wave[i, 0] + Wave[i, 1], age));
    }

    private float WaveHeight(int i)
    {
        float t = Mathf.InverseLerp(i == 0 ? 0f : Wave[i, 0] + .08f, Wave[i, 0] + Wave[i, 1], age);
        return radius * Wave[i, 4] * Ease(Wave[i, 0], Wave[i, 0] + .11f, age) * Mathf.Pow(1f - t, 1.25f);
    }

    private float WaveFade(int i) => Ease(Wave[i, 0], Wave[i, 0] + .06f, age) * (1f - Ease(Wave[i, 0] + Wave[i, 1] * .6f, Wave[i, 0] + Wave[i, 1], age));

    // What flies and froths: foam carried out over the whole surface, riding the crests, and drops in arcs thrown by the breach,
    // again by the bite, and a little as the head goes under.
    private void DrawSpray(Vector3 surface)
    {
        Quaternion view = Quaternion.Euler(31f, CameraYaw(), 0f);
        Vector3 right = view * Vector3.right, up = view * Vector3.up;
        Vector3 viewer = Quaternion.AngleAxis(CameraYaw(), Vector3.up) * Vector3.back;
        spray.Begin();
        spray.FlowFoam(surface + Vector3.up * .006f, radius * .86f, age + .09f);
        for (int i = 0; i < 3; i++)
            spray.CrestFoam(surface, WaveRadius(i), WaveHeight(i), radius, age, WaveFade(i) * (i == 0 ? .95f : .75f), right, up, viewer, i * 2.1f);
        spray.Mist(surface, radius, age + .12f, right, up);
        spray.Splash(surface, radius * .70f, radius * .70f, age + .11f, 80, radius * .95f, radius * .046f, right, up);
        spray.Splash(surface, radius * .80f, radius * .62f, age, 56, radius * .62f, radius * .040f, right, up);
        spray.Splash(surface, radius * .18f, radius * .42f, age - (SinkEnd - .20f), 26, radius * .40f, radius * .032f, right, up);
        spray.Draw();
    }

    internal static float CameraYaw()
    {
        var service = Application.isPlaying ? PNViewCamera.Service : null;
        return service != null ? PNCameraFacing.YawDegrees(service.Orientation, PNFacingMode.PitchYaw) : 0f;
    }

    // Nose up and tipped towards the enemy; the belly (the mouth) looks the same way, within MouthLimit of the camera.
    private Quaternion Facing(float lean)
    {
        Vector3 toCamera = Quaternion.AngleAxis(CameraYaw(), Vector3.up) * Vector3.back;
        float turn = Vector3.SignedAngle(toCamera, direction, Vector3.up);
        Vector3 mouth = Quaternion.AngleAxis(Mathf.Clamp(turn, -MouthLimit, MouthLimit), Vector3.up) * toCamera;
        Vector3 nose = Vector3.up * Mathf.Cos(lean * Mathf.Deg2Rad) + direction * Mathf.Sin(lean * Mathf.Deg2Rad);
        return Quaternion.LookRotation(nose, -mouth);
    }

    // Fast away, slowing to a stop.
    private static float Out(float from, float to, float time)
    {
        float left = 1f - Mathf.InverseLerp(from, to, time);
        return 1f - left * left;
    }

    private static float Ease(float from, float to, float time) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, time));

    internal void Hide()
    {
        placed = false;
        previousAge = float.NegativeInfinity;
        shake?.Dispose();
        shake = null;
        model?.Hide();
        water?.Hide();
        spray?.Hide();
    }

    internal bool Release()
    {
        if (!Application.isPlaying || !placed || age >= End) return false;
        released = true;
        return true;
    }

    private void LateUpdate()
    {
        if (!released || disposed) return;
        if (caster == null) { Dispose(); return; }
        age += clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime;
        if (age >= End) Dispose();
        else DrawFrame(false);
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
        shake?.Dispose();
        shake = null;
        model?.Dispose();
        water?.Dispose();
        spray?.Dispose();
    }
}

// The imported body and rig are reusable. The cast supplies the pose, waterline, scale and jaw opening.
internal sealed class PNGuraSharkModel : IDisposable
{
    private const string ResourcePath = "PNVfx/GuraShark";
    internal const string JawJoint = "Bone.028_Armature_16";
    internal const float HeadReferenceRadius = .205f;
    private const float OpenPoseTime = 2.5f;
    private static readonly Vector3 HeadAnchor = new(0f, .565f, .76f);
    // The foot of the dorsal fin on the model: everything lower stays under the water.
    private static readonly Vector3 FinAnchor = new(0f, .858f, .12f);
    private static readonly int FinMode = Shader.PropertyToID("_Fin");
    private static readonly int WaterMode = Shader.PropertyToID("_WaterSkin");
    private static readonly int Waterline = Shader.PropertyToID("_Waterline");
    private static readonly int FlowAge = Shader.PropertyToID("_SharkFlowAge");
    private static readonly int FlowDirection = Shader.PropertyToID("_SharkFlowDirection");
    private static readonly int FlowAcross = Shader.PropertyToID("_SharkFlowAcross");
    private static readonly int FlowOrigin = Shader.PropertyToID("_SharkFlowOrigin");
    private readonly GameObject node, source;
    private readonly Transform jaw;
    private readonly SkinnedMeshRenderer skin;
    private readonly AnimationClip swim, bite;
    private readonly MaterialPropertyBlock water = new();
    private readonly Material skinWater, depthCopy;

    public PNGuraSharkModel(Transform owner)
    {
        var prefab = Resources.Load<GameObject>(ResourcePath);
        var material = Resources.Load<Material>("PNVfx/GuraWater");
        if (prefab == null || material == null) return;

        node = new GameObject("Gura shark model") { layer = owner.gameObject.layer };
        // The Action owns this model. Keep it in the same scene without following
        // the caster's transform, which can still move after a view update.
        SceneManager.MoveGameObjectToScene(node, owner.gameObject.scene);
        source = UnityEngine.Object.Instantiate(prefab, node.transform, false);
        skin = source.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skin == null) { Hide(); return; }
        skin.gameObject.layer = node.layer;
        // Drawn twice, ahead of the water around it: first depth only, then the see-through skin, so only the nearest surface is seen.
        depthCopy = new Material(material) { name = "Gura shark depth", renderQueue = 2994 };
        depthCopy.SetFloat("_DepthWrite", 1f);
        depthCopy.SetFloat("_DepthOnly", 1f);
        skinWater = new Material(material) { name = "Gura shark water", renderQueue = 2995 };
        skin.sharedMaterials = new[] { depthCopy, skinWater };
        skin.shadowCastingMode = ShadowCastingMode.Off;
        skin.receiveShadows = false;
        skin.updateWhenOffscreen = true;
        foreach (var bone in skin.bones)
        {
            if (bone == null || bone.name != JawJoint) continue;
            jaw = bone;
            break;
        }
        foreach (var animator in source.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var clip in Resources.LoadAll<AnimationClip>(ResourcePath))
        {
            if (clip.name.EndsWith("|Swim", StringComparison.Ordinal)) swim = clip;
            if (clip.name.EndsWith("|ArmatureAction", StringComparison.Ordinal)) bite = clip;
        }
        water.SetFloat("_Solid", 1f);
        water.SetFloat("_Opacity", 1f);
        water.SetFloat(WaterMode, 2f);
        water.SetFloat(FinMode, 0f);
        water.SetTexture("_SharkAlbedo", Resources.Load<Texture2D>("PNVfx/GuraSharkAlbedo") ?? Texture2D.whiteTexture);
        Hide();
    }

    public void Draw(Vector3 centre, Quaternion facing, float radius, float waterline, float gape, float age)
    {
        if (node == null || skin == null || radius <= 0f) { Hide(); return; }
        node.transform.SetPositionAndRotation(centre, facing);
        node.transform.localScale = Vector3.one * (radius / HeadReferenceRadius * .90f);
        node.SetActive(true);

        Quaternion authoredJaw = default;
        if (jaw != null && bite != null)
        {
            bite.SampleAnimation(source, 0f);
            Quaternion rest = jaw.localRotation;
            bite.SampleAnimation(source, OpenPoseTime);
            authoredJaw = Quaternion.SlerpUnclamped(rest, jaw.localRotation, gape);
        }
        if (swim != null) swim.SampleAnimation(source, Mathf.Repeat(Mathf.Max(0f, age), swim.length));
        if (jaw != null && bite != null) jaw.localRotation = authoredJaw;
        source.transform.localPosition = -HeadAnchor;
        water.SetFloat(Waterline, waterline);
        water.SetFloat(FlowAge, age);
        water.SetVector(FlowDirection, facing * Vector3.forward);
        water.SetVector(FlowAcross, facing * Vector3.right);
        water.SetVector(FlowOrigin, new Vector4(centre.x, centre.y, centre.z, 1f / radius));
        skin.SetPropertyBlock(water);
    }

    // Only the dorsal fin of a shark swimming just under the surface: `at` is where the fin stands on the water.
    public void DrawFin(Vector3 at, Quaternion facing, float scale, float waterline, float age)
    {
        if (node == null || skin == null) return;
        node.transform.SetPositionAndRotation(at, facing);
        // Thicker than the model has it, so it still reads seen from the front.
        node.transform.localScale = new Vector3(scale * 2.4f, scale, scale);
        node.SetActive(true);
        if (swim != null) swim.SampleAnimation(source, Mathf.Repeat(Mathf.Max(0f, age), swim.length));
        source.transform.localPosition = -FinAnchor;
        water.SetFloat(Waterline, waterline);
        water.SetFloat(FinMode, 1f);
        water.SetFloat(FlowAge, age);
        water.SetVector(FlowDirection, facing * Vector3.forward);
        water.SetVector(FlowAcross, facing * Vector3.right);
        water.SetVector(FlowOrigin, new Vector4(at.x, at.y, at.z, 1f));
        skin.SetPropertyBlock(water);
    }

    public void Hide() { if (node != null) node.SetActive(false); }

    public void Dispose()
    {
        foreach (var owned in new[] { depthCopy, skinWater })
        {
            if (owned == null) continue;
            if (Application.isPlaying) UnityEngine.Object.Destroy(owned);
            else UnityEngine.Object.DestroyImmediate(owned);
        }
        if (node == null) return;
        Hide();
        if (Application.isPlaying) UnityEngine.Object.Destroy(node);
        else UnityEngine.Object.DestroyImmediate(node);
    }
}
#endif
