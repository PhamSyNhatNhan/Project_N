#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class PNGuraSharkBite : MonoBehaviour, IDisposable
{
    internal const float Start = -.14f, End = .53f;
    private const float RiseStart = -.094f, SinkEnd = .38f;
    private PNGuraSharkModel model;
    private PNGuraWaterFx water;
    private PNGuraWaterParticles splash;
    private EveTimeScale clock;
    private Transform caster;
    private Vector3 origin, direction;
    private float radius, age;
    private bool placed, released, disposed;

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
        bite.splash = PNGuraWaterParticles.Create(root.transform, "shark");
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
            float rise = Ease(RiseStart, -.038f, age);
            float sink = Ease(.081f, SinkEnd, age);
            float pose = age < -.022f ? Mathf.Lerp(1.80f, 1.875f, Ease(RiseStart, -.042f, age)) :
                age < 0f ? Mathf.Lerp(3.375f, 3.75f, Ease(-.022f, 0f, age)) : 3.75f;
            Vector3 centre = origin + Vector3.up * (radius * (-2.58f + 1.30f * rise - 1.38f * sink));
            model.Draw(centre, Quaternion.LookRotation(Vector3.up, -direction), radius, origin.y, pose, age - Start);
        }
        else model.Hide();

        var surface = origin + Vector3.up * .032f;
        if (age >= -.048f && age < .34f)
            splash?.Sample(surface, direction, new Vector3(radius*.68f,radius*.65f,radius*.68f), (age + .048f)*1.80f, seeking);
        else splash?.Hide();

        water.Begin();
        float breach = Ease(-.12f, -.041f, age);
        float breachFade = breach * (1f - Ease(-.01f, .24f, age));
        if (breachFade > .001f)
            water.Ripple(surface, radius * Mathf.Lerp(.32f, .86f, breach), radius*.075f, breachFade*.80f);
        float after = Ease(0f, End, age);
        float impact = Ease(-.037f, 0f, age) * (1f - after);
        if (impact > .001f)
            water.Ripple(surface + Vector3.up*.005f, radius * Mathf.Lerp(.76f, 1f, Ease(-.037f, 0f, age)), radius*.052f, impact);
        if (age >= 0f && age < .26f) DrawSpray(surface);
        float sinkRipple = Ease(.15f, .31f, age) * (1f - Ease(.30f, End, age));
        if (sinkRipple > .001f)
            water.Ripple(surface, radius * Mathf.Lerp(.53f, .93f, Ease(.15f, End, age)), radius*.035f, sinkRipple*.65f);
        water.Draw();
    }

    private void DrawSpray(Vector3 surface)
    {
        float progress = age / .26f;
        for (int i = 0; i < 12; i++)
        {
            Vector3 outward = Quaternion.AngleAxis(i * 137.5f, Vector3.up) * direction;
            float lift = .23f + .065f * (i % 4);
            float reach = Mathf.Lerp(.49f, .85f + (i % 3) * .062f, progress);
            float height = .045f + lift * 4f * progress * (1f - progress);
            Vector3 point = surface + radius * (outward * reach + Vector3.up * height);
            Vector3 travel = outward * .68f + Vector3.up * (1f - 2f * progress);
            water.Drop(point, travel, radius * (.022f + .007f * (i % 3)), .66f,
                (1f - progress) * Mathf.SmoothStep(.34f, .91f, progress * 4f));
        }
    }

    private static float Ease(float from, float to, float time) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, time));

    internal void Hide()
    {
        placed = false;
        model?.Hide();
        water?.Hide();
        splash?.Hide();
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
        model?.Dispose();
        water?.Dispose();
        splash?.Dispose();
    }
}

// The imported body and rig are reusable. The cast supplies the pose, waterline, scale and jaw opening.
internal sealed class PNGuraSharkModel : IDisposable
{
    private const string ResourcePath = "PNVfx/GuraShark";
    internal const string JawJoint = "Bone.028_Armature_16";
    internal const float HeadReferenceRadius = .205f;
    private static readonly Vector3 HeadAnchor = new(0f, .565f, .76f);
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
        skin.sharedMaterial = material;
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
        water.SetFloat("_Opacity", .95f);
        water.SetFloat(WaterMode, 2f);
        water.SetTexture("_SharkAlbedo", Resources.Load<Texture2D>("PNVfx/GuraSharkAlbedo") ?? Texture2D.whiteTexture);
        Hide();
    }

    public void Draw(Vector3 centre, Quaternion facing, float radius, float waterline, float jawTime, float age)
    {
        if (node == null || skin == null || radius <= 0f) { Hide(); return; }
        node.transform.SetPositionAndRotation(centre, facing);
        node.transform.localScale = Vector3.one * (radius / HeadReferenceRadius);
        node.SetActive(true);

        Quaternion authoredJaw = default;
        if (jaw != null && bite != null)
        {
            bite.SampleAnimation(source, jawTime);
            authoredJaw = jaw.localRotation;
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

    public void Hide() { if (node != null) node.SetActive(false); }

    public void Dispose()
    {
        if (node == null) return;
        Hide();
        if (Application.isPlaying) UnityEngine.Object.Destroy(node);
        else UnityEngine.Object.DestroyImmediate(node);
    }
}
#endif
