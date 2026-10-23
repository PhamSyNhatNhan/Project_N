#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// Native checks for the mode catalog (Docs/ModeMenuPlan.md M2): ordering, unlisted modes, mobile filter and the real registrations.
public static class PNModeCatalogEditorChecks
{
    private sealed class Policy : IPNSessionPolicy
    { public string ModeId => "fixture"; public int MaximumClients => 2; public double GraceSeconds => 15; public void OnOwnerLeft(Action retire) => retire(); }

    private static PNModeListing Listing(string name, int order) =>
        new PNModeListing(name, "d", new[] { 1, 2, 3 }, "", order, new[] { new PNModeStartOption("solo", "Chơi", PNModeStart.Offline) });

    public static string Run()
    {
        int checks = 0; void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        var hub = new EveRegistryHub(); var modes = hub.GetOrCreate<PNModeDefinition>();
        modes.Register(new PNModeDefinition(EveId.Parse("fixture:late"), typeof(Policy), p => new Policy(), Listing("Late", 5)));
        modes.Register(new PNModeDefinition(EveId.Parse("fixture:early"), typeof(Policy), p => new Policy(), Listing("Early", 1)));
        modes.Register(new PNModeDefinition(EveId.Parse("fixture:hidden"), typeof(Policy), p => new Policy()));
        modes.Register(new PNModeDefinition(EveId.Parse("project_n:shipped"), typeof(Policy), p => new Policy(), Listing("Shipped", 5)));
        hub.Seal();
        Check(PNModeCatalog.Listed(hub, false).Select(x => x.Id.ToString()).SequenceEqual(new[] { "fixture:early", "fixture:late", "project_n:shipped" }), "the catalog lists listed modes by order, then id; unlisted ones are left out");
        Check(PNModeCatalog.Listed(hub, true).Select(x => x.Id.ToString()).SequenceEqual(new[] { "project_n:shipped" }), "the mobile catalog keeps only shipped content");

        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "NoCatalogProbeMods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var real = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var listed = PNModeCatalog.Listed(real, false);
        Check(listed.Count == 1 && listed[0].Id.ToString() == "project_n:roguelite" && listed[0].Listing.Name == "Roguelite", "the Hall is not listed; the roguelite is");
        Check(listed[0].Listing.Starts.Select(s => s.Start).SequenceEqual(new[] { PNModeStart.Offline, PNModeStart.Host, PNModeStart.Join }) && listed[0].Listing.Starts[0].Id == "solo", "the roguelite starts: alone, host, join");
        Check(real.Get<PNModeDefinition>().Get(EveId.Parse("project_n:hall")).Listing == null, "the Hall definition has no listing");
        Check(PNModeCatalog.Listed(real, true).Count == 1, "the roguelite is also listed on mobile");
        return "PASS " + checks + " mode catalog checks";
    }
}
#endif
