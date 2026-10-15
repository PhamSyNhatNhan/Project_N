using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace EveEngine
{
    public enum EveHitboxKind { Box, Sphere, Capsule, Cylinder, Sector, Ellipse, Polygon, Cone }

    // EN: Immutable dimensions in world units; local X is forward and Y is thickness/axis.
    // VI: Kích thước bất biến theo world unit; X hướng tới, Y là độ dày/trục.
    public sealed class EveHitboxShape
    {
        public EveHitboxKind Kind { get; }
        public Vector3 Size { get; }
        public float Radius { get; }
        public float Height { get; }
        public float InnerRadius { get; }
        public float Angle { get; }
        public int Segments { get; }
        public Vector3 BoundsCenter { get; private set; }
        public Vector3 BoundsSize { get; private set; }
        public IReadOnlyList<Vector2> Points { get; private set; } = Array.AsReadOnly(Array.Empty<Vector2>());
        internal int[] PolygonTriangles { get; private set; }
        private EveHitboxShape(EveHitboxKind kind, Vector3 size, float radius, float height,
            float innerRadius = 0f, float angle = 360f, int segments = 48)
        {
            Kind = kind; Size = size; Radius = radius; Height = height;
            InnerRadius = innerRadius; Angle = angle; Segments = segments;
            BoundsSize = kind == EveHitboxKind.Box ? size : new Vector3(radius * 2f, height, radius * 2f);
        }
        public static EveHitboxShape Box(Vector3 size)
        {
            Positive(size.x); Positive(size.y); Positive(size.z);
            return new EveHitboxShape(EveHitboxKind.Box, size, 0f, size.y);
        }
        public static EveHitboxShape Sphere(float radius)
        {
            Positive(radius); Finite(radius * 2f);
            return new EveHitboxShape(EveHitboxKind.Sphere, Vector3.zero, radius, radius * 2f);
        }
        public static EveHitboxShape Capsule(float radius, float height)
        {
            Positive(radius); Positive(height);
            if (height < radius * 2f) throw new ArgumentException("Capsule total height must be at least its diameter.");
            return new EveHitboxShape(EveHitboxKind.Capsule, Vector3.zero, radius, height);
        }
        public static EveHitboxShape Cylinder(float radius, float height, int segments = 48)
            => Round(EveHitboxKind.Cylinder, radius, height, 0f, 360f, segments);
        public static EveHitboxShape Sector(float outerRadius, float thickness, float angle,
            float innerRadius = 0f, int segments = 48)
            => Round(EveHitboxKind.Sector, outerRadius, thickness, innerRadius, angle, segments);
        public static EveHitboxShape Ellipse(float radiusX, float radiusZ, float thickness, int segments = 48)
        {
            Positive(radiusX); Positive(radiusZ); Positive(thickness); ValidateSegments(segments);
            Finite(radiusX * 2f); Finite(radiusZ * 2f);
            var size = new Vector3(radiusX * 2f, thickness, radiusZ * 2f);
            return new EveHitboxShape(EveHitboxKind.Ellipse, size, Math.Max(radiusX, radiusZ), thickness, segments: segments)
                { BoundsSize = size };
        }
        // Cone apex at pose origin, base along +X. Height here means apex-to-base length.
        public static EveHitboxShape Cone(float baseRadius, float length, int segments = 48)
        {
            Positive(baseRadius); Positive(length); Finite(baseRadius * 2f); ValidateSegments(segments);
            return new EveHitboxShape(EveHitboxKind.Cone, Vector3.zero, baseRadius, length, segments: segments)
                { BoundsCenter = new Vector3(length * .5f, 0f, 0f), BoundsSize = new Vector3(length, baseRadius * 2f, baseRadius * 2f) };
        }
        // Vector2(x,y) maps to local (X,Z); simple concave outlines are triangulated, never convex-hulled.
        public static EveHitboxShape Polygon(IEnumerable<Vector2> points, float thickness)
        {
            Positive(thickness);
            var polygon = EveHitboxPolygon.Build(points);
            float minX = polygon.Points[0].x, maxX = minX, minZ = polygon.Points[0].y, maxZ = minZ;
            foreach (var p in polygon.Points)
            { minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minZ = Math.Min(minZ, p.y); maxZ = Math.Max(maxZ, p.y); }
            float width = maxX - minX, depth = maxZ - minZ;
            Finite(width); Finite(depth);
            return new EveHitboxShape(EveHitboxKind.Polygon, new Vector3(width, thickness, depth), 0f, thickness)
            {
                Points = new ReadOnlyCollection<Vector2>(polygon.Points), PolygonTriangles = polygon.Triangles,
                BoundsCenter = new Vector3(minX + width * .5f, 0f, minZ + depth * .5f),
                BoundsSize = new Vector3(width, thickness, depth)
            };
        }
        private static EveHitboxShape Round(EveHitboxKind kind, float radius, float height, float inner, float angle, int segments)
        {
            Positive(radius); Positive(height); Positive(angle); Finite(inner);
            ValidateSegments(segments); Finite(radius * 2f);
            if (inner < 0f || inner >= radius || angle > 360f)
                throw new ArgumentException("Invalid round hitbox dimensions; segments must be 8..64.");
            return new EveHitboxShape(kind, Vector3.zero, radius, height, inner, angle, segments);
        }
        private static void ValidateSegments(int segments)
        {
            if (segments < 8 || segments > 64) throw new ArgumentOutOfRangeException(nameof(segments), "Segments must be 8..64.");
        }
        internal static void Finite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        }
        private static void Positive(float value)
        {
            Finite(value); if (value <= 0f) throw new ArgumentOutOfRangeException(nameof(value), "Dimension must be positive.");
        }
    }

    public readonly struct EveHitboxPose
    {
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public EveHitboxPose(Vector3 position, Quaternion rotation)
        {
            EveHitboxShape.Finite(position.x); EveHitboxShape.Finite(position.y); EveHitboxShape.Finite(position.z);
            EveHitboxShape.Finite(rotation.x); EveHitboxShape.Finite(rotation.y);
            EveHitboxShape.Finite(rotation.z); EveHitboxShape.Finite(rotation.w);
            double length = Math.Sqrt((double)rotation.x * rotation.x + (double)rotation.y * rotation.y +
                (double)rotation.z * rotation.z + (double)rotation.w * rotation.w);
            if (length == 0d) throw new ArgumentException("Pose rotation cannot be zero.");
            Position = position;
            Rotation = new Quaternion((float)(rotation.x / length), (float)(rotation.y / length),
                (float)(rotation.z / length), (float)(rotation.w / length));
        }
        // EN: Explicit direction from skill; never read movement or Visual transforms.
        // VI: Hướng do skill truyền; không tự đọc movement hay transform Visual.
        public static EveHitboxPose Planar(Vector3 position, Vector3 direction)
        {
            EveHitboxShape.Finite(direction.x); EveHitboxShape.Finite(direction.y); EveHitboxShape.Finite(direction.z);
            if (direction.x == 0f && direction.z == 0f) throw new ArgumentException("Planar direction is required.");
            return new EveHitboxPose(position, Quaternion.Euler(0f, -(float)(Math.Atan2(direction.z, direction.x) * 180d / Math.PI), 0f));
        }
    }
}
