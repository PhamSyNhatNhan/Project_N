#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Unity.Cinemachine;
using UnityEngine;

// Native (edit mode, Brain in ManualUpdate, director.Advance driven by hand) zoom checks on the four world kinds: composer rig,
// pose-only perspective rig, orthographic camera-only world, world without any camera. Not rendered-frame acceptance.
public static class PNCameraZoomChecks
{
    public static string Run()
    {
        var rows = new List<string>(); void Check(bool value, string label) { if (!value) throw new Exception("FAIL " + label); rows.Add("PASS " + label); }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "fixture:camera-zoom"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile()); var worlds = new PNWorldRuntime(session, scheduler);
        var local = new PNLocalWorldContext(session); var host = new GameObject("Camera zoom fixture service"); var view = host.AddComponent<PNWorldPresentation>(); view.Bind(session, worlds, local);
        void Pump(Func<bool> ready) { for (int i = 0; i < 1000 && !ready(); i++) scheduler.AdvanceFrame(); if (!ready()) throw new Exception("Zoom fixture did not drain."); }
        PNCameraDirector director = null; int frame = 0;
        void Tick() { frame++; director.Brain.ManualUpdate(frame, .02f); }
        void Run(int frames) { for (int i = 0; i < frames; i++) { director.Advance(.02f); Tick(); } }
        try
        {
            var composerWorld = worlds.Create(EveId.Parse("fixture:world")); var poseWorld = worlds.Create(EveId.Parse("fixture:world")); var orthoWorld = worlds.Create(EveId.Parse("fixture:world")); var emptyWorld = worlds.Create(EveId.Parse("fixture:world"));
            Pump(() => composerWorld.CanExecute && poseWorld.CanExecute && orthoWorld.CanExecute && emptyWorld.CanExecute);
            GameObject Root(EveWorldContext w) => worlds.Get(w).Slots[0].Roots[0];
            var cr = Root(composerWorld); var pr = Root(poseWorld); var or = Root(orthoWorld); var er = Root(emptyWorld);
            // composer rig (authored distance 4.5)
            var cam1 = cr.AddComponent<Camera>(); cam1.tag = "MainCamera"; cam1.fieldOfView = 38;
            var composerVcam = new GameObject("composer vcam").AddComponent<CinemachineCamera>(); composerVcam.transform.SetParent(cr.transform); composerVcam.Priority.Value = 10;
            var composer = composerVcam.gameObject.AddComponent<CinemachinePositionComposer>(); composer.CameraDistance = 4.5f; composerVcam.transform.localPosition = new Vector3(0, 3, -3); composerVcam.Lens.FieldOfView = 38;
            // pose-only perspective
            var cam2 = pr.AddComponent<Camera>(); cam2.tag = "MainCamera"; cam2.fieldOfView = 38; pr.transform.position = new Vector3(100, 0, 0);
            var poseVcam = new GameObject("pose vcam").AddComponent<CinemachineCamera>(); poseVcam.transform.SetParent(pr.transform); poseVcam.Priority.Value = 10; poseVcam.Lens.FieldOfView = 38; poseVcam.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            // orthographic camera-only
            var cam3 = or.AddComponent<Camera>(); cam3.tag = "MainCamera"; cam3.orthographic = true; cam3.orthographicSize = 2; or.transform.position = new Vector3(200, 0, 0);
            view.Register(cr); view.Register(pr); view.Register(or); view.Register(er); local.Select(composerWorld);
            director = PNCameraDirector.Attach(host, session, worlds, local, view);
            view.Register(cr); view.Register(pr); view.Register(or); view.Register(er); local.Select(null); local.Select(composerWorld);
            director.Brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            var service = (IPNCameraService)director; var owner = new object();

            // ---- composer rig: distance zoom ------------------------------------------------------------------------------------------
            Run(5);
            Check((director.ViewCapabilities & PNCameraRigCapabilities.DistanceZoom) != 0 && (director.ViewCapabilities & PNCameraRigCapabilities.LensZoom) != 0, "composer rig reports distance and lens zoom");
            Check(Mathf.Approximately(composer.CameraDistance, 4.5f), "no request: authored distance is untouched");
            var h = service.RequestZoom(owner, "in", PNZoomRequest.Factor(.8f)); Run(200);
            Check(h.IsActive && Mathf.Abs(composer.CameraDistance - 4.5f * .8f) < .01f, "zoom in 0.8 moves the composer distance to 3.6");
            var extreme = service.RequestZoom(owner, "far-in", PNZoomRequest.Factor(.1f)); Run(300);
            Check(Mathf.Abs(composer.CameraDistance - 3f) < .01f, "extreme zoom in is clamped at the profile minimum distance (3.0)");
            extreme.Dispose(); h.Dispose(); Run(300);
            Check(!h.IsActive && Mathf.Abs(composer.CameraDistance - 4.5f) < .005f, "releasing returns the exact authored distance (no drift)");
            var outReq = service.RequestZoom(owner, "out", PNZoomRequest.Factor(9f)); Run(300);
            Check(Mathf.Abs(composer.CameraDistance - 7f) < .01f, "extreme zoom out is clamped at the profile maximum distance (7.0)");
            outReq.Dispose(); Run(300);
            Check(Mathf.Abs(service.SetBaseZoom(1.2f) - 1.2f) < 1e-5f, "player base zoom is applied"); Run(300);
            Check(Mathf.Abs(composer.CameraDistance - 4.5f * 1.2f) < .01f, "player base scales the authored distance");
            var onTop = service.RequestZoom(owner, "mod", PNZoomRequest.Factor(.5f)); Run(300);
            Check(Mathf.Abs(composer.CameraDistance - Mathf.Max(3f, 4.5f * 1.2f * .5f)) < .01f, "gameplay modifier multiplies the player base, then clamps");
            onTop.Dispose(); service.SetBaseZoom(1f); Run(300);
            var timed = service.RequestZoom(owner, "timed", PNZoomRequest.Factor(.8f, 0.5f, 0.1f)); Run(10);
            Check(timed.IsActive && director.ActiveZoomRequests == 1, "timed request is active while its duration runs");
            Run(60);
            Check(!timed.IsActive && director.ActiveZoomRequests == 0, "timed request releases itself after its duration");
            // winner/replacement bookkeeping on the director
            var first = service.RequestZoom(owner, "same", PNZoomRequest.Factor(.9f)); var second = service.RequestZoom(owner, "same", PNZoomRequest.Factor(.8f));
            Check(!first.IsActive && second.IsActive && director.ActiveZoomRequests == 1, "same owner and key replaces a zoom request");
            second.Dispose(); Run(300);
            // many requests
            var many = Enumerable.Range(0, 32).Select(i => service.RequestZoom(new object(), "k", PNZoomRequest.Factor(1.001f))).ToArray();
            var overflow = service.RequestZoom(new object(), "k", PNZoomRequest.Factor(1.001f));
            Check(many.All(x => x.IsActive) && !overflow.IsActive && overflow.RefusalReason == PNCameraRefusal.LimitReached, "request 33 is refused with LimitReached");
            foreach (var x in many) x.Dispose();
            Check(service.RequestZoom(null, "k", PNZoomRequest.Factor(.9f)).RefusalReason == PNCameraRefusal.OwnerGone && service.RequestZoom(owner, "", PNZoomRequest.Factor(.9f)).RefusalReason == PNCameraRefusal.InvalidParameter && service.RequestZoom(owner, "k", default).RefusalReason == PNCameraRefusal.InvalidParameter, "null owner, empty key and default request are refused with codes");

            // ---- pose-only perspective: lens zoom ---------------------------------------------------------------------------------------
            var carry = service.RequestZoom(owner, "carry", PNZoomRequest.Factor(.8f)); Run(5);
            local.Select(poseWorld); Run(20);
            Check(!carry.IsActive && director.ActiveZoomRequests == 0, "zoom requests of the previous world end when the view changes world");
            Check((director.ViewCapabilities & PNCameraRigCapabilities.DistanceZoom) == 0 && (director.ViewCapabilities & PNCameraRigCapabilities.LensZoom) != 0, "pose-only rig reports lens zoom without distance zoom");
            float fovStill = director.ViewCamera.fieldOfView;
            var lens = service.RequestZoom(owner, "lens", PNZoomRequest.Factor(.8f)); Run(200);
            var recomposer = poseVcam.GetComponent<CinemachineRecomposer>();
            Check(recomposer != null && Mathf.Abs(recomposer.ZoomScale - .8f) < .01f && director.ViewCamera.fieldOfView < fovStill - 1f, "pose-only rig zooms by lens scale (smaller FOV)");
            lens.Dispose(); Run(300);
            Check(Mathf.Abs(recomposer.ZoomScale - 1f) < .005f && Mathf.Abs(director.ViewCamera.fieldOfView - fovStill) < .1f, "lens zoom returns to the authored FOV after release");
            var lensOut = service.RequestZoom(owner, "lens-out", PNZoomRequest.Factor(9f)); Run(300);
            Check(Mathf.Abs(recomposer.ZoomScale - PNCameraProfile.Default.LensScaleMax) < .01f, "lens zoom out is clamped by the lens range");
            lensOut.Dispose(); Run(300);

            // ---- orthographic camera-only world ---------------------------------------------------------------------------------------------
            local.Select(orthoWorld); Run(20);
            Check(director.ViewCamera.orthographic && Mathf.Abs(director.ViewCamera.orthographicSize - 2f) < .01f, "orthographic world shows through the director at its authored size");
            var ortho = service.RequestZoom(owner, "ortho", PNZoomRequest.Factor(.8f)); Run(200);
            Check((director.ViewCapabilities & PNCameraRigCapabilities.DistanceZoom) == 0 && Mathf.Abs(director.ViewCamera.orthographicSize - 2f * Mathf.Max(PNCameraProfile.Default.LensScaleMin, .8f)) < .02f, "orthographic world zooms by orthographic size, never by distance");
            ortho.Dispose(); Run(300);
            Check(Mathf.Abs(director.ViewCamera.orthographicSize - 2f) < .01f, "orthographic size returns to authored after release");

            // ---- world with no camera: fallback composer rig ---------------------------------------------------------------------------------
            local.Select(emptyWorld); Run(20);
            Check(director.Brain.ActiveVirtualCamera != null && director.Brain.ActiveVirtualCamera.Name.Contains("Fallback") && (director.ViewCapabilities & PNCameraRigCapabilities.DistanceZoom) != 0, "world without a camera uses the fallback rig, which can distance-zoom");
            var fallbackZoom = service.RequestZoom(owner, "fallback", PNZoomRequest.Factor(.8f)); Run(200);
            Check(fallbackZoom.IsActive, "zoom is accepted on the fallback rig");
            fallbackZoom.Dispose();

            // ---- no view -----------------------------------------------------------------------------------------------------------------------
            local.Select(null); Run(5);
            Check(service.RequestZoom(owner, "none", PNZoomRequest.Factor(.8f)).RefusalReason == PNCameraRefusal.NoCamera, "no local view: zoom request is refused with NoCamera");
            Check(PNNullCameraService.Instance.RequestZoom(owner, "x", PNZoomRequest.Factor(.9f)).RefusalReason == PNCameraRefusal.NoCamera && PNNullCameraService.Instance.RequestLockOn(null, null, PNLockOnRequest.Default).RefusalReason == PNCameraRefusal.NoCamera, "the empty service refuses everything without throwing");

            // ---- profile layers on the live director --------------------------------------------------------------------------------------
            local.Select(composerWorld); Run(20);
            director.SetProfileLayer(PNCameraProfileSource.World, new PNCameraProfileLayer { DistanceScaleMax = 1.1f });
            var profiled = service.RequestZoom(owner, "profile", PNZoomRequest.Factor(2f)); Run(300);
            Check(Mathf.Abs(composer.CameraDistance - 4.5f * 1.1f) < .01f, "a world profile layer narrows the zoom range");
            bool refusedLayer = false; try { director.SetProfileLayer(PNCameraProfileSource.Player, new PNCameraProfileLayer { DistanceScaleMax = .1f }); } catch (Exception) { refusedLayer = true; }
            Check(refusedLayer && director.Profile.DistanceScaleMax == 1.1f, "an invalid profile layer is refused and changes nothing");
            profiled.Dispose(); director.SetProfileLayer(PNCameraProfileSource.World, null); Run(300);
            Check(Mathf.Abs(composer.CameraDistance - 4.5f) < .005f, "clearing the layer restores the default range and authored distance");

            // ---- teardown returns every rig as authored ---------------------------------------------------------------------------------------------
            var pending = service.RequestZoom(owner, "pending", PNZoomRequest.Factor(.8f)); Run(100);
            director.Release();
            Check(Mathf.Abs(composer.CameraDistance - 4.5f) < .001f && poseVcam.GetComponent<CinemachineRecomposer>() == null && !pending.IsActive, "director release returns distance and removes the added recomposer");
        }
        finally { try { director?.Release(); } catch { } view.Release(); UnityEngine.Object.DestroyImmediate(host); EveRegistrySession.End(); }
        return "PASS " + rows.Count + " native camera zoom cases (edit-mode, four world kinds; not rendered-frame acceptance)\n" + string.Join("\n", rows);
    }
}
#endif
