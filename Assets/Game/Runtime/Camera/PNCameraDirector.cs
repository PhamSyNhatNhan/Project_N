#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Camera render settings captured from a world's own camera and applied to the output when that world is viewed.
public sealed class PNRenderPreset
{
    public CameraClearFlags ClearFlags;
    public Color Background;
    public int CullingMask, VolumeLayerMask;
    public bool AllowHdr, AllowMsaa, PostProcessing;
    public float Near, Far;
    public AntialiasingMode Antialiasing;
    public Transform VolumeTrigger;
    public int RendererIndex;
    public static PNRenderPreset Capture(Camera camera)
    {
        var data = camera.GetUniversalAdditionalCameraData();
        var preset = new PNRenderPreset
        {
            ClearFlags = camera.clearFlags, Background = camera.backgroundColor, CullingMask = camera.cullingMask, AllowHdr = camera.allowHDR, AllowMsaa = camera.allowMSAA,
            Near = camera.nearClipPlane, Far = camera.farClipPlane, PostProcessing = data.renderPostProcessing, Antialiasing = data.antialiasing,
            VolumeLayerMask = data.volumeLayerMask.value, VolumeTrigger = data.volumeTrigger, RendererIndex = -1
        };
        var pipeline = UniversalRenderPipeline.asset;
        if (pipeline != null)
            for (int index = 0; index < pipeline.rendererDataList.Length; index++)
                if (pipeline.GetRenderer(index) == data.scriptableRenderer) { preset.RendererIndex = index; break; }
        return preset;
    }
    // Bits owned by other systems (combat UI layer, popup bloom volume layer) are kept as they are on the output.
    public void Apply(Camera output, int preservedCullingBits, int preservedVolumeBits)
    {
        var data = output.GetUniversalAdditionalCameraData();
        output.clearFlags = ClearFlags; output.backgroundColor = Background; output.allowHDR = AllowHdr; output.allowMSAA = AllowMsaa;
        output.nearClipPlane = Near; output.farClipPlane = Far;
        output.cullingMask = (CullingMask & ~preservedCullingBits) | (output.cullingMask & preservedCullingBits);
        data.renderPostProcessing = PostProcessing; data.antialiasing = Antialiasing;
        data.volumeLayerMask = VolumeLayerMask | preservedVolumeBits; data.volumeTrigger = VolumeTrigger;
        if (RendererIndex >= 0) data.SetRenderer(RendererIndex);
    }
}

// Owns the one rendering camera of a play session (Camera + Brain + AudioListener, tagged MainCamera). World cameras are claimed from
// PNWorldPresentation; only the viewed world's virtual cameras are admitted to the output through a reserved Brain channel.
public sealed partial class PNCameraDirector : MonoBehaviour, IPNViewCameraService, IPNCameraService
{
    public const string LegacyFlag = "--pn-legacy-camera";
    public static PNCameraDirector Current { get; private set; }
    public static bool LegacyRequested => Environment.GetCommandLineArgs().Contains(LegacyFlag);

    private sealed class WorldCamera
    {
        public Camera Camera; public PNPresentationClaim Claim; public EveWorldContext World; public PNRenderPreset Preset;
        public PNCameraProjection Projection; public CinemachineExternalCamera External; public bool ExternalAdded;
    }
    private sealed class Admission { public CinemachineVirtualCameraBase Vcam; public OutputChannels Channel; public Transform Follow, LookAt; public bool Owned; }

