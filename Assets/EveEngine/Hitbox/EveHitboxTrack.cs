using System;
using UnityEngine;

namespace EveEngine
{
    [Serializable]
    public sealed class EveHitboxGeometry
    {
        public EveHitboxKind Kind = EveHitboxKind.Sector;
        public Vector3 Size = Vector3.one;
        public float Radius = .6f, InnerRadius, Height = .4f, Angle = 100f;
        public Vector2 EllipseRadii = new Vector2(1f, .5f);
        public Vector2[] Points = { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(.5f, .5f), new Vector2(-.5f, .5f) };
        [Range(8, 64)] public int Segments = 32;
        public EveHitboxGeometry Clone()
        {
            var copy = (EveHitboxGeometry)MemberwiseClone(); copy.Points = Points == null ? null : (Vector2[])Points.Clone(); return copy;
        }
        public EveHitboxShape Shape(float radial = 1f, float axial = 1f, float angular = 1f)
        {
            switch (Kind)
            {
                case EveHitboxKind.Box: return EveHitboxShape.Box(new Vector3(Size.x * radial, Size.y * axial, Size.z * radial));
                case EveHitboxKind.Sphere: return EveHitboxShape.Sphere(Radius * radial);
                case EveHitboxKind.Capsule: return EveHitboxShape.Capsule(Radius * radial, Height * axial);
                case EveHitboxKind.Cylinder: return EveHitboxShape.Cylinder(Radius * radial, Height * axial, Segments);
                case EveHitboxKind.Sector: return EveHitboxShape.Sector(Radius * radial, Height * axial, Angle * angular, InnerRadius * radial, Segments);
                case EveHitboxKind.Ellipse: return EveHitboxShape.Ellipse(EllipseRadii.x * radial, EllipseRadii.y * radial, Height * axial, Segments);
                case EveHitboxKind.Cone: return EveHitboxShape.Cone(Radius * radial, Height * axial, Segments);
                case EveHitboxKind.Polygon:
                    if (Points == null) throw new ArgumentException("Polygon points are missing.");
                    var scaled = new Vector2[Points.Length];
                    for (int i = 0; i < scaled.Length; i++) scaled[i] = Points[i] * radial;
                    return EveHitboxShape.Polygon(scaled, Height * axial);
                default: throw new ArgumentOutOfRangeException(nameof(Kind));
            }
        }
    }

    public readonly struct EveHitboxFrame
    {
        public readonly EveHitboxShape Shape;
        public readonly EveHitboxPose Pose;
        public readonly bool Enabled;
        public EveHitboxFrame(EveHitboxShape shape, EveHitboxPose pose, bool enabled) { Shape = shape; Pose = pose; Enabled = enabled; }
    }

