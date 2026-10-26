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
        File.WriteAllText(file,"{\"schemaVersion\":"+(PNUserSettings.SchemaVersion+1)+",\"locale\":\"fr\",\"targetFrameRate\":60}");string newer=File.ReadAllText(file);
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
        Check(new PNUserSettings("vi").AutoTargetMode==PNTargetMode.Nearest&&baseJson.Contains("\"autoTargetMode\": \"nearest\"")&&baseJson.Contains("\"schemaVersion\": "+PNUserSettings.SchemaVersion),"current schema default auto target nearest");
        var lowest=new PNUserSettings("en",90).WithAutoTargetMode(PNTargetMode.LowestHealthPercent);
        var lowestParsed=PNUserSettings.Parse(lowest.ToJson(),out bool lowestUnsupported);
        Check(!lowestUnsupported&&lowestParsed.AutoTargetMode==PNTargetMode.LowestHealthPercent&&lowestParsed.TargetFrameRate==90,"auto target mode roundtrip");
        Check(lowest.WithLocale("vi").WithFrameRate(60).WithMobileStyle(PNMobileStyleDefaults.IconSet,PNMobileStyleDefaults.Palette).AutoTargetMode==PNTargetMode.LowestHealthPercent,"other setters retain auto target mode");
        Check(lowest.WithAutoTargetMode(PNTargetMode.LowestHealth).AutoTargetMode==PNTargetMode.LowestHealth&&lowest.WithAutoTargetMode(PNTargetMode.LowestHealth).Locale=="en","auto target setter retains other fields");
        string v2="{\"schemaVersion\":2,\"locale\":\"en\",\"targetFrameRate\":60,\"mobileIconSet\":\""+PNMobileStyleDefaults.IconSet+"\",\"mobilePalette\":\""+PNMobileStyleDefaults.Palette+"\"}";
        var fromV2=PNUserSettings.Parse(v2,out bool v2Unsupported);
        Check(!v2Unsupported&&fromV2.AutoTargetMode==PNTargetMode.Nearest&&fromV2.MobileIconSet==PNMobileStyleDefaults.IconSet&&fromV2.ToJson().Contains("\"schemaVersion\": "+PNUserSettings.SchemaVersion),"schema 2 migrates with nearest");
        var fromV1=PNUserSettings.Parse("{\"schemaVersion\":1,\"locale\":\"vi\",\"targetFrameRate\":30}",out bool v1Unsupported);
        Check(!v1Unsupported&&fromV1.AutoTargetMode==PNTargetMode.Nearest&&fromV1.TargetFrameRate==30,"schema 1 migrates with nearest");
        PNUserSettings.Parse(v2.Replace("}",",\"autoTargetMode\":\"nearest\"}"),out bool v2Field);Check(v2Field,"auto target field in a schema 2 file is protected as unknown");
        foreach(string json in new[]{baseJson.Replace("\"nearest\"","\"Nearest\""),baseJson.Replace("\"nearest\"","\"boss\""),baseJson.Replace("\"nearest\"","1"),WithoutMode(baseJson),"{\"schemaVersion\":0,\"locale\":\"vi\",\"targetFrameRate\":60}"})
        {bool invalid=false;try{PNUserSettings.Parse(json,out _);}catch(Exception e)when(e is FormatException||e is ArgumentException){invalid=true;}Check(invalid,"invalid auto target document rejected: "+json.Length);}
        bool badMode=false;try{new PNUserSettings("vi",60,PNMobileStyleDefaults.IconSet,PNMobileStyleDefaults.Palette,(PNTargetMode)9);}catch(ArgumentOutOfRangeException){badMode=true;}Check(badMode,"undefined auto target mode rejected");
        string v3Root=Path.Combine(folder,"schema3");Directory.CreateDirectory(v3Root);var v3Store=new PNSettingsStore(v3Root);File.WriteAllText(v3Store.FilePath,v2);
        var v3Load=v3Store.Load();Check(v3Load.Status==PNSettingsLoadStatus.Loaded&&v3Load.Value.AutoTargetMode==PNTargetMode.Nearest,"schema 2 file loads");
        v3Store.Save(v3Load.Value.WithAutoTargetMode(PNTargetMode.LowestHealth));Check(v3Store.Load().Value.AutoTargetMode==PNTargetMode.LowestHealth&&File.ReadAllText(v3Store.FilePath).Contains("lowest_health"),"auto target mode saved as schema 3");
        var pc=lowest.WithShowPcControls(true);
        Check(!new PNUserSettings("vi").ShowPcControls&&PNUserSettings.Parse(pc.ToJson(),out _).ShowPcControls,"PC HUD defaults off and roundtrips on");
        var preserved=pc.WithLocale("vi").WithFrameRate(144).WithMobileStyle("test:missing",PNMobileStyleDefaults.Palette).WithAutoTargetMode(PNTargetMode.LowestHealth);
        Check(preserved.ShowPcControls&&preserved.Locale=="vi"&&preserved.TargetFrameRate==144&&preserved.MobileIconSet=="test:missing"&&preserved.AutoTargetMode==PNTargetMode.LowestHealth,"all setters preserve PC HUD");
        var off=preserved.WithShowPcControls(false);
        Check(!off.ShowPcControls&&off.Locale==preserved.Locale&&off.TargetFrameRate==preserved.TargetFrameRate&&off.MobileIconSet==preserved.MobileIconSet&&off.MobilePalette==preserved.MobilePalette&&off.AutoTargetMode==preserved.AutoTargetMode,"PC setter preserves all preferences");
        var old3=Newtonsoft.Json.Linq.JObject.Parse(lowest.ToJson());old3["schemaVersion"]=3;old3.Remove("showPcControls");old3.Remove("showPcBindingLabels");
        Check(!fromV1.ShowPcControls&&!fromV2.ShowPcControls&&!PNUserSettings.Parse(old3.ToString(),out _).ShowPcControls,"schemas 1 2 3 default PC HUD off");
        old3["showPcControls"]=true;PNUserSettings.Parse(old3.ToString(),out bool oldField);Check(oldField,"PC field in old schema protected");
        foreach(var token in new Newtonsoft.Json.Linq.JToken[]{null,new Newtonsoft.Json.Linq.JValue("true"),new Newtonsoft.Json.Linq.JValue(1),Newtonsoft.Json.Linq.JValue.CreateNull()})
        {var json=Newtonsoft.Json.Linq.JObject.Parse(pc.ToJson());if(token==null)json.Remove("showPcControls");else json["showPcControls"]=token;bool invalid=false;try{PNUserSettings.Parse(json.ToString(),out _);}catch(FormatException){invalid=true;}Check(invalid,"missing or non-bool PC preference rejected");}
        string oldRoot=Path.Combine(folder,"schema3Migration");Directory.CreateDirectory(oldRoot);old3.Remove("showPcControls");old3.Remove("showPcBindingLabels");string oldText=old3.ToString();var oldStore=new PNSettingsStore(oldRoot);File.WriteAllText(oldStore.FilePath,oldText);
        var loaded=oldStore.Load();Check(!loaded.Value.ShowPcControls&&File.ReadAllText(oldStore.FilePath)==oldText,"schema3 load does not rewrite file");
        oldStore.Save(loaded.Value.WithShowPcControls(true));Check(oldStore.Load().Value.ShowPcControls&&oldStore.Load().Value.AutoTargetMode==lowest.AutoTargetMode,"schema3 explicit save migrates and keeps mode");
        Check(new PNUserSettings("vi").ShowPcBindingLabels&&fromV1.ShowPcBindingLabels&&fromV2.ShowPcBindingLabels,"labels default on including old schemas");
        var labelsOff=pc.WithShowPcBindingLabels(false);
        Check(!PNUserSettings.Parse(labelsOff.ToJson(),out _).ShowPcBindingLabels,"hidden labels roundtrip");
        var keep=labelsOff.WithLocale("en").WithFrameRate(120).WithAutoTargetMode(PNTargetMode.LowestHealth).WithMobileStyle(PNMobileStyleDefaults.IconSet,PNMobileStyleDefaults.Palette).WithShowPcControls(false);
        Check(!keep.ShowPcBindingLabels&&!keep.ShowPcControls&&keep.TargetFrameRate==120,"all setters preserve hidden labels");
        Check(labelsOff.ShowPcControls&&labelsOff.AutoTargetMode==pc.AutoTargetMode&&labelsOff.Locale==pc.Locale,"label setter independent of HUD and other preferences");
        var old4=Newtonsoft.Json.Linq.JObject.Parse(pc.ToJson());old4["schemaVersion"]=4;old4.Remove("showPcBindingLabels");
        Check(PNUserSettings.Parse(old4.ToString(),out _).ShowPcBindingLabels&&PNUserSettings.Parse(old4.ToString(),out _).ShowPcControls,"schema4 keeps HUD and defaults labels on");
        string old4Root=Path.Combine(folder,"schema4Migration");Directory.CreateDirectory(old4Root);var old4Store=new PNSettingsStore(old4Root);string old4Text=old4.ToString();File.WriteAllText(old4Store.FilePath,old4Text);
        var read4=old4Store.Load();Check(File.ReadAllText(old4Store.FilePath)==old4Text,"schema4 read does not rewrite");old4Store.Save(read4.Value.WithShowPcBindingLabels(false));Check(!old4Store.Load().Value.ShowPcBindingLabels&&old4Store.Load().Value.ShowPcControls,"schema4 explicit save migrates independently");
        old4["showPcBindingLabels"]=false;PNUserSettings.Parse(old4.ToString(),out bool protected4);Check(protected4,"new label field in old schema protected");
        foreach(var token in new Newtonsoft.Json.Linq.JToken[]{null,new Newtonsoft.Json.Linq.JValue("false"),new Newtonsoft.Json.Linq.JValue(0),Newtonsoft.Json.Linq.JValue.CreateNull()})
        {var json=Newtonsoft.Json.Linq.JObject.Parse(labelsOff.ToJson());if(token==null)json.Remove("showPcBindingLabels");else json["showPcBindingLabels"]=token;bool invalid=false;try{PNUserSettings.Parse(json.ToString(),out _);}catch(FormatException){invalid=true;}Check(invalid,"missing or non-bool label preference rejected");}
        return checks+PNMobileStyleChecks.Run();
    }
    private static string WithoutMode(string json){var root=Newtonsoft.Json.Linq.JObject.Parse(json);root.Remove("autoTargetMode");return root.ToString();}
}