    private EveSessionContext session;
    private PNWorldRuntime worlds;
    private PNLocalWorldContext local;
    private PNWorldPresentation presentation;
    private Camera output;
    private CinemachineBrain brain;
    private UniversalAdditionalCameraData outputData;
    private EveEventSubscription binding;
    private readonly List<WorldCamera> cameras = new List<WorldCamera>();
    private readonly Dictionary<CinemachineVirtualCameraBase, Admission> admitted = new Dictionary<CinemachineVirtualCameraBase, Admission>();
    private readonly Dictionary<Behaviour, bool> suppressed = new Dictionary<Behaviour, bool>();
    private GameObject fallbackObject;
    private CinemachineCamera fallback;
    private OutputChannels reserved;
    private EveWorldContext viewed;
    private PNCameraProjection projection = PNCameraProjection.Perspective;
    private bool brainConfigured, busy, released;
    private int uiBits;
    private bool covered;
    private int coveredMask;
    // ---- shake: director-owned impulse events on a private channel; legacy/other impulses (channel 1) are never touched ----
    public const int ShakeChannel = 1 << 12;
    private sealed class ShakeRecord { public CinemachineImpulseManager.ImpulseEvent Event; public float StartTime; public Guid World; public PNCameraClock Clock; public float Age, Duration; }
    private readonly PNCameraRequestStack<ShakeRecord> shakes = new PNCameraRequestStack<ShakeRecord>(32);
    private readonly Dictionary<CinemachineVirtualCameraBase, CinemachineImpulseListener> listeners = new Dictionary<CinemachineVirtualCameraBase, CinemachineImpulseListener>();
    private float playerShakeGain = 1f;
    private EveEntityLoader lastActor;
    public bool Available => !released && output != null;
    public float PlayerShakeGain { get => playerShakeGain; set => playerShakeGain = float.IsNaN(value) ? 1f : Mathf.Clamp(value, 0f, 2f); }
    public int ActiveShakes { get { shakes.ReleaseWhere(e => IsExpired(e.Payload) || e.Payload.Age >= e.Payload.Duration, PNCameraHandleEnd.Completed); return shakes.Count; } }

    public Camera ViewCamera => output;
    public CinemachineBrain Brain => brain;
    public OutputChannels ReservedChannel => reserved;
    public EveWorldContext ViewedWorld => viewed;
    public PNCameraProjection Projection => projection;
    public event Action Updated;
    public PNCameraOrientation Orientation
    {
        get { if (output == null) return default; var euler = output.transform.rotation.eulerAngles; return new PNCameraOrientation(euler.x, euler.y, YawOverridden); }
    }

    public static PNCameraDirector Attach(GameObject host, EveSessionContext session, PNWorldRuntime worlds, PNLocalWorldContext local, PNWorldPresentation presentation)
    {
        var director = host.GetComponent<PNCameraDirector>() ?? host.AddComponent<PNCameraDirector>();
        director.Bind(session, worlds, local, presentation);
        return director;
    }

    public void Bind(EveSessionContext value, PNWorldRuntime runtime, PNLocalWorldContext context, PNWorldPresentation view)
    {
        Release();
        released = false; session = value; worlds = runtime; local = context; presentation = view;
        Current = this; PNViewCamera.Register(this); PNCameraServices.Register(value, this);
        shakes.OwnerAlive = owner => !(owner is UnityEngine.Object unityOwner) || unityOwner != null;
        CreateOutput();
        reserved = ChooseReserved(0);
        ApplyReserved();
        presentation.RootScanned += OnRootScanned;
        worlds.WorldUnloading += OnWorldUnloading;
        CinemachineCore.CameraUpdatedEvent.AddListener(OnBrainUpdated);
        binding = EveEventManager.Global.Subscribe<PNLocalWorldBound>(PNLocalWorldBound.Event, (_, notice) =>
        {
            if (released || local == null || notice.Session != local.Session.SessionId || notice.Selection != local.SelectionRevision) return;
            Rebind();
        });
        value.RegisterCleanup(Release);
    }

    private void CreateOutput()
    {
        var go = new GameObject("PN Camera Output");
        go.transform.SetParent(transform, false);
        go.tag = "MainCamera";
        output = go.AddComponent<Camera>();
        output.clearFlags = CameraClearFlags.SolidColor; output.backgroundColor = Color.black;
        outputData = go.AddComponent<UniversalAdditionalCameraData>();
        brain = go.AddComponent<CinemachineBrain>();
        go.AddComponent<AudioListener>();
    }

    // ---- claim of world cameras -------------------------------------------------------------------------------------------------

