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
        File.WriteAllText(file,"{\"schemaVersion\":3,\"locale\":\"fr\",\"targetFrameRate\":60}");string newer=File.ReadAllText(file);
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
        return checks+PNMobileStyleChecks.Run();
    }
}
