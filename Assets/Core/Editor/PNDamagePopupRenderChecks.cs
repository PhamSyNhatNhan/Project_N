#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class PNDamagePopupRenderChecks
{
    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Requires Edit Mode.");
        var originalScene = SceneManager.GetActiveScene(); bool dirty = originalScene.isDirty;
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null, cameraRoot = null; PNDamagePopupBloom bloom = null;
        RenderTexture target = null; Texture2D readback = null;
        var oldActive = RenderTexture.active;
        try
        {
            root = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("PNUI/PNDamagePopup"));
            SceneManager.MoveGameObjectToScene(root, scene);
            var popup = root.GetComponent<PNDamagePopup>();
            cameraRoot = new GameObject("Popup render check camera"); SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.orthographicSize = .9f; camera.aspect = 1f; camera.transform.position = new Vector3(0f, .55f, -5f);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.scene = scene;
            var data = cameraRoot.AddComponent<UniversalAdditionalCameraData>(); data.SetRenderer(1);
            bloom = PNDamagePopupBloom.Ensure(camera);
            var volume = bloom.GetComponent<Volume>();
            if (!UnityEditor.SceneManagement.StageUtility.IsGameObjectRenderedByCamera(volume.gameObject, camera))
                throw new Exception("Test camera stage excludes popup Volume.");
            if (System.Array.IndexOf(VolumeManager.instance.GetVolumes(data.volumeLayerMask), volume) < 0)
                throw new Exception("Popup Volume not registered.");
            popup.Begin(128f, new Vector3(0f, .55f, 0f), new Color(.15f, .9f, 1f), null, true,
                new[] { EveEngine.EveId.Parse("project_n:fire"), EveEngine.EveId.Parse("project_n:ice"), EveEngine.EveId.Parse("project_n:lightning") });
            popup.Advance(0f, camera);
            target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGBHalf); target.Create();
            readback = new Texture2D(512, 512, TextureFormat.RGBAFloat, false, true);
            Color[] Render(bool post)
            {
                data.renderPostProcessing = post;
                // StandardRequest updates camera Volume state as normal gameplay rendering does.
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new Exception("URP render request unsupported.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); readback.Apply();
                return readback.GetPixels();
            }
            var off = Render(false); var on = Render(true);
            float peak = 0f; int halo = 0;
            for (int i = 0; i < off.Length; i++)
            {
                float a = Mathf.Max(off[i].r, off[i].g, off[i].b), b = Mathf.Max(on[i].r, on[i].g, on[i].b);
                peak = Mathf.Max(peak, a);
                if (a < .001f && b > .01f) halo++;
            }
            if (peak <= 1f) throw new Exception("Popup shader did not render HDR pixels: peak=" + peak);
            if (halo < 20) throw new Exception("Bloom did not spread outside the text: halo=" + halo + ", peak=" + peak +
                ", stack=" + VolumeManager.instance.stack.GetComponent<Bloom>().intensity.value + ", mask=" + data.volumeLayerMask.value +
                ", cameraType=" + camera.cameraType + ", profile=" + bloom.GetComponent<Volume>().sharedProfile.components.Count);
            popup.Advance(.3f, camera);
            var heldOff = Render(false); var heldOn = Render(true);
            if (Halo(heldOff, heldOn) < 20) throw new Exception("Bloom disappeared during the held middle of the popup lifetime.");
            popup.Advance(.14f, camera);
            var fadingOff = Render(false); var fadingOn = Render(true);
            if (Halo(fadingOff, fadingOn) < 20 || Peak(fadingOff) >= Peak(heldOff))
                throw new Exception("Late-life Bloom did not remain visible and diminish with the popup.");
            var png = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            for (int i = 0; i < on.Length; i++) on[i] = new Color(Mathf.LinearToGammaSpace(on[i].r), Mathf.LinearToGammaSpace(on[i].g), Mathf.LinearToGammaSpace(on[i].b), 1f);
            png.SetPixels(on); png.Apply();
            Directory.CreateDirectory("F:/unity/Project_N/.utmp");
            File.WriteAllBytes("F:/unity/Project_N/.utmp/PNDamagePopupGlow.png", png.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(png);
            // Test the normal number alone: element icons must not conceal missing text Bloom.
            popup.Begin(128d, new Vector3(0f, .55f, 0f), new Color(.15f, .9f, 1f), null, false, Array.Empty<EveEngine.EveId>());
            popup.Advance(0f, camera);
            var normalOff = Render(false); var normalOn = Render(true);
            float normalPeak = Peak(normalOff); int normalHalo = Halo(normalOff, normalOn);
            if (normalPeak <= 1f || Mathf.Abs(normalPeak - peak) > .01f || normalHalo < 20)
                throw new Exception("Normal and critical damage need equal HDR brightness: peak=" + normalPeak + ", critical=" + peak + ", halo=" + normalHalo);
            popup.Advance(.44f, camera);
            var normalFadingOff = Render(false); var normalFadingOn = Render(true);
            if (Peak(normalFadingOff) >= normalPeak || Halo(normalFadingOff, normalFadingOn, .0001f) < 20)
                throw new Exception("Normal Bloom did not persist while fading.");
            return "PASS actual URP popup render: crit + all 3 elements, HDR peak=" + peak + ", bloom halo pixels=" + halo +
                ", normal peak=" + normalPeak + ", normal halo=" + normalHalo + ", held and fading Bloom confirmed; preview .utmp/PNDamagePopupGlow.png";
        }
        finally
        {
            RenderTexture.active = oldActive;
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (bloom != null) UnityEngine.Object.DestroyImmediate(bloom.gameObject);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (cameraRoot != null) UnityEngine.Object.DestroyImmediate(cameraRoot);
            EditorSceneManager.ClosePreviewScene(scene);
            if (originalScene.isDirty != dirty) throw new Exception("Render checks changed gameplay scene.");
        }
    }
    private static int Halo(Color[] off, Color[] on, float minimumGlow = .01f)
    {
        int count = 0;
        for (int i = 0; i < off.Length; i++)
            if (Mathf.Max(off[i].r, off[i].g, off[i].b) < .00001f && Mathf.Max(on[i].r, on[i].g, on[i].b) > minimumGlow) count++;
        return count;
    }
    private static float Peak(Color[] pixels)
    {
        float peak = 0f; foreach (var p in pixels) peak = Mathf.Max(peak, p.r, p.g, p.b); return peak;
    }
}
#endif
