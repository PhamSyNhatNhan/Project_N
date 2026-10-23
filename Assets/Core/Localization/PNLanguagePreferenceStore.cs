using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

public static class PNLanguagePreferenceStore
{
    public static string Load(string file)
    {
        foreach(string candidate in new[]{file,file+".bak"})try {if(!File.Exists(candidate)||new FileInfo(candidate).Length>4096)continue;var root=PNLocalizationCatalog.Object(new UTF8Encoding(false,true).GetString(File.ReadAllBytes(candidate)),4096,candidate);PNLocalizationCatalog.Fields(root,"schemaVersion","locale");if(root["schemaVersion"]?.Type!=JTokenType.Integer||(int)root["schemaVersion"]!=1)continue;return PNLocalizationCatalog.Code((string)root["locale"]);}catch(Exception e)when(e is IOException||e is FormatException||e is UnauthorizedAccessException||e is DecoderFallbackException){ }
        return null;
    }
    public static void Save(string file,string code)
    {
        code=PNLocalizationCatalog.Code(code);Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+".tmp";
        var json=new JObject(new JProperty("schemaVersion",1),new JProperty("locale",code)).ToString();File.WriteAllText(temp,json,new UTF8Encoding(false));
        if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);
    }
}
