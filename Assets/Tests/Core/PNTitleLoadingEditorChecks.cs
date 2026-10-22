#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

public static class PNTitleLoadingEditorChecks
{
    public static string Run()
    {
        int checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; }
        foreach (var path in new[] { "PNFont/Philosopher-Regular", "PNFont/Philosopher-Bold" })
        {
            var font = Resources.Load<Font>(path); Check(font != null, path + " loads");
            font.RequestCharactersInTexture("Bắt đầu Cài đặt Thoát Đang tải… Thử lại", 29);
            foreach (char c in "BắtđầuCàiđặtThoátĐangtải…Thửlại") Check(font.HasCharacter(c), path + " missing " + c);
        }
        var context = new PNTransitionContext(PNTransitionReason.Join, "", "", false, .4f, 20, PNTransitionCoverage.Opaque);
        using (var screen = new PNUiToolkitTransitionScreen(PNTransitionPayload.Default))
        {
            screen.Show(context);
            Check(screen.ReadyToHide && screen.Alpha == 1, "default immediate coverage unchanged");
            Check(screen.Root.Q<PNTitleLoadingVisual>() != null && screen.Text == "Đang tải…", "default has new visual");
            screen.Tick(new PNTransitionState(PNTransitionPhase.Loading, .5f, 2), 2);
            screen.Hide("test"); Check(!screen.Finished, "default still fades out");
            screen.Tick(default, .125f); Check(Mathf.Abs(screen.Alpha - .5f) < .001f, "half fade unchanged");
            screen.Tick(default, .125f); Check(screen.Finished, "default completes fade unchanged");
        }
        // A distinct but value-identical payload is still mod-owned presentation.
        var d = PNTransitionPayload.Default;
        var custom = new PNTransitionPayload(d.R,d.G,d.B,d.A,d.Text,d.FadeIn,d.FadeOut,d.MinimumSeconds,d.MaximumSeconds,d.Coverage,null);
        using (var screen = new PNUiToolkitTransitionScreen(custom))
        {
            screen.Show(context);
            Check(screen.Text == "Loading" && screen.Root.Q<PNTitleLoadingVisual>() == null, "custom presentation preserved");
            Check(screen.Root.style.backgroundColor.value == new Color(d.R,d.G,d.B,d.A), "custom colour preserved");
        }
        return "PNTitleLoadingEditorChecks PASS " + checks;
    }
}
#endif
