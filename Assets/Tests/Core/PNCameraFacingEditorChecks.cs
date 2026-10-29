#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Native checks of the shared camera-facing system (Docs/VisualPlan.md P0): the new math equals the old Eve billboard formula, the system aligns every target exactly once
//     per tick (also while targets come and go or throw), and the target component keeps the foot fixed. Run in Edit Mode inside a disposable preview scene.
// VI: Kiem tra native he quay ve camera dung chung.
public static class PNCameraFacingEditorChecks
{
    private sealed class Probe : IPNCameraFacing
    {
        public int Calls; public Action OnAlign;
        public bool Align() { Calls++; OnAlign?.Invoke(); return true; }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception("PNCameraFacing check failed: " + message); }

    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        int checks = 0;
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            // 1. The new Align equals the previous Eve billboard formula for several camera poses, parent rotations and scales.
            var parent = new GameObject("facing parent"); SceneManager.MoveGameObjectToScene(parent, preview);
            parent.transform.rotation = Quaternion.Euler(0f, 33f, 5f); parent.transform.position = new Vector3(2f, .4f, 1f);
            var child = new GameObject("facing child"); child.transform.SetParent(parent.transform, false);
            child.transform.localScale = new Vector3(1.2f, .8f, 1f);
            var cameraObject = new GameObject("facing camera"); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.AddComponent<Camera>();
            var anchor = new Vector3(0f, -.2f, 0f); const float offset = .25f;
            foreach (var euler in new[] { new Vector3(0f, 25f, 10f), new Vector3(45f, 25f, 10f), new Vector3(60f, -70f, 0f), new Vector3(-20f, 200f, 15f), new Vector3(89f, 0f, 0f) })
            {
                camera.transform.rotation = Quaternion.Euler(euler);
                Require(PNCameraFacing.Align(child.transform, camera, PNFacingMode.PitchYaw, offset, anchor, false), "Align returned false with a camera");
                var f = camera.transform.forward;
                var legacyFacing = Quaternion.AngleAxis(Mathf.Atan2(-f.y, Mathf.Sqrt(f.x * f.x + f.z * f.z)) * Mathf.Rad2Deg, Vector3.right);
                var legacyRotation = Quaternion.Inverse(parent.transform.rotation) * legacyFacing;
                var legacyPosition = anchor - legacyRotation * Vector3.Scale(Vector3.down * offset, child.transform.localScale);
                Require(Quaternion.Angle(child.transform.localRotation, legacyRotation) < .001f, "rotation differs from the old formula at " + euler);
                Require(Vector3.Distance(child.transform.localPosition, legacyPosition) < 1e-5f, "position differs from the old formula at " + euler);
                var foot = child.transform.TransformPoint(Vector3.down * offset);
                Require(Vector3.Distance(foot, parent.transform.TransformPoint(anchor)) < 1e-4f, "the foot moved at " + euler);
                checks += 3;
            }
            Require(!PNCameraFacing.Align(null, camera, PNFacingMode.Pitch, 0f, Vector3.zero, false), "a null transform is refused"); checks++;

            // 2. The system: exactly one Align per target per tick; registering twice counts once; unregister while ticking is safe; a throwing target does not stop the others.
            int start = PNCameraFacingSystem.Count;
            var probes = new List<Probe>();
            for (int i = 0; i < 200; i++) { var p = new Probe(); probes.Add(p); PNCameraFacingSystem.Register(p); }
            PNCameraFacingSystem.Register(probes[0]);
            Require(PNCameraFacingSystem.Count == start + 200, "200 targets registered once each"); checks++;
            int ticks = PNCameraFacingSystem.TickCount;
            PNCameraFacingSystem.Tick();
            Require(PNCameraFacingSystem.TickCount == ticks + 1, "a tick is counted");
            foreach (var p in probes) Require(p.Calls == 1, "every target aligned exactly once per tick");
            checks += 2;
            probes[5].OnAlign = () => PNCameraFacingSystem.Unregister(probes[5]);
            probes[100].OnAlign = () => PNCameraFacingSystem.Unregister(probes[3]);
            PNCameraFacingSystem.Tick();
            Require(PNCameraFacingSystem.Count == start + 198, "targets can unregister themselves or others while the tick runs"); checks++;
            PNCameraFacingSystem.Tick();
            Require(probes[5].Calls == 2 && probes[3].Calls <= 3, "an unregistered target no longer aligns"); checks++;
            var thrower = new Probe { OnAlign = () => throw new InvalidOperationException("probe failure") };
            PNCameraFacingSystem.Register(thrower);
            int before = probes[10].Calls, errors = PNCameraFacingSystem.ErrorCount;
            bool logging = Debug.unityLogger.logEnabled; Debug.unityLogger.logEnabled = false;   // the failure is expected: keep it out of the Console
            try { PNCameraFacingSystem.Tick(); } finally { Debug.unityLogger.logEnabled = logging; }
            Require(probes[10].Calls == before + 1 && thrower.Calls == 1 && PNCameraFacingSystem.ErrorCount == errors + 1, "a throwing target is counted and the rest still align"); checks++;
            PNCameraFacingSystem.Unregister(thrower);
            foreach (var p in probes) PNCameraFacingSystem.Unregister(p);
            Require(PNCameraFacingSystem.Count == start, "everything unregistered"); checks++;
            try { PNCameraFacingSystem.Register(null); Require(false, "null target accepted"); } catch (ArgumentNullException) { checks++; }

            // 3. The component: Edit Mode aligns on enable, keeps the foot fixed, rejects bad values.
            var propRoot = new GameObject("prop root"); SceneManager.MoveGameObjectToScene(propRoot, preview);
            var prop = new GameObject("prop"); prop.SetActive(false); prop.transform.SetParent(propRoot.transform, false);
            var target = prop.AddComponent<PNCameraFacingTarget>();
            var settings = new SerializedObject(target);
            settings.FindProperty("viewCamera").objectReferenceValue = camera;
            settings.FindProperty("footOffset").floatValue = .3f; settings.FindProperty("footAnchor").vector3Value = new Vector3(0f, -.1f, 0f);
            settings.ApplyModifiedProperties();
            camera.transform.rotation = Quaternion.Euler(50f, 10f, 0f);
            prop.SetActive(true);
            Require(Quaternion.Angle(prop.transform.rotation, Quaternion.Euler(50f, 0f, 0f)) < .01f, "the component aligned on enable"); checks++;
            Require(Vector3.Distance(prop.transform.TransformPoint(Vector3.down * .3f), propRoot.transform.TransformPoint(new Vector3(0f, -.1f, 0f))) < 1e-4f, "the component keeps the foot fixed"); checks++;
            Require(PNCameraFacingSystem.Count == start, "Edit Mode does not register in the play loop"); checks++;
            bool threw = false; try { target.FootOffset = -1f; } catch (ArgumentOutOfRangeException) { threw = true; }
            Require(threw, "a negative foot offset is refused");
            threw = false; try { target.Mode = (PNFacingMode)7; } catch (ArgumentOutOfRangeException) { threw = true; }
            Require(threw, "an unknown mode is refused"); checks += 2;
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        return "PASS " + checks + " camera-facing checks: equals the old billboard formula (5 poses, parent rotation and scale), one align per target per tick (200 targets), unregister while ticking, throwing target isolated, component foot fixed, bad values refused.";
    }
}
#endif
