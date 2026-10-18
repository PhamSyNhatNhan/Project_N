#if UNITY_EDITOR
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Unity.Cinemachine;
using UnityEngine;

// Native (edit mode, Brain ManualUpdate, director.Advance by hand) checks for the camera extras: state snapshot, real-unit zoom, clocks, handle
// events, cinematic sequence/path/aim and blend styles, bounds, follow tuning and orbit. Not rendered-frame acceptance.
public static class PNCameraExtrasChecks
{
    public static string Run()
    {
        var rows = new List<string>(); void Check(bool value, string label) { if (!value) throw new Exception("FAIL " + label); rows.Add("PASS " + label); }
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "fixture:camera-extras"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile()); var worlds = new PNWorldRuntime(session, scheduler);
        var local = new PNLocalWorldContext(session); var host = new GameObject("Camera extras fixture service"); var view = host.AddComponent<PNWorldPresentation>(); view.Bind(session, worlds, local);
        void Pump(Func<bool> ready) { for (int i = 0; i < 1000 && !ready(); i++) scheduler.AdvanceFrame(); if (!ready()) throw new Exception("Extras fixture did not drain."); }
        PNCameraDirector director = null; int frame = 0;
        void Run(int frames, float scaled = .02f, float unscaled = -1f) { for (int i = 0; i < frames; i++) { director.Advance(scaled, unscaled); frame++; director.Brain.ManualUpdate(frame, .02f); } }
        try
        {
            var composerWorld = worlds.Create(EveId.Parse("fixture:world")); var poseWorld = worlds.Create(EveId.Parse("fixture:world")); var orthoWorld = worlds.Create(EveId.Parse("fixture:world")); var emptyWorld = worlds.Create(EveId.Parse("fixture:world"));
            Pump(() => composerWorld.CanExecute && poseWorld.CanExecute && orthoWorld.CanExecute && emptyWorld.CanExecute);
            GameObject Root(EveWorldContext w) => worlds.Get(w).Slots[0].Roots[0];
            var cr = Root(composerWorld); var pr = Root(poseWorld); var or = Root(orthoWorld); var er = Root(emptyWorld);
            var cam1 = cr.AddComponent<Camera>(); cam1.tag = "MainCamera"; cam1.fieldOfView = 38;
            var composerVcam = new GameObject("composer vcam").AddComponent<CinemachineCamera>(); composerVcam.transform.SetParent(cr.transform); composerVcam.Priority.Value = 10;
            composerVcam.transform.rotation = Quaternion.Euler(45f, 0f, 0f); composerVcam.transform.position = new Vector3(0, 3, -3);
            composerVcam.Lens.FieldOfView = 38; composerVcam.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            var composer = composerVcam.gameObject.AddComponent<CinemachinePositionComposer>(); composer.CameraDistance = 4.5f;
            var cam2 = pr.AddComponent<Camera>(); cam2.tag = "MainCamera"; cam2.fieldOfView = 38; pr.transform.position = new Vector3(100, 0, 0);
            var poseVcam = new GameObject("pose vcam").AddComponent<CinemachineCamera>(); poseVcam.transform.SetParent(pr.transform); poseVcam.Priority.Value = 10; poseVcam.Lens.FieldOfView = 38; poseVcam.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            var cam3 = or.AddComponent<Camera>(); cam3.tag = "MainCamera"; cam3.orthographic = true; cam3.orthographicSize = 2; or.transform.position = new Vector3(200, 0, 0);
            var prop = new GameObject("prop"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(prop, composerWorld.PhysicsOwner); prop.transform.position = new Vector3(5, 0, 6);
            view.Register(cr); view.Register(pr); view.Register(or); view.Register(er); local.Select(composerWorld);
            director = PNCameraDirector.Attach(host, session, worlds, local, view);
            view.Register(cr); view.Register(pr); view.Register(or); view.Register(er); local.Select(null); local.Select(composerWorld);
            director.Brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            var service = (IPNCameraService)director; var owner = new GameObject("owner"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner, composerWorld.PhysicsOwner);
            var output = director.ViewCamera;
            Run(10);

            // ---- state snapshot ------------------------------------------------------------------------------------------------------------
            var state = service.State;
            Check(state.Available && state.Rig == PNCameraRigKind.World && state.Mode == PNCameraMode.Normal && (state.Capabilities & PNCameraRigCapabilities.DistanceZoom) != 0 && !state.Orthographic && state.ViewedWorld == composerWorld.InstanceId, "state: composer world reports its rig kind, mode, capabilities and world");
            Check(Vector3.Distance(state.Position, output.transform.position) < .001f && Mathf.Abs(state.FieldOfView - output.fieldOfView) < .001f && !state.CinematicActive && !state.LockOnActive && !state.Blending && !state.YawOverridden, "state: pose, lens and activity flags match the view camera");
            local.Select(orthoWorld); Run(10);
            state = service.State;
            Check(state.Rig == PNCameraRigKind.External && state.Orthographic && Mathf.Abs(state.OrthographicSize - 2f) < .01f, "state: a camera-only orthographic world reports an external rig and its size");
            local.Select(emptyWorld); Run(10);
            Check(service.State.Rig == PNCameraRigKind.Fallback, "state: a world without a rig reports the fallback rig");
            local.Select(null); Run(3);
            Check(service.State.Mode == PNCameraMode.Background && service.State.ViewedWorld == Guid.Empty, "state: without a local view the mode is Background");
            Check(!PNNullCameraService.Instance.State.Available && PNNullCameraService.Instance.State.Mode == PNCameraMode.None, "state: the empty service reports no camera");
            local.Select(composerWorld); Run(10);

            // ---- real-unit zoom ------------------------------------------------------------------------------------------------------------
            var toDistance = service.RequestZoom(owner.transform, "dist", PNZoomRequest.ToDistance(3.6f)); Run(250);
            Check(Mathf.Abs(composer.CameraDistance - 3.6f) < .02f, "zoom to 3.6 distance units sets the composer distance");
            var farDistance = service.RequestZoom(owner.transform, "far", PNZoomRequest.ToDistance(100f)); Run(250);
            Check(Mathf.Abs(composer.CameraDistance - 7f) < .02f, "a distance beyond the profile range is clamped to the maximum");
            farDistance.Dispose(); toDistance.Dispose(); Run(300);
            Check(Mathf.Abs(composer.CameraDistance - 4.5f) < .01f, "releasing distance requests returns the authored distance");
            float fovStill = output.fieldOfView;
            var toFov = service.RequestZoom(owner.transform, "fov", PNZoomRequest.ToFieldOfView(32f)); Run(250);
            Check(Mathf.Abs(composer.CameraDistance - 4.5f) < .01f && output.fieldOfView < fovStill - 2f, "a field-of-view request on a composer rig zooms the lens and leaves the distance alone");
            toFov.Dispose(); Run(300);
            Check(Mathf.Abs(output.fieldOfView - fovStill) < .2f, "releasing the field-of-view request returns the authored lens");
            Check(service.RequestZoom(owner.transform, "ortho", PNZoomRequest.ToOrthographicSize(2f)).RefusalReason == PNCameraRefusal.NotSupported, "an orthographic-size request on a perspective view is refused with NotSupported");
            local.Select(poseWorld); Run(20);
            Check(service.RequestZoom(owner.transform, "dist2", PNZoomRequest.ToDistance(3f)).RefusalReason == PNCameraRefusal.NotSupported, "a distance request on a pose-only rig is refused with NotSupported");
            float poseFov = output.fieldOfView;
            var poseZoom = service.RequestZoom(owner.transform, "fov2", PNZoomRequest.ToFieldOfView(31f)); Run(250);
            Check(Mathf.Abs(output.fieldOfView - Mathf.Max(31f, poseFov * PNCameraProfile.Default.LensScaleMin)) < .3f, "a field-of-view request zooms a pose-only rig's lens (limited by the lens range)");
            poseZoom.Dispose(); Run(300);
            local.Select(orthoWorld); Run(20);
            Check(service.RequestZoom(owner.transform, "fov3", PNZoomRequest.ToFieldOfView(30f)).RefusalReason == PNCameraRefusal.NotSupported, "a field-of-view request on an orthographic view is refused");
            var orthoZoom = service.RequestZoom(owner.transform, "os", PNZoomRequest.ToOrthographicSize(1.7f)); Run(250);
            Check(Mathf.Abs(output.orthographicSize - 1.7f) < .05f, "an orthographic-size request sets the orthographic size");
            orthoZoom.Dispose(); Run(300);
            local.Select(composerWorld); Run(20);

            // ---- clocks ------------------------------------------------------------------------------------------------------------------------------
            var realTimed = service.RequestZoom(owner.transform, "real", PNZoomRequest.Factor(.9f, .3f, .05f, 0, PNCameraEase.Linear, PNCameraClock.Real));
            var cameraTimed = service.RequestZoom(owner.transform, "cam", PNZoomRequest.Factor(.9f, .3f, .05f, 0, PNCameraEase.Linear, PNCameraClock.Camera));
            Run(30, 0f, .02f);   // time scale 0: scaled delta 0, real delta 0.02
            Check(!realTimed.IsActive && realTimed.EndReason == PNCameraHandleEnd.Completed && cameraTimed.IsActive, "at time scale 0 a real-clock request runs out while a camera-clock one waits");
            Run(30, .02f, .02f);
            Check(!cameraTimed.IsActive && cameraTimed.EndReason == PNCameraHandleEnd.Completed, "the camera-clock request runs out once the camera clock moves");

            // ---- handle events --------------------------------------------------------------------------------------------------------------------
            var events = new List<string>();
            var evZoom = service.RequestZoom(owner.transform, "ev", PNZoomRequest.Factor(.95f)); evZoom.Ended += r => events.Add("zoom:" + r);
            evZoom.Dispose();
            var evZoom2 = service.RequestZoom(owner.transform, "ev2", PNZoomRequest.Factor(.95f)); evZoom2.Ended += r => events.Add("zoom2:" + r);
            local.Select(poseWorld); Run(5);
            Check(events.SequenceEqual(new[] { "zoom:Released", "zoom2:WorldChanged" }), "zoom handles report Released and WorldChanged");
            local.Select(composerWorld); Run(10);

            // ---- cinematic sequence, events, blend styles ------------------------------------------------------------------------------------------------
            var shotA = PNCinematicShot.Pose(new Vector3(10, 8, 0), new Vector3(40, 0, 0), duration: .3f, blendIn: 0f);
            var shotB = PNCinematicShot.Pose(new Vector3(20, 8, 0), new Vector3(50, 0, 0), fieldOfView: 50f, duration: .3f, blendIn: .2f, easeIn: PNCameraEase.Linear);
            var shotC = PNCinematicShot.Pose(new Vector3(30, 8, 0), new Vector3(60, 0, 0), duration: .3f, blendIn: 0f, blendOut: .3f, easeOut: PNCameraEase.EaseOut);
            var sequence = new PNCinematicSequence(new[] { shotA, shotB, shotC });
            var started = new List<int>(); var seqEnded = new List<PNCameraHandleEnd>();
            var handle = (PNCinematicHandle)service.RequestCinematicSequence(owner.transform, "seq", sequence);
            handle.ShotStarted += i => started.Add(i); handle.Ended += r => seqEnded.Add(r);
            Run(2);
            Check(handle.IsActive && director.CinematicActive && director.CinematicShotIndex == 0 && handle.CurrentShot == 0, "a sequence starts on its first shot");
            Check(director.Blender.CustomBlends.Length == 2 && director.Blender.CustomBlends[0].Blend.Style == CinemachineBlendDefinition.Styles.Cut && director.Blender.CustomBlends[1].Blend.Style == CinemachineBlendDefinition.Styles.EaseOut && Mathf.Approximately(director.Blender.CustomBlends[1].Blend.Time, .3f), "blend styles come from the first shot's blend-in and the last shot's blend-out (a zero blend-in is a cut)");
            Run(12);  // 0.24+ s: still shot 0
            Check(Vector3.Distance(output.transform.position, shotA.Position) < .2f && director.CinematicShotIndex == 0, "the first shot holds its pose");
            Run(4);   // ~0.36 s: the second shot has started and is blending in
            Check(director.CinematicShotIndex == 1, "the sequence moves on to the second shot");
            float midDistance = Vector3.Distance(output.transform.position, shotB.Position);
            Check(midDistance > .1f && midDistance < Vector3.Distance(shotA.Position, shotB.Position), "during the second shot's blend-in the camera is between the two poses");
            Run(10);  // ~0.56 s: blend-in done, still on the second shot
            Check(director.CinematicShotIndex == 1 && Vector3.Distance(output.transform.position, shotB.Position) < .5f && Mathf.Abs(output.fieldOfView - 50f) < 2f, "the second shot is reached with its lens");
            Run(5);   // ~0.66 s
            Check(director.CinematicShotIndex == 2 && started.SequenceEqual(new[] { 0, 1, 2 }), "ShotStarted fires once per shot, in order");
            Run(15);  // ~0.96 s
            Check(!handle.IsActive && handle.EndReason == PNCameraHandleEnd.Completed && seqEnded.SequenceEqual(new[] { PNCameraHandleEnd.Completed }), "the sequence completes and reports Completed once");
            Run(40);
            Check(!director.CinematicActive, "after completion the world rig is back");
            var lostSeq = new PNCinematicSequence(new[] { PNCinematicShot.Pose(new Vector3(10, 8, 0), new Vector3(40, 0, 0), blendIn: 0f) });
            var releasedSeq = (PNCinematicHandle)service.RequestCinematicSequence(owner.transform, "seq2", lostSeq); var endsRelease = new List<PNCameraHandleEnd>(); releasedSeq.Ended += r => endsRelease.Add(r); Run(3);
            releasedSeq.Dispose();
            Check(endsRelease.SequenceEqual(new[] { PNCameraHandleEnd.Released }), "releasing a cinematic reports Released");
            Run(40);
            Check(service.RequestCinematicSequence(null, "x", sequence).RefusalReason == PNCameraRefusal.OwnerGone && service.RequestCinematicSequence(owner.transform, "", sequence).RefusalReason == PNCameraRefusal.InvalidParameter, "sequence requests validate owner and key");
            bool threw = false; try { new PNCinematicSequence(new[] { PNCinematicShot.Pose(Vector3.up, Vector3.zero, fieldOfView: 40f), PNCinematicShot.Pose(Vector3.up, Vector3.zero, fieldOfView: 40f) }); } catch (ArgumentException) { threw = true; }
            Check(threw && Throws(() => new PNCinematicSequence(new PNCinematicShot[0])) && Throws(() => new PNCinematicSequence(Enumerable.Repeat(PNCinematicShot.Pose(Vector3.up, Vector3.zero, duration: 1f), 17))), "a sequence needs durations on all but the last shot and 1..16 shots");

            // ---- path ----------------------------------------------------------------------------------------------------------------------------------
            var points = new[] { new PNPoint(0, 8, -2), new PNPoint(10, 12, -2), new PNPoint(20, 8, -2) };
            var pathHandle = service.RequestCinematic(owner.transform, "path", PNCinematicShot.AlongPath(points, new Vector3(50, 0, 0), 1f, blendIn: 0f, blendOut: 0f)); Run(26);   // ~0.5 s
            Check(Vector3.Distance(output.transform.position, new Vector3(10, 12, -2)) < .6f && Quaternion.Angle(output.transform.rotation, Quaternion.Euler(50, 0, 0)) < .5f, "halfway along a waypoint path the camera is at the middle waypoint with its fixed orientation");
            Run(40);
            Check(!pathHandle.IsActive && pathHandle.EndReason == PNCameraHandleEnd.Completed, "a path shot ends after its duration");
            Run(40);
            Check(Throws(() => PNCinematicShot.AlongPath(points, Vector3.zero, 0f)) && Throws(() => PNCinematicShot.AlongPath(new[] { points[0] }, Vector3.zero, 1f)), "a path shot needs a duration and at least two points");

            // ---- aim -----------------------------------------------------------------------------------------------------------------------------------
            var aimPitchOnly = service.RequestCinematic(owner.transform, "aim", PNCinematicShot.Pose(new Vector3(0, 6, 0), Vector3.zero, blendIn: 0f, blendOut: 0f, aimTarget: prop.transform)); Run(5);
            var aimEuler = output.transform.rotation.eulerAngles;
            Check(Mathf.Abs(Mathf.DeltaAngle(0f, aimEuler.y)) < .1f && aimEuler.x > 20f && aimEuler.x < 80f, "an aim shot without yaw permission only pitches towards the target (yaw stays 0)");
            aimPitchOnly.Dispose(); Run(5);
            var aimFull = service.RequestCinematic(owner.transform, "aim2", PNCinematicShot.Pose(new Vector3(0, 6, 0), Vector3.zero, blendIn: 0f, blendOut: 0f, allowYaw: true, aimTarget: prop.transform)); Run(5);
            var toProp = (prop.transform.position - output.transform.position).normalized;
            Check(Vector3.Angle(output.transform.forward, toProp) < .5f && director.YawOverridden, "an aim shot with yaw permission looks straight at the target and reports a yaw override");
            aimFull.Dispose(); Run(5);

            // ---- blend style mapping ----------------------------------------------------------------------------------------------------------------------
            foreach (PNCameraEase ease in new[] { PNCameraEase.Linear, PNCameraEase.EaseIn, PNCameraEase.EaseOut, PNCameraEase.HardIn, PNCameraEase.HardOut, PNCameraEase.EaseInOut })
            {
                var styled = service.RequestCinematic(owner.transform, "style", PNCinematicShot.Pose(new Vector3(10, 8, 0), new Vector3(40, 0, 0), blendIn: .4f, blendOut: .4f, easeIn: ease, easeOut: ease)); Run(1);
                var expected = ease == PNCameraEase.Linear ? CinemachineBlendDefinition.Styles.Linear : ease == PNCameraEase.EaseIn ? CinemachineBlendDefinition.Styles.EaseIn : ease == PNCameraEase.EaseOut ? CinemachineBlendDefinition.Styles.EaseOut : ease == PNCameraEase.HardIn ? CinemachineBlendDefinition.Styles.HardIn : ease == PNCameraEase.HardOut ? CinemachineBlendDefinition.Styles.HardOut : CinemachineBlendDefinition.Styles.EaseInOut;
                Check(director.Blender.CustomBlends[0].Blend.Style == expected && director.Blender.CustomBlends[1].Blend.Style == expected, "blend style " + ease + " maps to the Cinemachine style");
                styled.Dispose(); Run(40);
            }

            // ---- bounds ----------------------------------------------------------------------------------------------------------------------------------
            float restZ = output.transform.position.z;
            var bounds = service.RequestBounds(owner.transform, "box", new PNCameraBounds(new Vector3(0, 3, 4), new Vector3(30, 40, 4))); Run(60);
            Check(bounds.IsActive && director.BoundsActive && output.transform.position.z > restZ + .5f && output.transform.position.z >= 2f - .15f, "bounds keep the camera inside the box (it is pulled in from z=-3 to the box edge)");
            bounds.Dispose(); Run(60);
            Check(!director.BoundsActive && Mathf.Abs(output.transform.position.z - restZ) < .05f, "releasing the bounds lets the camera return to its authored position");
            Check(Throws(() => new PNCameraBounds(Vector3.zero, new Vector3(0, 1, 1))) && Throws(() => new PNCameraBounds(new Vector3(float.NaN, 0, 0), Vector3.one)), "bounds data is validated");
            Check(service.RequestBounds(null, "b", new PNCameraBounds(Vector3.zero, Vector3.one)).RefusalReason == PNCameraRefusal.OwnerGone, "bounds need an owner");

            // ---- follow tuning ------------------------------------------------------------------------------------------------------------------------------
            var authoredOffset = composer.TargetOffset; var authoredDamping = composer.Damping; var authoredLookahead = composer.Lookahead.Time; var authoredDead = composer.Composition.DeadZone.Size;
            var tuning = service.RequestFollowTuning(owner.transform, "tune", new PNFollowTuning { TargetOffset = new Vector3(0, 1.5f, 0), Damping = new Vector3(.1f, .2f, .3f), DeadZone = new Vector2(.2f, .3f), LookaheadTime = .1f }); Run(3);
            Check(tuning.IsActive && composer.TargetOffset == new Vector3(0, 1.5f, 0) && composer.Damping == new Vector3(.1f, .2f, .3f) && composer.Composition.DeadZone.Enabled && composer.Composition.DeadZone.Size == new Vector2(.2f, .3f) && composer.Lookahead.Enabled && Mathf.Approximately(composer.Lookahead.Time, .1f), "follow tuning writes offset, damping, dead zone and lookahead on a composer rig");
            var tuning2 = service.RequestFollowTuning(owner.transform, "tune2", new PNFollowTuning { Damping = new Vector3(2f, 2f, 2f) }); Run(3);
            Check(composer.Damping == new Vector3(2f, 2f, 2f) && composer.TargetOffset == authoredOffset, "the latest tuning wins and fields it does not set return to the authored value");
            tuning2.Dispose(); tuning.Dispose(); Run(3);
            Check(composer.TargetOffset == authoredOffset && composer.Damping == authoredDamping && Mathf.Approximately(composer.Lookahead.Time, authoredLookahead) && composer.Composition.DeadZone.Size == authoredDead, "releasing the tuning restores the authored composition");
            Check(Throws(() => new PNFollowTuning().Validate()) && Throws(() => new PNFollowTuning { Damping = new Vector3(-1, 0, 0) }.Validate()) && Throws(() => new PNFollowTuning { DeadZone = new Vector2(2, 0) }.Validate()) && Throws(() => new PNFollowTuning { LookaheadTime = 5f }.Validate()), "tuning data is validated");
            Check(service.RequestFollowTuning(owner.transform, "bad", new PNFollowTuning()).RefusalReason == PNCameraRefusal.InvalidParameter, "an empty tuning is refused as invalid");
            local.Select(poseWorld); Run(20);
            Check(service.RequestFollowTuning(owner.transform, "pose", new PNFollowTuning { TargetOffset = Vector3.up }).RefusalReason == PNCameraRefusal.NotSupported, "follow tuning on a pose-only rig is refused with NotSupported");
            local.Select(composerWorld); Run(20);

            // ---- orbit ----------------------------------------------------------------------------------------------------------------------------------------
            var authoredRotation = composerVcam.transform.rotation;
            var orbit = service.RequestOrbit(owner.transform, "orbit", new PNOrbitRequest(40f, 10f, 0f)); Run(5);
            var orbitEuler = composerVcam.transform.rotation.eulerAngles;
            Check(orbit.IsActive && Mathf.Abs(Mathf.DeltaAngle(40f, orbitEuler.y)) < .5f && Mathf.Abs(orbitEuler.x - 55f) < .5f && director.YawOverridden && Mathf.Abs(Mathf.DeltaAngle(40f, director.Orientation.Yaw)) < 1f, "orbit turns a composer rig by the requested yaw and pitch and reports a yaw override");
            orbit.Dispose(); Run(5);
            Check(Quaternion.Angle(composerVcam.transform.rotation, authoredRotation) < .01f && !director.YawOverridden, "releasing the orbit restores the authored rotation exactly");
            var eased = service.RequestOrbit(owner.transform, "eased", new PNOrbitRequest(60f, 0f, 1f, PNCameraEase.EaseIn)); Run(25);
            float halfYaw = director.OrbitYaw;
            Check(halfYaw > 0f && halfYaw < 30f, "an eased orbit is partway through after half its blend (ease-in is slow at first)");
            Run(40);
            Check(Mathf.Abs(director.OrbitYaw - 60f) < .1f, "the orbit reaches the requested yaw");
            eased.Dispose(); Run(80);
            Check(Mathf.Abs(director.OrbitYaw) < .1f || Quaternion.Angle(composerVcam.transform.rotation, authoredRotation) < .5f, "the orbit eases back after release");
            Check(Throws(() => new PNOrbitRequest(400f)) && Throws(() => new PNOrbitRequest(10f, 45f)) && Throws(() => new PNOrbitRequest(10f, 0f, -1f)), "orbit data is validated");
            local.Select(poseWorld); Run(20);
            Check(service.RequestOrbit(owner.transform, "pose", new PNOrbitRequest(10f)).RefusalReason == PNCameraRefusal.NotSupported, "orbit on a pose-only rig is refused with NotSupported");
            local.Select(composerWorld); Run(20);

            // ---- teardown ----------------------------------------------------------------------------------------------------------------------------------------
            var pendingBounds = service.RequestBounds(owner.transform, "end", new PNCameraBounds(new Vector3(0, 3, 4), new Vector3(30, 40, 4)));
            var pendingTuning = service.RequestFollowTuning(owner.transform, "end", new PNFollowTuning { TargetOffset = Vector3.up });
            var pendingOrbit = service.RequestOrbit(owner.transform, "end", new PNOrbitRequest(20f, 0f, 0f)); Run(5);
            director.Release();
            Check(!pendingBounds.IsActive && !pendingTuning.IsActive && !pendingOrbit.IsActive && pendingBounds.EndReason == PNCameraHandleEnd.SessionEnded, "ending the session ends bounds, tuning and orbit requests with SessionEnded");
            Check(composer.TargetOffset == authoredOffset && Quaternion.Angle(composerVcam.transform.rotation, authoredRotation) < .01f && composerVcam.GetComponent<CinemachineConfiner3D>() == null, "teardown hands the rig back as authored (offset, rotation, no confiner)");
        }
        finally { try { director?.Release(); } catch { } view.Release(); UnityEngine.Object.DestroyImmediate(host); EveRegistrySession.End(); }
        return "PASS " + rows.Count + " native camera extras cases (edit-mode; not rendered-frame acceptance)\n" + string.Join("\n", rows);
    }
    private static bool Throws(Action action) { try { action(); return false; } catch (Exception) { return true; } }
}
#endif