    private void OnRootScanned(GameObject root)
    {
        if (released || busy || root == null) return;
        busy = true;
        try
        {
            SuppressLegacyDrivers(root);
            foreach (var camera in root.GetComponentsInChildren<Camera>(true)) TryClaim(camera);
            if (viewed != null && local?.World == viewed) Refresh(false);
        }
        catch (Exception error) { Debug.LogException(error); }
        finally { busy = false; }
    }

    private bool Eligible(Camera camera)
    {
        if (camera == null || camera == output || !camera.CompareTag("MainCamera") || camera.targetTexture != null) return false;
        if (cameras.Exists(x => x.Camera == camera)) return false;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        return data == null || data.renderType == CameraRenderType.Base;
    }

    private void TryClaim(Camera camera)
    {
        if (!Eligible(camera)) return;
        var world = presentation.WorldOf(camera);
        if (world == null) return;
        var group = new List<Component> { camera };
        var cameraBrain = camera.GetComponent<CinemachineBrain>(); if (cameraBrain != null) group.Add(cameraBrain);
        var listener = camera.GetComponent<AudioListener>(); if (listener != null) group.Add(listener);
        var record = new WorldCamera { Camera = camera, World = world, Projection = camera.orthographic ? PNCameraProjection.Orthographic : PNCameraProjection.Perspective };
        record.Preset = PNRenderPreset.Capture(camera);
        if (!brainConfigured && cameraBrain != null) CopyBrainSettings(cameraBrain);
        record.Claim = presentation.Claim(group.ToArray());
        try { foreach (var component in group) ((Behaviour)component).enabled = false; }
        catch { record.Claim.Release(); throw; }
        cameras.Add(record);
        if (cameras.Count == 1 || world == worlds.DefaultWorld) ApplyPreset(record.Preset);
    }

    private void CopyBrainSettings(CinemachineBrain source)
    {
        brainConfigured = true;
        brain.UpdateMethod = source.UpdateMethod; brain.BlendUpdateMethod = source.BlendUpdateMethod; brain.IgnoreTimeScale = source.IgnoreTimeScale;
        brain.DefaultBlend = source.DefaultBlend; brain.WorldUpOverride = source.WorldUpOverride;
    }

