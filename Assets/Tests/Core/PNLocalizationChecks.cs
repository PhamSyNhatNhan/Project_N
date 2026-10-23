using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;

// The same contract checks run under real Unity and in the standalone suite.
public static class PNLocalizationChecks
{
    public static int Run(string projectRoot)
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("Localization: "+label);checks++;}
        void Bad(Action action,string label){bool rejected=false;try{action();}catch(FormatException){rejected=true;}Check(rejected,label);}
        const string manifest="{\"schemaVersion\":1,\"default\":\"vi\",\"fallback\":\"en\",\"languages\":[{\"code\":\"vi\",\"nativeName\":\"Tiếng Việt\"},{\"code\":\"en\",\"nativeName\":\"English\"}],\"overrides\":[]}";
        PNLocalizationPack Pack(string ns,bool isBase,string m,Dictionary<string,string> files,params string[] deps)=>new PNLocalizationPack(ns,isBase,deps,m,f=>files.TryGetValue(f,out var t)?t:null);
        var files=new Dictionary<string,string>{{"vi.json","{\"project_n:a\":\"Vi\",\"project_n:empty\":\"\"}"},{"en.json","{\"project_n:a\":\"En\",\"project_n:b\":\"Fallback\"}"}};
        var b=Pack("project_n",true,manifest,files);var catalog=PNLocalizationCatalog.Build(new[]{b});
        Check(catalog.TryGet("vi","project_n:a",out var a)&&a.Text=="Vi","selected locale");
        Check(catalog.TryGet("vi","project_n:b",out a)&&a.Text=="Fallback","fallback per key");
        Check(catalog.TryGet("vi","project_n:empty",out a)&&a.Text=="","empty intentional");
        Check(!catalog.TryGet("vi","project_n:missing",out _),"missing key");
        Bad(()=>PNLocalizationCatalog.Object("{\"x\":1,\"x\":2}",100,"duplicate"),"duplicate JSON property");
        Bad(()=>PNLocalizationCatalog.Object("{} {}",100,"trailing"),"trailing JSON");
        Bad(()=>PNLocalizationCatalog.RelativeFile("../en.json"),"parent traversal");
        Bad(()=>PNLocalizationCatalog.RelativeFile("/en.json"),"absolute path");
        Bad(()=>PNLocalizationCatalog.RelativeFile("https://x/en.json"),"URI path");
        Bad(()=>PNLocalizationCatalog.Build(new[]{Pack("project_n",true,manifest.Replace("\"default\":\"vi\"","\"default\":\"fr\""),files)}),"unknown default");
        Bad(()=>PNLocalizationCatalog.Build(new[]{Pack("project_n",true,manifest.Replace("\"code\":\"vi\"","\"code\":\"vi\",\"fallback\":\"vi\""),files)}),"self fallback");
        Bad(()=>PNLocalizationCatalog.Build(new[]{Pack("project_n",true,manifest.Replace("\"overrides\":[]","\"oops\":1"),files)}),"unknown manifest field");
        Bad(()=>PNLocalizationCatalog.Build(new[]{Pack("project_n",true,manifest,new Dictionary<string,string>{{"en.json","{}"}})}),"declared file missing");
        var mod=Pack("addon",false,null,new Dictionary<string,string>{{"vi.json","{\"addon:a\":\"Mod\"}"}});
        catalog=PNLocalizationCatalog.Build(new[]{b,mod});Check(catalog.TryGet("vi","addon:a",out a)&&a.Text=="Mod","mod convention without manifest");
        var foreign=new Dictionary<string,string>{{"vi.json","{\"project_n:a\":\"Override\"}"}};
        Bad(()=>PNLocalizationCatalog.Build(new[]{b,Pack("addon",false,null,foreign,"project_n")}),"undeclared override");
        const string over="{\"schemaVersion\":1,\"languages\":[],\"overrides\":[{\"locale\":\"vi\",\"keys\":[\"project_n:a\"]}]}";
        var translator=Pack("addon",false,over,foreign,"project_n");catalog=PNLocalizationCatalog.Build(new[]{b,translator});
        Check(catalog.TryGet("vi","project_n:a",out a)&&a.Text=="Override"&&a.Source.StartsWith("addon/"),"override and provenance");
        Bad(()=>PNLocalizationCatalog.Build(new[]{b,Pack("addon",false,over,foreign)}),"foreign dependency required");
        Bad(()=>PNLocalizationCatalog.Build(new[]{b,translator,Pack("sibling",false,over,foreign,"project_n")}),"sibling collision");
        const string french="{\"schemaVersion\":1,\"languages\":[{\"code\":\"fr\",\"nativeName\":\"Français\"}],\"overrides\":[]}";
        var f=Pack("french",false,french,new Dictionary<string,string>{{"fr.json","{\"french:a\":\"Bonjour\"}"}});
        catalog=PNLocalizationCatalog.Build(new[]{b,f});Check(catalog.Languages.ContainsKey("fr")&&catalog.TryGet("fr","project_n:b",out a)&&a.Text=="Fallback","new locale plus fallback");
        Check(!PNLocalizationCatalog.Build(new[]{b}).Languages.ContainsKey("fr"),"removed locale on rebuild");
        var source=new PNGameContentSource(Path.Combine(projectRoot,"Assets/StreamingAssets/BaseContent"),Path.Combine(projectRoot,".utmp/NoLocalizationMods"),Array.Empty<string>());
        var docs=source.ReadDocuments().ToArray();Check(docs.Length>0&&!docs.Any(d=>d.Name.Contains("Localization")),"presentation excluded from gameplay documents");
        var baseRoot=Path.Combine(projectRoot,"Assets/StreamingAssets/BaseContent/Localization");
        catalog=PNLocalizationCatalog.Build(new[]{new PNLocalizationPack("project_n",true,Array.Empty<string>(),PNLocalizationCatalog.ReadFile(baseRoot,"languages.json",true),name=>PNLocalizationCatalog.ReadFile(baseRoot,name))});
        Check(catalog.TryGet("vi","project_n:ui.start",out a)&&a.Text=="Bắt đầu","real vi JSON");
        Check(catalog.TryGet("en","project_n:ui.start",out a)&&a.Text=="Start","real en JSON");
        var artifact=Path.Combine(projectRoot,".utmp/Localization/ContractChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(artifact);
        string pref=Path.Combine(artifact,"Settings/localization.json");
        Check(PNLanguagePreferenceStore.Load(pref)==null,"missing preference");
        PNLanguagePreferenceStore.Save(pref,"en");Check(PNLanguagePreferenceStore.Load(pref)=="en","saved locale persists");
        PNLanguagePreferenceStore.Save(pref,"vi");Check(PNLanguagePreferenceStore.Load(pref)=="vi","atomic preference update");
        File.WriteAllText(pref,"broken");Check(PNLanguagePreferenceStore.Load(pref)=="en","corrupt preference recovers backup");
        var fixtureBase=Path.Combine(artifact,"Base");Directory.CreateDirectory(Path.Combine(fixtureBase,"Localization"));
        File.WriteAllText(Path.Combine(fixtureBase,"manifest.json"),"{\"schemaVersion\":1,\"namespace\":\"project_n\",\"version\":\"1.0.0\"}");
        string gameplay=Path.Combine(fixtureBase,"gameplay.json");File.WriteAllText(gameplay,"{\"schemaVersion\":1}");
        string translation=Path.Combine(fixtureBase,"Localization/vi.json");File.WriteAllText(translation,"{\"project_n:a\":\"A\"}");
        var localSource=new PNGameContentSource(fixtureBase,Path.Combine(artifact,"Mods"),Array.Empty<string>());
        var before=localSource.ReadDocuments().ToArray();var hashHub=new EveRegistryHub();hashHub.Seal();
        var beforeHash=EveContentCompatibility.Create(hashHub,before,"localization-check");
        File.WriteAllText(translation,"{\"project_n:a\":\"B\"}");
        beforeHash.Validate(EveContentCompatibility.Create(hashHub,localSource.ReadDocuments(),"localization-check"));Check(true,"translation edit preserves gameplay compatibility");
        File.WriteAllText(gameplay,"{\"schemaVersion\":1,\"characters\":[]}");
        Check(beforeHash.ContentHashes["pack:project_n"]!=EveContentCompatibility.Create(hashHub,localSource.ReadDocuments(),"localization-check").ContentHashes["pack:project_n"],"gameplay JSON still changes hash");
        Check(new EveDirectoryContentSource(fixtureBase,Path.Combine(artifact,"Mods"),Array.Empty<string>()).ReadDocuments().Count()>localSource.ReadDocuments().Count(),"generic engine source retains default policy");
        return checks;
    }
}
