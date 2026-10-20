#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// EN: Break effect on an enemy when its lorica breaks (docs: Docs/EnemyLoricaDesign.md), built entirely in code (no texture, no pack asset):
//       - Shards come from a Voronoi diagram of a round pane: seeds are dense near the centre (the impact) and sparse toward the rim, each cell is clipped to the
//         pane and becomes one shard mesh, so the pieces differ in size and shape (small chips near the middle, long wedges toward the edge). The meshes are built once.
//       - They are split into three size classes (small, medium, large); small pieces fly first and fastest, large ones later and slower. Drag, then gravity, random 3D tumble.
//       - The shard shader is clear glass: a faint tint that is more opaque at grazing angles and greener toward the edges, plus additive reflections (sky, window bars,
//         a hard glint, a bright rim) that slide across a shard as it turns.
//       - About 28 tiny glints of glass dust fly with them.
//     Presentation only: it runs on the enemy's own clock (a time stop freezes it) and removes itself.
// VI: Hiệu ứng khi lorica vỡ: mảnh sinh từ sơ đồ Voronoi của tấm kính tròn (mảnh nhỏ gần tâm, mảnh nêm dài ra rìa), shader kính trong có phản chiếu, bụi kính.
public static class PNLoricaShatter
{
    // Two looks of the same pipeline: Glass (default, the user's final choice 2026-10-21) = clear pane; Mirror = silvered shards that reflect the broken enemy's own sprite, kept selectable.
    public enum LookKind { Glass, Mirror }
    public static LookKind Look = LookKind.Glass;
    public static int MaxActive = 8;            // simultaneous breaks drawn; a further break is skipped (presentation only)
    private static int active;
    private static Material shardMaterial, mirrorMaterial, dustMaterial, glintMaterial; private static Mesh[][] groups; private static Texture2D environment; private static MaterialPropertyBlock block;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void ResetStatics() { shardMaterial = mirrorMaterial = dustMaterial = glintMaterial = null; groups = null; environment = null; block = null; active = 0; Look = LookKind.Glass; MaxActive = 8; }
    private static Material Make(string shaderName, string name)
    {
        var shader = Shader.Find(shaderName); if (shader == null) throw new System.InvalidOperationException("Missing shader " + shaderName);
        return new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
    }
    private static void Prepare()
    {
        if (environment == null) environment = BuildEnvironment();
        if (shardMaterial == null) { shardMaterial = Make("Project_N/VFX/GlassShard", "Lorica shard"); shardMaterial.SetTexture("_EnvTex", environment); }
        if (mirrorMaterial == null) { mirrorMaterial = Make("Project_N/VFX/GlassShard", "Lorica mirror shard"); mirrorMaterial.SetTexture("_EnvTex", environment); mirrorMaterial.SetFloat("_Mirror", 1f); }
        if (dustMaterial == null) { dustMaterial = Make("Project_N/VFX/EveEnergy", "Lorica dust"); dustMaterial.SetFloat("_Particle", 1f); }
        if (glintMaterial == null) glintMaterial = Make("Project_N/VFX/GlassGlint", "Lorica glint");
        if (groups == null) groups = BuildShards();
    }

    // Builds the materials, the environment image and the shard meshes ahead of the first break. The shader programs themselves are compiled by PNShaderWarmup
    // (the shaders are registered in PNShaderWarmupContent); PNShaderWarmup calls this once per content generation, under the transition cover.
    public static void Warm() => Prepare();

