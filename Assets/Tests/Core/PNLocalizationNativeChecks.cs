#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

public static class PNLocalizationNativeChecks
{
    public static int Run(string projectRoot)
    {
        if(Application.isPlaying)throw new InvalidOperationException("Idle Edit required.");
        int checks=PNLocalizationChecks.Run(projectRoot);
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        string root=Path.Combine(projectRoot,"Assets/StreamingAssets/BaseContent/Localization");
        var files=new Dictionary<string,string>{{"vi.json",PNLocalizationCatalog.ReadFile(root,"vi.json",true)},{"en.json",PNLocalizationCatalog.ReadFile(root,"en.json",true)}};
        string manifest=PNLocalizationCatalog.ReadFile(root,"languages.json",true);
        PNLocalizationCatalog Catalog()=>PNLocalizationCatalog.Build(new[]{new PNLocalizationPack("project_n",true,Array.Empty<string>(),manifest,f=>files.TryGetValue(f,out var v)?v:null)});
        PNLocalization.EnsureBase();PNLocalization.Select("vi",false);
        try
        {
            Check(PNLocalization.Text("project_n:example.damage",new Dictionary<string,object>{{"damage",12},{"duration",3}})=="Gây 12 sát thương trong 3 giây.","native named args");
            PNLocalization.Select("en",false);Check(LocalizationSettings.StringDatabase.GetLocalizedString(PNLocalization.CollectionName,"project_n:ui.start")=="Start","native provider path");
            var pluralEn=JObject.Parse(files["en.json"]);pluralEn["project_n:plural"]=new JObject{{"text","{count} {count:plural:item|items}"},{"smart",true}};files["en.json"]=pluralEn.ToString();
            var withFrench=JObject.Parse(manifest);((JArray)withFrench["languages"]).Add(new JObject{{"code","fr"},{"nativeName","Français"}});manifest=withFrench.ToString();files["fr.json"]="{}";
            var pluralHub=new EveRegistryHub();pluralHub.Seal();var pluralContent=new EveContentContext(EveCodeModSession.Empty());pluralContent.Publish(pluralHub,EveContentCompatibility.Create(pluralHub,Array.Empty<EveContentDocument>(),"localization-check"));
            try { PNLocalization.PublishContent(Catalog(),pluralContent);
                Check(PNLocalization.Text("project_n:plural",new Dictionary<string,object>{{"count",1}})=="1 item","English singular");
                Check(PNLocalization.Text("project_n:plural",new Dictionary<string,object>{{"count",2}})=="2 items","English plural");
                PNLocalization.Select("vi",false);
                Check(PNLocalization.Text("project_n:plural",new Dictionary<string,object>{{"count",1}})=="1 item","English fallback retains English plural grammar");
                PNLocalization.Select("fr",false);
                Check(PNLocalization.Text("project_n:plural",new Dictionary<string,object>{{"count",0}})=="0 items","English fallback does not use French zero-as-singular rule");
            }finally{pluralContent.Dispose();}
            var before=PNLocalization.Text("project_n:ui.start");int revision=PNLocalization.Revision;
            var bad=JObject.Parse(files["vi.json"]);bad["project_n:broken"]=new JObject{{"text","Unclosed {"},{"smart",true}};files["vi.json"]=bad.ToString();
            var hub=new EveRegistryHub();hub.Seal();var content=new EveContentContext(EveCodeModSession.Empty());content.Publish(hub,EveContentCompatibility.Create(hub,Array.Empty<EveContentDocument>(),"localization-check"));
            bool failed=false;try{PNLocalization.PublishContent(Catalog(),content);}catch(Exception){failed=true;}
            Check(failed&&PNLocalization.Revision==revision&&PNLocalization.Text("project_n:ui.start")==before,"malformed smart candidate leaves current generation intact");content.Dispose();
            Check(Resources.FindObjectsOfTypeAll<PNJsonLocalizationSettings>().Length==1,"failed candidate settings cleaned");
        }
        finally { PNLocalization.Shutdown(); }
        Check(Resources.FindObjectsOfTypeAll<PNJsonLocalizationSettings>().Length==0,"settings cleanup");
        Check(System.Linq.Enumerable.Count(Resources.FindObjectsOfTypeAll<StringTable>(),t=>t.hideFlags==HideFlags.HideAndDontSave)==0,"table cleanup");
        return checks;
    }
}
#endif
