#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

internal sealed class PNGuraAttackWeapon : IDisposable
{
    private const float TailFraction = -.18f;
    private static readonly Color Steel = new(.018f, .39f, .59f);
    private static readonly Color Edge = new(.30f, .88f, .95f);
    private static readonly Color Collar = new(.043f, .22f, .36f);
    private static readonly PNGuraFacets Shape = CreateShape();
    private readonly PNGuraVolume volume;
    private readonly Vector3[] vertices = new Vector3[Shape.Points.Count];

    public readonly struct Pose
    {
        public readonly Vector3 Grip, Axis, Across;
        public readonly float Length, Width, Bend;
        public Pose(Vector3 grip, Vector3 axis, Vector3 across, float length, float width, float bend)
        {
            Grip = grip; Axis = axis; Across = across;
            Length = length; Width = width; Bend = bend;
        }

        public Vector3 Tail => At(TailFraction * Shoulder / .67f);
        public float Shoulder => 1f - Mathf.Min(.33f, .39f / Length);
        public Vector3 At(float t) => Grip + Axis * (Length * t) + Across * (Bend * t * t);
        public Vector3 Tangent(float t) => (Axis * Length + Across * (2f * Bend * t)).normalized;
    }

    public PNGuraAttackWeapon(Transform parent, string name = "Gura attack trident", bool ghost = false)
    {
        volume = new PNGuraVolume(parent, name, ghost);
        volume.Mesh.MarkDynamic();
    }

    public void Draw(in Pose pose, float opacity = 1f)
    {
        if (opacity <= 0f) { Hide(); return; }
        Vector3 normal = Vector3.Cross(pose.Axis, pose.Across).normalized;
        float shoulder = pose.Shoulder;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = Shape.Points[i];
            float t = vertex.z <= .67f
                ? vertex.z * shoulder / .67f
                : Mathf.Lerp(shoulder, 1f, (vertex.z - .67f) / .33f);
            Vector3 right = Vector3.Cross(normal, pose.Tangent(t));
            Vector3 world = pose.At(t) + (right * vertex.x + normal * vertex.y) * pose.Width;
            vertices[i] = volume.Transform.InverseTransformPoint(world);
        }
        Shape.Apply(volume.Mesh, vertices);
        volume.Show(opacity);
    }

    public void Hide() => volume.Hide();
    public void Dispose() => volume.Dispose();

    private static PNGuraFacets CreateShape()
    {
        var shape = new PNGuraFacets();
        const int shaftSections = 12;
        for (int i = 0; i < shaftSections; i++)
        {
            float a = Mathf.Lerp(-.14f, .71f, i / (float)shaftSections);
            float b = Mathf.Lerp(-.14f, .71f, (i + 1f) / shaftSections);
            Tube(shape, new Vector3(0, 0, a), new Vector3(0, 0, b), .059f, .059f, 8, Steel);
        }
        Tube(shape, new Vector3(0,0,TailFraction),new Vector3(0,0,-.13f),0f,.113f, 6, Edge);
        Tube(shape, new Vector3(0,0,-.13f),new Vector3(0,0,-.08f),.113f,.068f, 6, Collar);
        Tube(shape, new Vector3(0,0,.59f),new Vector3(0,0,.69f),.088f,.153f, 8, Collar);
        Tube(shape, new Vector3(0,0,.69f),new Vector3(0,0,.77f),.153f,.065f, 8, Edge);
        Prong(shape, new Vector3(0,0,.69f), new Vector3(0,0,.89f), new Vector3(0,0,1f), .106f);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            var basePoint = new Vector3(sign * .10f, 0, .67f);
            var shoulder = new Vector3(sign * .36f, 0, .78f);
            Tube(shape, basePoint, shoulder, .082f, .073f, 6, Steel);
            Prong(shape, shoulder, new Vector3(sign * .39f, 0, .86f), new Vector3(sign * .47f, 0, .955f), .077f);
        }
        return shape;
    }

    private static void Prong(PNGuraFacets shape, Vector3 a, Vector3 b, Vector3 tip, float width)
    {
        Tube(shape, a, b, width * .52f, width, 4, Steel);
        Tube(shape, b, tip, width, 0f, 4, Edge);
    }

    private static void Tube(PNGuraFacets shape, Vector3 a, Vector3 b, float start, float end, int sides, Color color)
    {
        for (int i = 0; i < sides; i++)
        {
            Vector3 u = Ring(i, sides);
            Vector3 v = Ring(i + 1, sides);
            Color tint = i % 3 == 0 ? Color.Lerp(color, Edge, .34f) : color;
            if (end == 0f) shape.Triangle(a + u * start, a + v * start, b, tint);
            else if (start == 0f) shape.Triangle(a, b + v * end, b + u * end, tint);
            else shape.Quad(a + u * start, a + v * start, b + v * end, b + u * end, tint);
        }
    }

    private static Vector3 Ring(int side, int count)
    {
        float angle = side * 2f * Mathf.PI / count;
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * .74f, 0);
    }
}
#endif
