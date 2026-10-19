using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Immutable, bounded and Unity-free. Disk integrity is handled by the file store.
public sealed class PNWorldSaveEnvelope
{
    public const int MaximumBytes=64*1024*1024;
    public string Json {get;}
    public int Bytes {get;}
    public JObject Document=>Parse(Json);
    public Guid SaveWorldId {get;}
    public string Definition {get;}
    public string ContentHash {get;}
    public PNWorldSaveEnvelope(string json)
    {
        if(json==null||Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new FormatException("World save exceeds bound.");
        var doc=Parse(json);Validate(doc);SaveWorldId=Guid.Parse((string)doc["world"]);Definition=(string)doc["definition"];ContentHash=(string)doc["hash"];Json=doc.ToString(Formatting.None);Bytes=Encoding.UTF8.GetByteCount(Json);
    }
    public static PNWorldSaveEnvelope Create(Guid world,string definition,string hash,EveContentCompatibility compatibility,JToken descriptor,JObject providers,JToken state)
        =>new PNWorldSaveEnvelope(new JObject{["format"]=1,["world"]=world.ToString("N"),["definition"]=definition,["hash"]=hash,
            ["protocol"]=compatibility.ProtocolVersion,["rules"]=compatibility.RulesVersion,["content"]=JObject.FromObject(compatibility.ContentHashes),
            ["descriptor"]=descriptor.DeepClone(),["providers"]=providers.DeepClone(),["state"]=state.DeepClone()}.ToString(Formatting.None));
    public void ValidateCompatibility(EveContentCompatibility compatibility)
    {
        var d=Document;var hashes=((JObject)d["content"]).Properties().ToDictionary(p=>p.Name,p=>(string)p.Value,StringComparer.Ordinal);
        compatibility.Validate(new EveContentCompatibility((int)d["protocol"],(string)d["rules"],hashes));
    }
    public static string Hash(byte[] data)
    {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
    private static JObject Parse(string text)
    {using(var sr=new StringReader(text))using(var reader=new JsonTextReader(sr){MaxDepth=64,DateParseHandling=DateParseHandling.None}){var result=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new FormatException("Trailing save data.");return result;}}
    private static void Validate(JObject d)
    {
        if((int?)d["format"]!=1||!Guid.TryParse((string)d["world"],out var id)||id==Guid.Empty||!EveId.TryParse((string)d["definition"],out _)||
            ((string)d["hash"])?.Length!=64||!((string)d["hash"]).All(Uri.IsHexDigit)||!(d["descriptor"] is JObject)||!(d["providers"] is JObject)||!(d["content"] is JObject)||
            !(d["state"] is JArray state)||state.Count!=1||!(state[0] is JObject))throw new FormatException("Invalid portable world envelope.");
        var record=(JObject)state[0];if((int?)record["schema"]!=1||!(record["actors"] is JArray actors)||actors.Count>256||!(record["world"] is JArray)||
            (double?)record["time"]==null||(double)record["time"]<0)throw new FormatException("Invalid checkpoint structure.");
        var keys=actors.Select(a=>(string)a["key"]).ToArray();if(keys.Any(k=>string.IsNullOrWhiteSpace(k)||k.Length>4096)||keys.Distinct(StringComparer.Ordinal).Count()!=keys.Length)throw new FormatException("Unique stable actor keys required.");
        foreach(var actor in actors)
        {
            if(!(actor is JObject)||!(actor["state"] is JArray)||string.IsNullOrWhiteSpace((string)actor["slot"])||!EveId.TryParse((string)actor["character"],out _))throw new FormatException("Invalid saved actor.");
            foreach(var field in new[]{"position","velocity","angular","rotation"})
                if(!(actor[field] is JArray vector)||vector.Count!=(field=="rotation"?4:3)||vector.Any(x=>x.Type!=JTokenType.Float&&x.Type!=JTokenType.Integer))throw new FormatException("Invalid saved actor vector.");
            var rotation=(JArray)actor["rotation"];double norm=rotation.Sum(x=>(double)x*(double)x);if(norm<.99||norm>1.01)throw new FormatException("Unit actor rotation required.");
        }
        foreach(var value in d.Descendants().OfType<JValue>())if(value.Type==JTokenType.Float){var n=(double)value;if(double.IsNaN(n)||double.IsInfinity(n))throw new FormatException("Finite save numbers required.");}
    }
}
