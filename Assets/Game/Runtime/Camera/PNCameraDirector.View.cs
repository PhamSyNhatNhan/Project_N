#if UNITY_5_3_OR_NEWER
#pragma warning disable CS0618 // scene rigs may still be the legacy virtual camera / FramingTransposer; they are read as a data source only
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Unity.Cinemachine;
using UnityEngine;

// One gameplay rig for every world (Docs/CameraSinglePathPlan.md). The director owns a single composer rig that follows the local actor; what it looks like (projection, angle, distance, lens, follow feel, background)
// is the resolved PNCameraView of the viewed world: game default -> mode -> the world's own camera as a data source -> the world definition -> the world layer set through the API -> the player. A world's scene
// camera is read (never driven), so the same world looks the same whatever was viewed before and in whatever order things loaded.
public sealed partial class PNCameraDirector
{
    private PNCameraViewLayer modeView, playerView;
    private readonly Dictionary<Guid, PNCameraViewLayer> apiWorldViews = new Dictionary<Guid, PNCameraViewLayer>();
    private PNCameraView appliedView;
    private bool viewApplied, viewSmoothing;
    private float viewedSince;
    private float curPitch, curYaw, curDistance, curFov, curOrtho, tgtPitch, tgtYaw, tgtDistance, tgtFov, tgtOrtho;
    public const float DefaultRigDistance = 5.5f;
    // A look that changes within this many real seconds of the world being viewed is applied at once (the world is being entered); later changes blend.
    private const float EntryWindow = 1f;

    public PNCameraView View => appliedView ?? PNCameraView.Default;

    // Mode, world (of the viewed world) and player layers; null clears. An invalid layer throws and leaves the camera as it was.
    public void SetViewLayer(PNCameraViewSource source, PNCameraViewLayer layer)
    {
        if (source == PNCameraViewSource.Game) throw new ArgumentOutOfRangeException(nameof(source), "The game default is not replaceable.");
        var previousMode = modeView; var previousPlayer = playerView; Guid worldId = viewed != null ? viewed.InstanceId : Guid.Empty; apiWorldViews.TryGetValue(worldId, out var previousWorld);
        try
        {
            if (source == PNCameraViewSource.Mode) modeView = layer;
            else if (source == PNCameraViewSource.Player) playerView = layer;
            else if (worldId != Guid.Empty) { if (layer == null) apiWorldViews.Remove(worldId); else apiWorldViews[worldId] = layer; }
            else if (layer != null) throw new InvalidOperationException("No world is viewed.");
            if (viewed != null) ApplyView(ResolveView(viewed, true), Time.unscaledTime - viewedSince < EntryWindow);
        }
        catch
        {
            modeView = previousMode; playerView = previousPlayer;
            if (worldId != Guid.Empty) { if (previousWorld == null) apiWorldViews.Remove(worldId); else apiWorldViews[worldId] = previousWorld; }
            throw;
        }
    }

    private PNCameraView ResolveView(EveWorldContext world, bool strict = false)
    {
        var view = PNCameraView.Default.With(modeView);
        var record = cameras.Find(x => x.World == world);
        view = view.With(record?.Source);
        PNCameraViewLayer fromDefinition = null;
        try { fromDefinition = session.Registry.Get<PNWorldDefinition>().Get(world.DefinitionId).CameraView; } catch (Exception error) when (error is KeyNotFoundException || error is InvalidOperationException) { }
        if (strict) view = view.With(fromDefinition);
        else try { view = view.With(fromDefinition); } catch (Exception error) { Debug.LogException(error); }
        if (apiWorldViews.TryGetValue(world.InstanceId, out var api)) view = view.With(api);
        return view.With(playerView);
    }

    private CinemachineCamera GameplayRig()
    {
        if (fallback != null) return fallback;
        fallbackObject = new GameObject("PN Camera Rig");
        fallbackObject.transform.SetParent(transform, false);
        fallbackObject.transform.rotation = Quaternion.Euler(PNCameraView.Default.Pitch, PNCameraView.Default.Yaw, 0f);
        fallback = fallbackObject.AddComponent<CinemachineCamera>();
        fallback.Priority.Value = 10;
        fallback.Lens = LensSettings.FromCamera(output);
        fallbackObject.AddComponent<CinemachinePositionComposer>().CameraDistance = DefaultRigDistance;
        return fallback;
    }

