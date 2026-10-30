#if UNITY_5_3_OR_NEWER
using System;
using System.IO;
using UnityEngine;

// The only active settings writer. Localization publishes catalogs independently of preferences.
public static class PNSettingsService
{
    private static PNSettingsStore store;
    private static PNUserSettings current;
    private static bool ownsFramePolicy;
    private static int previousFps, previousVSync;
    public static PNSettingsLoadStatus LoadStatus { get; private set; }
    public static string SaveError { get; private set; }
    public static event Action Changed;
    // Raised by Apply (the one Save) after it is stored, when the ink or shadow choice changed: the Hall re-builds its atlases under the cover (PNVisualLookApply).
    public static event Action VisualLookChanged;
    public static PNUserSettings Current { get { EnsureLoaded(); return current; } }
    public static string FilePath { get { EnsureLoaded(); return store.FilePath; } }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { Shutdown(); Changed = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register() { Application.quitting -= Shutdown; Application.quitting += Shutdown; }
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorStop()
    { UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayState; UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayState; }
    private static void OnEditorPlayState(UnityEditor.PlayModeStateChange state)
    {
        const string key="ProjectN.Settings.EditorFramePolicy";
        // Unity can reload Quality settings during Play entry/exit. Capture the Editor
        // values before that reload, across domain reload, and restore after it finishes.
        if(state==UnityEditor.PlayModeStateChange.ExitingEditMode)
        { UnityEditor.SessionState.SetBool(key,true);UnityEditor.SessionState.SetInt(key+".fps",Application.targetFrameRate);UnityEditor.SessionState.SetInt(key+".vsync",QualitySettings.vSyncCount); }
        if(state==UnityEditor.PlayModeStateChange.ExitingPlayMode)Shutdown();
        if(state==UnityEditor.PlayModeStateChange.EnteredEditMode&&UnityEditor.SessionState.GetBool(key,false))
        { Application.targetFrameRate=UnityEditor.SessionState.GetInt(key+".fps",-1);QualitySettings.vSyncCount=UnityEditor.SessionState.GetInt(key+".vsync",0);UnityEditor.SessionState.EraseBool(key); }
    }
#endif
    public static void Shutdown()
    {
        if (ownsFramePolicy) { Application.targetFrameRate = previousFps; QualitySettings.vSyncCount = previousVSync; ownsFramePolicy = false; }
        store = null; current = null; SaveError = null;
    }
    public static void Load(PNSettingsStore source)
    {
        if (current != null) throw new InvalidOperationException("Settings are already loaded.");
        store = source ?? throw new ArgumentNullException(nameof(source));
        var result = store.Load(); current = result.Value; LoadStatus = result.Status;
        // A phone starts with the lightest look: baked shadow (ink is baked by default everywhere). Only a missing/corrupt file gets this; a saved choice is kept.
        if (Application.isMobilePlatform && (result.Status == PNSettingsLoadStatus.Missing || result.Status == PNSettingsLoadStatus.Corrupt)) current = current.WithPaperShadow(PNPaperShadowMode.Baked);
        SaveError = result.Status == PNSettingsLoadStatus.Unsupported || result.Status == PNSettingsLoadStatus.Unavailable ? result.Diagnostic : null;
        ApplyFramePolicy(); ApplyVisualPolicy();
        if (result.Status == PNSettingsLoadStatus.Legacy) Persist();
    }
    public static void EnsureLoaded()
    {
        if (current != null) return;
        string folder = Path.Combine(Application.persistentDataPath, "Settings");
        // Verification uses an isolated root, never the player's settings. No production command-line override.
        if (Debug.isDebugBuild || Application.isEditor)
            foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith("-pn-settings-fixture=", StringComparison.Ordinal)) folder = Path.Combine(arg.Substring("-pn-settings-fixture=".Length), "Settings");
        Load(new PNSettingsStore(folder));
    }
    internal static void ResolveDefaultLocale(string code)
    { EnsureLoaded(); if (current.Locale == null) current = current.WithLocale(code); }
    public static bool SetLocale(string code)
    {
        EnsureLoaded(); code = PNLocalizationCatalog.Code(code);
        PNLocalization.Select(code, false); // Validates/applies before modifying durable preference.
        bool changed = current.Locale != code;
        current = current.WithLocale(code);
        bool saved = !changed && SaveError == null && File.Exists(store.FilePath) || Persist();
        Notify(); return saved;
    }
    public static bool SetFrameRate(int value)
    {
        EnsureLoaded(); var next = current.WithFrameRate(value);
        bool changed = current.TargetFrameRate != value; current = next; ApplyFramePolicy();
        if (current.Locale == null && PNLocalization.IsReady) ResolveDefaultLocale(PNLocalization.Selected);
        bool saved = !changed && SaveError == null && File.Exists(store.FilePath) || Persist();
        Notify(); return saved;
    }
    public static bool SetMobileIconSet(string id)
    {
        EnsureLoaded();if(!PNMobileStyleCatalog.HasIconSet(id))throw new ArgumentException("Unknown mobile icon set.");
        current=current.WithMobileStyle(id,current.MobilePalette);bool saved=Persist();Notify();return saved;
    }
    public static bool SetMobilePalette(string id)
    {
        EnsureLoaded();if(!PNMobileStyleCatalog.HasPalette(id))throw new ArgumentException("Unknown mobile palette.");
        current=current.WithMobileStyle(current.MobileIconSet,id);bool saved=Persist();Notify();return saved;
    }
    public static bool SetAutoTargetMode(PNTargetMode mode)
    {
        EnsureLoaded();var next=current.WithAutoTargetMode(mode);bool changed=current.AutoTargetMode!=mode;current=next;
        if (current.Locale == null && PNLocalization.IsReady) ResolveDefaultLocale(PNLocalization.Selected);
        bool saved = !changed && SaveError == null && File.Exists(store.FilePath) || Persist();
        Notify(); return saved;
    }
    public static bool SetShowPcControls(bool value)
    {
        EnsureLoaded();bool changed=current.ShowPcControls!=value;current=current.WithShowPcControls(value);
        if(current.Locale==null&&PNLocalization.IsReady)ResolveDefaultLocale(PNLocalization.Selected);
        bool saved=!changed&&SaveError==null&&File.Exists(store.FilePath)||Persist();
        Notify();return saved;
    }
    public static bool SetShowPcBindingLabels(bool value)
    {
        EnsureLoaded();bool changed=current.ShowPcBindingLabels!=value;current=current.WithShowPcBindingLabels(value);
        if(current.Locale==null&&PNLocalization.IsReady)ResolveDefaultLocale(PNLocalization.Selected);
        bool saved=!changed&&SaveError==null&&File.Exists(store.FilePath)||Persist();
        Notify();return saved;
    }
    public static bool SetPaperShadow(PNPaperShadowMode mode)
    {
        EnsureLoaded();bool changed=current.PaperShadow!=mode;current=current.WithPaperShadow(mode);ApplyVisualPolicy();
        if(current.Locale==null&&PNLocalization.IsReady)ResolveDefaultLocale(PNLocalization.Selected);
        bool saved=!changed&&SaveError==null&&File.Exists(store.FilePath)||Persist();
        Notify();return saved;
    }
    public static bool SetInkMode(PNInkMode mode)
    {
        EnsureLoaded();bool changed=current.InkMode!=mode;current=current.WithInkMode(mode);ApplyVisualPolicy();
        if(current.Locale==null&&PNLocalization.IsReady)ResolveDefaultLocale(PNLocalization.Selected);
        bool saved=!changed&&SaveError==null&&File.Exists(store.FilePath)||Persist();
        Notify();return saved;
    }
    // The shadow and ink looks only change in the title screen and the Hall (a rebuild needs the cover and no run in progress). Everything else is editable anywhere.
    public static bool VisualLookChangeAllowed { get { return PNGameFlow.Phase == PNFlowPhase.Hall; } }   // the Title has no session yet, and Phase falls back to Hall there
    // What a Save did to the visual look: a rebuild of the atlas set is needed when the ink mode changed (the shadow mode alone only re-selects materials, or re-bakes the shade).
    public static bool LookChangedBy(PNUserSettings draft) => draft.InkMode != current.InkMode || draft.PaperShadow != current.PaperShadow;
    // One Save for the whole settings view: validates the draft, applies every part, writes once and notifies once. Returns whether the file was written.
    public static bool Apply(PNUserSettings draft)
    {
        EnsureLoaded();if(draft==null)throw new ArgumentNullException(nameof(draft));
        if(!VisualLookChangeAllowed){draft=draft.WithInkMode(current.InkMode).WithPaperShadow(current.PaperShadow);}
        if(draft.Locale!=null)PNLocalization.Select(PNLocalizationCatalog.Code(draft.Locale),false);
        bool look=LookChangedBy(draft);bool changed=draft.ToJsonOrNull()!=current.ToJsonOrNull();current=draft;ApplyFramePolicy();ApplyVisualPolicy();
        if(current.Locale==null&&PNLocalization.IsReady)ResolveDefaultLocale(PNLocalization.Selected);
        bool saved=!changed&&SaveError==null&&File.Exists(store.FilePath)||Persist();
        Notify();
        if(look&&VisualLookChanged!=null)foreach(Action listener in VisualLookChanged.GetInvocationList())try{listener();}catch(Exception e){Debug.LogException(e);}
        return saved;
    }
    // The Visual look reads the player's overlap-shadow and ink choices from here (actors re-select their material on the next tick).
    private static void ApplyVisualPolicy() { PNVisualPaperSettings.Mode = current.PaperShadow; PNVisualPaperSettings.Ink = current.InkMode; }
    private static void ApplyFramePolicy()
    {
        if (!ownsFramePolicy) { previousFps = Application.targetFrameRate; previousVSync = QualitySettings.vSyncCount; ownsFramePolicy = true; }
        if (!Application.isMobilePlatform) QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = current.TargetFrameRate == 0 ? (Application.isMobilePlatform ? Math.Max(30, (int)Screen.currentResolution.refreshRateRatio.value) : -1) : current.TargetFrameRate;
    }
    private static bool Persist()
    {
        try { store.Save(current); SaveError = null; return true; }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
        { SaveError = e.Message; Debug.LogWarning("Settings not saved: " + e.Message); return false; }
    }
    private static void Notify()
    { if (Changed == null) return; foreach (Action listener in Changed.GetInvocationList()) try { listener(); } catch (Exception e) { Debug.LogException(e); } }
}
#endif
