using System;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// EN: `project_n:gura_slash_wave`: a flying slash (the way out of the near form of Gura's Skill). A directional projectile drawn as a dome of water flying round side first: a white rim
//     with a glow, a see-through body with curling waves streaming in it and a soft wavering tail, in the colours of her own slashes (shader `Project_N/VFX/GuraSlashComet` draws all of it
//     on one flat sheet standing along the flight, so the round head always leads).
//     Launch input (optional): {tilt: degrees in [-80, 80]}, the roll of the blade about its flight, from the pose facing the camera; a pair of opposite rolls crosses as an X seen along
//     the flight (picture only: the hit box is the definition's volume).
// VI: Nhat chem bay: vom nuoc vien trang, song cuon troi trong than va duoi mem; luoi dung doc huong bay, tilt lan quanh chinh truc bay (thanh chu X khi nhin doc duong bay).
#if UNITY_5_3_OR_NEWER
public sealed class PNGuraSlashWave : PNDirectionalProjectile
{
    // The half width of the dome, and how far the sheet reaches around its centre in those radii (the shader's uv).
    private const float Radius = .52f, Ahead = 1.45f, Behind = 4.6f, Side = 1.65f, FadeSeconds = .16f;

    private GameObject node;
    private Mesh mesh;
    private MeshRenderer sheet;
    private MaterialPropertyBlock properties;
    // Three columns (the tail's end, the centre, ahead) of two, so the part behind the centre can be drawn short on its own.
    private readonly Vector3[] points = new Vector3[6];
    private readonly Color[] colors = new Color[6];
    private float tilt, fading;
    private Vector3 last, heading;

    private static readonly int AgeProperty = Shader.PropertyToID("_Age"), SeedProperty = Shader.PropertyToID("_Seed");

    public override void OnPoolCreated()
    {
        base.OnPoolCreated();
        node = new GameObject("Gura slash wave") { layer = gameObject.layer };
        node.transform.SetParent(transform, false);
        mesh = new Mesh { name = "Gura slash wave" };
        mesh.MarkDynamic();
        mesh.vertices = points;
        mesh.uv = new[] { new Vector2(-Behind, -Side), new Vector2(-Behind, Side), new Vector2(0f, -Side), new Vector2(0f, Side), new Vector2(Ahead, -Side), new Vector2(Ahead, Side) };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3, 2, 3, 4, 4, 3, 5 };
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        sheet = node.AddComponent<MeshRenderer>();
        sheet.sharedMaterial = Resources.Load<Material>("PNVfx/GuraSlashComet");
        sheet.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sheet.receiveShadows = false;
        properties = new MaterialPropertyBlock();
        node.SetActive(false);
    }

    protected override void OnStart(PNProjectileSpawn spawn)
    {
        base.OnStart(spawn);
        tilt = Launch.Input?["tilt"]?.Value<float>() ?? 0f;
        fading = 0f;
        last = spawn.Position;
        heading = spawn.Direction;
    }

    private void LateUpdate()
    {
        if (!IsRunning) return;
        last = Position;
        heading = Forward;
        Draw(Mathf.Clamp01(Age / .1f), 0f);
    }

    // shown = how far it has grown out of the swing (0..1); gone = how far it has faded at its end (0..1).
    private void Draw(float shown, float gone)
    {
        if (node == null) return;
        Vector3 forward = heading;
        forward.y = 0f;
        forward = forward.sqrMagnitude < 1e-6f ? Vector3.right : forward.normalized;
        float scale = (float)Launch.ScaleFactor, radius = Radius * scale * Mathf.Lerp(.55f, 1f, shown) * (1f + .1f * gone);

        // A blade lying along its flight in the world, rolled about the flight itself. The roll starts from the pose that faces the camera, so the two of a pair show equally much of
        // themselves; flying toward or away from the camera's side of the stage their rolls read as "/" and "\\", an X.
        float behind = Mathf.Lerp(.35f, 1f, shown) * (1f - .35f * gone);
        var camera = PNViewCamera.Current;
        Vector3 view = camera != null ? camera.transform.forward : Vector3.forward;
        Vector3 axis = forward;
        Vector3 facing = Vector3.Cross(view, axis);
        facing = facing.sqrMagnitude < 1e-4f ? Vector3.up : facing.normalized;
        // Taken upward (or to the right) first, so the sign of the roll means the same on the screen whichever side it is flying to.
        Vector3 up = camera != null ? camera.transform.up : Vector3.up, right = camera != null ? camera.transform.right : Vector3.right;
        if (Vector3.Dot(facing, up) + Vector3.Dot(facing, right) * .01f < 0f) facing = -facing;
        Vector3 across = Quaternion.AngleAxis(tilt, axis) * facing;
        Vector3 centre = last + Vector3.up * (.55f * scale) - axis * (radius * .4f);

        var color = new Color(1f, 1f, 1f, Mathf.Lerp(.6f, 1f, shown) * (1f - gone));
        for (int i = 0; i < points.Length; i++)
        {
            float a = i < 2 ? -Behind * behind : i < 4 ? 0f : Ahead;
            float b = (i & 1) == 0 ? -Side : Side;
            points[i] = node.transform.InverseTransformPoint(centre + (axis * a + across * b) * radius);
            colors[i] = color;
        }
        mesh.vertices = points;
        mesh.colors = colors;
        mesh.RecalculateBounds();
        properties.SetFloat(AgeProperty, Age + fading);
        properties.SetFloat(SeedProperty, tilt < 0f ? 3.7f : 0f);
        sheet.SetPropertyBlock(properties);
        node.SetActive(true);
    }

    public override float EndingDuration => FadeSeconds;

    protected override void OnEnding(float delta)
    {
        fading += delta;
        Draw(1f, Mathf.Clamp01(fading / FadeSeconds));
    }

    protected override void Cleanup()
    {
        if (node != null) node.SetActive(false);
        base.Cleanup();
    }

    public override void DisposePooledResources()
    {
        if (node != null)
        {
            node.SetActive(false);
            if (Application.isPlaying) { Destroy(mesh); Destroy(node); }
            else { DestroyImmediate(mesh); DestroyImmediate(node); }
        }
        node = null;
        mesh = null;
        base.DisposePooledResources();
    }

    public static void ValidateLaunch(JToken input) => PNGuraSlashWaveInput.Validate(input);
}
#else
public sealed class PNGuraSlashWave
{
    public static void ValidateLaunch(JToken input) => PNGuraSlashWaveInput.Validate(input);
}
#endif

internal static class PNGuraSlashWaveInput
{
    public static void Validate(JToken input)
    {
        if (input == null || input.Type == JTokenType.Null) return;
        var obj = PNBuffContentBatch.Object(input);
        PNBuffContentBatch.Fields(obj, "tilt");
        if (obj["tilt"] == null) return;
        double tilt = PNBuffContentBatch.Number(obj, "tilt");
        if (!(tilt >= -80d && tilt <= 80d)) throw new FormatException("gura_slash_wave tilt must be in [-80, 80].");
    }
}