    // EN: Mutable per-cast copy. Curves sample normalized action time, shared by preview and runtime.
    // VI: Bản sao riêng có thể chỉnh bằng code. Curve dùng tiến độ action chuẩn hóa chung cho preview/runtime.
    [Serializable]
    public sealed class EveHitboxTrackData
    {
        public EveHitboxGeometry Geometry = new EveHitboxGeometry();
        public Vector3 Offset = new Vector3(.15f, .18f, 0f), Euler;
        public AnimationCurve Radial = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        public AnimationCurve Axial = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        public AnimationCurve Angular = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        public AnimationCurve OffsetX = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve OffsetY = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve OffsetZ = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve Pitch = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve Yaw = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve Roll = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        [Range(0f, 1f)] public float EnableFrom, EnableTo = 1f;
        [NonSerialized] public Vector3 AnchorOffset;
        [NonSerialized] public Quaternion AnchorRotation = Quaternion.identity;
        [NonSerialized] public int LayerMask = ~0;
        private static AnimationCurve Copy(AnimationCurve curve)
        {
            if (curve == null) throw new ArgumentException("Timeline curve is missing.");
            return new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        }
        public EveHitboxTrackData Clone()
        {
            if (Geometry == null) throw new ArgumentException("Hitbox geometry is missing.");
            var copy = (EveHitboxTrackData)MemberwiseClone(); copy.Geometry = Geometry.Clone();
            copy.Radial = Copy(Radial); copy.Axial = Copy(Axial); copy.Angular = Copy(Angular);
            copy.OffsetX = Copy(OffsetX); copy.OffsetY = Copy(OffsetY); copy.OffsetZ = Copy(OffsetZ);
            copy.Pitch = Copy(Pitch); copy.Yaw = Copy(Yaw); copy.Roll = Copy(Roll); return copy;
        }
        public EveHitboxFrame Sample(float progress, EveHitboxPose parent)
        {
            if (float.IsNaN(progress) || float.IsInfinity(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
            float t = Mathf.Clamp01(progress);
            var anchor = new EveHitboxPose(parent.Position + parent.Rotation * AnchorOffset, parent.Rotation * AnchorRotation);
            var offset = Offset + new Vector3(OffsetX.Evaluate(t), OffsetY.Evaluate(t), OffsetZ.Evaluate(t));
            var euler = Euler + new Vector3(Pitch.Evaluate(t), Yaw.Evaluate(t), Roll.Evaluate(t));
            var pose = new EveHitboxPose(anchor.Position + anchor.Rotation * offset, anchor.Rotation * Quaternion.Euler(euler));
            float radial = Radial.Evaluate(t), axial = Axial.Evaluate(t), angular = Angular.Evaluate(t);
            if (radial < 0f || axial < 0f || angular < 0f) throw new ArgumentException("Geometry scales cannot be negative.");
            if (radial == 0f || axial == 0f || angular == 0f) return new EveHitboxFrame(null, pose, false);
            return new EveHitboxFrame(Geometry.Shape(radial, axial, angular), pose, t >= EnableFrom && t <= EnableTo);
        }
        public void Validate()
        {
            if (Geometry == null || !float.IsFinite(EnableFrom) || !float.IsFinite(EnableTo) ||
                EnableFrom < 0f || EnableTo > 1f || EnableFrom > EnableTo) throw new ArgumentException("Invalid hitbox track enable range.");
            // Validate key data and representative samples; runtime also validates every actual sample.
            foreach (var curve in new[] { Radial, Axial, Angular, OffsetX, OffsetY, OffsetZ, Pitch, Yaw, Roll })
            {
                if (curve == null || curve.length == 0) throw new ArgumentException("Timeline needs nonempty curves.");
                foreach (var key in curve.keys)
                    if (!float.IsFinite(key.time) || !float.IsFinite(key.value) || !float.IsFinite(key.inWeight) || !float.IsFinite(key.outWeight) ||
                        float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent)) throw new ArgumentException("Invalid curve key.");
            }
            Geometry.Shape();
            for (int i = 0; i <= 128; i++) Sample(i / 128f, new EveHitboxPose(Vector3.zero, Quaternion.identity));
        }
    }

    public sealed class EveHitboxTrack : MonoBehaviour
    {
        [Tooltip("Only colliders on these layers are scanned. Nothing disables scanning; Everything scans all layers.")]
        public LayerMask LayerMask = ~0;
        public EveHitboxTrackData Data = new EveHitboxTrackData();
        [Range(0f, 1f)] public float PreviewTime = .35f;
        [NonSerialized] public EveHitboxTrackData RuntimeData;
        [NonSerialized] public Transform RuntimeRoot;
        [NonSerialized] public float RuntimeTime;
        [NonSerialized] public bool RuntimeEnabled = true;
        public EveHitboxTrackData Capture(Transform root)
        {
            if (Data == null) throw new ArgumentException("Hitbox track data is missing.");
            var copy = Data.Clone();
            copy.LayerMask = LayerMask.value;
            copy.AnchorOffset = Quaternion.Inverse(root.rotation) * (transform.position - root.position);
            copy.AnchorRotation = Quaternion.Inverse(root.rotation) * transform.rotation; copy.Validate(); return copy;
        }
        private void OnDrawGizmosSelected()
        {
            try
            {
                var frame = RuntimeData != null && RuntimeRoot != null
                    ? RuntimeData.Sample(RuntimeTime, new EveHitboxPose(RuntimeRoot.position, RuntimeRoot.rotation))
                    : Data.Sample(PreviewTime, new EveHitboxPose(transform.position, transform.rotation));
                if (frame.Shape != null) EveHitboxGizmos.Draw(frame.Shape, frame.Pose, frame.Enabled && (RuntimeData == null || RuntimeEnabled) ? Color.green : Color.gray);
            }
            catch (ArgumentException) { }
        }
    }
}
