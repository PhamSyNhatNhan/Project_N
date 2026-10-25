using System;
using Newtonsoft.Json.Linq;

// Machine preferences, independent of profile/world/gameplay content.
public sealed class PNUserSettings
{
    public const int SchemaVersion = 3, MaxBytes = 16384;
    public static bool ValidFrameRate(int value) => value == 0 || value == 30 || value == 60 || value == 90 || value == 120 || value == 144;
    public string Locale { get; }
    public int TargetFrameRate { get; }
    public string MobileIconSet { get; }
    public string MobilePalette { get; }
    // How the player's character picks its target automatically (schema 3; older files read as Nearest).
    public PNTargetMode AutoTargetMode { get; }
    public PNUserSettings(string locale = null, int targetFrameRate = 60)
        :this(locale,targetFrameRate,PNMobileStyleDefaults.IconSet,PNMobileStyleDefaults.Palette) { }
    public PNUserSettings(string locale,int targetFrameRate,string mobileIconSet,string mobilePalette,PNTargetMode autoTargetMode=PNTargetMode.Nearest)
    {
        Locale = locale == null ? null : PNLocalizationCatalog.Code(locale);
        if (!ValidFrameRate(targetFrameRate)) throw new ArgumentOutOfRangeException(nameof(targetFrameRate));
        if (!Enum.IsDefined(typeof(PNTargetMode), autoTargetMode)) throw new ArgumentOutOfRangeException(nameof(autoTargetMode));
        TargetFrameRate = targetFrameRate;
        MobileIconSet=EveEngine.EveId.Parse(mobileIconSet).ToString();MobilePalette=EveEngine.EveId.Parse(mobilePalette).ToString();
        AutoTargetMode=autoTargetMode;
    }
    public PNUserSettings WithLocale(string value)=>new PNUserSettings(value,TargetFrameRate,MobileIconSet,MobilePalette,AutoTargetMode);
    public PNUserSettings WithFrameRate(int value)=>new PNUserSettings(Locale,value,MobileIconSet,MobilePalette,AutoTargetMode);
    public PNUserSettings WithMobileStyle(string iconSet,string palette)=>new PNUserSettings(Locale,TargetFrameRate,iconSet,palette,AutoTargetMode);
    public PNUserSettings WithAutoTargetMode(PNTargetMode value)=>new PNUserSettings(Locale,TargetFrameRate,MobileIconSet,MobilePalette,value);
    public string ToJson()
    {
        if (Locale == null) throw new InvalidOperationException("Default locale must be resolved before saving.");
        return new JObject(new JProperty("schemaVersion", SchemaVersion), new JProperty("locale", Locale), new JProperty("targetFrameRate", TargetFrameRate),new JProperty("mobileIconSet",MobileIconSet),new JProperty("mobilePalette",MobilePalette),new JProperty("autoTargetMode",PNTargetRule.ModeId(AutoTargetMode))).ToString();
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
            if (field.Name != "schemaVersion" && field.Name != "locale" && field.Name != "targetFrameRate" && !(version>=2&&(field.Name=="mobileIconSet"||field.Name=="mobilePalette")) && !(version>=3&&field.Name=="autoTargetMode")) { unsupported = true; return null; }
        if (root["locale"]?.Type != JTokenType.String || root["targetFrameRate"]?.Type != JTokenType.Integer) throw new FormatException("Invalid settings fields.");
        long fps = (long)root["targetFrameRate"];
        if (fps < 0 || fps > int.MaxValue || !ValidFrameRate((int)fps)) throw new FormatException("Unsupported frame rate.");
        if(version==1)return new PNUserSettings((string)root["locale"], (int)fps);
        if(root["mobileIconSet"]?.Type!=JTokenType.String||root["mobilePalette"]?.Type!=JTokenType.String)throw new FormatException("Invalid mobile style IDs.");
        if(version==2)return new PNUserSettings((string)root["locale"], (int)fps,(string)root["mobileIconSet"],(string)root["mobilePalette"]);
        if(root["autoTargetMode"]?.Type!=JTokenType.String)throw new FormatException("Invalid auto target mode.");
        return new PNUserSettings((string)root["locale"], (int)fps,(string)root["mobileIconSet"],(string)root["mobilePalette"],PNTargetRule.ParseMode((string)root["autoTargetMode"]));
    }
}
