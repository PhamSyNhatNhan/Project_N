#if UNITY_EDITOR
using System;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class PNSettingsEditorChecks
{
    public static int Run(string root)
    {
        if(Application.isPlaying)throw new InvalidOperationException("Idle Edit required.");
        int checks=PNSettingsChecks.Run(root);void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        int oldFps=Application.targetFrameRate,oldVSync=QualitySettings.vSyncCount;var oldShadow=PNVisualPaperSettings.Mode;var oldInk=PNVisualPaperSettings.Ink;
        string dir=Path.Combine(root,".utmp/Settings/Native-"+Guid.NewGuid().ToString("N"));
        PNSettingsService.Shutdown();PNLocalization.Shutdown();
        try
        {
            PNSettingsService.Load(new PNSettingsStore(dir));PNLocalization.EnsureBase();
            Check(PNSettingsService.Current.Locale=="vi","manifest resolves default");
            Check(PNSettingsService.SetLocale("en")&&PNLocalization.Selected=="en","shared locale setter");
            Check(PNSettingsService.SetFrameRate(120)&&Application.targetFrameRate==120&&QualitySettings.vSyncCount==0,"FPS policy");
            Check(new PNSettingsStore(dir).Load().Value.Locale=="en","FPS preserves locale");
            Check(PNSettingsService.SetShowPcControls(true)&&new PNSettingsStore(dir).Load().Value.ShowPcControls,"PC HUD setter saves");
            Check(PNSettingsService.SetShowPcBindingLabels(false)&&!new PNSettingsStore(dir).Load().Value.ShowPcBindingLabels&&PNSettingsService.Current.ShowPcControls,"label setter saves independently");
            PNSettingsService.SetAutoTargetMode(PNTargetMode.LowestHealth);PNSettingsService.SetFrameRate(90);
            Check(PNSettingsService.Current.ShowPcControls&&PNSettingsService.Current.Locale=="en","mode/FPS setters retain PC HUD");
            PNLocalization.Select("vi",false);Check(new PNSettingsStore(dir).Load().Value.Locale=="en","temporary locale selection not persisted");
            Check(PNSettingsService.SetFrameRate(0)&&Application.targetFrameRate==-1,"unlimited mapping");
            PNSettingsService.Shutdown();PNSettingsService.Load(new PNSettingsStore(dir));
            Check(PNSettingsService.Current.Locale=="en"&&PNSettingsService.Current.TargetFrameRate==0&&PNSettingsService.Current.ShowPcControls,"service reload includes PC HUD");
            Check(!PNSettingsService.Current.ShowPcBindingLabels,"service reload restores hidden labels");
            Check(PNSettingsService.Current.PaperShadow==PNPaperShadowMode.Actors&&PNVisualPaperSettings.Mode==PNPaperShadowMode.Actors,"the default paper shadow is applied to the visual look at load");
            Check(PNSettingsService.SetPaperShadow(PNPaperShadowMode.Baked)&&PNVisualPaperSettings.Mode==PNPaperShadowMode.Baked&&new PNSettingsStore(dir).Load().Value.PaperShadow==PNPaperShadowMode.Baked,"paper shadow setter applies to the visual look and saves");
            Check(PNSettingsService.Current.ShowPcControls&&!PNSettingsService.Current.ShowPcBindingLabels&&PNSettingsService.Current.Locale=="en","paper shadow setter keeps the other preferences");
            PNSettingsService.SetFrameRate(60);PNSettingsService.Shutdown();PNVisualPaperSettings.Mode=PNPaperShadowMode.Off;PNSettingsService.Load(new PNSettingsStore(dir));
            Check(PNSettingsService.Current.PaperShadow==PNPaperShadowMode.Baked&&PNVisualPaperSettings.Mode==PNPaperShadowMode.Baked,"a reload restores the saved paper shadow into the visual look");
            PNSettingsService.SetPaperShadow(PNPaperShadowMode.Actors);PNSettingsService.SetFrameRate(0);
            Check(PNSettingsService.Current.InkMode==PNInkMode.Baked&&PNVisualPaperSettings.Ink==PNInkMode.Baked,"the default ink is baked and applied to the visual look at load");
            Check(PNSettingsService.SetInkMode(PNInkMode.LiveActors)&&PNVisualPaperSettings.Ink==PNInkMode.LiveActors&&new PNSettingsStore(dir).Load().Value.InkMode==PNInkMode.LiveActors&&PNSettingsService.Current.PaperShadow==PNPaperShadowMode.Actors,"ink setter applies, saves and leaves the shadow alone");
            Check(PNVisualPaperSettings.IsLive(false)&&PNVisualPaperSettings.IsLive(true),"live actors: every actor is live");
            PNSettingsService.SetInkMode(PNInkMode.LiveKeyActors);Check(PNVisualPaperSettings.IsLive(true)&&!PNVisualPaperSettings.IsLive(false),"live key actors: only key actors are live");
            Check(PNSettingsService.VisualLookChangeAllowed,"the look can change while no run is active");
            int looks=0,changes=0;Action onLook=()=>looks++,onChange=()=>changes++;PNSettingsService.VisualLookChanged+=onLook;PNSettingsService.Changed+=onChange;
            var draft=PNSettingsService.Current.WithInkMode(PNInkMode.LiveAll).WithPaperShadow(PNPaperShadowMode.Baked).WithFrameRate(30).WithShowPcControls(false);
            Check(PNSettingsService.LookChangedBy(draft)&&PNSettingsService.Current.TargetFrameRate==0,"nothing applies before Save");
            Check(PNSettingsService.Apply(draft)&&changes==1&&looks==1,"Save: one write, one Changed, one look change");
            Check(PNVisualPaperSettings.Ink==PNInkMode.LiveAll&&PNVisualPaperSettings.Mode==PNPaperShadowMode.Baked&&Application.targetFrameRate==30&&!PNSettingsService.Current.ShowPcControls,"Save applies every part");
            var disk=new PNSettingsStore(dir).Load().Value;Check(disk.InkMode==PNInkMode.LiveAll&&disk.PaperShadow==PNPaperShadowMode.Baked&&disk.TargetFrameRate==30,"Save wrote everything");
            Check(PNSettingsService.Apply(draft)&&changes==2&&looks==1,"saving an unchanged draft does not request a look change");
            PNSettingsService.VisualLookChanged-=onLook;PNSettingsService.Changed-=onChange;
            PNSettingsService.SetInkMode(PNInkMode.Baked);PNSettingsService.SetPaperShadow(PNPaperShadowMode.Actors);PNSettingsService.SetFrameRate(0);
            string localization=Path.Combine(root,"Assets/StreamingAssets/BaseContent/Localization");
            var manifest=JObject.Parse(File.ReadAllText(Path.Combine(localization,"languages.json")));
            ((JArray)manifest["languages"]).Add(new JObject{{"code","fr"},{"nativeName","Français"}});
            var catalog=PNLocalizationCatalog.Build(new[]{new PNLocalizationPack("project_n",true,Array.Empty<string>(),manifest.ToString(),name=>name=="fr.json"?"{}":File.ReadAllText(Path.Combine(localization,name)))});
            EveContentContext MakeContent(){var hub=new EveRegistryHub();hub.Seal();var content=new EveContentContext(EveCodeModSession.Empty());content.Publish(hub,EveContentCompatibility.Create(hub,Array.Empty<EveContentDocument>(),"settings-check"));return content;}
            using(var content=MakeContent()){PNLocalization.PublishContent(catalog,content);Check(PNSettingsService.SetLocale("fr"),"persist additional locale");}
            Check(PNLocalization.Selected=="vi"&&PNSettingsService.Current.Locale=="fr"&&new PNSettingsStore(dir).Load().Value.Locale=="fr","unload fallback preserves preference");
            using(var content=MakeContent()){PNLocalization.PublishContent(catalog,content);Check(PNLocalization.Selected=="fr","locale restored when catalog returns");}
            PNSettingsService.SetLocale("en");
            string legacy=Path.Combine(dir,"legacy");Directory.CreateDirectory(legacy);PNLanguagePreferenceStore.Save(Path.Combine(legacy,"localization.json"),"en");
            PNSettingsService.Shutdown();PNSettingsService.Load(new PNSettingsStore(legacy));
            Check(File.Exists(Path.Combine(legacy,"settings.json"))&&File.Exists(Path.Combine(legacy,"localization.json")),"migration writes new and keeps legacy");
            string locked=Path.Combine(dir,"locked");Directory.CreateDirectory(locked);File.WriteAllText(Path.Combine(locked,"settings.json"),"{\"schemaVersion\":9}");
            PNSettingsService.Shutdown();PNSettingsService.Load(new PNSettingsStore(locked));PNSettingsService.ResolveDefaultLocale("vi");
            Check(!PNSettingsService.SetLocale("en")&&PNLocalization.Selected=="en"&&PNSettingsService.SaveError!=null,"apply despite protected save, explicit error");
            Check(File.ReadAllText(Path.Combine(locked,"settings.json")).Contains("9"),"newer schema preserved by service");
        }
        finally{PNLocalization.Shutdown();PNSettingsService.Shutdown();PNVisualPaperSettings.Mode=oldShadow;PNVisualPaperSettings.Ink=oldInk;}
        Check(Application.targetFrameRate==oldFps&&QualitySettings.vSyncCount==oldVSync,"frame policy restored");
        return checks;
    }
    public static void Batch()
    {
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));string output=null;
        foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--pn-settings-output="))output=arg.Substring("--pn-settings-output=".Length);
        if(output==null)throw new ArgumentException("Output required");Directory.CreateDirectory(output);
        try{int checks=Run(root);File.WriteAllText(Path.Combine(output,"native.txt"),"PASS "+checks+" Settings checks");EditorApplication.Exit(0);}
        catch(Exception e){File.WriteAllText(Path.Combine(output,"native.txt"),"FAIL "+e);EditorApplication.Exit(1);}
    }
}
#endif
