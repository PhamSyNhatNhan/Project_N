using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Configures the reusable runtime with Project_N content modules.
// VI: Cáº¥u hÃ¬nh runtime dÃ¹ng láº¡i báº±ng cÃ¡c module ná»™i dung cá»§a Project_N.
public class EveGameRuntime : EveRuntime
{
    [SerializeField] private string baseContentFolder = "BaseContent";
    [SerializeField] private string modsFolder = "Mods";
    [SerializeField] private string enabledModsFile = "enabled.json";
    private IReadOnlyList<EveContentPack> packs;
#if UNITY_5_3_OR_NEWER
    private PNLocalizationCatalog localizationCandidate;
    private PNWorldBundleService bundleService;
    private System.Guid bundleGeneration;
    public PNWorldBundleService WorldAssets
    {
        get
        {
            if(Content==null||!Content.IsReady)throw new System.InvalidOperationException("Ready content required for assets.");
            if(bundleService==null||bundleGeneration!=Content.GenerationId)
            {bundleService=new PNWorldBundleService((packs??System.Array.Empty<EveContentPack>()).Where(p=>p.Assets!=null).SelectMany(p=>p.Assets.Bundles));bundleGeneration=Content.GenerationId;Content.RegisterCleanup(bundleService.Dispose);}
            return bundleService;
        }
    }
#endif
    // EN: Bump when executable gameplay rules change, even if JSON stays the same.
    // VI: TÄƒng phiÃªn báº£n khi luáº­t gameplay trong code Ä‘á»•i dÃ¹ JSON váº«n giá»¯ nguyÃªn.
    public const string CurrentRulesVersion = "project_n:98";
    public override string RulesVersion => CurrentRulesVersion;
#if UNITY_5_3_OR_NEWER
    internal bool IsBootstrapOwner { get; set; }
    protected override void Awake()
    {
        if (!Application.isPlaying) return;
        if (IsBootstrapOwner) { EnsureContentLoaded(); return; }
        // A scene entry never owns content, even after the previous play has ended.
        try { PNGameBootstrap.SceneEntry(this); }
        finally { Destroy(this); }
    }
    protected override void BindSession(EveSessionContext session)
    {
        if (!Application.isPlaying) return;
        // Register before binding, so partial initialization also has a cleanup owner.
        session.RegisterCleanup(() => GetComponent<PNAreaManager>()?.Shutdown());
        session.RegisterCleanup(() => GetComponent<PNProjectileManager>()?.Shutdown());
        session.RegisterCleanup(() => GetComponent<PNUiRuntime>()?.Unbind());
        PNAreaManager.Attach(this);
        PNProjectileManager.Attach(this, PNGameBootstrap.Config.Projectiles);
        PNUiRuntime.Attach(this);
        PNGameSession.Attach(this, session);
    }
#endif
    protected virtual bool ModsAllowed
    {
        get
        {
#if UNITY_5_3_OR_NEWER
            return Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer;
#else
            return true;
#endif
        }
    }
    protected override IEnumerable<EveOverrideSelection> ReadOverrideSelections()
        => ModsAllowed ? EveOverrideSelection.Read(Path.Combine(ModsDirectory, "overrides.json"))
            : System.Array.Empty<EveOverrideSelection>();
    protected virtual string ModsDirectory => Path.Combine(Application.persistentDataPath, modsFolder);

    protected override IEnumerable<EveContentDocument> ReadContentDocuments()
    {
        string modsDirectory = ModsDirectory;
        var source = new PNGameContentSource(
            Path.Combine(Application.streamingAssetsPath, baseContentFolder),
            modsDirectory, ModsAllowed ? EveDirectoryContentSource.ReadEnabledMods(Path.Combine(modsDirectory, enabledModsFile)) : System.Array.Empty<string>());
        packs = source.ReadPacks();
#if UNITY_5_3_OR_NEWER
        localizationCandidate=PNLocalization.PreparePacks(packs);
#endif
        return source.ReadDocuments(packs);
    }

    protected override void OnContentPublished(EveContentContext content)
    {
#if UNITY_5_3_OR_NEWER
        // Existing memory-document runtimes override ReadContentDocuments without pack I/O.
        PNMobileStyleCatalog.Publish(content);
        // They retain base presentation; the normal pack path always supplies its staged catalogue.
        if(localizationCandidate==null){PNLocalization.EnsureBase();return;}
        PNLocalization.PublishContent(localizationCandidate,content);localizationCandidate=null;
#endif
    }

    protected override EveCodeModSession LoadCodeMods() => ModsAllowed ? new EveCodeModLoader("project_n").Load(packs) : EveCodeModSession.Empty();
#if UNITY_5_3_OR_NEWER
    protected override IReadOnlyDictionary<string, string> ReadGameplayAssetHashes(EveRegistryHub registry)
    {
        var hashes=PNSlashPrefabContent.Hashes(registry).ToDictionary(x=>x.Key,x=>x.Value);
        foreach(var pack in packs??System.Array.Empty<EveContentPack>())if(pack.Assets!=null)
        {
            if(!ModsAllowed)throw new System.NotSupportedException("Mobile does not load mod assets.");
            hashes.Add("assets:"+pack.Namespace,pack.Assets.Hash);
            foreach(var bundle in pack.Assets.Bundles)
            {if(bundle.Target!="StandaloneWindows64"||bundle.UnityVersion!=Application.unityVersion)throw new System.NotSupportedException("Mod bundle target/Unity version differs: "+bundle.Id);hashes.Add("bundle:"+bundle.Id,bundle.Hash);}
        }
        return hashes;
    }
#endif

    protected override IEnumerable<EveIContentModule> CreateContentModules()
    {
        yield return new PNContentModule();
        yield return new PNGameplayContentModule();
        yield return new PNMobileUiContentModule();
    }
}

