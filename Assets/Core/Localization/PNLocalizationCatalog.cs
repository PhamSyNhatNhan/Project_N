using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EveEngine;

// Presentation data only: never registered in, or hashed as, gameplay definitions.
public sealed class PNLanguage
{
    public string Code { get; }
    public string NativeName { get; }
    public string File { get; }
    public string FontProfile { get; }
    public string Fallback { get; }
    public PNLanguage(string code, string name, string file, string font, string fallback)
    { Code=code; NativeName=name; File=file; FontProfile=font; Fallback=fallback; }
}
public sealed class PNTranslation
{
    public string Text { get; }
    public bool Smart { get; }
    public string Source { get; }
    public PNTranslation(string text, bool smart, string source) { Text=text; Smart=smart; Source=source; }
}
public sealed class PNLocalizationPack
{
    public string Namespace { get; }
    public bool IsBase { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public string Manifest { get; }
    public Func<string,string> ReadOptional { get; }
    public PNLocalizationPack(string ns, bool isBase, IEnumerable<string> dependencies, string manifest, Func<string,string> readOptional)
    { Namespace=ns; IsBase=isBase; Dependencies=Array.AsReadOnly(dependencies.ToArray()); Manifest=manifest; ReadOptional=readOptional; }
}
public sealed class PNLocalizationCatalog
{
    public const int MaxFileBytes=4*1024*1024, MaxManifestBytes=256*1024, MaxEntryBytes=16*1024, MaxKeys=50000, MaxLocales=128;
    public string Default { get; private set; }
    public string Fallback { get; private set; }
    public IReadOnlyDictionary<string,PNLanguage> Languages { get; private set; }
    private readonly Dictionary<string,Dictionary<string,PNTranslation>> tables=new Dictionary<string,Dictionary<string,PNTranslation>>(StringComparer.Ordinal);
    public IEnumerable<string> Keys => tables.Values.SelectMany(t=>t.Keys).Distinct(StringComparer.Ordinal).OrderBy(k=>k,StringComparer.Ordinal);
    public IEnumerable<string> Namespaces => Keys.Select(k=>k.Split(':')[0]).Distinct(StringComparer.Ordinal);
    public static string Code(string input)
    {
        if(string.IsNullOrWhiteSpace(input)||input.Length>32)throw new FormatException("Invalid locale code.");
        try { var culture=CultureInfo.GetCultureInfo(input); if(culture.Equals(CultureInfo.InvariantCulture))throw new FormatException("Invariant locale is not a language.");return culture.Name; }
        catch(CultureNotFoundException e){throw new FormatException("Unsupported locale code: "+input,e);}
    }
    public static JObject Object(string json, int maxBytes, string source)
    {
        if(json==null||Encoding.UTF8.GetByteCount(json)>maxBytes)throw new FormatException(source+": missing or oversized JSON.");
        try { using(var sr=new StringReader(json))using(var reader=new JsonTextReader(sr){MaxDepth=32}) {
            var obj=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            if(reader.Read())throw new FormatException(source+": trailing JSON."); return obj;
        }} catch(JsonException e){throw new FormatException(source+": "+e.Message,e);}
    }
    public static void Fields(JObject obj, params string[] allowed)
    { foreach(var p in obj.Properties())if(!allowed.Contains(p.Name,StringComparer.Ordinal))throw new FormatException("Unknown localization field: "+p.Name); }
    private static string Required(JObject obj,string name)
    { var t=obj[name];if(t?.Type!=JTokenType.String||string.IsNullOrWhiteSpace((string)t))throw new FormatException("Required string: "+name);return (string)t; }
    private static string Optional(JObject obj,string name,string fallback=null)
    { if(obj[name]==null)return fallback;return Required(obj,name); }
    public static string RelativeFile(string value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>120||!value.EndsWith(".json",StringComparison.Ordinal)||value.Any(c=>!(char.IsLetterOrDigit(c)||c=='-'||c=='_'||c=='.'))||value.Contains(".."))throw new FormatException("Localization file must be a JSON filename inside Localization/: "+value);
        return value;
    }
    public static string ReadFile(string root,string file,bool required=false,int maxBytes=MaxFileBytes)
    {
        RelativeFile(file); string path=Path.Combine(root,file);
        if(!System.IO.File.Exists(path)){if(required)throw new FileNotFoundException("Missing localization file",path);return null;}
        if((System.IO.File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0||(System.IO.File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new FormatException("Linked localization paths are not allowed: "+path);
        if(new FileInfo(path).Length>maxBytes)throw new FormatException("Oversized localization file: "+path);
        return new UTF8Encoding(false,true).GetString(System.IO.File.ReadAllBytes(path));
    }
    public static PNLocalizationCatalog Build(IEnumerable<PNLocalizationPack> inputs)
    {
        var packs=inputs.ToArray(); if(packs.Count(p=>p.IsBase)!=1)throw new FormatException("One base localization pack required.");
        var result=new PNLocalizationCatalog();long totalBytes=0;var languages=new Dictionary<string,PNLanguage>(StringComparer.Ordinal);var manifests=new Dictionary<PNLocalizationPack,JObject>();
        var mappings=new Dictionary<PNLocalizationPack,Dictionary<string,string>>();var knownPacks=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in packs){if(!knownPacks.Add(p.Namespace))throw new FormatException("Duplicate localization pack: "+p.Namespace);}
        foreach(var p in packs.OrderByDescending(p=>p.IsBase))
        {
            var map=new Dictionary<string,string>(StringComparer.Ordinal); mappings.Add(p,map);
            if(p.Manifest==null){if(p.IsBase)throw new FormatException("Base languages.json required.");continue;}
            var root=Object(p.Manifest,MaxManifestBytes,p.Namespace+"/Localization/languages.json");Fields(root,"schemaVersion","default","fallback","languages","overrides");
            if(root["schemaVersion"]?.Type!=JTokenType.Integer||(int)root["schemaVersion"]!=1)throw new FormatException("Localization schemaVersion must be 1.");
            manifests.Add(p,root);
            if(p.IsBase){result.Default=Code(Required(root,"default"));result.Fallback=Code(Required(root,"fallback"));}
            else if(root["default"]!=null||root["fallback"]!=null)throw new FormatException("Only base may set default/fallback.");
            if(!(root["languages"] is JArray array))throw new FormatException("languages array required.");
            var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in array){var l=item as JObject??throw new FormatException("Language must be object.");Fields(l,"code","nativeName","file","fontProfile","fallback");
                string code=Code(Required(l,"code")),file=RelativeFile(Optional(l,"file",code+".json"));if(map.ContainsKey(code)||!files.Add(file))throw new FormatException("Duplicate locale/file: "+code);map.Add(code,file);
                var desc=new PNLanguage(code,Optional(l,"nativeName",languages.TryGetValue(code,out var prior)?prior.NativeName:null)??throw new FormatException("nativeName required: "+code),file,Optional(l,"fontProfile","default"),l["fallback"]==null?null:Code(Required(l,"fallback")));
                if(desc.NativeName.Length>100)throw new FormatException("Language name too long.");
                if(languages.TryGetValue(code,out var old)){if(old.NativeName!=desc.NativeName||old.FontProfile!=desc.FontProfile||old.Fallback!=desc.Fallback)throw new FormatException("Conflicting locale metadata: "+code);}
                else languages.Add(code,desc);
            }
        }
        if(languages.Count>MaxLocales||!languages.ContainsKey(result.Default)||!languages.ContainsKey(result.Fallback))throw new FormatException("Invalid default/fallback/locales count.");
        result.Languages=new ReadOnlyDictionary<string,PNLanguage>(languages);
        foreach(var code in languages.Keys)result.Chain(code).ToArray();
        var writers=new Dictionary<string,PNLocalizationPack>(StringComparer.Ordinal);
        foreach(var p in packs)
        {
            var permissions=new HashSet<string>(StringComparer.Ordinal);
            if(manifests.TryGetValue(p,out var manifest)&&manifest["overrides"]!=null){if(!(manifest["overrides"] is JArray overrides))throw new FormatException("overrides must be array.");foreach(var t in overrides){var o=t as JObject??throw new FormatException("override must be object.");Fields(o,"locale","keys");var code=Code(Required(o,"locale"));if(!languages.ContainsKey(code)||!(o["keys"] is JArray keys))throw new FormatException("Invalid override locale/keys.");foreach(var k in keys){if(k.Type!=JTokenType.String||!permissions.Add(code+"/"+(string)k))throw new FormatException("Duplicate/invalid override key.");}}}
            int keyCount=0;
            foreach(var language in languages.Values)
            {
                var explicitFile=mappings[p].TryGetValue(language.Code,out string file);file=file??language.Code+".json";
                string json=p.ReadOptional(file);if(json==null){if(explicitFile||p.IsBase&&language.Code==result.Fallback)throw new FormatException("Missing declared localization file: "+p.Namespace+"/"+file);continue;}
                totalBytes+=Encoding.UTF8.GetByteCount(json);if(totalBytes>32*1024*1024)throw new FormatException("Localization catalogue exceeds 32MiB.");
                string source=p.Namespace+"/Localization/"+file;var root=Object(json,MaxFileBytes,source);
                if(!result.tables.TryGetValue(language.Code,out var table))result.tables.Add(language.Code,table=new Dictionary<string,PNTranslation>(StringComparer.Ordinal));
                foreach(var entry in root.Properties())
                {
                    if(++keyCount>MaxKeys)throw new FormatException("Too many localization entries: "+p.Namespace);
                    var split=entry.Name.Split(':');if(split.Length!=2||!EveId.TryParse(entry.Name,out _)||split[1].Any(c=>!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='_'||c=='-'||c=='.')))throw new FormatException("Invalid translation key: "+entry.Name);
                    string ns=split[0],writerKey=language.Code+"/"+entry.Name;
                    if(ns!=p.Namespace){if(!p.Dependencies.Contains(ns)||!permissions.Contains(writerKey)||!result.Keys.Contains(entry.Name))throw new FormatException("Foreign translation key requires dependency/override and existing owner key: "+entry.Name);}
                    if(writers.TryGetValue(writerKey,out var writer)&&writer!=p&&!p.Dependencies.Contains(writer.Namespace))throw new FormatException("Conflicting translation providers: "+entry.Name);
                    string text;bool smart=false;
                    if(entry.Value.Type==JTokenType.String)text=(string)entry.Value;
                    else {var v=entry.Value as JObject??throw new FormatException("Translation must be string/object: "+entry.Name);Fields(v,"text","smart");if(v["text"]?.Type!=JTokenType.String||v["smart"]?.Type!=JTokenType.Boolean)throw new FormatException("text/string and smart/bool required.");text=(string)v["text"];smart=(bool)v["smart"];}
                    if(Encoding.UTF8.GetByteCount(text)>MaxEntryBytes)throw new FormatException("Translation entry too long: "+entry.Name);
                    table[entry.Name]=new PNTranslation(text,smart,source);writers[writerKey]=p;
                }
            }
            foreach(var permission in permissions)if(!writers.TryGetValue(permission,out var writer)||writer!=p)throw new FormatException("Unused localization override: "+permission);
        }
        return result;
    }
    public IEnumerable<string> Chain(string code)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);int depth=0;
        while(code!=null){if(!Languages.TryGetValue(code,out var l))throw new FormatException("Unknown fallback locale: "+code);if(!seen.Add(code)||++depth>16)throw new FormatException("Cyclic/deep locale fallback: "+code);yield return code;code=l.Fallback;}
        if(seen.Add(Fallback))yield return Fallback;
    }
    public bool TryGet(string code,string key,out PNTranslation entry)
        =>TryGet(code,key,out entry,out _);
    public bool TryGet(string code,string key,out PNTranslation entry,out string resolvedLocale)
    {
        foreach(string c in Chain(code))if(tables.TryGetValue(c,out var table)&&table.TryGetValue(key,out entry)){resolvedLocale=c;return true;}
        entry=null;resolvedLocale=null;return false;
    }
}
