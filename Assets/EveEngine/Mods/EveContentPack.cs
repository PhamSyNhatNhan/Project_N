using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EveEngine
{
    public sealed class EveModCode
    {
        public string ApiVersion { get; }
        public string Assembly { get; }
        public string EntryPoint { get; }
        public IReadOnlyList<string> Libraries { get; }
        public EveModCode(string apiVersion, string assembly, string entryPoint, IEnumerable<string> libraries = null)
        {
            if (apiVersion != EveModApi.Version) throw new NotSupportedException($"Mod API {apiVersion} is unsupported; expected {EveModApi.Version}.");
            if (string.IsNullOrWhiteSpace(assembly) || string.IsNullOrWhiteSpace(entryPoint))
                throw new ArgumentException("Code assembly and entry point are required.");
            ApiVersion = apiVersion; Assembly = assembly; EntryPoint = entryPoint;
            Libraries = Array.AsReadOnly((libraries ?? Array.Empty<string>()).ToArray());
        }
    }

    // EN: Immutable validated pack metadata is shared by JSON loading and the code-mod loader.
    // VI: Metadata pack đã kiểm tra, bất biến được dùng chung bởi loader JSON và mod code.
    public sealed class EveContentPack
    {
        public string Directory { get; }
        public string Namespace { get; }
        public string Version { get; }
        public IReadOnlyDictionary<string, string> Dependencies { get; }
        public EveModCode Code { get; }
        public bool IsBase { get; }
        public EveAssetCatalogue Assets {get;}
        internal string ManifestJson { get; }

        internal EveContentPack(string directory, string nameSpace, string version,
            IDictionary<string, string> dependencies, EveModCode code, bool isBase, string manifestJson,string assets=null)
        {
            Directory = System.IO.Path.GetFullPath(directory); Namespace = nameSpace; Version = version;
            Dependencies = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(dependencies ?? new Dictionary<string, string>(), StringComparer.Ordinal));
            Code = code; IsBase = isBase; ManifestJson = manifestJson;
            Assets=EveAssetCatalogue.Read(Directory,Namespace,assets);
        }
    }
}
