using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Player review of the Buff glow: in the Hall it gives the local player the six stock Buffs that have art plus one without (legacy fallback),
// reads every card's glow element (texture present, tint) and saves the frame and a magnified crop of the HUD Buff grid.
// Enabled only by -pn-buff-glow-review=<directory> (use with --pn-auto-start=hall). Run the exe in a window.
public sealed class PNBuffGlowReview : MonoBehaviour
{
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);
    private string output; private readonly StringBuilder trace = new StringBuilder(); private int errors; private readonly List<string> errorLines = new List<string>();
    private int fails;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (Arg("-pn-buff-glow-review=") == null) return;
        var go = new GameObject("PNBuffGlowReview"); DontDestroyOnLoad(go); var self = go.AddComponent<PNBuffGlowReview>();
        Application.logMessageReceived += (c, s, t) => { if (t == LogType.Error || t == LogType.Exception) { self.errors++; if (self.errorLines.Count < 5) self.errorLines.Add(t + ": " + c); } };
    }

    private void Log(string line) { trace.AppendLine(Time.realtimeSinceStartup.ToString("0.00") + "s " + line); }
    private void Check(bool ok, string text) { Log((ok ? "PASS " : "FAIL ") + text); if (!ok) fails++; }

    private IEnumerator Start()
    {
        output = Arg("-pn-buff-glow-review="); Directory.CreateDirectory(output); Application.runInBackground = true;
        float limit = Time.realtimeSinceStartup + 120; bool ready = false;
        while (Time.realtimeSinceStartup < limit)
        {
            ready = EveRegistrySession.IsReady && PNGameFlow.Phase == PNFlowPhase.Hall && PNLocalWorldContext.Current?.Actor != null && PNLocalWorldContext.Current.Actor.IsReady
                    && FindObjectsByType<PNUiRuntime>(FindObjectsSortMode.None).Any(u => u.HudPlayer != null);
            if (ready) break; yield return null;
        }
        Log("hall ready=" + ready); if (!ready) { Finish("NOT-ready"); yield break; }
        yield return new WaitForSeconds(2f);
        var ui = FindObjectsByType<PNUiRuntime>(FindObjectsSortMode.None).First(); var player = ui.HudPlayer;
        var buffs = player.GetComponent<PNBuffManager>().Buffs; var source = PNCombatSource.Capture(player);
        var ids = new[] { "burn", "frostbite", "electrocution", "fire_vulnerability", "ice_vulnerability", "lightning_vulnerability" };
        foreach (var id in ids) { try { buffs.Apply(new PNBuffApplyRequest(EveId.Parse("project_n:" + id), source, null)); } catch (Exception e) { Log("apply " + id + " failed: " + e.Message); fails++; } }
        try { buffs.Apply(new PNBuffApplyRequest(EveId.Parse("project_n:timescale_reduction_area"), source, new JObject { { "reduction", 0 }, { "minimumReduction", 0 } })); } catch (Exception e) { Log("apply fallback failed: " + e.Message); fails++; }
        yield return new WaitForSeconds(1.2f);
        var root = ui.HudRoot.Q("player-buffs"); var cards = root.Query(className: "buff-card").ToList();
        Check(cards.Count == 7, "seven cards on the HUD, got " + cards.Count);
        var expected = new Dictionary<string, Color32> { { "PNBurn", new Color32(255, 64, 48, 255) }, { "PNFrostbite", new Color32(80, 150, 255, 255) }, { "PNElectrocution", new Color32(255, 205, 70, 255) },
            { "PNFireVulnerability", new Color32(255, 64, 48, 255) }, { "PNIceVulnerability", new Color32(80, 150, 255, 255) }, { "PNLightningVulnerability", new Color32(255, 205, 70, 255) } };
        int matched = 0, fallbackWhite = 0;
        foreach (var card in cards)
        {
            var glow = card.Q(className: "buff-glow"); var image = card.Q(className: "buff-image");
            string sprite = image.resolvedStyle.backgroundImage.sprite != null ? image.resolvedStyle.backgroundImage.sprite.name : "none";
            var tint = glow.resolvedStyle.unityBackgroundImageTintColor; var tex = glow.resolvedStyle.backgroundImage.texture;
            Log("card " + card.name + " icon=" + sprite + " glowTexture=" + (tex != null ? tex.width + "px" : "none") + " tint=" + ((Color32)tint) + " size=" + card.resolvedStyle.width + "x" + card.resolvedStyle.height);
            if (expected.TryGetValue(sprite, out var want)) { if (tex != null && (Color32)tint is var t && t.r == want.r && t.g == want.g && t.b == want.b) matched++; }
            else if (tex != null && ((Color32)tint).r == 255 && ((Color32)tint).g == 255 && ((Color32)tint).b == 255) fallbackWhite++;
        }
        Check(matched == 6, "six icons carry their glow colour, matched " + matched);
        Check(fallbackWhite == 1, "the card without art shows the fallback icon with a white glow, got " + fallbackWhite);
        yield return Shot("hud", root);
        // The icon sizes to choose from: the card stays 30 px, the icon (and its baked glow, a quarter bigger each side) is inset by 0, 2 (the style sheet), 3 px.
        foreach (var inset in new[] { 0f, 2f, 3f })
        {
            float glowInset = inset - .25f * (30 - 2 * inset);
            foreach (var card in cards)
            {
                var image = card.Q(className: "buff-image"); var glow = card.Q(className: "buff-glow");
                image.style.left = inset; image.style.right = inset; image.style.top = inset; image.style.bottom = inset;
                glow.style.left = glowInset; glow.style.right = glowInset; glow.style.top = glowInset; glow.style.bottom = glowInset;
            }
            yield return new WaitForSeconds(.3f); yield return Shot("hud_icon" + (30 - 2 * (int)inset), root);
        }
        Finish(fails == 0 ? "PASS" : "FAIL");
    }

    private IEnumerator Shot(string label, VisualElement region)
    {
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, label + ".png"), tex.EncodeToPNG());
        var panel = region.panel; float k = panel != null ? Screen.width / panel.visualTree.layout.width : 1f; var b = region.worldBound;
        int pad = 24; int x = Mathf.Max(0, Mathf.RoundToInt(b.x * k) - pad), w = Mathf.Min(tex.width - x, Mathf.RoundToInt(b.width * k) + 2 * pad);
        int top = Mathf.Max(0, Mathf.RoundToInt(b.y * k) - pad), h = Mathf.Min(tex.height - top, Mathf.RoundToInt(Mathf.Max(b.height, 70) * k) + 2 * pad);
        int y = tex.height - top - h; const int scale = 5;
        var crop = tex.GetPixels32(); var zoom = new Texture2D(w * scale, h * scale, TextureFormat.RGBA32, false); var outPx = new Color32[w * scale * h * scale];
        for (int yy = 0; yy < h * scale; yy++) for (int xx = 0; xx < w * scale; xx++) outPx[yy * w * scale + xx] = crop[(y + yy / scale) * tex.width + x + xx / scale];
        zoom.SetPixels32(outPx); zoom.Apply(); File.WriteAllBytes(Path.Combine(output, label + "_zoom.png"), zoom.EncodeToPNG());
        Log("shot " + label + " screen " + tex.width + "x" + tex.height + " crop " + w + "x" + h + " at " + x + "," + y);
        Destroy(tex); Destroy(zoom);
    }

    private void Finish(string verdict)
    {
        Log("errors=" + errors); foreach (var line in errorLines) Log("  " + line);
        File.WriteAllText(Path.Combine(output, "result.txt"), verdict + "\n" + trace); Application.Quit();
    }
}
