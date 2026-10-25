using System;
using System.IO;
using System.Text;

public static class PNSettingsChecks
{
    public static int Run(string root)
    {
        int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception("Settings: "+label);checks++;}
        string folder=Path.Combine(root,".utmp/Settings/Contracts-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var store=new PNSettingsStore(folder);string file=store.FilePath;
        Check(store.Load().Status==PNSettingsLoadStatus.Missing,"missing defaults");
        PNLanguagePreferenceStore.Save(Path.Combine(folder,"localization.json"),"en");
        var legacy=store.Load();Check(legacy.Status==PNSettingsLoadStatus.Legacy&&legacy.Value.Locale=="en","legacy preference");
        store.Save(legacy.Value);Check(File.Exists(Path.Combine(folder,"localization.json")),"legacy retained");
        store.Save(new PNUserSettings("vi",120));Check(store.Load().Value.TargetFrameRate==120,"FPS and locale saved together");
        Check(PNUserSettings.Parse(File.ReadAllText(file+".bak"),out _).Locale=="en","previous valid backup");
        string broken="{broken";File.WriteAllText(file,broken);var recovered=store.Load();Check(recovered.Status==PNSettingsLoadStatus.Backup&&recovered.Value.Locale=="en","backup recovery");
        store.Save(new PNUserSettings("vi",90));Check(File.ReadAllText(file+".bak").Contains("en"),"corrupt main cannot replace good backup");
        Check(Directory.GetFiles(folder,"*.corrupt-*").Length==1,"invalid source preserved");
        File.WriteAllText(file,"{\"schemaVersion\":4,\"locale\":\"fr\",\"targetFrameRate\":60}");string newer=File.ReadAllText(file);
        Check(store.Load().Status==PNSettingsLoadStatus.Unsupported,"newer main locks writes");
        bool refused=false;try{store.Save(new PNUserSettings("vi"));}catch(InvalidOperationException){refused=true;}
        Check(refused&&File.ReadAllText(file)==newer,"newer file untouched");
        string missingRoot=Path.Combine(folder,"backupOnly");Directory.CreateDirectory(missingRoot);var backupOnly=new PNSettingsStore(missingRoot);
        File.WriteAllText(backupOnly.FilePath+".bak",new PNUserSettings("en",144).ToJson());File.WriteAllText(backupOnly.FilePath+".tmp","incomplete");
        Check(backupOnly.Load().Status==PNSettingsLoadStatus.Backup&&backupOnly.Load().Value.TargetFrameRate==144,"missing main uses backup, ignores temp");
        backupOnly.Save(new PNUserSettings("fr",0));Check(backupOnly.Load().Value.Locale=="fr"&&backupOnly.Load().Value.TargetFrameRate==0,"unavailable locale code and unlimited preserved");
        File.WriteAllText(backupOnly.FilePath,"broken");File.WriteAllText(backupOnly.FilePath+".bak",newer);Check(backupOnly.Load().Status==PNSettingsLoadStatus.Unsupported,"newer backup protected");
        string baseJson=new PNUserSettings("vi").ToJson();
        foreach(string json in new[]{baseJson.Replace("60","61"),baseJson.Replace("60","\"60\""),baseJson.Replace("60","60.0"),baseJson.Replace("\"locale\": \"vi\"","\"locale\": null"),"{\"schemaVersion\":1,\"locale\":\"vi\",\"locale\":\"en\",\"targetFrameRate\":60}",baseJson+"{}",new string(' ',PNUserSettings.MaxBytes+1)})
        {bool invalid=false;try{PNUserSettings.Parse(json,out _);}catch(Exception e)when(e is FormatException||e is ArgumentException){invalid=true;}Check(invalid,"invalid document rejected");}
        PNUserSettings.Parse(baseJson.Replace("\"locale\"","\"unknown\""),out bool unsupported);Check(unsupported,"unknown fields protected");
        string corruptRoot=Path.Combine(folder,"corrupt");Directory.CreateDirectory(corruptRoot);var corrupt=new PNSettingsStore(corruptRoot);
        File.WriteAllBytes(corrupt.FilePath,new byte[]{0xff,0xfe,0xff});PNLanguagePreferenceStore.Save(Path.Combine(corruptRoot,"localization.json"),"en");
        Check(corrupt.Load().Status==PNSettingsLoadStatus.Corrupt&&corrupt.Load().Value.Locale==null,"invalid UTF8 does not revert to stale legacy");
        var live=new PNSettingsStore(Path.Combine(folder,"changedAfterLoad"));live.Load();Directory.CreateDirectory(Path.GetDirectoryName(live.FilePath));File.WriteAllText(live.FilePath,newer);
        refused=false;try{live.Save(new PNUserSettings("vi"));}catch(InvalidOperationException){refused=true;}Check(refused,"newer file appearing after load refused");
        // Schema 3: auto target mode (default Nearest; older schemas read as Nearest and save as 3).
        Check(new PNUserSettings("vi").AutoTargetMode==PNTargetMode.Nearest&&baseJson.Contains("\"autoTargetMode\": \"nearest\"")&&baseJson.Contains("\"schemaVersion\": 3"),"schema 3 default auto target nearest");
        var lowest=new PNUserSettings("en",90).WithAutoTargetMode(PNTargetMode.LowestHealthPercent);
        var lowestParsed=PNUserSettings.Parse(lowest.ToJson(),out bool lowestUnsupported);
        Check(!lowestUnsupported&&lowestParsed.AutoTargetMode==PNTargetMode.LowestHealthPercent&&lowestParsed.TargetFrameRate==90,"auto target mode roundtrip");
        Check(lowest.WithLocale("vi").WithFrameRate(60).WithMobileStyle(PNMobileStyleDefaults.IconSet,PNMobileStyleDefaults.Palette).AutoTargetMode==PNTargetMode.LowestHealthPercent,"other setters retain auto target mode");
        Check(lowest.WithAutoTargetMode(PNTargetMode.LowestHealth).AutoTargetMode==PNTargetMode.LowestHealth&&lowest.WithAutoTargetMode(PNTargetMode.LowestHealth).Locale=="en","auto target setter retains other fields");
        string v2="{\"schemaVersion\":2,\"locale\":\"en\",\"targetFrameRate\":60,\"mobileIconSet\":\""+PNMobileStyleDefaults.IconSet+"\",\"mobilePalette\":\""+PNMobileStyleDefaults.Palette+"\"}";
        var fromV2=PNUserSettings.Parse(v2,out bool v2Unsupported);
        Check(!v2Unsupported&&fromV2.AutoTargetMode==PNTargetMode.Nearest&&fromV2.MobileIconSet==PNMobileStyleDefaults.IconSet&&fromV2.ToJson().Contains("\"schemaVersion\": 3"),"schema 2 migrates to 3 with nearest");
        var fromV1=PNUserSettings.Parse("{\"schemaVersion\":1,\"locale\":\"vi\",\"targetFrameRate\":30}",out bool v1Unsupported);
        Check(!v1Unsupported&&fromV1.AutoTargetMode==PNTargetMode.Nearest&&fromV1.TargetFrameRate==30,"schema 1 migrates with nearest");
        PNUserSettings.Parse(v2.Replace("}",",\"autoTargetMode\":\"nearest\"}"),out bool v2Field);Check(v2Field,"auto target field in a schema 2 file is protected as unknown");
        foreach(string json in new[]{baseJson.Replace("\"nearest\"","\"Nearest\""),baseJson.Replace("\"nearest\"","\"boss\""),baseJson.Replace("\"nearest\"","1"),WithoutMode(baseJson),"{\"schemaVersion\":0,\"locale\":\"vi\",\"targetFrameRate\":60}"})
        {bool invalid=false;try{PNUserSettings.Parse(json,out _);}catch(Exception e)when(e is FormatException||e is ArgumentException){invalid=true;}Check(invalid,"invalid auto target document rejected: "+json.Length);}
        bool badMode=false;try{new PNUserSettings("vi",60,PNMobileStyleDefaults.IconSet,PNMobileStyleDefaults.Palette,(PNTargetMode)9);}catch(ArgumentOutOfRangeException){badMode=true;}Check(badMode,"undefined auto target mode rejected");
        string v3Root=Path.Combine(folder,"schema3");Directory.CreateDirectory(v3Root);var v3Store=new PNSettingsStore(v3Root);File.WriteAllText(v3Store.FilePath,v2);
        var v3Load=v3Store.Load();Check(v3Load.Status==PNSettingsLoadStatus.Loaded&&v3Load.Value.AutoTargetMode==PNTargetMode.Nearest,"schema 2 file loads");
        v3Store.Save(v3Load.Value.WithAutoTargetMode(PNTargetMode.LowestHealth));Check(v3Store.Load().Value.AutoTargetMode==PNTargetMode.LowestHealth&&File.ReadAllText(v3Store.FilePath).Contains("lowest_health"),"auto target mode saved as schema 3");
        return checks+PNMobileStyleChecks.Run();
    }
    private static string WithoutMode(string json){var root=Newtonsoft.Json.Linq.JObject.Parse(json);root.Remove("autoTargetMode");return root.ToString();}
}
