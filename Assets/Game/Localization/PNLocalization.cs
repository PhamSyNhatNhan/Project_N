#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

// One app-level owner. Base text is available before gameplay content and survives EndPlay.
public static class PNLocalization
{
    public const string CollectionName="project_n.localization";
    private sealed class Locales : ILocalesProvider
    {
        public List<Locale> LocalesList = new List<Locale>();
        List<Locale> ILocalesProvider.Locales => LocalesList;
        public Locale GetLocale(LocaleIdentifier id) => LocalesList.Find(l=>l.Identifier==id);
        public void AddLocale(Locale l) { if(GetLocale(l.Identifier)!=null)throw new InvalidOperationException("Duplicate locale.");LocalesList.Add(l); }
        public bool RemoveLocale(Locale l)=>LocalesList.Remove(l);
    }
    private sealed class Adapter : ITableProvider, IDisposable
    {
        internal readonly PNLocalizationCatalog Catalog;
        internal readonly PNJsonLocalizationSettings Settings;
        internal readonly Locales Locales = new Locales();
        private readonly Dictionary<string,StringTable> tables=new Dictionary<string,StringTable>(StringComparer.Ordinal);
        private readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        internal Adapter(PNLocalizationCatalog catalog)
        {
            Catalog=catalog; Settings=ScriptableObject.CreateInstance<PNJsonLocalizationSettings>();owned.Add(Settings);
            try {
                Settings.SetAvailableLocales(Locales);
                Settings.SetStringDatabase(new LocalizedStringDatabase {TableProvider=this});
                Settings.GetStartupLocaleSelectors().Clear();
                foreach(var language in catalog.Languages.Values){if(language.FontProfile!="default")throw new FormatException("Unknown font profile: "+language.FontProfile);var l=Locale.CreateLocale(language.Code);l.LocaleName=language.NativeName;owned.Add(l);Locales.AddLocale(l);}
                {
                    var shared=ScriptableObject.CreateInstance<SharedTableData>();owned.Add(shared);shared.TableCollectionName=CollectionName;
                    var keys=catalog.Keys.ToArray();
                    foreach(string key in keys)shared.AddKey(key);
                    foreach(var l in Locales.LocalesList){var table=ScriptableObject.CreateInstance<StringTable>();owned.Add(table);table.SharedData=shared;table.LocaleIdentifier=l.Identifier;
                        foreach(string key in keys)if(catalog.TryGet(l.Identifier.Code,key,out var e)){
                            if(e.Smart){var parser=Settings.GetStringDatabase().SmartFormatter.Parser;var parsed=parser.ParseFormat(e.Text,Settings.GetStringDatabase().SmartFormatter.GetNotEmptyFormatterExtensionNames());parsed.ReleaseToPool();}
                            var entry=table.AddEntry(key,e.Text);entry.IsSmart=e.Smart;
                        }
                        tables.Add(CollectionName+"/"+l.Identifier.Code,table);
                    }
                }
                foreach(var obj in owned)obj.hideFlags=HideFlags.HideAndDontSave;
            } catch {Dispose();throw;}
        }
        public AsyncOperationHandle<T> ProvideTableAsync<T>(string name,Locale locale) where T:LocalizationTable
        { tables.TryGetValue(name+"/"+locale.Identifier.Code,out var t);return Addressables.ResourceManager.CreateCompletedOperation(t as T,t==null?"Unknown JSON localization table: "+name:null); }
        internal string Read(string code,string key,IDictionary<string,object> args,string fallback)
        {
            if(key.IndexOf(':')<1||!tables.TryGetValue(CollectionName+"/"+code,out var table))return fallback??"["+key+"]";
            var entry=table.GetEntry(key);if(entry==null)return fallback??"["+key+"]";
            if(!entry.IsSmart)return entry.Value;
            if(args!=null)foreach(var value in args.Values)if(value!=null&&!(value is string||value is bool||value is byte||value is short||value is int||value is long||value is float||value is double||value is decimal))throw new ArgumentException("Localization args must be scalar values.");
            Catalog.TryGet(code,key,out _,out string resolvedLocale);
            var text=entry.GetLocalizedString(CultureInfo.GetCultureInfo(resolvedLocale??code),new object[]{args??new Dictionary<string,object>()});
            if(text.Length>PNLocalizationCatalog.MaxEntryBytes)throw new FormatException("Formatted text too long: "+key);return text;
        }
        public void Dispose()
        {
            if(Settings!=null){((IDisposable)Settings).Dispose();Settings.ReleaseInitialization();}
            for(int i=owned.Count-1;i>=0;i--)Retire(owned[i]);owned.Clear();tables.Clear();
        }
    }
    private static Adapter current;
    private static PNLocalizationCatalog baseCatalog;
    private static string requested,selected;
    private static LocalizationSettings previous;
    private static readonly HashSet<string> diagnostics=new HashSet<string>(StringComparer.Ordinal);
    public static event Action Changed;
    public static bool IsReady=>current!=null;
    public static int Revision { get; private set; }
    public static string Selected=>selected;
    public static IReadOnlyList<PNLanguage> Languages=>current==null?Array.Empty<PNLanguage>():current.Catalog.Languages.Values.OrderBy(l=>l.Code,StringComparer.Ordinal).ToArray();
    public static string BaseDirectory=>Path.Combine(Application.streamingAssetsPath,"BaseContent","Localization");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset(){Shutdown();Changed=null;requested=null;selected=null;baseCatalog=null;diagnostics.Clear();}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterQuit(){Application.quitting-=Shutdown;Application.quitting+=Shutdown;}
    public static void Shutdown()
    {
        var old=current;current=null;
        if(old!=null){if(LocalizationSettings.HasSettings&&ReferenceEquals(LocalizationSettings.Instance,old.Settings))LocalizationSettings.Instance=previous;old.Dispose();}
        previous=null;
    }
    private static void Retire(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
    private static PNLocalizationPack BasePack(Func<string,string> read)=>new PNLocalizationPack("project_n",true,Array.Empty<string>(),read("languages.json"),read);
    public static void EnsureBase()
    {
        if(IsReady)return;
        if(BaseDirectory.Contains("://"))throw new InvalidOperationException("Packaged localization requires PrepareBase coroutine before synchronous content boot.");
        InstallBase(PNLocalizationCatalog.Build(new[]{BasePack(f=>PNLocalizationCatalog.ReadFile(BaseDirectory,f,f=="languages.json",f=="languages.json"?PNLocalizationCatalog.MaxManifestBytes:PNLocalizationCatalog.MaxFileBytes))}));
    }
    private static void InstallBase(PNLocalizationCatalog catalog)
    { requested=requested??PNLanguagePreferenceStore.Load(PreferenceFile);Publish(catalog);baseCatalog=catalog; }
    public static IEnumerator PrepareBase(Action<Exception> completed)
    {
        if(IsReady){completed(null);yield break;}
        if(!BaseDirectory.Contains("://")){Exception error=null;try{EnsureBase();}catch(Exception e){error=e;}completed(error);yield break;}
        var files=new Dictionary<string,string>(StringComparer.Ordinal);Exception failure=null;
        yield return ReadPackaged("languages.json",v=>files["languages.json"]=v,e=>failure=e);
        if(failure!=null){completed(failure);yield break;}
        string[] names=null;
        try {var manifest=PNLocalizationCatalog.Object(files["languages.json"],PNLocalizationCatalog.MaxManifestBytes,"languages.json");names=((Newtonsoft.Json.Linq.JArray)manifest["languages"]).Select(l=>PNLocalizationCatalog.RelativeFile((string)l["file"]??PNLocalizationCatalog.Code((string)l["code"])+".json")).ToArray();}
        catch(Exception e){failure=e;}
        if(failure!=null){completed(failure);yield break;}
        foreach(string file in names){yield return ReadPackaged(file,v=>files[file]=v,e=>failure=e);if(failure!=null){completed(failure);yield break;}}
        try{InstallBase(PNLocalizationCatalog.Build(new[]{BasePack(f=>files.TryGetValue(f,out var value)?value:null)}));}catch(Exception e){failure=e;}completed(failure);
    }
    private static IEnumerator ReadPackaged(string file,Action<string> success,Action<Exception> failure)
        =>PNLocalizationJsonReader.Read(BaseDirectory,file,success,failure);
    internal static PNLocalizationCatalog PreparePacks(IReadOnlyList<EveContentPack> packs)
    {
        EnsureBase();
        return PNLocalizationCatalog.Build(packs.Select(p=>{string root=Path.Combine(p.Directory,"Localization");return new PNLocalizationPack(p.Namespace,p.IsBase,p.Dependencies.Keys,PNLocalizationCatalog.ReadFile(root,"languages.json",p.IsBase,PNLocalizationCatalog.MaxManifestBytes),f=>PNLocalizationCatalog.ReadFile(root,f));}));
    }
    internal static void PublishContent(PNLocalizationCatalog catalog,EveContentContext content)
    { content.RegisterCleanup(()=>{if(baseCatalog!=null)Publish(baseCatalog);});Publish(catalog); }
    private static void Publish(PNLocalizationCatalog catalog)
    {
        var next=new Adapter(catalog);var old=current;var oldSelected=selected;
        if(old==null)previous=LocalizationSettings.HasSettings?LocalizationSettings.Instance:null;
        try {LocalizationSettings.Instance=next.Settings;LocalizationSettings.PreloadBehavior=PreloadBehavior.NoPreloading;
            selected=catalog.Languages.ContainsKey(requested??"")?requested:catalog.Default;
            next.Settings.SetSelectedLocale(next.Locales.GetLocale(new LocaleIdentifier(selected)));
            var init=LocalizationSettings.InitializationOperation;if(!init.IsDone)init.WaitForCompletion();if(init.Status!=AsyncOperationStatus.Succeeded)throw init.OperationException??new InvalidOperationException("Localization initialization failed.");
            current=next;
        }catch{selected=oldSelected;LocalizationSettings.Instance=old?.Settings??previous;next.Dispose();throw;}
        diagnostics.Clear();Notify();old?.Dispose();
    }
    public static void Select(string code,bool persist=true)
    {
        EnsureBase();code=PNLocalizationCatalog.Code(code);if(!current.Catalog.Languages.ContainsKey(code))throw new ArgumentException("Unavailable locale: "+code);
        // All text is staged on content load; selecting a language has no outstanding async I/O.
        current.Settings.SetSelectedLocale(current.Locales.GetLocale(new LocaleIdentifier(code)));selected=requested=code;Notify();if(persist)PNLanguagePreferenceStore.Save(PreferenceFile,code);
    }
    private static string PreferenceFile=>Path.Combine(Application.persistentDataPath,"Settings","localization.json");
    private static void Notify(){Revision++;var handlers=Changed;if(handlers==null)return;foreach(Action handler in handlers.GetInvocationList())try{handler();}catch(Exception e){Debug.LogException(e);}}
    public static string Text(string key,IDictionary<string,object> args=null,string fallback=null)
    {
        if(current==null)return fallback??"["+key+"]";
        try {return current.Read(selected,key,args,fallback);}catch(Exception e){if(diagnostics.Add(key))Debug.LogWarning("Localization "+key+": "+e.Message);return fallback??"["+key+"]";}
    }
    public static string ContentText(string id,string kind,string field,string fallback)=>Text(id.Split(':')[0]+":"+kind+"."+id.Split(':').Last()+"."+field,null,fallback);
}
#endif