    // Writes the resolved look onto the rig. `snap` puts the angle, distance and lens there at once; otherwise they blend (Advance) at the profile's smooth speed.
    private void ApplyView(PNCameraView view, bool snap)
    {
        var rig = GameplayRig();
        var composer = rig.GetComponent<CinemachinePositionComposer>();
        if (appliedView != null && viewApplied && appliedView.SameAs(view) && !snap) return;
        appliedView = view;
        var composition = composer.Composition;
        composition.ScreenPosition = new Vector2(view.ScreenX, view.ScreenY);
        composition.DeadZone = new ScreenComposerSettings.DeadZoneSettings { Enabled = view.DeadZoneWidth > 0f || view.DeadZoneHeight > 0f, Size = new Vector2(view.DeadZoneWidth, view.DeadZoneHeight) };
        composition.HardLimits = new ScreenComposerSettings.HardLimitSettings { Enabled = view.SoftZoneWidth > 0f && view.SoftZoneHeight > 0f, Size = new Vector2(view.SoftZoneWidth, view.SoftZoneHeight), Offset = Vector2.zero };
        composer.Composition = composition;
        composer.Damping = new Vector3(view.DampingX, view.DampingY, view.DampingZ);
        composer.Lookahead = new LookaheadSettings { Enabled = view.LookaheadTime > 0f, Time = view.LookaheadTime, Smoothing = 0f, IgnoreY = false };
        var lens = rig.Lens;
        lens.ModeOverride = view.Projection == PNCameraProjection.Orthographic ? LensSettings.OverrideModes.Orthographic : LensSettings.OverrideModes.Perspective;
        rig.Lens = lens;
        output.clearFlags = CameraClearFlags.SolidColor; output.backgroundColor = new Color(view.BackgroundR, view.BackgroundG, view.BackgroundB, 1f);
        tgtPitch = view.Pitch; tgtYaw = view.Yaw; tgtDistance = view.Distance; tgtFov = view.FieldOfView; tgtOrtho = view.OrthographicSize;
        if (snap || !viewApplied) { curPitch = tgtPitch; curYaw = tgtYaw; curDistance = tgtDistance; curFov = tgtFov; curOrtho = tgtOrtho; viewSmoothing = false; }
        else viewSmoothing = true;
        viewApplied = true;
        WriteRig();
    }

    private void WriteRig()
    {
        if (fallback == null) return;
        var rotation = Quaternion.Euler(curPitch, curYaw, 0f);
        rigZooms.TryGetValue(fallback, out var zoom);
        if (zoom != null && zoom.Orbited) zoom.AuthoredRotation = rotation; else fallbackObject.transform.rotation = rotation;
        var lens = fallback.Lens; lens.FieldOfView = curFov; lens.OrthographicSize = curOrtho; fallback.Lens = lens;
        var composer = fallback.GetComponent<CinemachinePositionComposer>();
        composer.CameraDistance = curDistance * (zoom != null ? distanceScale : 1f);
        if (zoom != null) { zoom.BaseDistance = curDistance; zoom.BaseFov = curFov; zoom.BaseOrtho = curOrtho; }
    }

    private void UpdateView(float deltaTime)
    {
        if (!viewSmoothing || fallback == null) return;
        float k = 1f - Mathf.Exp(-Mathf.Max(.01f, Profile.SmoothSpeed) * deltaTime);
        curPitch = Mathf.Lerp(curPitch, tgtPitch, k); curYaw = Mathf.LerpAngle(curYaw, tgtYaw, k); curDistance = Mathf.Lerp(curDistance, tgtDistance, k);
        curFov = Mathf.Lerp(curFov, tgtFov, k); curOrtho = Mathf.Lerp(curOrtho, tgtOrtho, k);
        if (Mathf.Abs(curPitch - tgtPitch) < .02f && Mathf.Abs(Mathf.DeltaAngle(curYaw, tgtYaw)) < .02f && Mathf.Abs(curDistance - tgtDistance) < .005f && Mathf.Abs(curFov - tgtFov) < .02f && Mathf.Abs(curOrtho - tgtOrtho) < .005f)
        { curPitch = tgtPitch; curYaw = tgtYaw; curDistance = tgtDistance; curFov = tgtFov; curOrtho = tgtOrtho; viewSmoothing = false; }
        WriteRig();
    }

    // ---- the world's own camera as a data source ------------------------------------------------------------------------------------

    private IEnumerable<CinemachineVirtualCameraBase> VirtualCamerasOf(EveWorldContext world)
    {
        var seen = new HashSet<CinemachineVirtualCameraBase>();
        if (world.PhysicsOwner.IsValid())
            foreach (var root in world.PhysicsOwner.GetRootGameObjects())
                foreach (var vcam in root.GetComponentsInChildren<CinemachineVirtualCameraBase>(true)) if (vcam != null && vcam != fallback && seen.Add(vcam)) yield return vcam;
        foreach (var vcam in FindObjectsByType<CinemachineVirtualCameraBase>(FindObjectsInactive.Include))
            if (vcam != null && vcam != fallback && vcam.GetComponentInParent<PNCameraDirector>() == null && presentation.WorldOf(vcam) == world && seen.Add(vcam)) yield return vcam;
    }

    private static float Normal(float angle) => angle > 180f ? angle - 360f : angle;

