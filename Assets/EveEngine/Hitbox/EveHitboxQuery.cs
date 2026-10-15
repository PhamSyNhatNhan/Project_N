using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EveEngine
{
    // EN: Reusable caller-owned buffers/probes. All queries are instantaneous overlaps, not sweeps.
    // VI: Buffer/probe tái dùng do caller sở hữu. Dò giao tại một thời điểm, không sweep.
    public sealed class EveHitboxQuery : IDisposable
    {
        public EveHitboxShape Shape { get; }
        private readonly PhysicsScene world;
        private Collider[] buffer = new Collider[16];
        private readonly List<MeshCollider> probes = new List<MeshCollider>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private GameObject probeRoot;
        private bool disposed, querying;
        private bool UsesMesh => Shape.Kind != EveHitboxKind.Box && Shape.Kind != EveHitboxKind.Sphere && Shape.Kind != EveHitboxKind.Capsule;

        public EveHitboxQuery(EveHitboxShape shape) : this(shape, Physics.defaultPhysicsScene) { }
        public EveHitboxQuery(EveHitboxShape shape, PhysicsScene world)
        {
            Shape = shape ?? throw new ArgumentNullException(nameof(shape));
            if (!world.IsValid()) throw new ArgumentException("Physics scene is invalid.");
            this.world = world;
            try { if (UsesMesh) BuildProbes(); }
            catch { Dispose(); throw; }
        }

        // EN: Replace output, grow full buffers and retry; never silently truncate crowded scenes.
        // VI: Thay output, tăng buffer đầy rồi dò lại; không âm thầm bỏ mục tiêu khi đông.
        public int Query(EveHitboxPose pose, List<Collider> results, int layerMask = ~0,
            QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
        {
            if (disposed) throw new ObjectDisposedException(nameof(EveHitboxQuery));
            if (querying) throw new InvalidOperationException("Hitbox queries cannot reenter.");
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (!world.IsValid()) throw new InvalidOperationException("Hitbox physics scene has been unloaded.");
            if (UsesMesh && probeRoot == null)
            {
                // The previous target scene may have unloaded since the last query.
                foreach (var mesh in meshes) Destroy(mesh);
                meshes.Clear(); probes.Clear(); BuildProbes();
            }
            // Validate even default(EveHitboxPose).
            pose = new EveHitboxPose(pose.Position, pose.Rotation);
            if (triggers != QueryTriggerInteraction.UseGlobal && triggers != QueryTriggerInteraction.Ignore && triggers != QueryTriggerInteraction.Collide)
                throw new ArgumentOutOfRangeException(nameof(triggers));
            querying = true;
            results.Clear();
            try
            {
                int count;
                while ((count = Overlap(pose, layerMask, triggers)) == buffer.Length)
                    Array.Resize(ref buffer, checked(buffer.Length * 2));
                for (int i = 0; i < count; i++)
                {
                    var target = buffer[i];
                    if (target == null) continue;
                    if (probes.Count == 0 || Intersects(target, pose)) results.Add(target);
                }
                return results.Count;
            }
            finally
            {
                foreach (var probe in probes) probe.enabled = false;
                Array.Clear(buffer, 0, buffer.Length); querying = false;
            }
        }
        private int Overlap(EveHitboxPose pose, int mask, QueryTriggerInteraction triggers)
        {
            switch (Shape.Kind)
            {
                case EveHitboxKind.Box:
                    return world.OverlapBox(pose.Position, Shape.Size * .5f, buffer, pose.Rotation, mask, triggers);
                case EveHitboxKind.Sphere:
                    return world.OverlapSphere(pose.Position, Shape.Radius, buffer, mask, triggers);
                case EveHitboxKind.Capsule:
                    Vector3 axis = pose.Rotation * Vector3.up * (Shape.Height * .5f - Shape.Radius);
                    return world.OverlapCapsule(pose.Position - axis, pose.Position + axis, Shape.Radius, buffer, mask, triggers);
                default:
                    return world.OverlapBox(pose.Position + pose.Rotation * Shape.BoundsCenter, Shape.BoundsSize * .5f,
                        buffer, pose.Rotation, mask, triggers);
            }
        }
        private bool Intersects(Collider target, EveHitboxPose pose)
        {
            // Native 6000.6 penetration requires enabled probes in the target's scene, including preview scenes.
            // Enable after broadphase only; no simulation/callback runs during this synchronous query.
            if (probeRoot.scene != target.gameObject.scene)
            {
                foreach (var probe in probes) probe.enabled = false;
                SceneManager.MoveGameObjectToScene(probeRoot, target.gameObject.scene);
            }
            if (!probes[0].enabled) foreach (var probe in probes) probe.enabled = true;
            foreach (var probe in probes)
                if (Physics.ComputePenetration(probe, pose.Position, pose.Rotation, target,
                    target.transform.position, target.transform.rotation, out _, out _)) return true;
            return false;
        }
        private void BuildProbes()
        {
            probeRoot = new GameObject("Eve Hitbox Query Probes") { hideFlags = HideFlags.HideAndDontSave };
            if (Shape.Kind == EveHitboxKind.Cone)
            {
                var vertices = new Vector3[Shape.Segments + 1];
                var triangles = new List<int>();
                for (int i = 0; i < Shape.Segments; i++)
                {
                    var p = Point(360f * i / Shape.Segments, Shape.Radius);
                    vertices[i + 1] = new Vector3(Shape.Height, p.x, p.z);
                    triangles.AddRange(new[] { 0, (i + 1) % Shape.Segments + 1, i + 1 });
                }
                for (int i = 1; i < Shape.Segments - 1; i++) triangles.AddRange(new[] { 1, i + 1, i + 2 });
                AddMesh(vertices, triangles.ToArray()); return;
            }
            if (Shape.Kind == EveHitboxKind.Polygon)
            {
                var triangles = Shape.PolygonTriangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var points = new Vector3[3];
                    for (int j = 0; j < 3; j++)
                    { var p = Shape.Points[triangles[i + j]]; points[j] = new Vector3(p.x, 0f, p.y); }
                    AddPrism(points);
                }
                return;
            }
            if (Shape.Kind == EveHitboxKind.Cylinder || Shape.Kind == EveHitboxKind.Ellipse)
            {
                var points = new Vector3[Shape.Segments];
                for (int i = 0; i < points.Length; i++)
                {
                    var p = Point(360f * i / points.Length, 1f);
                    points[i] = Shape.Kind == EveHitboxKind.Ellipse ?
                        new Vector3(p.x * Shape.Size.x * .5f, 0f, p.z * Shape.Size.z * .5f) : p * Shape.Radius;
                }
                AddPrism(points);
                return;
            }
            int count = Math.Max(1, (int)Math.Ceiling(Shape.Angle / 360f * Shape.Segments));
            if (Shape.InnerRadius == 0f && Shape.Angle <= 180f)
            {
                var points = new Vector3[count + 2];
                for (int i = 0; i <= count; i++) points[i + 1] = Point(-Shape.Angle * .5f + Shape.Angle * i / count, Shape.Radius);
                AddPrism(points); return;
            }
            // Each annular slice is convex; union preserves the hole and sectors wider than 180 degrees.
            for (int i = 0; i < count; i++)
            {
                float a = -Shape.Angle * .5f + Shape.Angle * i / count;
                float b = -Shape.Angle * .5f + Shape.Angle * (i + 1) / count;
                if (Shape.InnerRadius == 0f) AddPrism(new[] { Vector3.zero, Point(a, Shape.Radius), Point(b, Shape.Radius) });
                else AddPrism(new[] { Point(a, Shape.InnerRadius), Point(a, Shape.Radius), Point(b, Shape.Radius), Point(b, Shape.InnerRadius) });
            }
        }
        private static Vector3 Point(float degrees, float radius)
        {
            double angle = degrees * Math.PI / 180d;
            return new Vector3((float)Math.Cos(angle) * radius, 0f, (float)Math.Sin(angle) * radius);
        }
        private void AddPrism(Vector3[] polygon)
        {
            int n = polygon.Length;
            var vertices = new Vector3[n * 2];
            var triangles = new List<int>();
            for (int i = 0; i < n; i++)
            {
                vertices[i] = polygon[i] - Vector3.up * Shape.Height * .5f;
                vertices[i + n] = polygon[i] + Vector3.up * Shape.Height * .5f;
                int j = (i + 1) % n;
                triangles.AddRange(new[] { i, i + n, j + n, i, j + n, j });
            }
            for (int i = 1; i < n - 1; i++)
                triangles.AddRange(new[] { 0, i, i + 1, n, n + i + 1, n + i });
            AddMesh(vertices, triangles.ToArray());
        }
        private void AddMesh(Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = "Eve Hitbox Convex Probe", hideFlags = HideFlags.HideAndDontSave };
            meshes.Add(mesh);
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateBounds();
            var collider = probeRoot.AddComponent<MeshCollider>();
            probes.Add(collider);
            collider.enabled = false; collider.convex = true; collider.sharedMesh = mesh;
        }
        public void Dispose()
        {
            if (disposed) return;
            if (querying) throw new InvalidOperationException("Cannot dispose during a query.");
            disposed = true;
            Destroy(probeRoot);
            foreach (var mesh in meshes) Destroy(mesh);
            probeRoot = null; meshes.Clear(); probes.Clear(); Array.Clear(buffer, 0, buffer.Length);
        }
        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
