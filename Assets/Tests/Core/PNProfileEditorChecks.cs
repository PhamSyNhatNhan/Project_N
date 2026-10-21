#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// Native checks for the player profile: the real base content (Eve's data.playable block), the rules version and the service on a real folder.
public static class PNProfileEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "NoProfileProbeMods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var eve = PNPlayableSettings.Parse(hub.Characters.Get(EveId.Parse("project_n:eve")).Data);
        Check(eve != null && eve.Name == "Eve" && eve.Availability == PNPlayableAvailability.Default && eve.AccentRgb.SequenceEqual(new[] { 231, 185, 143 }), "Eve has a playable block in the base content");
        var eve2 = PNPlayableSettings.Parse(hub.Characters.Get(EveId.Parse("project_n:eve_2")).Data);
        Check(eve2 != null && eve2.Name == "Eve 2" && eve2.Availability == PNPlayableAvailability.Default, "Eve 2 (a renamed copy of Eve) is playable by default");
        Check(hub.Characters.Get(EveId.Parse("project_n:eve_2")).Stats.Count == hub.Characters.Get(EveId.Parse("project_n:eve")).Stats.Count && hub.Characters.Get(EveId.Parse("project_n:eve_2")).SkillIds.Count == 0 && hub.Characters.Get(EveId.Parse("project_n:eve")).SkillIds.Count > 0, "Eve 2 has the same stats as Eve and no skills of its own (only the shared Dash)");
        foreach (var id in new[] { "project_n:dummy", "project_n:dummy_lorica" })
            Check(PNPlayableSettings.Parse(hub.Characters.Get(EveId.Parse(id)).Data) == null, id + " is not playable");
        Check(EveGameRuntime.CurrentRulesVersion == "project_n:90", "rules version is project_n:90");

        var folder = Path.Combine(Application.temporaryCachePath, "PNProfileChecks_" + Guid.NewGuid().ToString("N"));
        try
        {
            PNPlayerProfileService.ResetStatics();
            PNPlayerProfileService.Load(new PNProfileStore(folder));
            Check(PNPlayerProfileService.LoadStatus == PNProfileLoadStatus.Missing && PNPlayerProfileService.CanPlay("project_n:eve", eve), "a new profile lets Eve be played");
            PNPlayerProfileService.SetLastCharacter("project_n:eve"); PNPlayerProfileService.SetState("mod:vex", PNCharacterState.Owned);
            PNPlayerProfileService.ResetStatics();
            PNPlayerProfileService.Load(new PNProfileStore(folder));
            Check(PNPlayerProfileService.LoadStatus == PNProfileLoadStatus.Loaded && PNPlayerProfileService.Current.LastCharacter == "project_n:eve" && PNPlayerProfileService.Current.Count == 1, "the profile survives a reload from disk");
            PNPlayerProfileService.SetState("project_n:eve", PNCharacterState.Forbidden);
            Check(!PNPlayerProfileService.CanPlay("project_n:eve", eve) || PNPlayerProfileService.AllowForbidden, "Eve marked Forbidden in the profile is refused unless the allow flag is set");
        }
        finally { PNPlayerProfileService.ResetStatics(); try { Directory.Delete(folder, true); } catch (IOException) { } }
        return "PASS " + checks + " player profile checks";
    }
}
#endif
