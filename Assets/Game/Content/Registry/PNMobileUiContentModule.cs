using EveEngine;

public sealed class PNMobileUiContentModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        hub.NameRegistry<PNMobileIconSetDefinition>("mobile_icon_sets");
        hub.NameRegistry<PNMobilePaletteDefinition>("mobile_palettes");
        foreach(var set in PNMobileStyleDefaults.IconSets)hub.MobileIconSets().Register(set);
        foreach(var palette in PNMobileStyleDefaults.Palettes)hub.MobilePalettes().Register(palette);
    }
}
