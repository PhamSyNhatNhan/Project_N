#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Native (edit mode, Brain in ManualUpdate) checks for the presentation claim seam and the camera director. These are not
// rendered-frame acceptance; the Player capture run owns that.
public static class PNCameraDirectorChecks
{
    public static string Run()
    {
        var rows = new List<string>(); void Check(bool value, string label) { if (!value) throw new Exception("FAIL " + label); rows.Add("PASS " + label); }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "fixture:camera"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile()); var worlds = new PNWorldRuntime(session, scheduler);
        var local = new PNLocalWorldContext(session); var host = new GameObject("Camera fixture service"); var view = host.AddComponent<PNWorldPresentation>(); view.Bind(session, worlds, local);
        void Pump(Func<bool> ready) { for (int i = 0; i < 1000 && !ready(); i++) scheduler.AdvanceFrame(); if (!ready()) throw new Exception("Camera fixture did not drain."); }
        PNCameraDirector director = null; var extra = new List<GameObject>();
        int frame = 0; void Tick() { frame++; director.Brain.ManualUpdate(frame, .02f); }
        try
        {
            var a = worlds.Create(EveId.Parse("fixture:world")); var b = worlds.Create(EveId.Parse("fixture:world")); var c = worlds.Create(EveId.Parse("fixture:world")); var d = worlds.Create(EveId.Parse("fixture:world")); Pump(() => a.CanExecute && b.CanExecute && c.CanExecute && d.CanExecute);
            var ar = worlds.Get(a).Slots[0].Roots[0]; var br = worlds.Get(b).Slots[0].Roots[0]; var cr = worlds.Get(c).Slots[0].Roots[0]; var dr = worlds.Get(d).Slots[0].Roots[0];

            // ---- claim seam on a plain presentation ---------------------------------------------------------------------------------
            var ac = ar.AddComponent<Camera>(); ac.tag = "MainCamera"; var bc = br.AddComponent<Camera>(); bc.tag = "MainCamera";
            view.Register(ar); view.Register(br); local.Select(a);
            Check(ac.enabled && !bc.enabled, "baseline: viewed world camera enabled, other hidden");
            var claimA = view.Claim(ac); ac.enabled = false;
            local.Select(b); local.Select(a);
            Check(!ac.enabled && claimA.Active, "claimed camera is not toggled by presentation view switches");
            claimA.Release(); Check(ac.enabled, "release while world viewed restores the original enabled state");
            claimA.Release(); Check(ac.enabled && !claimA.Active, "releasing twice is a no-op");
            var claimB = view.Claim(bc);
            Check(true, "claim of a hidden camera accepted");
            claimB.Release(); Check(!bc.enabled, "release while world not viewed keeps it hidden");
            local.Select(b); Check(bc.enabled && !ac.enabled, "hidden-then-viewed camera gets its ORIGINAL enabled state (true) back");
            local.Select(a);
            bool threw = false; try { view.Claim(ac, null); } catch (ArgumentException) { threw = true; }
            var again = view.Claim(ac); again.Release();
            Check(threw && ac.enabled, "claim with an invalid member claims nothing (atomic)");
            var one = view.Claim(ac); threw = false; try { view.Claim(ac); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "double claim of one component is rejected"); one.Release();
            var doomed = ar.AddComponent<AudioListener>(); var gone = view.Claim(doomed); UnityEngine.Object.DestroyImmediate(doomed);
            gone.Release(); Check(true, "release after the component was destroyed does not throw");
            var hold = view.Claim(ac); ac.enabled = false; view.Release();
            Check(ac.enabled, "presentation teardown returns a still-claimed component to its original state");
            view = host.GetComponent<PNWorldPresentation>(); view.Bind(session, worlds, local);
            UnityEngine.Object.DestroyImmediate(ac); UnityEngine.Object.DestroyImmediate(bc);
            hold.Release();

            // ---- director -----------------------------------------------------------------------------------------------------------
            var preexistingListeners = new HashSet<AudioListener>(UnityEngine.Object.FindObjectsByType<AudioListener>().Where(x => x.enabled));
            var preexistingMains = new HashSet<Camera>(UnityEngine.Object.FindObjectsByType<Camera>().Where(x => x.enabled && x.CompareTag("MainCamera")));   // e.g. the open scene's own camera when run in the main Editor
            var amain = ar.AddComponent<Camera>(); amain.tag = "MainCamera"; amain.fieldOfView = 38; ar.AddComponent<AudioListener>(); var abrain = ar.AddComponent<CinemachineBrain>();
            var avcam = new GameObject("A vcam").AddComponent<CinemachineCamera>(); avcam.transform.SetParent(ar.transform); avcam.transform.localPosition = new Vector3(0, 3, -3); avcam.transform.rotation = Quaternion.Euler(30f, 0f, 0f); avcam.Lens = new LensSettings { FieldOfView = 38f, NearClipPlane = .3f, FarClipPlane = 1000f, ModeOverride = LensSettings.OverrideModes.Perspective }; avcam.Priority.Value = 10;
            var bmain = br.AddComponent<Camera>(); bmain.tag = "MainCamera"; bmain.orthographic = true; bmain.orthographicSize = 2; br.transform.position = new Vector3(40, 0, 0);
            var bvcam = new GameObject("B hidden decoy vcam").AddComponent<CinemachineCamera>(); bvcam.transform.SetParent(cr.transform); bvcam.Priority.Value = 99; // lives in world C, which is never viewed
            view.Register(ar); view.Register(br); view.Register(cr); view.Register(dr); local.Select(a);
            director = PNCameraDirector.Attach(host, session, worlds, local, view);
            view.Register(ar); view.Register(br); view.Register(cr); local.Select(null); local.Select(a);
            director.Brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            Check(PNViewCamera.Service == director && director.ViewCamera != null && director.ViewCamera.CompareTag("MainCamera"), "director registers the view camera service");
            int mains = UnityEngine.Object.FindObjectsByType<Camera>().Count(x => x.enabled && x.CompareTag("MainCamera") && !preexistingMains.Contains(x));
            Check(mains == 1 && !amain.enabled && !bmain.enabled, "exactly one enabled MainCamera (the output); world cameras claimed and disabled [" + string.Join(",", UnityEngine.Object.FindObjectsByType<Camera>().Where(x => x.enabled && x.CompareTag("MainCamera")).Select(x => x.name + "@" + x.gameObject.scene.name)) + " a=" + amain.enabled + " b=" + bmain.enabled + "]");
            Check(!abrain.enabled && ar.GetComponent<AudioListener>().enabled == false && UnityEngine.Object.FindObjectsByType<AudioListener>().Count(x => x.enabled && !preexistingListeners.Contains(x)) == 1, "world Brain and AudioListener claimed; one AudioListener");
            // One gameplay rig for every world (Docs/CameraSinglePathPlan.md): the world's own vcam is only a data source, never admitted to the Brain.
            CinemachineVirtualCameraBase Rig() => director.Brain.ActiveVirtualCamera as CinemachineVirtualCameraBase;
            Tick(); Tick();
            Check(director.ReservedChannel != OutputChannels.Default && director.Brain.ChannelMask == director.ReservedChannel && Rig() != null && Rig().Name == "PN Camera Rig" && Rig().OutputChannel == director.ReservedChannel, "the viewed world is shown through the director's one gameplay rig on the reserved channel");
            Check(avcam.OutputChannel != director.ReservedChannel && !ReferenceEquals(Rig(), avcam), "the world's own vcam is not admitted: it is read as a data source");
            Check(Mathf.Approximately(director.View.Pitch, 30f) && Mathf.Approximately(director.View.FieldOfView, 38f) && director.View.BackgroundR == 0f, "the world's vcam gave the pitch and lens of the view; the background stays the view's (black)");
            Check(Mathf.Abs(Rig().transform.rotation.eulerAngles.x - 30f) < .01f && Mathf.Abs(Rig().State.Lens.FieldOfView - 38f) < .01f, "the rig takes the angle and lens of the resolved view");

            // hidden-world vcam with the highest priority is never chosen, even if a mod re-enables it
            bvcam.enabled = true; Tick(); Tick();
            Check(Rig().Name == "PN Camera Rig" && bvcam.OutputChannel != director.ReservedChannel, "unadmitted vcam (priority 99) never wins, even enabled");

            // a vcam appearing later that carries the reserved bit forces a new reserved channel
            var oldReserved = director.ReservedChannel; var late = new GameObject("Late mod vcam").AddComponent<CinemachineCamera>(); late.transform.SetParent(cr.transform); late.Priority.Value = 1000; late.OutputChannel = oldReserved;
            view.Register(ar); Tick(); Tick();
            Check(director.ReservedChannel != oldReserved && Rig().OutputChannel == director.ReservedChannel && director.Brain.ChannelMask == director.ReservedChannel && late.OutputChannel == oldReserved, "collision with the reserved bit moves the rig to a new channel in the same step");
            Check(Rig().Name == "PN Camera Rig", "colliding mod vcam still never selected");

            // view B: a camera-only world is read as a data source too: orthographic, the camera's size; no ExternalCamera is added
            local.Select(b); Tick(); Tick();
            Check(Rig().Name == "PN Camera Rig" && director.ViewCamera.orthographic && director.View.Projection == PNCameraProjection.Orthographic && Mathf.Approximately(director.ViewCamera.orthographicSize, 2), "a camera-only orthographic world is shown orthographic through the same rig");
            Check(bmain.GetComponent<CinemachineExternalCamera>() == null, "no ExternalCamera is added to a world camera any more");
            local.Select(a); Tick(); Tick();
            Check(!director.ViewCamera.orthographic && Mathf.Approximately(director.View.Pitch, 30f), "switching back to the perspective world restores the projection and the look");

            // world with no camera and no vcam -> the game default view
            local.Select(d); Tick(); Tick();
            Check(Rig().Name == "PN Camera Rig" && director.View.SameAs(PNCameraView.Default), "a world without a camera takes the game default view");

            // layers: player layer over the world's, refused when invalid, cleared again
            local.Select(a); Tick(); Tick();
            director.SetViewLayer(PNCameraViewSource.Player, new PNCameraViewLayer { Pitch = 20f });
            for (int i = 0; i < 400; i++) { director.Advance(.05f); Tick(); }
            Check(Mathf.Approximately(director.View.Pitch, 20f) && Mathf.Abs(Rig().transform.rotation.eulerAngles.x - 20f) < .05f, "a player layer changes the angle (blended in, reaching the target)");
            bool refused = false; try { director.SetViewLayer(PNCameraViewSource.Player, new PNCameraViewLayer { Pitch = 120f }); } catch (ArgumentOutOfRangeException) { refused = true; }
            Check(refused && Mathf.Approximately(director.View.Pitch, 20f), "an invalid layer is refused and the view stays");
            director.SetViewLayer(PNCameraViewSource.Player, null);
            for (int i = 0; i < 400; i++) { director.Advance(.05f); Tick(); }
            Check(Mathf.Approximately(director.View.Pitch, 30f), "clearing the layer returns to the world's look");
            local.Select(null); Tick();
            Check(director.ViewedWorld == null && director.Brain.ChannelMask == director.ReservedChannel, "no local view: output keeps rendering");

            // teardown
            local.Select(a);
            director.Release(); view.Refresh();
            Check(PNViewCamera.Service == null && director.ViewCamera == null, "director release unregisters the view camera service");
            Check(avcam.OutputChannel == OutputChannels.Default, "the world's vcam channel was never touched");
            Check(amain.enabled && abrain.enabled && !bmain.enabled, "claimed components return to their original state under current view visibility");
        }
        finally { try { director?.Release(); } catch { } view.Release(); foreach (var x in extra) if (x != null) UnityEngine.Object.DestroyImmediate(x); UnityEngine.Object.DestroyImmediate(host); EveRegistrySession.End(); }
        return "PASS " + rows.Count + " native camera director cases (edit-mode Brain ManualUpdate; not rendered-frame acceptance)\n" + string.Join("\n", rows);
    }
}
#endif
