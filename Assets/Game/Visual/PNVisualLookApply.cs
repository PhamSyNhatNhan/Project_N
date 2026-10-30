#if UNITY_5_3_OR_NEWER
using EveEngine;
using UnityEngine;

// EN: Applies a changed ink or shadow look to the running Hall. The Settings Save raises PNSettingsService.VisualLookChanged; in the Hall this opens the loading cover (a Custom
//     transition cycle on the same world), rebuilds the atlas set in steps while it is up (PNVisualRuntime.BeginRebuild, driven by PNShaderWarmup.Pump), re-attaches every actor
//     in one frame and lets the cover lift (the warm-up reports done). In the Title nothing is running: the next content load builds the right set.
// VI: Áp dụng thay đổi mực/bóng cho Hall đang chạy: màn che, dựng lại bộ atlas theo bước, gắn lại actor, rồi hạ màn che.
public static class PNVisualLookApply
{
    public static int Requests { get; private set; }
    public static bool LastCovered { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { PNSettingsService.VisualLookChanged -= OnLookChanged; PNSettingsService.VisualLookChanged += OnLookChanged; Requests = 0; LastCovered = false; }
    private static void OnLookChanged() => Request();
    // Returns true when a rebuild was started.
    public static bool Request()
    {
        Requests++;
        if (!EveRegistrySession.IsReady || !PNSettingsService.VisualLookChangeAllowed) return false;
        var registry = EveRegistrySession.Context.Registry;
        if (!registry.Snapshot().ContainsKey(typeof(PNVisualPartDefinition))) return false;
        var controller = PNTransitionController.Current; var world = PNLocalWorldContext.Current?.World?.DefinitionId.ToString() ?? "";
        PNVisualRuntime.BeginRebuild(registry);
        LastCovered = controller != null;
        controller?.BeginCycle(PNTransitionReason.Custom, world, world, false, default, default);
        return true;
    }
}
#endif
