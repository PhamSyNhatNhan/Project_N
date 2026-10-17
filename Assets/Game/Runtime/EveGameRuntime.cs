using System.Collections.Generic;
using System.IO;
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
    // EN: Bump when executable gameplay rules change, even if JSON stays the same.
    // VI: TÄƒng phiÃªn báº£n khi luáº­t gameplay trong code Ä‘á»•i dÃ¹ JSON váº«n giá»¯ nguyÃªn.
    public const string CurrentRulesVersion = "project_n:77";
    public override string RulesVersion => CurrentRulesVersion;
#if UNITY_5_3_OR_NEWER
    internal bool IsBootstrapOwner { get; set; }
    protected override void Awake()
    {
        if (!Application.isPlaying) return;
        if (IsBootstrapOwner) { EnsureContentLoaded(); return; }
        // A scene entry never owns content, even after the previous play has ended.
        try { PNGameBootstrap.Begin(PNGameBootstrap.Config); }
        finally { Destroy(this); }
    }
    protected override void BindSession(EveSessionContext session)
    {
        if (!Application.isPlaying) return;
        // Register before binding, so partial initialization also has a cleanup owner.
        session.RegisterCleanup(() => GetComponent<PNAreaManager>()?.Shutdown());
        session.RegisterCleanup(() => GetComponent<PNUiRuntime>()?.Unbind());
        PNAreaManager.Attach(this);
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
        var source = new EveDirectoryContentSource(
            Path.Combine(Application.streamingAssetsPath, baseContentFolder),
            modsDirectory, ModsAllowed ? EveDirectoryContentSource.ReadEnabledMods(Path.Combine(modsDirectory, enabledModsFile)) : System.Array.Empty<string>());
        packs = source.ReadPacks();
        return source.ReadDocuments(packs);
    }

    protected override EveCodeModSession LoadCodeMods() => ModsAllowed ? new EveCodeModLoader("project_n").Load(packs) : EveCodeModSession.Empty();
#if UNITY_5_3_OR_NEWER
    protected override IReadOnlyDictionary<string, string> ReadGameplayAssetHashes(EveRegistryHub registry)
        => PNSlashPrefabContent.Hashes(registry);
#endif

    protected override IEnumerable<EveIContentModule> CreateContentModules()
    {
        yield return new PNContentModule();
        yield return new PNGameplayContentModule();
    }
}

