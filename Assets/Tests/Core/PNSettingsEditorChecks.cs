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
        int oldFps=Application.targetFrameRate,oldVSync=QualitySettings.vSyncCount;
        string dir=Path.Combine(root,".utmp/Settings/Native-"+Guid.NewGuid().ToString("N"));
        PNSettingsService.Shutdown();PNLocalization.Shutdown();
        try
        {
            PNSettingsService.Load(new PNSettingsStore(dir));PNLocalization.EnsureBase();
            Check(PNSettingsService.Current.Locale=="vi","manifest resolves default");
            Check(PNSettingsService.SetLocale("en")&&PNLocalization.Selected=="en","shared locale setter");
            Check(PNSettingsService.SetFrameRate(120)&&Application.targetFrameRate==120&&QualitySettings.vSyncCount==0,"FPS policy");
            Check(new PNSettingsStore(dir).Load().Value.Locale=="en","FPS preserves locale");
            PNLocalization.Select("vi",false);Check(new PNSettingsStore(dir).Load().Value.Locale=="en","temporary locale selection not persisted");
            Check(PNSettingsService.SetFrameRate(0)&&Application.targetFrameRate==-1,"unlimited mapping");
            PNSettingsService.Shutdown();PNSettingsService.Load(new PNSettingsStore(dir));
            Check(PNSettingsService.Current.Locale=="en"&&PNSettingsService.Current.TargetFrameRate==0,"service reload");
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
        finally{PNLocalization.Shutdown();PNSettingsService.Shutdown();}
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
