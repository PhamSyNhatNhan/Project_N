using System;
using System.Linq;
using EveEngine;

public static class PNMobileStyleChecks
{
    public static int Run()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("Mobile style: "+label);n++;}
        void Reject(Action action,string label){bool failed=false;try{action();}catch(ArgumentException){failed=true;}catch(InvalidOperationException){failed=true;}Check(failed,label);}
        var defaults=PNMobileStyleDefaults.IconSets;Check(defaults.Count==2&&PNMobileStyleDefaults.Palettes.Count==4,"two sets four palettes");
        Check(defaults.All(x=>x.Icons.Count==5)&&PNMobileStyleDefaults.Palettes.All(x=>x.Colors.Count==5),"complete channels");
        var copy=defaults[0].Icons.ToArray();var set=new PNMobileIconSetDefinition(EveId.Parse("test:icons"),"test:label",copy);copy[0]=null;Check(set.Icons[0]!=null,"constructor snapshot");
        Reject(()=>new PNMobileIconSetDefinition(EveId.Parse("test:bad"),"test:label",copy),"null icon rejected");
        Reject(()=>new PNMobilePaletteDefinition(EveId.Parse("test:bad"),"test:label",new PNMobileRgb[4]),"missing channel rejected");
        Reject(()=>new PNMobileRgb(float.NaN,0,0),"NaN rejected");Reject(()=>new PNMobileRgb(0,2,0),"colour bounds");
        Reject(()=>new PNIconCommand((PNIconCommandKind)999),"unknown command");Reject(()=>new PNIconCommand(PNIconCommandKind.Line,-.1f,0),"coordinate bounds");
        Reject(()=>new PNIconPath(new[]{new PNIconCommand(PNIconCommandKind.Line),new PNIconCommand(PNIconCommandKind.Line)}),"path requires move");
        Reject(()=>new PNIconPath(new[]{new PNIconCommand(PNIconCommandKind.Move),new PNIconCommand(PNIconCommandKind.Line)},false,0),"invisible path rejected");
        Reject(()=>new PNMobileIconGeometry(new PNIconPath[9]),"path count bounds");
        var foreign=new PNMobileIconSetDefinition(EveId.Parse("test:foreign"),"other:label",defaults[0].Icons);
        Reject(()=>foreign.ValidateRegistrationReferences("test",Array.Empty<string>()),"undeclared label dependency");
        foreign.ValidateRegistrationReferences("test",new[]{"other"});Check(true,"declared label dependency");
        var hub=new EveRegistryHub();new PNMobileUiContentModule().Register(hub);hub.MobileIconSets().Register(set);
        Reject(()=>hub.MobileIconSets().Register(set),"duplicate rejected");hub.Seal();Reject(()=>hub.MobileIconSets().Register(foreign),"sealed registration rejected");
        var hash=EveContentCompatibility.Create(hub,Array.Empty<EveContentDocument>(),"style-check");
        Check(hash.ContentHashes.Keys.Any(x=>x.Contains("PNMobileIconSetDefinition")),"registry fingerprint present");
        var a=new PNUserSettings("en",120,"test:missing",PNMobileStyleDefaults.Palette);var b=a.WithLocale("vi").WithFrameRate(90);
        Check(b.MobileIconSet=="test:missing"&&b.TargetFrameRate==90&&b.Locale=="vi","other setters retain style");
        var parsed=PNUserSettings.Parse(a.ToJson(),out bool unsupported);Check(!unsupported&&parsed.MobileIconSet=="test:missing","missing content ID roundtrip");
        parsed=PNUserSettings.Parse("{\"schemaVersion\":1,\"locale\":\"en\",\"targetFrameRate\":144}",out unsupported);
        Check(!unsupported&&parsed.Locale=="en"&&parsed.TargetFrameRate==144&&parsed.MobileIconSet==PNMobileStyleDefaults.IconSet,"schema1 migration");
        return n;
    }
}
