#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Unity.Cinemachine;
using UnityEngine;

// Native (edit mode, Brain ManualUpdate, director.Advance by hand) cinematic checks: priority over the world rig, blends, hand-back, yaw opt-in,
// refusals, release mid-blend and on world change, zoom hand-back. Not rendered-frame acceptance.
public static class PNCameraCinematicChecks
{
    public static string Run()
    {
        var rows = new List<string>(); void Check(bool value, string label) { if (!value) throw new Exception("FAIL " + label); rows.Add("PASS " + label); }
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "fixture:camera-cinematic"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile()); var worlds = new PNWorldRuntime(session, scheduler);
        var local = new PNLocalWorldContext(session); var host = new GameObject("Camera cinematic fixture service"); var view = host.AddComponent<PNWorldPresentation>(); view.Bind(session, worlds, local);
        void Pump(Func<bool> ready) { for (int i = 0; i < 1000 && !ready(); i++) scheduler.AdvanceFrame(); if (!ready()) throw new Exception("Cinematic fixture did not drain."); }
        PNCameraDirector director = null; int frame = 0;
        void Run(int frames) { for (int i = 0; i < frames; i++) { director.Advance(.02f); frame++; director.Brain.ManualUpdate(frame, .02f); } }
        try
        {
            var a = worlds.Create(EveId.Parse("fixture:world")); var b = worlds.Create(EveId.Parse("fixture:world")); Pump(() => a.CanExecute && b.CanExecute);
            var ar = worlds.Get(a).Slots[0].Roots[0]; var br = worlds.Get(b).Slots[0].Roots[0];
            var cam = ar.AddComponent<Camera>(); cam.tag = "MainCamera"; cam.fieldOfView = 38;
            var vcam = new GameObject("world vcam").AddComponent<CinemachineCamera>(); vcam.transform.SetParent(ar.transform); vcam.transform.position = new Vector3(0, 3, -3); vcam.transform.rotation = Quaternion.Euler(45, 0, 0); vcam.Priority.Value = 10; vcam.Lens.FieldOfView = 38;
            vcam.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            var prop = new GameObject("prop"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(prop, a.PhysicsOwner); prop.transform.position = new Vector3(5, 0, 5);
            view.Register(ar); view.Register(br); local.Select(a);
            director = PNCameraDirector.Attach(host, session, worlds, local, view);
            view.Register(ar); view.Register(br); local.Select(null); local.Select(a);
            director.Brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            var service = (IPNCameraService)director; var owner = new GameObject("owner"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner, a.PhysicsOwner);
            var output = director.ViewCamera;
            Run(10);
            var restPos = output.transform.position; var restRot = output.transform.rotation; float restFov = output.fieldOfView;
            Check(Vector3.Distance(restPos, vcam.transform.position) < .01f && !director.CinematicActive, "baseline: the world rig is live, no cinematic");

            // ---- shot in, hold, blend out -----------------------------------------------------------------------------------------------
            var shot = PNCinematicShot.Pose(new Vector3(10, 8, -4), new Vector3(35, 0, 0), fieldOfView: 60f, blendIn: .5f, blendOut: .5f);
            var h = service.RequestCinematic(owner.transform, "intro", shot);
            Check(h.IsActive && h.RefusalReason == "", "a valid shot is accepted");
            Run(5);
            Check(director.CinematicActive && ReferenceEquals(director.Brain.ActiveVirtualCamera, director.CinematicRig) || director.Brain.IsBlending, "the cinematic rig takes over (blending in)");
            float midDistance = Vector3.Distance(output.transform.position, shot.Position);
            Check(midDistance > .1f && midDistance < Vector3.Distance(restPos, shot.Position), "mid-blend the camera is between the world pose and the shot");
            Run(60);
            Check(Vector3.Distance(output.transform.position, shot.Position) < .05f && Mathf.Abs(output.fieldOfView - 60f) < .1f && Quaternion.Angle(output.transform.rotation, Quaternion.Euler(35, 0, 0)) < .5f, "after the blend the camera sits on the shot (pose and FOV)");
            Check(!director.YawOverridden && Mathf.Abs(Mathf.DeltaAngle(0f, director.Orientation.Yaw)) < .01f, "yaw stays 0 and no yaw override for a normal shot");
            h.Dispose(); Run(70);
            Check(!director.CinematicActive && Vector3.Distance(output.transform.position, restPos) < .05f && Quaternion.Angle(output.transform.rotation, restRot) < .5f && Mathf.Abs(output.fieldOfView - restFov) < .1f, "release blends back to exactly the previous rig and lens");

            // ---- follow shot ---------------------------------------------------------------------------------------------------------------
            var follow = PNCinematicShot.Follow(prop.transform, new Vector3(0, 4, -4), new Vector3(45, 0, 0), blendIn: 0f, blendOut: 0f);
            var hf = service.RequestCinematic(owner.transform, "follow", follow); Run(3);
            Check(Vector3.Distance(output.transform.position, prop.transform.position + follow.Offset) < .05f, "follow shot keeps target + offset with fixed orientation");
            prop.transform.position += new Vector3(2, 0, 0); Run(3);
            Check(Vector3.Distance(output.transform.position, prop.transform.position + follow.Offset) < .05f, "follow shot tracks the target as it moves");
            UnityEngine.Object.DestroyImmediate(prop); Run(3);
            Check(!hf.IsActive && !director.CinematicActive, "destroying the followed target releases the shot");
            Run(5);

            // ---- priority, replacement, duration ---------------------------------------------------------------------------------------------
            var low = service.RequestCinematic(owner.transform, "low", PNCinematicShot.Pose(new Vector3(0, 20, 0), new Vector3(90, 0, 0), blendIn: 0f, blendOut: 0f, priority: 1));
            var high = service.RequestCinematic(owner.transform, "high", PNCinematicShot.Pose(new Vector3(30, 5, 0), new Vector3(20, 0, 0), blendIn: 0f, blendOut: 0f, priority: 5));
            Run(5);
            Check(Vector3.Distance(output.transform.position, new Vector3(30, 5, 0)) < .05f, "the higher-priority shot wins");
            high.Dispose(); Run(5);
            Check(Vector3.Distance(output.transform.position, new Vector3(0, 20, 0)) < .05f && low.IsActive, "releasing the winner hands over to the next shot");
            low.Dispose(); Run(5);
            var timed = service.RequestCinematic(owner.transform, "timed", PNCinematicShot.Pose(new Vector3(0, 12, -2), new Vector3(60, 0, 0), duration: .4f, blendIn: 0f, blendOut: 0f)); Run(5);
            Check(director.CinematicActive && timed.IsActive, "a timed shot is live during its duration");
            Run(30);
            Check(!timed.IsActive && !director.CinematicActive, "a timed shot ends by itself");

            // ---- yaw ------------------------------------------------------------------------------------------------------------------------------
            Check(service.RequestCinematic(owner.transform, "yaw-refused", PNCinematicShot.Pose(new Vector3(0, 5, 0), new Vector3(45, 45, 0))).RefusalReason == PNCameraRefusal.InvalidParameter, "a shot with yaw but without AllowYaw is refused");
            var yawShot = service.RequestCinematic(owner.transform, "yaw", PNCinematicShot.Pose(new Vector3(0, 5, 0), new Vector3(45, 90, 0), blendIn: 0f, blendOut: 0f, allowYaw: true)); Run(5);
            Check(director.YawOverridden && director.Orientation.YawOverridden && Mathf.Abs(Mathf.DeltaAngle(90f, director.Orientation.Yaw)) < .5f && PNViewCamera.Service.Orientation.YawOverridden, "an opt-in shot reports a yaw override with the new yaw");
            yawShot.Dispose(); Run(5);
            Check(!director.YawOverridden && Mathf.Abs(Mathf.DeltaAngle(0f, director.Orientation.Yaw)) < .5f, "yaw override and yaw return to 0 after release");

            // ---- Edit Mode billboards ignore a yaw override (pitch-only stays the authored behaviour outside play) ----------------------------
            var spriteObject = new GameObject("edit-mode billboard"); spriteObject.AddComponent<SpriteRenderer>(); var billboard = spriteObject.AddComponent<ErModeCharacterBillboard>();
            typeof(ErModeCharacterBillboard).GetField("viewCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(billboard, director.ViewCamera);
            var editYaw = service.RequestCinematic(owner.transform, "edit-yaw", PNCinematicShot.Pose(new Vector3(0, 5, 0), new Vector3(45, 90, 0), blendIn: 0f, blendOut: 0f, allowYaw: true)); Run(5);
            Check(director.YawOverridden && billboard.AlignToCamera() && Quaternion.Angle(spriteObject.transform.rotation, Quaternion.Euler(45f, 0f, 0f)) < .01f, "Edit Mode billboard stays pitch-only even while a yaw override is reported");
            editYaw.Dispose(); Run(5);
            UnityEngine.Object.DestroyImmediate(spriteObject);

            // ---- release in the middle of a blend ----------------------------------------------------------------------------------------------
            var blendy = service.RequestCinematic(owner.transform, "mid", PNCinematicShot.Pose(new Vector3(40, 10, 0), new Vector3(30, 0, 0), blendIn: 1f, blendOut: 1f)); Run(15);
            Check(director.CinematicActive, "blend-in is in progress");
            blendy.Dispose(); Run(150);
            Check(!director.CinematicActive && Vector3.Distance(output.transform.position, restPos) < .05f && Quaternion.Angle(output.transform.rotation, restRot) < .5f, "releasing in the middle of the blend settles back on the world rig without a snap or stuck state");

            // ---- refusals ---------------------------------------------------------------------------------------------------------------------------
            Check(service.RequestCinematic(null, "x", PNCinematicShot.Pose(Vector3.up, Vector3.zero, fieldOfView: 40f)).RefusalReason == PNCameraRefusal.OwnerGone, "null owner is refused");
            Check(service.RequestCinematic(owner.transform, "", PNCinematicShot.Pose(Vector3.up, Vector3.zero, fieldOfView: 40f)).RefusalReason == PNCameraRefusal.InvalidParameter, "empty key is refused");
            Check(service.RequestCinematic(owner.transform, "hold", PNCinematicShot.Pose(Vector3.up, Vector3.zero, fieldOfView: 40f, holdInput: true)).RefusalReason == PNCameraRefusal.NotSupported, "a shot asking to hold input is refused with NotSupported");
            bool threw = false; try { PNCinematicShot.Pose(Vector3.up, Vector3.zero, fieldOfView: 40f, orthographicSize: 3f); } catch (ArgumentException) { threw = true; }
            Check(threw && Throws(() => PNCinematicShot.Pose(new Vector3(float.NaN, 0, 0), Vector3.zero)) && Throws(() => PNCinematicShot.Pose(Vector3.up, Vector3.zero, duration: -1f)) && Throws(() => PNCinematicShot.Follow(null, Vector3.zero, Vector3.zero)), "shot data is validated at construction");
            var strayWorldOwner = new GameObject("stray");
            Check(service.RequestCinematic(null, "n", default).RefusalReason == PNCameraRefusal.OwnerGone, "default shot with no owner is refused");
            UnityEngine.Object.DestroyImmediate(strayWorldOwner);
            var many = Enumerable.Range(0, 8).Select(i => service.RequestCinematic(owner.transform, "k" + i, PNCinematicShot.Pose(new Vector3(i, 5, 0), new Vector3(40, 0, 0), blendIn: 0f, blendOut: 0f))).ToArray();
            Check(service.RequestCinematic(owner.transform, "k-over", PNCinematicShot.Pose(Vector3.up, Vector3.zero, fieldOfView: 30f)).RefusalReason == PNCameraRefusal.LimitReached, "the ninth concurrent shot is refused with LimitReached");
            foreach (var x in many) x.Dispose(); Run(5);

            // ---- world change cancels the shot and cuts -------------------------------------------------------------------------------------------
            var changing = service.RequestCinematic(owner.transform, "change", PNCinematicShot.Pose(new Vector3(50, 9, 0), new Vector3(30, 0, 0), blendIn: 0f, blendOut: 0f)); Run(5);
            local.Select(b); Run(10);
            Check(!changing.IsActive && !director.CinematicActive, "changing the viewed world ends the cinematic");
            local.Select(a); Run(10);
            Check(Vector3.Distance(output.transform.position, restPos) < .05f, "back in the first world the world rig is live again");

            // ---- zoom survives a cinematic -----------------------------------------------------------------------------------------------------------
            var zoom = service.RequestZoom(owner.transform, "z", PNZoomRequest.Factor(.85f)); Run(200);
            float zoomedFov = output.fieldOfView;
            var cine = service.RequestCinematic(owner.transform, "over-zoom", PNCinematicShot.Pose(new Vector3(20, 8, 0), new Vector3(40, 0, 0), fieldOfView: 70f, blendIn: 0f, blendOut: 0f)); Run(10);
            Check(Mathf.Abs(output.fieldOfView - 70f) < .1f, "the shot's own lens overrides the zoomed lens while it is live");
            cine.Dispose(); Run(10);
            Check(Mathf.Abs(output.fieldOfView - zoomedFov) < .2f, "after the shot the previous zoom level is back");
            zoom.Dispose(); Run(300);
            Check(Mathf.Abs(output.fieldOfView - restFov) < .1f, "releasing the zoom returns the authored lens");

            // ---- teardown ------------------------------------------------------------------------------------------------------------------------------
            var pending = service.RequestCinematic(owner.transform, "pending", PNCinematicShot.Pose(new Vector3(9, 9, 0), new Vector3(30, 0, 0), blendIn: 0f, blendOut: 0f)); Run(5);
            var blenderAsset = director.Blender;
            director.Release();
            Check(!pending.IsActive && director.CinematicRig == null && blenderAsset == null, "director release removes the cinematic rig and the blender asset");
        }
        finally { try { director?.Release(); } catch { } view.Release(); UnityEngine.Object.DestroyImmediate(host); EveRegistrySession.End(); }
        return "PASS " + rows.Count + " native camera cinematic cases (edit-mode; not rendered-frame acceptance)\n" + string.Join("\n", rows);
    }
    private static bool Throws(Action action) { try { action(); return false; } catch (Exception) { return true; } }
}
#endif
