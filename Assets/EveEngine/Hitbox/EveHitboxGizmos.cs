using UnityEngine;
namespace EveEngine
{
    public static class EveHitboxGizmos
    {
        public static void Draw(EveHitboxShape shape, EveHitboxPose pose, Color color)
        {
            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            try
            {
                Gizmos.matrix = Matrix4x4.TRS(pose.Position, pose.Rotation, Vector3.one);
                Gizmos.color = color;
                if (shape.Kind == EveHitboxKind.Box) Gizmos.DrawWireCube(Vector3.zero, shape.Size);
                else if (shape.Kind == EveHitboxKind.Sphere) Gizmos.DrawWireSphere(Vector3.zero, shape.Radius);
                else if (shape.Kind == EveHitboxKind.Capsule)
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
                else if (shape.Kind == EveHitboxKind.Cone) DrawCone(shape);
                else if (shape.Kind == EveHitboxKind.Polygon || shape.Kind == EveHitboxKind.Ellipse) DrawOutline(shape);
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