    // ---- Reflection environment -------------------------------------------------------------------------------------------------------------------------
    // A small equirectangular image painted in code, mirrored by the shards: blue sky, a bright horizon band, four hard-edged windows with mullions, a softbox overhead,
    // a sun spot and a dark ground. u is the azimuth (0..1 around), v the elevation (-90..+90 degrees), matching the lookup in GlassShard.shader.
    private static Texture2D BuildEnvironment()
    {
        const int width = 256, height = 128; var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { name = "Lorica glass environment", hideFlags = HideFlags.HideAndDontSave, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color[width * height]; float[] windows = { .06f, .22f, .38f, .55f, .72f, .88f };
        for (int y = 0; y < height; y++)
        {
            float elevation = ((y + .5f) / height - .5f) * Mathf.PI; Color color;
            if (elevation >= 0f) color = Color.Lerp(new Color(.42f, .60f, .85f), new Color(.04f, .12f, .32f), Mathf.Pow(elevation / (Mathf.PI * .5f), .6f));
            else color = Color.Lerp(new Color(.03f, .06f, .09f), Color.black, -elevation / (Mathf.PI * .5f));
            for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width; var c = color;
                c += Color.white * (.9f * Mathf.Exp(-Mathf.Pow(elevation / .06f, 2f)));                                          // bright horizon band
                foreach (float centre in windows)
                {
                    float du = Mathf.Abs(Mathf.Repeat(u - centre + .5f, 1f) - .5f);
                    float mask = (1f - Mathf.SmoothStep(0f, 1f, (du - .04f) / .004f)) * Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, (elevation - .10f) / .02f)) * (1f - Mathf.SmoothStep(0f, 1f, (elevation - .90f) / .02f));
                    if (mask <= 0f) continue;
                    float mullion = (du < .004f || Mathf.Abs(elevation - .5f) < .012f) ? .12f : 1f;
                    c = Color.Lerp(c, Color.white * 1.3f * mullion, mask);
                }
                if (elevation > 1.15f) c += Color.white * 1.2f * Mathf.SmoothStep(0f, 1f, (elevation - 1.15f) / .1f);               // softbox overhead
                float du2 = (Mathf.Repeat(u - .2f + .5f, 1f) - .5f) * Mathf.PI * 2f * Mathf.Cos(elevation), de = elevation - .55f;
                c += Color.white * 2f * (1f - Mathf.SmoothStep(0f, 1f, (Mathf.Sqrt(du2 * du2 + de * de) - .07f) / .02f));        // sun spot
                c.a = 1f; pixels[y * width + x] = c;
            }
        }
        texture.SetPixels(pixels); texture.Apply(false, false); return texture;
    }

    // ---- Voronoi shards ---------------------------------------------------------------------------------------------------------------------------------
    // Keeps the part of a convex polygon where dot(p, normal) <= limit (one Sutherland-Hodgman step).
    private static List<Vector2> Clip(List<Vector2> polygon, Vector2 normal, float limit)
    {
        var result = new List<Vector2>(polygon.Count + 2);
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; float da = Vector2.Dot(a, normal) - limit, db = Vector2.Dot(b, normal) - limit;
            if (da <= 0f) result.Add(a);
            if (da < 0f && db > 0f || da > 0f && db < 0f) result.Add(a + (b - a) * (da / (da - db)));
        }
        return result;
    }
    // Round pane of radius 1: seeds dense near the middle, plus a ring of outer seeds that only bound the real cells. One mesh per cell, in three area classes.
    private static Mesh[][] BuildShards()
    {
        var random = new System.Random(23); const int inner = 32, outer = 12, edges = 16;
        var seeds = new List<Vector2>();
        for (int i = 0; i < inner; i++) { float r = Mathf.Pow((float)random.NextDouble(), 1.7f) * .95f, angle = (float)(random.NextDouble() * System.Math.PI * 2d); seeds.Add(new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r)); }
        for (int j = 0; j < outer; j++) { float angle = j * Mathf.PI * 2f / outer; seeds.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.35f); }
        var cells = new List<KeyValuePair<float, Mesh>>();
        for (int i = 0; i < inner; i++)
        {
            var polygon = new List<Vector2> { new Vector2(-3, -3), new Vector2(3, -3), new Vector2(3, 3), new Vector2(-3, 3) };
            for (int j = 0; j < seeds.Count && polygon.Count >= 3; j++)
            {
                if (j == i) continue; var normal = (seeds[j] - seeds[i]).normalized; polygon = Clip(polygon, normal, Vector2.Dot((seeds[i] + seeds[j]) * .5f, normal));
            }
            for (int k = 0; k < edges && polygon.Count >= 3; k++) { float angle = k * Mathf.PI * 2f / edges; polygon = Clip(polygon, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), 1f); }
            if (polygon.Count < 3) continue;
            float area = 0f; var centroid = Vector2.zero;
            for (int k = 0; k < polygon.Count; k++) { var a = polygon[k]; var b = polygon[(k + 1) % polygon.Count]; area += (a.x * b.y - b.x * a.y) * .5f; centroid += polygon[k]; }
            if (area < .0006f) continue; centroid /= polygon.Count;
            var vertices = new Vector3[polygon.Count + 1]; var uvs = new Vector2[polygon.Count + 1]; var normals = new Vector3[polygon.Count + 1]; var triangles = new int[polygon.Count * 3]; var centres = new Vector2[polygon.Count + 1];
            for (int k = 0; k < centres.Length; k++) centres[k] = centroid;      // the shard's place in the pane, so the mirror look can show the sprite part it covered
            vertices[0] = Vector3.zero; uvs[0] = new Vector2(1, 0); normals[0] = Vector3.forward;
            for (int k = 0; k < polygon.Count; k++)
            {
                var offset = polygon[k] - centroid; vertices[k + 1] = new Vector3(offset.x, offset.y, 0); uvs[k + 1] = new Vector2(0, 0);
                // Outline normals lean outward a little so the reflection changes across the face (a flat shard would mirror one colour).
                normals[k + 1] = (Vector3.forward + new Vector3(offset.x, offset.y, 0f).normalized * .45f).normalized;
                triangles[k * 3] = 0; triangles[k * 3 + 1] = 1 + k; triangles[k * 3 + 2] = 1 + (k + 1) % polygon.Count;
            }
            var mesh = new Mesh { name = "Lorica shard " + cells.Count, hideFlags = HideFlags.HideAndDontSave, vertices = vertices, uv = uvs, uv2 = centres, normals = normals, triangles = triangles };
            mesh.RecalculateBounds(); cells.Add(new KeyValuePair<float, Mesh>(area, mesh));
        }
        cells.Sort((x, y) => y.Key.CompareTo(x.Key));      // largest first
        int large = Mathf.Max(3, cells.Count / 6), medium = Mathf.Max(4, cells.Count / 3);
        var result = new Mesh[3][]; var small = new List<Mesh>(); var mid = new List<Mesh>(); var big = new List<Mesh>();
        for (int i = 0; i < cells.Count; i++) (i < large ? big : i < large + medium ? mid : small).Add(cells[i].Value);
        if (small.Count == 0) small.AddRange(mid);
        result[0] = small.ToArray(); result[1] = mid.ToArray(); result[2] = big.ToArray();
        return result;
    }

    // `center` and `size` describe the enemy's body in world space; `clock` (optional) is its time scale. `sortingLayerId`/`sortingOrder` put the effect in front
    // of the enemy's sprite and `toward` moves it a little toward the camera.
    public static void Play(Scene scene, Vector3 center, Vector3 size, EveTimeScale clock, int sortingLayerId, int sortingOrder, Vector3 toward, Sprite sprite = null, bool flipX = false)
    {
        if (active >= MaxActive) return;
        Prepare(); active++;
        var root = new GameObject("Lorica shatter"); if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = center + toward;
        float body = Mathf.Clamp(Mathf.Max(size.x, size.y) * .5f, .15f, .8f);       // radius of the pane the shards come from
        bool mirror = Look == LookKind.Mirror;
        var systems = new List<ParticleSystem>
        {
            Shards(root.transform, mirror, "Small shards", groups[0], 22, 0f, body, 1.5f, 1.9f, 2.6f, 5.2f, .6f, 1.05f, sortingLayerId, sortingOrder),
            Shards(root.transform, mirror, "Medium shards", groups[1], 12, .025f, body, 1.4f, 1.8f, 1.7f, 3.5f, .85f, 1.3f, sortingLayerId, sortingOrder),
            Shards(root.transform, mirror, "Large shards", groups[2], 6, .05f, body, 1.3f, 1.7f, .9f, 2.1f, 1.05f, 1.55f, sortingLayerId, sortingOrder),
            Dust(root.transform, body, sortingLayerId, sortingOrder),
            Glints(root.transform, body, sortingLayerId, sortingOrder)
        };
        if (mirror && sprite != null && sprite.texture != null)
        {
            // Pane radius 1 = `body` world units; the sprite is `size` wide, so pane units map to sprite uv by body/size (x negative = mirrored image).
            var texture = sprite.texture; var rect = sprite.textureRect; if (block == null) block = new MaterialPropertyBlock();
            block.Clear(); block.SetTexture("_SpriteTex", texture);
            block.SetVector("_Rect", new Vector4(rect.x / texture.width, rect.y / texture.height, rect.width / texture.width, rect.height / texture.height));
            block.SetVector("_Map", new Vector4(body / Mathf.Max(.01f, size.x) * (flipX ? 1f : -1f), body / Mathf.Max(.01f, size.y), 0f, 0f));
            for (int i = 0; i < 3; i++) systems[i].GetComponent<ParticleSystemRenderer>().SetPropertyBlock(block);
        }
        root.AddComponent<Runner>().Begin(systems.ToArray(), clock);
        foreach (var system in systems) system.Play(true);
    }
    private static ParticleSystem Shards(Transform parent, bool mirror, string name, Mesh[] meshes, int count, float delay, float body, float minScale, float maxScale, float minSpeed, float maxSpeed, float minLife, float maxLife, int layer, int order)
    {
        var node = new GameObject(name); node.transform.SetParent(parent, false);
        var system = node.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main; main.playOnAwake = false; main.loop = false; main.duration = .3f; main.startDelay = delay; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife); main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(body * minScale, body * maxScale); main.gravityModifier = .9f; main.maxParticles = count + 8;
        // The red channel is a per-shard random seed for the reflection in the shader; alpha stays 1.
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(.1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f));
        main.startRotation3D = true; main.startRotationX = new ParticleSystem.MinMaxCurve(0, 6.28f); main.startRotationY = new ParticleSystem.MinMaxCurve(0, 6.28f); main.startRotationZ = new ParticleSystem.MinMaxCurve(0, 6.28f);
        var emission = system.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(count * 2 / 3)), new ParticleSystem.Burst(.04f, (short)(count - count * 2 / 3)) });
        var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = body * .5f; shape.radiusThickness = 1f;
        var drag = system.limitVelocityOverLifetime; drag.enabled = true; drag.limit = 100f; drag.dampen = 0f; drag.drag = 2.3f;
        var spin = system.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-9f, 9f); spin.y = new ParticleSystem.MinMaxCurve(-9f, 9f); spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var color = system.colorOverLifetime; color.enabled = true; var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .65f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var shrink = system.sizeOverLifetime; shrink.enabled = true; shrink.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(.7f, 1), new Keyframe(1f, .5f)));
        var renderer = system.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.SetMeshes(meshes); renderer.sharedMaterial = mirror ? mirrorMaterial : shardMaterial;
        // The mirror look needs the shard's pane position (mesh uv2): a particle renderer passes it only as a custom vertex stream.
        var streams = new List<ParticleSystemVertexStream>(); renderer.GetActiveVertexStreams(streams);
        if (!streams.Contains(ParticleSystemVertexStream.UV2)) { streams.Add(ParticleSystemVertexStream.UV2); renderer.SetActiveVertexStreams(streams); }
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.alignment = ParticleSystemRenderSpace.World; renderer.sortingLayerID = layer; renderer.sortingOrder = order;
        system.Simulate(0, true, true, false);
        return system;
    }
    // Sparkles with a glow (shader GlassGlint: hot core, soft halo, thin four-point star), flashing one after another across the cloud of shards during the first half second.
    private static ParticleSystem Glints(Transform parent, float body, int layer, int order)
    {
        var node = new GameObject("Glass glints"); node.transform.SetParent(parent, false);
        var system = node.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main; main.playOnAwake = false; main.loop = false; main.duration = .6f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.14f, .32f); main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, 1.2f); main.startSize = new ParticleSystem.MinMaxCurve(body * .30f, body * .62f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 1.57f); main.gravityModifier = .3f; main.maxParticles = 40; main.startColor = Color.white;
        var emission = system.emission; emission.rateOverTime = 0;
        var bursts = new ParticleSystem.Burst[8]; for (int i = 0; i < bursts.Length; i++) bursts[i] = new ParticleSystem.Burst(.02f + i * .07f, (short)3);      // Unity allows at most 8 bursts per system
        emission.SetBursts(bursts);
        var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = body * .75f; shape.radiusThickness = 1f;
        var color = system.colorOverLifetime; color.enabled = true; var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .25f), new GradientAlphaKey(0f, 1f) }); color.color = gradient;
        var renderer = system.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.sharedMaterial = glintMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.sortingLayerID = layer; renderer.sortingOrder = order + 3;
        system.Simulate(0, true, true, false);
        return system;
    }
    private static ParticleSystem Dust(Transform parent, float body, int layer, int order)
    {
        var node = new GameObject("Glass dust"); node.transform.SetParent(parent, false);
        var system = node.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main; main.playOnAwake = false; main.loop = false; main.duration = .3f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .95f); main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.6f); main.startSize = new ParticleSystem.MinMaxCurve(.03f, .075f);
        main.gravityModifier = .25f; main.maxParticles = 40; main.startColor = new Color(.85f, .95f, 1f, 1f);
        var emission = system.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)28) });
        var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = body * .3f; shape.radiusThickness = 1f;
        var color = system.colorOverLifetime; color.enabled = true; var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.8f, .5f), new GradientAlphaKey(0f, 1f) }); color.color = gradient;
        var shrink = system.sizeOverLifetime; shrink.enabled = true; shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, .2f));
        var renderer = system.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.sharedMaterial = dustMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.sortingLayerID = layer; renderer.sortingOrder = order + 2;
        system.Simulate(0, true, true, false);
        return system;
    }
    private sealed class Runner : MonoBehaviour
    {
        private ParticleSystem[] systems; private EveTimeScale clock; private float age, effectAge;
        private void OnDestroy() { if (active > 0) active--; }
        public void Begin(ParticleSystem[] value, EveTimeScale scale) { systems = value; clock = scale; }
        private void Update()
        {
            age += Time.unscaledDeltaTime; float scale = clock != null ? Mathf.Max(0f, clock.Scale) : 1f; effectAge += Time.unscaledDeltaTime * scale;
            bool alive = false;
            foreach (var system in systems) { if (system == null) continue; var main = system.main; main.simulationSpeed = scale; if (system.IsAlive(true)) alive = true; }
            if (age > 12f || age > .5f && !alive && effectAge > .3f) Destroy(gameObject);
        }
    }
}
#endif
