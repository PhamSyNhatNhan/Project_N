#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;

// Presentation-owned jump wake. Samples actual positions on the Action clock;
// camera redraws do not advance or append history. The live actor mesh/atlas are borrowed.
public sealed class PNStarforgerFlightVisual : IDisposable
{
    private const int Capacity = 32;
    private const float TailSeconds = .20f;
    private readonly Vector3[] points = new Vector3[Capacity];
    private readonly float[] times = new float[Capacity];
    private readonly GameObject root, coat;
    private readonly MeshFilter coatFilter;
    private readonly MeshRenderer coatRenderer;
    private readonly Material coatMaterial;
    private readonly PNStarreaperEnergyMesh fx;
    private PNVisualActor actor;
    private int count;
    private float age, fade, height;
    private bool disposed;

    public PNStarforgerFlightVisual(Transform parent)
    {
        root = new GameObject("Starforger jump energy"); root.layer = parent.gameObject.layer; root.transform.SetParent(parent, false);
        try
        {
            fx = new PNStarreaperEnergyMesh(root.transform, "Starforger flight");
            coat = new GameObject("Starforger jump coat"); coat.layer = root.layer; coat.transform.SetParent(root.transform, false);
            coatFilter = coat.AddComponent<MeshFilter>(); coatRenderer = coat.AddComponent<MeshRenderer>();
            var shader = Resources.Load<Shader>("PNStarreaperPierce");
            if (shader == null) throw new InvalidOperationException("Missing PNStarreaperPierce shader.");
            coatMaterial = new Material(shader) { name = "Starforger jump coat", hideFlags = HideFlags.HideAndDontSave, renderQueue = 3014 };
            coatMaterial.SetFloat("_EchoMode", 3); coatMaterial.SetFloat("_DstBlend", 10);
            coatMaterial.SetTexture("_RainNoiseTex", Resources.Load<Texture2D>("StarreaperVfx/RainNoise"));
            coatRenderer.sharedMaterial = coatMaterial; coatRenderer.shadowCastingMode = ShadowCastingMode.Off; coatRenderer.receiveShadows = false;
            Clear(); PNViewCamera.Updated += OnView;
        }
        catch { Dispose(); throw; }
    }

    public void Advance(PNVisualActor value, float seconds, float landedAt)
    {
        if (disposed) return;
        actor = value; age = seconds;
        fade = landedAt < 0 ? 1 : 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - landedAt) / TailSeconds));
        if (actor == null || fade <= 0) { Clear(); return; }
        root.SetActive(true);
        var bounds = actor.Mesh.bounds;
        height = actor.transform.TransformVector(Vector3.up * bounds.size.y).magnitude;
        var centre = actor.transform.TransformPoint(bounds.center);
        if (count == 0 || (age > times[count - 1] && Vector3.Distance(points[count - 1], centre) > .004f))
        {
            if (count == Capacity) { Array.Copy(points, 1, points, 0, Capacity - 1); Array.Copy(times, 1, times, 0, Capacity - 1); count--; }
            points[count] = centre; times[count++] = age;
        }
        while (count > 1 && age - times[1] > TailSeconds)
        { Array.Copy(points, 1, points, 0, --count); Array.Copy(times, 1, times, 0, count); }
        coat.SetActive(true); coatFilter.sharedMesh = actor.Mesh;
        coat.transform.SetPositionAndRotation(actor.transform.position + actor.transform.forward * -.009f, actor.transform.rotation);
        coat.transform.localScale = actor.transform.localScale;
        coatRenderer.sortingOrder = actor.Renderer.sortingOrder + 1;
        coatMaterial.SetTexture("_MainTex", actor.Renderer.sharedMaterial.GetTexture("_MainTex"));
        coatMaterial.SetFloat("_FxTime", age); coatMaterial.SetFloat("_EchoOpacity", fade * .58f);
        OnView();
    }

    private void OnView() { Render(PNViewCamera.Current); }
    public void Render(Camera camera)
    {
        if (disposed || actor == null || camera == null || fade <= 0 || !root.activeSelf) return;
        coat.transform.SetPositionAndRotation(actor.transform.position + actor.transform.forward * -.009f, actor.transform.rotation);
        fx.Begin(age);
        var right = camera.transform.right; var up = camera.transform.up; var front = -camera.transform.forward * .006f;
        for (int i = 1; i < count; i++)
        {
            float u0 = Mathf.Clamp01(1 - (age - times[i - 1]) / TailSeconds), u1 = Mathf.Clamp01(1 - (age - times[i]) / TailSeconds);
            var d = points[i] - points[i - 1];
            if (d.sqrMagnitude < .000001f) continue;
            var crossA = Across(i-1,camera.transform.forward);var crossB = Across(i,camera.transform.forward);
            // A continuous, textured plume, tapering into its history (no duplicate puppets).
            fx.Strip(false, points[i - 1], points[i], crossA * (.15f + .85f * u0), crossB * (.15f + .85f * u1), height * .95f,
                new Color(.045f, .012f, .085f, fade * .65f), 9, u0, u1);
            fx.Strip(true, points[i - 1] + front, points[i] + front, crossA * (.15f + .85f * u0), crossB * (.15f + .85f * u1), height * .95f,
                new Color(.85f, .22f, 1.6f, fade * .75f), 6, u0, u1);
            fx.Strip(true, points[i - 1] + front, points[i] + front, crossA * (.15f + .85f * u0), crossB * (.15f + .85f * u1), height * 1.05f,
                new Color(.50f, .10f, 1.1f, fade * .40f), 9, u0, u1);
        }
        var centre = actor.transform.TransformPoint(actor.Mesh.bounds.center);
        for (int i = 0; i < 9; i++)
        {
            float u = Mathf.Repeat(age * 1.3f + i * .618034f, 1), a = i * 2.39996f;
            var at = centre + right * (Mathf.Cos(a) * .17f) + up * ((u - .5f) * height * 1.1f);
            float alpha = Mathf.Sin(u * Mathf.PI) * fade;
            fx.Quad(true, at + front * 2, right, up, .035f, .035f, new Color(1.5f, .92f, .32f, alpha), 3);
        }
        fx.End();
    }

    private Vector3 Across(int i,Vector3 viewForward)
    {
        var tangent=points[Mathf.Min(count-1,i+1)]-points[Mathf.Max(0,i-1)];
        return Vector3.Cross(viewForward,tangent).normalized;
    }

    public void Clear()
    {
        count = 0; actor = null; fade = 0;
        if (root != null) root.SetActive(false);
        fx?.Clear();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; PNViewCamera.Updated -= OnView;
        fx?.Dispose(); Destroy(coatMaterial); Destroy(root);
    }
    private static void Destroy(UnityEngine.Object o) { if (o == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }
}
#endif
