#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using EveEngine;
using UnityEngine;

public static class PNMobileStyleEditorChecks
{
    public static int Run(string root)
    {
        int count=PNSettingsEditorChecks.Run(root);void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
        PNSettingsService.Shutdown();PNLocalization.Shutdown();
        string path=Path.Combine(root,".utmp/MobileControls/Native-"+Guid.NewGuid().ToString("N"));
        EveContentContext current=null;
        void Publish(EveContentContext c)=>typeof(PNMobileStyleCatalog).GetMethod("Publish",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{c});
        EveContentContext Content()
        {
            var hub=new EveRegistryHub();new PNMobileUiContentModule().Register(hub);
            hub.MobileIconSets().Register(new PNMobileIconSetDefinition(EveId.Parse("native:icons"),"native:label",PNMobileStyleDefaults.IconSets[1].Icons));
            hub.Seal();var c=new EveContentContext(EveCodeModSession.Empty());c.Publish(hub,EveContentCompatibility.Create(hub,Array.Empty<EveContentDocument>(),"style-check"));return c;
        }
        try
        {
            PNSettingsService.Load(new PNSettingsStore(path));PNLocalization.EnsureBase();
            Check(PNMobileStyleCatalog.IconSets.Count==2,"stock available before content");
            current=Content();Publish(current);Check(PNMobileStyleCatalog.HasIconSet("native:icons"),"catalog content published");
            Check(PNSettingsService.SetMobileIconSet("native:icons"),"mod style saves");PNSettingsService.SetFrameRate(90);PNSettingsService.SetLocale("en");
            Check(PNSettingsService.Current.MobileIconSet=="native:icons","locale/FPS keep mod style");
            string before=File.ReadAllText(PNSettingsService.FilePath);current.Dispose();current=null;
            Check(PNMobileStyleCatalog.ResolveIconSet("native:icons").Id.ToString()==PNMobileStyleDefaults.IconSet,"unload uses stock");
            Check(File.ReadAllText(PNSettingsService.FilePath)==before&&PNSettingsService.Current.MobileIconSet=="native:icons","fallback keeps preference and disk");
            current=Content();Publish(current);Check(PNMobileStyleCatalog.ResolveIconSet("native:icons").Id.ToString()=="native:icons","returning definition restored");
            var newer=Content();Publish(newer);current.Dispose();current=newer;Check(PNMobileStyleCatalog.HasIconSet("native:icons"),"old cleanup cannot clear newer catalog");
            PNSettingsService.Shutdown();PNSettingsService.Load(new PNSettingsStore(path));Check(PNSettingsService.Current.MobileIconSet=="native:icons","restart restores selection");
        }
        finally{current?.Dispose();PNSettingsService.Shutdown();PNLocalization.Shutdown();}
        return count;
    }
}
#endif
