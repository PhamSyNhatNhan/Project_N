using System;
using EveEngine;
using static EveSessionSmokeTests;

// EN: Verifies typed combat content, module extension and coop compatibility without applying damage.
// VI: Kiểm tra nội dung combat theo loại, mở rộng module và tương thích coop mà không gây damage.
public static class PNCombatRegistryTests
{
    public static void Run()
    {
        var hub = CreateHub();
        var damageTypes = hub.DamageTypes();
        var elements = hub.Elements();
        Check(damageTypes.Count == 3 &&
            damageTypes.Get(PNContentModule.Physical).Id == PNContentModule.Physical &&
            damageTypes.Get(PNContentModule.Magic).Id == PNContentModule.Magic &&
            damageTypes.Get(PNContentModule.True).Id == PNContentModule.True,
            "game registers physical, magic and true damage types");
        Check(elements.Count == 3 &&
            elements.Get(PNContentModule.Fire).Id == PNContentModule.Fire &&
            elements.Get(PNContentModule.Ice).Id == PNContentModule.Ice &&
            elements.Get(PNContentModule.Lightning).Id == PNContentModule.Lightning,
            "game registers fire, ice and lightning elements");
        Check(!elements.TryGet(PNContentModule.Physical, out _) &&
            !damageTypes.TryGet(PNContentModule.Fire, out _) &&
            !elements.TryGet(EveId.Parse("project_n:none"), out _),
            "damage and elements resolve independently without a None element");
        Reject(() => new PNDamageTypeDefinition(default), "invalid damage type ID rejected");
        Reject(() => new PNElementDefinition(default), "invalid element ID rejected");
        Reject(() => damageTypes.Register(new PNDamageTypeDefinition(PNContentModule.Physical)),
            "duplicate damage type ID rejected");
        Reject(() => elements.Register(new PNElementDefinition(PNContentModule.Fire)),
            "duplicate element ID rejected");

        var extraDamage = EveId.Parse("addon:custom_damage");
        var extraElement = EveId.Parse("addon:custom_element");
        var extended = CreateHub();
        extended.DamageTypes().Register(new PNDamageTypeDefinition(extraDamage));
        extended.Elements().Register(new PNElementDefinition(extraElement));
        extended.Seal();
        Check(extended.DamageTypes().Get(extraDamage).Id == extraDamage &&
            extended.Elements().Get(extraElement).Id == extraElement,
            "code modules can extend both registries before seal");

        hub.Seal();
        Check(damageTypes.IsSealed && elements.IsSealed, "combat registries seal with their session hub");
        Reject(() => damageTypes.Register(new PNDamageTypeDefinition(extraDamage)), "sealed damage registry rejects writes");
        Reject(() => elements.Register(new PNElementDefinition(extraElement)), "sealed element registry rejects writes");
        var same = CreateHub();
        same.Seal();
        var compatibility = Fingerprint(hub);
        compatibility.Validate(Fingerprint(same));
        Check(compatibility.ContentHashes.ContainsKey("registry:" + typeof(PNDamageTypeDefinition).FullName) &&
            compatibility.ContentHashes.ContainsKey("registry:" + typeof(PNElementDefinition).FullName),
            "both combat registries participate in coop fingerprints");

        var changedDamage = CreateHub();
        changedDamage.DamageTypes().Register(new PNDamageTypeDefinition(extraDamage));
        changedDamage.Seal();
        Reject(() => compatibility.Validate(Fingerprint(changedDamage)), "coop rejects mismatched damage definitions");
        var changedElement = CreateHub();
        changedElement.Elements().Register(new PNElementDefinition(extraElement));
        changedElement.Seal();
        Reject(() => compatibility.Validate(Fingerprint(changedElement)), "coop rejects mismatched element definitions");
    }

    private static EveRegistryHub CreateHub()
    {
        var hub = new EveRegistryHub();
        new PNContentModule().Register(hub);
        return hub;
    }

    private static EveContentCompatibility Fingerprint(EveRegistryHub hub)
        => EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:combat_registry");
}