    // The look a claimed scene camera describes. Values are brought inside the hard limits of PNCameraView so an odd scene can never make the view invalid.
    private PNCameraViewLayer CaptureView(Camera camera, EveWorldContext world)
    {
        // The background is not read from the scene camera: it is the view's (black by default, a world declares its own); the camera still gives the render preset (renderer, post-processing, HDR).
        var layer = new PNCameraViewLayer();
        var vcam = VirtualCamerasOf(world).FirstOrDefault(x => !(x is CinemachineExternalCamera));
        if (vcam == null)
        {
            var euler = camera.transform.rotation.eulerAngles;
            layer.Projection = camera.orthographic ? PNCameraProjection.Orthographic : PNCameraProjection.Perspective;
            layer.FieldOfView = Mathf.Clamp(camera.fieldOfView, 1f, 170f); layer.OrthographicSize = Mathf.Clamp(camera.orthographicSize, .1f, 1000f);
            layer.Pitch = Mathf.Clamp(Normal(euler.x), -89f, 89f); layer.Yaw = Normal(euler.y);
            return layer;
        }
        LensSettings lens = vcam is CinemachineVirtualCamera legacy ? legacy.m_Lens.ToLensSettings() : vcam is CinemachineCamera current ? current.Lens : vcam.State.Lens;
        bool orthographic = lens.ModeOverride == LensSettings.OverrideModes.Orthographic || lens.ModeOverride == LensSettings.OverrideModes.None && camera.orthographic;
        layer.Projection = orthographic ? PNCameraProjection.Orthographic : PNCameraProjection.Perspective;
        layer.FieldOfView = Mathf.Clamp(lens.FieldOfView, 1f, 170f); layer.OrthographicSize = Mathf.Clamp(lens.OrthographicSize, .1f, 1000f);
        var rotation = vcam.transform.rotation.eulerAngles;
        layer.Pitch = Mathf.Clamp(Normal(rotation.x), -89f, 89f); layer.Yaw = Normal(rotation.y);
        foreach (var component in vcam.GetComponentsInChildren<CinemachineComponentBase>(true))
        {
            if (component is CinemachinePositionComposer composer)
            {
                layer.Distance = Mathf.Clamp(composer.CameraDistance, .1f, 1000f);
                layer.ScreenX = Mathf.Clamp(composer.Composition.ScreenPosition.x, -.5f, .5f); layer.ScreenY = Mathf.Clamp(composer.Composition.ScreenPosition.y, -.5f, .5f);
                layer.DampingX = Mathf.Clamp(composer.Damping.x, 0f, 20f); layer.DampingY = Mathf.Clamp(composer.Damping.y, 0f, 20f); layer.DampingZ = Mathf.Clamp(composer.Damping.z, 0f, 20f);
                layer.LookaheadTime = composer.Lookahead.Enabled ? Mathf.Clamp01(composer.Lookahead.Time) : 0f;
                var dead = composer.Composition.DeadZone; layer.DeadZoneWidth = dead.Enabled ? Mathf.Clamp(dead.Size.x, 0f, 2f) : 0f; layer.DeadZoneHeight = dead.Enabled ? Mathf.Clamp(dead.Size.y, 0f, 2f) : 0f;
                var hard = composer.Composition.HardLimits; layer.SoftZoneWidth = hard.Enabled ? Mathf.Clamp(hard.Size.x, 0f, 2f) : 0f; layer.SoftZoneHeight = hard.Enabled ? Mathf.Clamp(hard.Size.y, 0f, 2f) : 0f;
            }
            else if (component is CinemachineFramingTransposer old)
            {
                layer.Distance = Mathf.Clamp(old.m_CameraDistance, .1f, 1000f);
                layer.ScreenX = Mathf.Clamp(old.m_ScreenX - .5f, -.5f, .5f); layer.ScreenY = Mathf.Clamp(old.m_ScreenY - .5f, -.5f, .5f);
                layer.DampingX = Mathf.Clamp(old.m_XDamping, 0f, 20f); layer.DampingY = Mathf.Clamp(old.m_YDamping, 0f, 20f); layer.DampingZ = Mathf.Clamp(old.m_ZDamping, 0f, 20f);
                layer.LookaheadTime = Mathf.Clamp01(old.m_LookaheadTime);
                layer.DeadZoneWidth = Mathf.Clamp(old.m_DeadZoneWidth, 0f, 2f); layer.DeadZoneHeight = Mathf.Clamp(old.m_DeadZoneHeight, 0f, 2f);
                layer.SoftZoneWidth = old.m_UnlimitedSoftZone ? 0f : Mathf.Clamp(old.m_SoftZoneWidth, 0f, 2f); layer.SoftZoneHeight = old.m_UnlimitedSoftZone ? 0f : Mathf.Clamp(old.m_SoftZoneHeight, 0f, 2f);
            }
        }
        return layer;
    }
}
#endif
