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
    public const string CurrentRulesVersion = "project_n:74";
    public override string RulesVersion => CurrentRulesVersion;
#if UNITY_5_3_OR_NEWER
    public override void Initialize()
    {
        base.Initialize();
        if(Application.isPlaying && IsReady) { PNUiRuntime.Attach(this); PNAreaManager.Attach(this); }
    }
#endif

#if UNITY_5_3_OR_NEWER
    public override void EndSession()
    {
        var areas = GetComponent<PNAreaManager>(); if (areas != null) areas.Shutdown();
        base.EndSession();
    }
#endif
    protected override IEnumerable<EveOverrideSelection> ReadOverrideSelections()
        => EveOverrideSelection.Read(Path.Combine(Application.persistentDataPath, modsFolder, "overrides.json"));

    protected override IEnumerable<EveContentDocument> ReadContentDocuments()
    {
        string modsDirectory = Path.Combine(Application.persistentDataPath, modsFolder);
        var source = new EveDirectoryContentSource(
            Path.Combine(Application.streamingAssetsPath, baseContentFolder),
            modsDirectory, EveDirectoryContentSource.ReadEnabledMods(Path.Combine(modsDirectory, enabledModsFile)));
        packs = source.ReadPacks();
        return source.ReadDocuments(packs);
    }

    protected override EveCodeModSession LoadCodeMods() => new EveCodeModLoader("project_n").Load(packs);
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

