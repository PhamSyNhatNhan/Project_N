#if UNITY_EDITOR
using System;
using EveEngine;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EveHitboxTrack))]
public sealed class EveHitboxTrackEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var track = (EveHitboxTrack)target;
        float previousTime = track.PreviewTime;
        if (DrawDefaultInspector())
        {
            if (track.PreviewTime != previousTime)
                track.SendMessageUpwards("SetPreviewTime", track.PreviewTime, SendMessageOptions.DontRequireReceiver);
            else track.SendMessageUpwards("RefreshPreview", SendMessageOptions.DontRequireReceiver);
            SceneView.RepaintAll();
        }
        EditorGUILayout.HelpBox("Dimensions use world units. Transform scale is ignored. Radial scales XZ, Axial scales thickness/length, Angular scales sector angle. Curves use normalized action time; 0 = start, 1 = end.", MessageType.Info);
        try { ((EveHitboxTrack)target).Data.Validate(); }
        catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
    }
    private void OnSceneGUI()
    {
        var track = (EveHitboxTrack)target;
        if (track.RuntimeData != null) return; // Runtime copies are edited through the invocation API.
        try
        {
            var data = track.Data;
            var frame = data.Sample(track.PreviewTime, new EveHitboxPose(track.transform.position, track.transform.rotation));
            if (frame.Shape == null) return;
            float radial = data.Radial.Evaluate(track.PreviewTime), axial = data.Axial.Evaluate(track.PreviewTime), angular = data.Angular.Evaluate(track.PreviewTime);
            Undo.RecordObject(track, "Edit hitbox geometry");
            EditorGUI.BeginChangeCheck();
            var worldOffset = Handles.PositionHandle(frame.Pose.Position, frame.Pose.Rotation);
            var worldRotation = Handles.RotationHandle(frame.Pose.Rotation, frame.Pose.Position);
            var geometry = data.Geometry.Clone();
            using (new Handles.DrawingScope(Matrix4x4.TRS(frame.Pose.Position, frame.Pose.Rotation, Vector3.one)))
            {
                switch (geometry.Kind)
                {
                    case EveHitboxKind.Box:
                        var box = new UnityEditor.IMGUI.Controls.BoxBoundsHandle(); box.center = Vector3.zero; box.size = frame.Shape.Size; box.DrawHandle();
                        geometry.Size = new Vector3(box.size.x / radial, box.size.y / axial, box.size.z / radial); break;
                    case EveHitboxKind.Ellipse:
                        geometry.EllipseRadii = new Vector2(Length(frame.Shape.Size.x * .5f, Vector3.right) / radial, Length(frame.Shape.Size.z * .5f, Vector3.forward) / radial);
                        geometry.Height = 2f * Length(frame.Shape.Height * .5f, Vector3.up) / axial; break;
                    case EveHitboxKind.Polygon:
                        for (int i = 0; i < geometry.Points.Length; i++)
                        {
                            var point = geometry.Points[i]; var position = new Vector3(point.x * radial, 0f, point.y * radial);
                            position = Handles.PositionHandle(position, Quaternion.identity);
                            geometry.Points[i] = new Vector2(position.x / radial, position.z / radial);
                        }
                        geometry.Height = 2f * Length(frame.Shape.Height * .5f, Vector3.up) / axial; break;
                    case EveHitboxKind.Cone:
                        geometry.Radius = Length(frame.Shape.Radius, Vector3.up, Vector3.right * frame.Shape.Height) / radial;
                        geometry.Height = Length(frame.Shape.Height, Vector3.right) / axial; break;
                    default:
                        geometry.Radius = Length(frame.Shape.Radius, Vector3.right) / radial;
                        if (geometry.Kind != EveHitboxKind.Sphere) geometry.Height = 2f * Length(frame.Shape.Height * .5f, Vector3.up) / axial;
                        if (geometry.Kind == EveHitboxKind.Sector)
                        {
                            geometry.InnerRadius = Mathf.Min(geometry.Radius * .999f, Length(frame.Shape.InnerRadius, Vector3.right) / radial);
                            float half = frame.Shape.Angle * .5f * Mathf.Deg2Rad;
                            var edge = new Vector3(Mathf.Cos(half), 0f, Mathf.Sin(half)) * frame.Shape.Radius;
                            var moved = Handles.FreeMoveHandle(edge, HandleUtility.GetHandleSize(edge) * .06f, Vector3.zero, Handles.DotHandleCap);
                            geometry.Angle = Mathf.Clamp(Mathf.Atan2(moved.z, moved.x) * Mathf.Rad2Deg * 2f / angular, .01f, 360f);
                        }
                        break;
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                data.Geometry = geometry;
                data.Offset += Quaternion.Inverse(track.transform.rotation) * (worldOffset - frame.Pose.Position);
                if (Quaternion.Angle(worldRotation, frame.Pose.Rotation) > .001f)
                    data.Euler = (Quaternion.Inverse(track.transform.rotation) * worldRotation).eulerAngles
                        - new Vector3(data.Pitch.Evaluate(track.PreviewTime), data.Yaw.Evaluate(track.PreviewTime), data.Roll.Evaluate(track.PreviewTime));
                EditorUtility.SetDirty(track); PrefabUtility.RecordPrefabInstancePropertyModifications(track); SceneView.RepaintAll();
                track.SendMessageUpwards("RefreshPreview", SendMessageOptions.DontRequireReceiver);
            }
        }
        catch (ArgumentException) { }
    }
    private static float Length(float value, Vector3 axis, Vector3 origin = default)
    {
        var position = origin + axis * value;
        var next = Handles.Slider(position, axis, HandleUtility.GetHandleSize(position) * .06f, Handles.DotHandleCap, 0f);
        return Mathf.Max(.001f, Vector3.Dot(next - origin, axis));
    }
}
#endif
