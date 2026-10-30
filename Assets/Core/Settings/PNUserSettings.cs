using System;
using Newtonsoft.Json.Linq;

// Machine preferences, independent of profile/world/gameplay content.
public sealed class PNUserSettings
{
    public const int SchemaVersion = 7, MaxBytes = 16384;
    public static bool ValidFrameRate(int value) => value == 0 || value == 30 || value == 60 || value == 90 || value == 120 || value == 144;
    public string Locale { get; }
    public int TargetFrameRate { get; }
    public string MobileIconSet { get; }
    public string MobilePalette { get; }
    // How the player's character picks its target automatically (schema 3; older files read as Nearest).
    public PNTargetMode AutoTargetMode { get; }
    public bool ShowPcControls { get; }
    public bool ShowPcBindingLabels { get; }
    // The overlap shadow between the parts of a puppet (schema 6; older files read as Actors).
    public PNPaperShadowMode PaperShadow { get; }
    // How the ink marks are made: baked into the atlas or live (schema 7; older files read as Baked).
    public PNInkMode InkMode { get; }
    public PNUserSettings(string locale = null, int targetFrameRate = 60)
        :this(locale,targetFrameRate,PNMobileStyleDefaults.IconSet,PNMobileStyleDefaults.Palette) { }
    public PNUserSettings(string locale,int targetFrameRate,string mobileIconSet,string mobilePalette,PNTargetMode autoTargetMode=PNTargetMode.Nearest)
        :this(locale,targetFrameRate,mobileIconSet,mobilePalette,autoTargetMode,false) { }
    public PNUserSettings(string locale,int targetFrameRate,string mobileIconSet,string mobilePalette,PNTargetMode autoTargetMode,bool showPcControls,bool showPcBindingLabels=true,PNPaperShadowMode paperShadow=PNPaperShadowMode.Actors,PNInkMode inkMode=PNInkMode.Baked)
    {
        Locale = locale == null ? null : PNLocalizationCatalog.Code(locale);
        if (!ValidFrameRate(targetFrameRate)) throw new ArgumentOutOfRangeException(nameof(targetFrameRate));
        if (!Enum.IsDefined(typeof(PNTargetMode), autoTargetMode)) throw new ArgumentOutOfRangeException(nameof(autoTargetMode));
        if (!Enum.IsDefined(typeof(PNPaperShadowMode), paperShadow)) throw new ArgumentOutOfRangeException(nameof(paperShadow));
        if (!Enum.IsDefined(typeof(PNInkMode), inkMode)) throw new ArgumentOutOfRangeException(nameof(inkMode));
        TargetFrameRate = targetFrameRate;
        MobileIconSet=EveEngine.EveId.Parse(mobileIconSet).ToString();MobilePalette=EveEngine.EveId.Parse(mobilePalette).ToString();
        AutoTargetMode=autoTargetMode;
        ShowPcControls=showPcControls;
        ShowPcBindingLabels=showPcBindingLabels;
        PaperShadow=paperShadow;
        InkMode=inkMode;
    }
    public PNUserSettings WithLocale(string value)=>new PNUserSettings(value,TargetFrameRate,MobileIconSet,MobilePalette,AutoTargetMode,ShowPcControls,ShowPcBindingLabels,PaperShadow,InkMode);
    public PNUserSettings WithFrameRate(int value)=>new PNUserSettings(Locale,value,MobileIconSet,MobilePalette,AutoTargetMode,ShowPcControls,ShowPcBindingLabels,PaperShadow,InkMode);
    public PNUserSettings WithMobileStyle(string iconSet,string palette)=>new PNUserSettings(Locale,TargetFrameRate,iconSet,palette,AutoTargetMode,ShowPcControls,ShowPcBindingLabels,PaperShadow,InkMode);
    public PNUserSettings WithAutoTargetMode(PNTargetMode value)=>new PNUserSettings(Locale,TargetFrameRate,MobileIconSet,MobilePalette,value,ShowPcControls,ShowPcBindingLabels,PaperShadow,InkMode);
    public PNUserSettings WithShowPcControls(bool value)=>new PNUserSettings(Locale,TargetFrameRate,MobileIconSet,MobilePalette,AutoTargetMode,value,ShowPcBindingLabels,PaperShadow,InkMode);
    public PNUserSettings WithShowPcBindingLabels(bool value)=>new PNUserSettings(Locale,TargetFrameRate,MobileIconSet,MobilePalette,AutoTargetMode,ShowPcControls,value,PaperShadow,InkMode);
    public PNUserSettings WithPaperShadow(PNPaperShadowMode value)=>new PNUserSettings(Locale,TargetFrameRate,MobileIconSet,MobilePalette,AutoTargetMode,ShowPcControls,ShowPcBindingLabels,value,InkMode);
    public PNUserSettings WithInkMode(PNInkMode value)=>new PNUserSettings(Locale,TargetFrameRate,MobileIconSet,MobilePalette,AutoTargetMode,ShowPcControls,ShowPcBindingLabels,PaperShadow,value);
    // Comparison helper for a draft: the saved text, or the empty string while the locale is still unresolved.
    public string ToJsonOrNull() => Locale == null ? "" : ToJson();
    public string ToJson()
    {
        if (Locale == null) throw new InvalidOperationException("Default locale must be resolved before saving.");
        return new JObject(new JProperty("schemaVersion", SchemaVersion), new JProperty("locale", Locale), new JProperty("targetFrameRate", TargetFrameRate),new JProperty("mobileIconSet",MobileIconSet),new JProperty("mobilePalette",MobilePalette),new JProperty("autoTargetMode",PNTargetRule.ModeId(AutoTargetMode)),new JProperty("showPcControls",ShowPcControls),new JProperty("showPcBindingLabels",ShowPcBindingLabels),new JProperty("paperShadow",PNPaperShadowModes.Id(PaperShadow)),new JProperty("inkMode",PNInkModes.Id(InkMode))).ToString();
    }
    public static PNUserSettings Parse(string json, out bool unsupported)
    {
        unsupported = false;
        var root = PNLocalizationCatalog.Object(json, MaxBytes, "settings.json");
        if (root["schemaVersion"]?.Type != JTokenType.Integer) throw new FormatException("Invalid settings schema.");
        var version = (long)root["schemaVersion"];
        if (version > SchemaVersion) { unsupported = true; return null; }
        if (version < 1) throw new FormatException("Invalid settings schema.");
        foreach (var field in root.Properties())
            if (field.Name != "schemaVersion" && field.Name != "locale" && field.Name != "targetFrameRate" && !(version>=2&&(field.Name=="mobileIconSet"||field.Name=="mobilePalette")) && !(version>=3&&field.Name=="autoTargetMode") && !(version>=4&&field.Name=="showPcControls") && !(version>=5&&field.Name=="showPcBindingLabels") && !(version>=6&&field.Name=="paperShadow") && !(version>=7&&field.Name=="inkMode")) { unsupported = true; return null; }
        if (root["locale"]?.Type != JTokenType.String || root["targetFrameRate"]?.Type != JTokenType.Integer) throw new FormatException("Invalid settings fields.");
        long fps = (long)root["targetFrameRate"];
        if (fps < 0 || fps > int.MaxValue || !ValidFrameRate((int)fps)) throw new FormatException("Unsupported frame rate.");
        if(version==1)return new PNUserSettings((string)root["locale"], (int)fps);
        if(root["mobileIconSet"]?.Type!=JTokenType.String||root["mobilePalette"]?.Type!=JTokenType.String)throw new FormatException("Invalid mobile style IDs.");
        if(version==2)return new PNUserSettings((string)root["locale"], (int)fps,(string)root["mobileIconSet"],(string)root["mobilePalette"]);
        if(root["autoTargetMode"]?.Type!=JTokenType.String)throw new FormatException("Invalid auto target mode.");
        if(version>=4&&root["showPcControls"]?.Type!=JTokenType.Boolean)throw new FormatException("Invalid PC controls preference.");
        if(version>=5&&root["showPcBindingLabels"]?.Type!=JTokenType.Boolean)throw new FormatException("Invalid PC binding labels preference.");
        if(version>=6&&root["paperShadow"]?.Type!=JTokenType.String)throw new FormatException("Invalid paper shadow preference.");
        if(version>=7&&root["inkMode"]?.Type!=JTokenType.String)throw new FormatException("Invalid ink mode preference.");
        return new PNUserSettings((string)root["locale"], (int)fps,(string)root["mobileIconSet"],(string)root["mobilePalette"],PNTargetRule.ParseMode((string)root["autoTargetMode"]),version>=4&&(bool)root["showPcControls"],version<5||(bool)root["showPcBindingLabels"],version<6?PNPaperShadowMode.Actors:PNPaperShadowModes.Parse((string)root["paperShadow"]),version<7?PNInkMode.Baked:PNInkModes.Parse((string)root["inkMode"]));
    }
}
