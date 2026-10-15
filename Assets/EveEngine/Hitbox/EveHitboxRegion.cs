using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EveEngine
{
    // EN: Optional authoring component. Dimensions/offsets are world units, independent of transform scale.
    // VI: Component cấu hình tùy chọn. Kích thước/offset theo world unit, không nhân scale transform.
    public sealed class EveHitboxRegion : MonoBehaviour
    {
        [SerializeField] private EveHitboxKind kind = EveHitboxKind.Sector;
        [SerializeField] private Vector3 boxSize = new Vector3(1f, .2f, 1f);
        [SerializeField] private float radius = 1f;
        [SerializeField, Tooltip("Y thickness/height; for Cone, apex-to-base length along +X.")] private float height = .2f;
        [SerializeField] private Vector2 ellipseRadii = new Vector2(1f, .5f);
        [SerializeField] private Vector2[] polygonPoints = { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f),
            new Vector2(.5f, .5f), new Vector2(-.5f, .5f) };
        [SerializeField] private float innerRadius;
        [SerializeField, Range(0.1f, 360f)] private float angle = 90f;
        [SerializeField, Range(8, 64)] private int segments = 48;
        [SerializeField] private Vector3 offset;
        [SerializeField] private Vector3 rotation;
        [SerializeField] private LayerMask layers = ~0;
        [SerializeField] private QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore;
        private EveHitboxQuery query;
        private bool changed;

        public EveHitboxShape CreateShape()
        {
            switch (kind)
            {
                case EveHitboxKind.Box: return EveHitboxShape.Box(boxSize);
                case EveHitboxKind.Sphere: return EveHitboxShape.Sphere(radius);
                case EveHitboxKind.Capsule: return EveHitboxShape.Capsule(radius, height);
                case EveHitboxKind.Cylinder: return EveHitboxShape.Cylinder(radius, height, segments);
                case EveHitboxKind.Sector: return EveHitboxShape.Sector(radius, height, angle, innerRadius, segments);
                case EveHitboxKind.Ellipse: return EveHitboxShape.Ellipse(ellipseRadii.x, ellipseRadii.y, height, segments);
                case EveHitboxKind.Polygon: return EveHitboxShape.Polygon(polygonPoints, height);
                case EveHitboxKind.Cone: return EveHitboxShape.Cone(radius, height, segments);
                default: throw new System.ArgumentOutOfRangeException(nameof(kind));
            }
        }
        public EveHitboxPose GetPose(EveHitboxPose parentPose) => new EveHitboxPose(
            parentPose.Position + parentPose.Rotation * offset, parentPose.Rotation * Quaternion.Euler(rotation));
        public int Query(EveHitboxPose parentPose, List<Collider> results)
        {
            if (changed) { query?.Dispose(); query = null; changed = false; }
            if (query == null) query = new EveHitboxQuery(CreateShape(), gameObject.scene.GetPhysicsScene());
            return query.Query(GetPose(parentPose), results, layers, triggers);
        }
        public int Query(List<Collider> results) => Query(new EveHitboxPose(transform.position, transform.rotation), results);
        private void OnValidate() => changed = true;
        private void OnDisable() { query?.Dispose(); query = null; }
        private void OnDestroy() { query?.Dispose(); query = null; }
        private void OnDrawGizmosSelected()
        {
            EveHitboxShape shape;
            EveHitboxPose pose;
            try { shape = CreateShape(); pose = GetPose(new EveHitboxPose(transform.position, transform.rotation)); }
            catch (System.ArgumentException) { return; }
            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            try
            {
                Gizmos.matrix = Matrix4x4.TRS(pose.Position, pose.Rotation, Vector3.one);
                Gizmos.color = new Color(1f, .3f, .1f, .9f);
                if (kind == EveHitboxKind.Box) Gizmos.DrawWireCube(Vector3.zero, shape.Size);
                else if (kind == EveHitboxKind.Sphere) Gizmos.DrawWireSphere(Vector3.zero, shape.Radius);
                else if (kind == EveHitboxKind.Capsule)
                {
                    float end = shape.Height * .5f - shape.Radius;
                    // End circles plus four side lines and cap arcs.
                    for (int i = 0; i < 48; i++)
                    {
                        float a = i * Mathf.PI * 2f / 48f, b = (i + 1) * Mathf.PI * 2f / 48f;
                        var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * shape.Radius;
                        var q = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * shape.Radius;
                        Gizmos.DrawLine(p + Vector3.up * end, q + Vector3.up * end);
                        Gizmos.DrawLine(p - Vector3.up * end, q - Vector3.up * end);
                    }
                    for (int side = 0; side < 4; side++)
                    {
                        var axis = Quaternion.Euler(0f, side * 90f, 0f) * Vector3.right;
                        Gizmos.DrawLine(axis * shape.Radius - Vector3.up * end, axis * shape.Radius + Vector3.up * end);
                        for (int i = 0; i < 12; i++)
                        {
                            float a = i * Mathf.PI / 24f, b = (i + 1) * Mathf.PI / 24f;
                            var p = axis * (Mathf.Cos(a) * shape.Radius) + Vector3.up * (end + Mathf.Sin(a) * shape.Radius);
                            var q = axis * (Mathf.Cos(b) * shape.Radius) + Vector3.up * (end + Mathf.Sin(b) * shape.Radius);
                            Gizmos.DrawLine(p, q); Gizmos.DrawLine(new Vector3(p.x, -p.y, p.z), new Vector3(q.x, -q.y, q.z));
                        }
                    }
                }
                else if (kind == EveHitboxKind.Cone) DrawCone(shape);
                else if (kind == EveHitboxKind.Polygon || kind == EveHitboxKind.Ellipse) DrawOutline(shape);
                else DrawRound(shape);
            }
            finally { Gizmos.matrix = previousMatrix; Gizmos.color = previousColor; }
        }
        private static void DrawCone(EveHitboxShape shape)
        {
            for (int i = 0; i < shape.Segments; i++)
            {
                float a = i * Mathf.PI * 2f / shape.Segments, b = (i + 1) * Mathf.PI * 2f / shape.Segments;
                var p = new Vector3(shape.Height, Mathf.Cos(a) * shape.Radius, Mathf.Sin(a) * shape.Radius);
                var q = new Vector3(shape.Height, Mathf.Cos(b) * shape.Radius, Mathf.Sin(b) * shape.Radius);
                Gizmos.DrawLine(p, q); Gizmos.DrawLine(Vector3.zero, p);
            }
        }
        private static void DrawOutline(EveHitboxShape shape)
        {
            int count = shape.Kind == EveHitboxKind.Polygon ? shape.Points.Count : shape.Segments;
            Vector3 Point(int index)
            {
                if (shape.Kind == EveHitboxKind.Polygon)
                { var p = shape.Points[index % count]; return new Vector3(p.x, 0f, p.y); }
                float a = index * Mathf.PI * 2f / count;
                return new Vector3(Mathf.Cos(a) * shape.Size.x * .5f, 0f, Mathf.Sin(a) * shape.Size.z * .5f);
            }
            for (int i = 0; i < count; i++)
            {
                var p = Point(i); var q = Point(i + 1); var y = Vector3.up * shape.Height * .5f;
                Gizmos.DrawLine(p - y, q - y); Gizmos.DrawLine(p + y, q + y); Gizmos.DrawLine(p - y, p + y);
            }
        }
        private static void DrawRound(EveHitboxShape shape)
        {
            int count = shape.Kind == EveHitboxKind.Cylinder ? shape.Segments :
                System.Math.Max(1, (int)System.Math.Ceiling(shape.Angle / 360f * shape.Segments));
            for (int i = 0; i < count; i++)
            {
                float start = shape.Kind == EveHitboxKind.Cylinder ? 0f : -shape.Angle * .5f;
                float a = (start + shape.Angle * i / count) * Mathf.Deg2Rad;
                float b = (start + shape.Angle * (i + 1) / count) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var q = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                for (int side = -1; side <= 1; side += 2)
                {
                    var y = Vector3.up * (side * shape.Height * .5f);
                    Gizmos.DrawLine(y + p * shape.Radius, y + q * shape.Radius);
                    if (shape.InnerRadius > 0f) Gizmos.DrawLine(y + p * shape.InnerRadius, y + q * shape.InnerRadius);
                    if (shape.Kind == EveHitboxKind.Sector && (i == 0 || i == count - 1))
                    {
                        var edge = i == 0 ? p : q;
                        Gizmos.DrawLine(y + edge * shape.InnerRadius, y + edge * shape.Radius);
                    }
                }
                Gizmos.DrawLine(p * shape.Radius - Vector3.up * shape.Height * .5f,
                    p * shape.Radius + Vector3.up * shape.Height * .5f);
            }
        }
    }
}