    private void SuppressLegacyDrivers(GameObject root)
    {
        // The pre-director CameraManager follows and shakes through its own virtual-camera search; with a director it would be a second writer.
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour != null && behaviour.enabled && behaviour.GetType().Name == "CameraManager" && !suppressed.ContainsKey(behaviour))
            { suppressed.Add(behaviour, behaviour.enabled); behaviour.enabled = false; }
    }

    private void OnWorldUnloading(EveWorldContext world)
    {
        if (released) return;
        for (int i = cameras.Count - 1; i >= 0; i--)
        {
            if (cameras[i].World != world) continue;
            DropExternal(cameras[i]); cameras[i].Claim.Release(); cameras.RemoveAt(i);
        }
        foreach (var vcam in admitted.Keys.Where(x => x == null || WorldOfVcam(x) == world).ToArray()) Unadmit(vcam);
        if (viewed == world) viewed = null;
    }

    // ---- reserved channel -----------------------------------------------------------------------------------------------------

    // Loaded scenes plus every world's own scene (world scenes are not always visible to a scene-wide find).
    private IEnumerable<CinemachineVirtualCameraBase> AllVirtualCameras()
    {
        var seen = new HashSet<CinemachineVirtualCameraBase>();
        foreach (var vcam in FindObjectsByType<CinemachineVirtualCameraBase>(FindObjectsInactive.Include)) if (seen.Add(vcam)) yield return vcam;
        if (session == null) yield break;
        foreach (var world in session.Worlds.Snapshot())
        {
            if (!world.PhysicsOwner.IsValid() || !world.PhysicsOwner.isLoaded) continue;
            foreach (var root in world.PhysicsOwner.GetRootGameObjects())
                foreach (var vcam in root.GetComponentsInChildren<CinemachineVirtualCameraBase>(true)) if (seen.Add(vcam)) yield return vcam;
        }
    }

    private OutputChannels ChooseReserved(OutputChannels exclude)
    {
        int used = (int)exclude;
        foreach (var vcam in AllVirtualCameras())
            if (vcam != null && !admitted.ContainsKey(vcam)) used |= (int)vcam.OutputChannel;
        for (int bit = 15; bit >= 1; bit--) if ((used & (1 << bit)) == 0) return (OutputChannels)(1 << bit);
        Debug.LogError("PNCameraDirector: no free Cinemachine output channel; the camera output cannot isolate worlds.");
        return 0;
    }

    private void ApplyReserved()
    {
        brain.ChannelMask = reserved;
        foreach (var admission in admitted.Values) if (admission.Vcam != null) admission.Vcam.OutputChannel = reserved;
    }

    // A vcam outside the admitted set carrying the reserved bit (e.g. a mod added it later) triggers a new reserved channel.
    private void SweepCollisions()
    {
        if (reserved == 0) return;
        foreach (var vcam in AllVirtualCameras())
        {
            if (vcam == null || admitted.ContainsKey(vcam) || ((int)vcam.OutputChannel & (int)reserved) == 0) continue;
            reserved = ChooseReserved(reserved); ApplyReserved(); return;
        }
    }

    // ---- binding ------------------------------------------------------------------------------------------------------------------

    private void Rebind() => Refresh(true);

    private void Refresh(bool fromViewChange)
    {
        if (released || output == null) return;
        var target = local.World;
        bool cut = !ReferenceEquals(viewed, target);
        if (cut && viewed != null) { ForgetWorldZoom(viewed.InstanceId); ForgetWorldExtras(viewed.InstanceId); lockOns.Clear(PNCameraHandleEnd.WorldChanged); StopLockOn(); cinematics.Clear(PNCameraHandleEnd.WorldChanged); StopCinematic(true); }
        if (cut || !ReferenceEquals(lastActor, local.Actor)) CancelAllShakes(PNCameraHandleEnd.WorldChanged);
        lastActor = local.Actor;
        foreach (var vcam in admitted.Keys.ToArray()) if (vcam == null || target == null || vcam != lockRig && vcam != cinematicRig && WorldOfVcam(vcam) != target) Unadmit(vcam);
        viewed = target;
        if (target == null) return;
        SweepCollisions();
        if (reserved == 0) return;
        var actor = local.Actor != null ? local.Actor.transform : null;
        var rigs = RigsOf(target);
        if (rigs.Count == 0) rigs = ExternalRigsOf(target);
        if (rigs.Count == 0) rigs = FallbackRig(target, actor);
        foreach (var vcam in rigs) Admit(vcam, actor);
        var record = cameras.Find(x => x.World == target) ?? cameras.Find(x => x.World == worlds.DefaultWorld) ?? cameras.FirstOrDefault();
        if (cut && record != null) ApplyPreset(record.Preset);
        projection = cameras.Find(x => x.World == target)?.Projection ?? record?.Projection ?? PNCameraProjection.Perspective;
        output.orthographic = projection == PNCameraProjection.Orthographic;
        if (cut) brain.ResetState();
    }

    private void Admit(CinemachineVirtualCameraBase vcam, Transform actor)
    {
        if (vcam == null) return;
        if (!admitted.TryGetValue(vcam, out var admission))
        {
            admission = new Admission { Vcam = vcam, Channel = vcam.OutputChannel, Follow = vcam.Follow, LookAt = vcam.LookAt, Owned = vcam == fallback };
            admitted.Add(vcam, admission);
        }
        vcam.OutputChannel = reserved; EnsureListener(vcam);
        if (vcam != lockWorldRig && (actor != null || !admission.Owned)) { vcam.Follow = actor; vcam.LookAt = actor; }
    }

    private void Unadmit(CinemachineVirtualCameraBase vcam)
    {
        if (vcam == null) { admitted.Remove(vcam); listeners.Remove(vcam); return; }
        if (!admitted.TryGetValue(vcam, out var admission)) return;
        admitted.Remove(vcam); DropListener(vcam); DropRigZoom(vcam);
        try { vcam.OutputChannel = admission.Channel; if (!admission.Owned) { vcam.Follow = admission.Follow; vcam.LookAt = admission.LookAt; } }
        catch (Exception error) { Debug.LogException(error); }
    }

    private EveWorldContext WorldOfVcam(CinemachineVirtualCameraBase vcam) => vcam == null ? null : presentation.WorldOf(vcam);

    private List<CinemachineVirtualCameraBase> RigsOf(EveWorldContext world)
    {
        var result = new List<CinemachineVirtualCameraBase>();
        if (!world.PhysicsOwner.IsValid()) return result;
        foreach (var root in world.PhysicsOwner.GetRootGameObjects())
            foreach (var vcam in root.GetComponentsInChildren<CinemachineVirtualCameraBase>(true))
                if (vcam != null && vcam != fallback && !(vcam is CinemachineExternalCamera && !cameras.Exists(x => x.External == vcam))) result.Add(vcam);
        return result;
    }

    // A world with only a plain Camera (e.g. a mod world) is read through an ExternalCamera on that camera (it works on disabled cameras).
    private List<CinemachineVirtualCameraBase> ExternalRigsOf(EveWorldContext world)
    {
        var result = new List<CinemachineVirtualCameraBase>();
        var record = cameras.Find(x => x.World == world && x.Camera != null);
        if (record == null) return result;
        if (record.External == null)
        {
            record.External = record.Camera.GetComponent<CinemachineExternalCamera>();
            if (record.External == null) { record.External = record.Camera.gameObject.AddComponent<CinemachineExternalCamera>(); record.ExternalAdded = true; record.External.Priority.Value = 10; }
        }
        result.Add(record.External);
        return result;
    }

    private List<CinemachineVirtualCameraBase> FallbackRig(EveWorldContext world, Transform actor)
    {
        if (fallback == null)
        {
            fallbackObject = new GameObject("PN Camera Fallback Rig");
            fallbackObject.transform.SetParent(transform, false);
            fallbackObject.transform.SetPositionAndRotation(output.transform.position, output.transform.rotation);
            fallback = fallbackObject.AddComponent<CinemachineCamera>();
            fallback.Priority.Value = 10;
            fallback.Lens = LensSettings.FromCamera(output);
            fallback.Lens.ModeOverride = output.orthographic ? LensSettings.OverrideModes.Orthographic : LensSettings.OverrideModes.Perspective;
            var composer = fallbackObject.AddComponent<CinemachinePositionComposer>();
            composer.CameraDistance = actor != null ? Mathf.Max(1f, Vector3.Distance(output.transform.position, actor.position)) : 10f;
        }
        return new List<CinemachineVirtualCameraBase> { fallback };
    }

    private void DropExternal(WorldCamera record)
    {
        if (record.External == null) return;
        Unadmit(record.External);
        if (record.ExternalAdded) Kill(record.External);
        record.External = null; record.ExternalAdded = false;
    }

    // Transition mode: while an opaque transition screen fully covers the view the output renders nothing (the UI overlay camera still draws).
    public bool TransitionCovered => covered;
    public void SetTransitionCover(bool on)
    {
        if (output == null || covered == on) return;
        covered = on;
        if (on) { coveredMask = output.cullingMask; output.cullingMask = 0; }
        else output.cullingMask = coveredMask;
    }

    private void ApplyPreset(PNRenderPreset preset)
    {
        if (preset == null || output == null) return;
        int ui = 0; var uiRuntime = PNGameBootstrap.Runtime != null ? PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>() : null;
        if (uiRuntime != null) ui = 1 << uiRuntime.WorldLayer;
        preset.Apply(output, ui, PNDamagePopupBloom.Exists ? PNDamagePopupBloom.VolumeBit : 0);
        if (covered) { coveredMask = output.cullingMask; output.cullingMask = 0; }
        if (PNDamagePopupBloom.Exists) PNDamagePopupBloom.Ensure(output);
    }

    // Runs right after Brain wrote the output pose/lens: set the projection flag Brain does not manage, then notify camera-dependent systems.
    private void OnBrainUpdated(CinemachineBrain updated)
    {
        if (released || updated != brain) return;
        if (shakes.Count > 0) shakes.ReleaseWhere(e => IsExpired(e.Payload), PNCameraHandleEnd.Completed);
        output.orthographic = projection == PNCameraProjection.Orthographic;
        try { Updated?.Invoke(); } catch (Exception error) { Debug.LogException(error); }
        PNViewCamera.RaiseUpdated();
    }


    // ---- shake API ---------------------------------------------------------------------------------------------------------------

    public IPNCameraHandle RequestShake(UnityEngine.Object source, string key, PNShakeSpec spec)
    {
        if (released || output == null || viewed == null) return new PNRefusedCameraHandle(PNCameraRefusal.NoCamera);
        if (source == null) return new PNRefusedCameraHandle(PNCameraRefusal.OwnerGone);
        var component = source as Component ?? (source as GameObject)?.transform;
        if (component == null || local?.World == null || !local.Contains(component)) return new PNRefusedCameraHandle(PNCameraRefusal.OtherWorld);
        if (string.IsNullOrEmpty(key) || key.Length > 128 || !(spec.Amplitude > 0f)) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        // Distance falloff is evaluated once, from the source to the viewed actor (or the camera when there is no actor).
        var focus = local.Actor != null ? local.Actor.transform.position : output.transform.position;
        float attenuation = spec.Attenuation(Vector3.Distance(component.transform.position, focus));
        if (attenuation <= 0f) return new PNRefusedCameraHandle(PNCameraRefusal.OutOfRange);
        var definition = new CinemachineImpulseDefinition
        {
            ImpulseChannel = ShakeChannel, ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform, ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Custom,
            CustomImpulseShape = WaveCurve(spec), ImpulseDuration = spec.Duration, DissipationDistance = 1f
        };
        // The uniform impulse signal is velocity x curve: peak amplitude (world units) x combined gain x falloff along the requested direction.
        var direction = new Vector3(spec.DirectionX, spec.DirectionY, spec.DirectionZ).normalized;
        var impulse = definition.CreateAndReturnEvent(output.transform.position, direction * (spec.Amplitude * Profile.ShakeGain * playerShakeGain * attenuation));
        if (impulse == null) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        // Linear fade shapes (without them Cinemachine damps the attack/decay exponentially, which is not what fadeIn/fadeOut mean).
        impulse.Envelope.AttackShape = AnimationCurve.Linear(0f, 0f, 1f, 1f); impulse.Envelope.DecayShape = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        impulse.Envelope.AttackTime = spec.FadeIn; impulse.Envelope.SustainTime = Mathf.Max(0f, spec.Duration - spec.FadeIn - spec.FadeOut); impulse.Envelope.DecayTime = spec.FadeOut;
        var record = new ShakeRecord { Event = impulse, StartTime = impulse.StartTime, World = viewed.InstanceId, Clock = spec.Clock, Duration = spec.Duration };
        foreach (var existing in shakes.Entries.ToArray()) if (ReferenceEquals(existing.Owner, source) && existing.Key == key) CancelRecord(existing.Payload);
        try
        {
            var handle = shakes.Request(source, key, 0, viewed.InstanceId, record);
            handle.Ended += _ => CancelRecord(record);
            return handle;
        }
        catch (InvalidOperationException) { CancelRecord(record); return new PNRefusedCameraHandle(PNCameraRefusal.LimitReached); }
    }

    // The requested waveform sampled over the shake duration (at least 8 samples per cycle, bounded at 256 keys).
    private static AnimationCurve WaveCurve(PNShakeSpec spec)
    {
        int samples = Mathf.Clamp(Mathf.CeilToInt(spec.Frequency * spec.Duration * (spec.Shape == PNShakeShape.Rumble ? 24f : 8f)), 16, 256);
        var keys = new Keyframe[samples + 1];
        for (int i = 0; i <= samples; i++) { float t = i / (float)samples; keys[i] = new Keyframe(t, spec.Wave(t)); }
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }

    private bool IsExpired(ShakeRecord record) => record.Event == null || record.Event.StartTime != record.StartTime || record.Event.Expired;

    // Cancels only the event this director created. A recycled event object (StartTime differs) belongs to someone else and is left alone.
    private void CancelRecord(ShakeRecord record)
    {
        var impulse = record.Event; record.Event = null;
        if (impulse != null && impulse.StartTime == record.StartTime && !impulse.Expired) impulse.Cancel(CinemachineCore.CurrentTime, true);
    }

    private void CancelAllShakes(PNCameraHandleEnd reason)
    {
        foreach (var entry in shakes.Entries.ToArray()) CancelRecord(entry.Payload);
        shakes.Clear(reason);
    }

    // Ages every shake on its own clock and ends it when its duration has run. (Cinemachine only reports an impulse event as expired about one
    // second after its envelope for uniform impulses, so the handle does not wait for that.) Real-clock shakes keep running while time scale is
    // below 1: Cinemachine's impulse clock is scaled game time, so each frame the event start is moved back by the difference between real and
    // scaled time.
    private void AdvanceShakes(float scaledDelta, float unscaledDelta)
    {
        if (shakes.Count == 0) return;
        float extra = unscaledDelta - scaledDelta;
        foreach (var entry in shakes.Entries)
        {
            var record = entry.Payload;
            record.Age += Age(record.Clock, scaledDelta, unscaledDelta);
            if (record.Clock != PNCameraClock.Real || extra <= 0f || record.Event == null || record.Event.StartTime != record.StartTime || record.Event.Expired) continue;
            record.Event.StartTime -= extra; record.StartTime = record.Event.StartTime;
        }
        shakes.ReleaseWhere(e => e.Payload.Age >= e.Payload.Duration, PNCameraHandleEnd.Completed);
    }

    private void EnsureListener(CinemachineVirtualCameraBase vcam)
    {
        if (vcam == null || listeners.ContainsKey(vcam)) return;
        var listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();
        listener.ApplyAfter = CinemachineCore.Stage.Noise; listener.ChannelMask = ShakeChannel; listener.Gain = 1f; listener.Use2DDistance = false; listener.UseCameraSpace = true;
        listener.SignalCombinationMode = CinemachineImpulseListener.SignalCombinationModes.Additive;
        listener.ReactionSettings = new CinemachineImpulseListener.ImpulseReaction { AmplitudeGain = 1f, FrequencyGain = 1f, Duration = 1f };
        listeners[vcam] = listener;
    }

    private void DropListener(CinemachineVirtualCameraBase vcam)
    {
        if (vcam != null && listeners.TryGetValue(vcam, out var listener)) { listeners.Remove(vcam); if (listener != null) Kill(listener); }
    }

    // ---- teardown -----------------------------------------------------------------------------------------------------------------

    public void Release()
    {
        if (released) return;
        released = true;
        CinemachineCore.CameraUpdatedEvent.RemoveListener(OnBrainUpdated);
        binding?.Dispose(); binding = null;
        if (presentation != null) presentation.RootScanned -= OnRootScanned;
        if (worlds != null) worlds.WorldUnloading -= OnWorldUnloading;
        foreach (var vcam in admitted.Keys.ToArray()) Unadmit(vcam);
        foreach (var record in cameras) { DropExternal(record); record.Claim?.Release(); }
        cameras.Clear();
        foreach (var pair in suppressed) if (pair.Key != null) pair.Key.enabled = pair.Value;
        suppressed.Clear();
        if (fallbackObject != null) Kill(fallbackObject); fallbackObject = null; fallback = null;
        ReleaseExtrasSession(); ReleaseCinematicSession(); ReleaseLockOnSession(); zooms.Clear(); foreach (var vcam in rigZooms.Keys.ToArray()) DropRigZoom(vcam);
        CancelAllShakes(PNCameraHandleEnd.SessionEnded); foreach (var pair in listeners) if (pair.Value != null) Kill(pair.Value); listeners.Clear();
        PNViewCamera.Unregister(this); PNCameraServices.Unregister(this);
        if (ReferenceEquals(Current, this)) Current = null;
        if (output != null) Kill(output.gameObject);
        covered = false; output = null; brain = null; outputData = null; viewed = null;
        session = null; worlds = null; local = null; presentation = null;
        Updated = null;
    }
    private static void Kill(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    private void OnDestroy() => Release();
}
#endif
