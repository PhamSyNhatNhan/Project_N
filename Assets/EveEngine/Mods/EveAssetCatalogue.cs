using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace EveEngine
{
    public sealed class EveBundleDescriptor
    {
        public string Id {get;}
        public string Path {get;}
        public string Hash {get;}
        public string ContentHash {get;private set;}
        public long Bytes {get;}
        public string Target {get;}
        public string UnityVersion {get;}
        public IReadOnlyList<string> Dependencies {get;}
        public IReadOnlyDictionary<string,string> Scenes {get;}
        internal EveBundleDescriptor(string owner,string root,JObject value)
        {
            Id=EveId.Parse(owner+":"+(string)value["key"]).ToString();Path=EveAssetCatalogue.Resolve(root,(string)value["path"]);Hash=(string)value["sha256"];Bytes=(long?)value["bytes"]??0;
            Target=(string)value["target"];UnityVersion=(string)value["unity"];
            if(Hash?.Length!=64||!Hash.All(Uri.IsHexDigit)||Bytes<1||Bytes>2L*1024*1024*1024||string.IsNullOrWhiteSpace(Target)||string.IsNullOrWhiteSpace(UnityVersion))throw new FormatException("Invalid bundle metadata: "+Id);
            if(!(value["dependencies"] is JArray dependencies)||dependencies.Count>64||!(value["scenes"] is JObject scenes)||scenes.Count>64)throw new FormatException("Bounded bundle dependencies/scenes required.");
            Dependencies=Array.AsReadOnly(dependencies.Select(x=>EveId.Parse((string)x).ToString()).ToArray());
            var map=scenes.Properties().ToDictionary(p=>p.Name,p=>(string)p.Value,StringComparer.Ordinal);
            if(map.Any(p=>!EveId.TryParse(owner+":"+p.Key,out _)||string.IsNullOrWhiteSpace(p.Value)||!p.Value.StartsWith("Assets/",StringComparison.Ordinal)||!p.Value.EndsWith(".unity",StringComparison.Ordinal)||p.Value.Contains("..")||p.Value.Contains('\\'))||map.Values.Distinct(StringComparer.Ordinal).Count()!=map.Count)throw new FormatException("Canonical unique scene paths required.");
            Scenes=new ReadOnlyDictionary<string,string>(map);ContentHash=Hash;Verify();
        }
        internal void SealDependencyHash(IEnumerable<EveBundleDescriptor> dependencies)
        {using(var sha=SHA256.Create())ContentHash=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Id+":"+Hash+":"+string.Join(";",dependencies.OrderBy(d=>d.Id,StringComparer.Ordinal).Select(d=>d.Id+":"+d.ContentHash))))).Replace("-","").ToLowerInvariant();}
        public void Verify()
        {
            EveAssetCatalogue.RejectLinks(Path);var info=new FileInfo(Path);if(!info.Exists||info.Length!=Bytes)throw new InvalidDataException("Bundle length mismatch: "+Id);
            using(var stream=File.OpenRead(Path))using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=Hash.ToLowerInvariant())throw new InvalidDataException("Bundle hash mismatch: "+Id);
        }
    }
    public sealed class EveAssetCatalogue
    {
        public IReadOnlyList<EveBundleDescriptor> Bundles {get;}
        public string Hash {get;}
        public string FilePath {get;}
        private EveAssetCatalogue(string root,string owner,string path)
        {
            string file=Resolve(root,path);FilePath=file;if(new FileInfo(file).Length>1024*1024)throw new FormatException("Asset catalogue exceeds bound.");
            var text=File.ReadAllText(file);var json=JObject.Parse(text,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            if((int?)json["schema"]!=1||!(json["bundles"] is JArray bundles)||bundles.Count>128)throw new FormatException("Asset catalogue schema/bound differs.");
            var selected=bundles.Select(x=>new EveBundleDescriptor(owner,root,(JObject)x)).ToArray();
            if(selected.Select(x=>x.Id).Distinct().Count()!=selected.Length||selected.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=selected.Length)throw new FormatException("Duplicate asset bundle identity/path.");
            Bundles=Array.AsReadOnly(selected);using(var sha=SHA256.Create())Hash=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None)))).Replace("-","").ToLowerInvariant();
        }
        public static EveAssetCatalogue Read(string root,string owner,string path)=>path==null?null:new EveAssetCatalogue(root,owner,path);
        public static string Resolve(string root,string relative)
        {
            if(string.IsNullOrWhiteSpace(relative)||System.IO.Path.IsPathRooted(relative)||relative.Contains(':'))throw new FormatException("Relative pack path required.");
            root=System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;
            var result=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,relative));if(!result.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new FormatException("Pack path escapes root.");RejectLinks(result);return result;
        }
        public static void RejectLinks(string path)
        {for(var p=new DirectoryInfo(path);p!=null;p=p.Parent)if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Pack reparse path refused.");if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Pack reparse file refused.");}
        public static void ValidateGraph(IEnumerable<EveContentPack> packs)
        {
            var array=packs.ToArray();var all=array.Where(x=>x.Assets!=null).SelectMany(x=>x.Assets.Bundles).ToDictionary(x=>x.Id,StringComparer.Ordinal);var visiting=new HashSet<string>();var visited=new HashSet<string>();
            void Visit(string id)
            {
                if(visited.Contains(id))return;if(!all.TryGetValue(id,out var item)||!visiting.Add(id))throw new FormatException("Bundle dependency missing/cyclic: "+id);
                var owner=array.Single(x=>x.Namespace==EveId.Parse(id).Namespace);
                foreach(var dependency in item.Dependencies){var ns=EveId.Parse(dependency).Namespace;if(ns!=owner.Namespace&&!owner.Dependencies.ContainsKey(ns))throw new FormatException("Bundle foreign dependency is undeclared: "+dependency);Visit(dependency);}
                item.SealDependencyHash(item.Dependencies.Select(d=>all[d]));visiting.Remove(id);visited.Add(id);
            }
            foreach(var id in all.Keys)Visit(id);
        }
    }
}
