#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// Read view of the published content generation; stock is available before Title boot.
public static class PNMobileStyleCatalog
{
    private static EveRegistryHub hub;
    private static object generation;
    public static event Action Changed;
    public static IReadOnlyList<PNMobileIconSetDefinition> IconSets=>hub==null?PNMobileStyleDefaults.IconSets:Array.AsReadOnly(hub.MobileIconSets().Snapshot().Values.OrderBy(x=>x.Order).ThenBy(x=>x.Id.ToString(),StringComparer.Ordinal).ToArray());
    public static IReadOnlyList<PNMobilePaletteDefinition> Palettes=>hub==null?PNMobileStyleDefaults.Palettes:Array.AsReadOnly(hub.MobilePalettes().Snapshot().Values.OrderBy(x=>x.Order).ThenBy(x=>x.Id.ToString(),StringComparer.Ordinal).ToArray());
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset(){hub=null;generation=null;Changed=null;}
    internal static void Publish(EveContentContext content)
    {
        // Legacy memory-content fixtures may not install the UI module.
        if(!content.Registry.Snapshot().ContainsKey(typeof(PNMobileIconSetDefinition)))return;
        object token=new object();content.RegisterCleanup(()=>{if(ReferenceEquals(generation,token)){hub=null;generation=null;Notify();}});
        hub=content.Registry;generation=token;Notify();
    }
    public static bool HasIconSet(string id)=>TryIcon(id,out _);
    public static bool HasPalette(string id)=>TryPalette(id,out _);
    private static bool TryIcon(string id,out PNMobileIconSetDefinition value)
    {var key=EveId.Parse(id);if(hub!=null)return hub.MobileIconSets().TryGet(key,out value);value=PNMobileStyleDefaults.IconSets.FirstOrDefault(x=>x.Id==key);return value!=null;}
    private static bool TryPalette(string id,out PNMobilePaletteDefinition value)
    {var key=EveId.Parse(id);if(hub!=null)return hub.MobilePalettes().TryGet(key,out value);value=PNMobileStyleDefaults.Palettes.FirstOrDefault(x=>x.Id==key);return value!=null;}
    public static PNMobileIconSetDefinition ResolveIconSet(string requested)=>TryIcon(requested,out var result)?result:PNMobileStyleDefaults.IconSets[0];
    public static PNMobilePaletteDefinition ResolvePalette(string requested)=>TryPalette(requested,out var result)?result:PNMobileStyleDefaults.Palettes[0];
    private static void Notify(){if(Changed==null)return;foreach(Action listener in Changed.GetInvocationList())try{listener();}catch(Exception e){Debug.LogException(e);}}
}
#endif
