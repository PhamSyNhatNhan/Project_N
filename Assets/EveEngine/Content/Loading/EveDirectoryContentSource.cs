using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace EveEngine
{
    // EN: Reads a base pack and selected JSON mods from ordinary filesystem directories.
    // VI: Đọc gói gốc và các mod JSON được chọn từ thư mục trên hệ thống tệp.
    public class EveDirectoryContentSource
    {
        // EN: User-editable mod selection file stored outside the game build.
        // VI: File chọn mod bên ngoài bản build, người chơi có thể chỉnh sửa.
        private sealed class EveModSelection
        {
            public int schemaVersion { get; set; }
            public string[] enabledMods { get; set; }
        }

        // EN: Manifest declares the namespace, version and exact dependency versions of a pack.
        // VI: Manifest khai báo namespace, phiên bản và phiên bản phụ thuộc chính xác của gói.
        private sealed class EvePackManifest
        {
            public int schemaVersion { get; set; }
            [JsonProperty("namespace")] public string Namespace { get; set; }
            public string version { get; set; }
            public Dictionary<string, string> dependencies { get; set; }
            public EveCodeDto code { get; set; }
            public string assets {get;set;}
        }

        private sealed class EveCodeDto
        {
            public string apiVersion { get; set; }
            public string assembly { get; set; }
            public string entryPoint { get; set; }
            public string[] libraries { get; set; }
        }

        // EN: Associates validated pack metadata with its directory.
        // VI: Gắn thông tin gói đã kiểm tra với thư mục của gói.
        private sealed class EvePack
        {
            public string Directory;
            public EvePackManifest Manifest;
        }

        private readonly string baseDirectory;
        private readonly string modsDirectory;
        private readonly string[] enabledMods;

        public EveDirectoryContentSource(string baseDirectory, string modsDirectory, IEnumerable<string> enabledMods)
        {
            this.baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
            this.modsDirectory = modsDirectory ?? throw new ArgumentNullException(nameof(modsDirectory));
            this.enabledMods = (enabledMods ?? throw new ArgumentNullException(nameof(enabledMods))).ToArray();
        }

        public static string[] ReadEnabledMods(string selectionFile)
        {
            // EN: Dropping a manifest pack into Mods is enough when no selection exists.
            // VI: Chỉ cần chép gói có manifest vào Mods khi chưa có file chọn mod.
            if (!File.Exists(selectionFile))
            {
                string directory = Path.GetDirectoryName(selectionFile);
                if (!Directory.Exists(directory)) return Array.Empty<string>();
                return Directory.EnumerateDirectories(directory)
                    .Where(folder => File.Exists(Path.Combine(folder, "manifest.json")))
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
            }
            try
            {
                var selection = JsonConvert.DeserializeObject<EveModSelection>(File.ReadAllText(selectionFile));
                if (selection == null || selection.schemaVersion != 1 || selection.enabledMods == null)
                    throw new FormatException("Expected schemaVersion 1 and an enabledMods array.");
                return selection.enabledMods;
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException)
            {
                throw new FormatException($"Invalid mod selection '{selectionFile}': {ex.Message}", ex);
            }
        }

        public virtual IReadOnlyList<EveContentPack> ReadPacks()
        {
            var packs = new Dictionary<string, EvePack>(StringComparer.Ordinal);
            AddPack(packs, baseDirectory);
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in enabledMods.OrderBy(value => value, StringComparer.Ordinal))
            {
                ValidateName(folder);
                if (folder == "." || folder == ".." || !folders.Add(folder))
                    throw new FormatException($"Invalid or duplicate mod folder '{folder}'.");
                AddPack(packs, Path.Combine(modsDirectory, folder));
            }

            var ordered = new List<EvePack>();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in packs.Keys.OrderBy(value => value, StringComparer.Ordinal))
                Visit(packs[id], packs, visiting, visited, ordered);

            var result=ordered.Select(pack => new EveContentPack(pack.Directory, pack.Manifest.Namespace,
                pack.Manifest.version, pack.Manifest.dependencies,
                pack.Manifest.code == null ? null : new EveModCode(pack.Manifest.code.apiVersion,
                    pack.Manifest.code.assembly, pack.Manifest.code.entryPoint, pack.Manifest.code.libraries),
                string.Equals(Path.GetFullPath(pack.Directory), Path.GetFullPath(baseDirectory), StringComparison.OrdinalIgnoreCase),
                JsonConvert.SerializeObject(pack.Manifest),pack.Manifest.assets)).ToArray();
            EveAssetCatalogue.ValidateGraph(result);return result;
        }

        public virtual IEnumerable<EveContentDocument> ReadDocuments() => ReadDocuments(ReadPacks());

        public IEnumerable<EveContentDocument> ReadDocuments(IEnumerable<EveContentPack> packs)
        {
            // EN: Stable ordering produces the same registration order from the same files.
            // VI: Thứ tự ổn định cho cùng thứ tự đăng ký khi dùng cùng các file.
            foreach (EveContentPack pack in packs)
            {
                yield return new EveContentDocument(pack.Namespace + "/manifest.json",
                    pack.ManifestJson, pack.Namespace, true);
                foreach (string file in Directory.EnumerateFiles(pack.Directory, "*.json", SearchOption.AllDirectories)
                    .OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (string.Equals(file, Path.Combine(pack.Directory, "manifest.json"), StringComparison.Ordinal)) continue;
                    if(pack.Assets!=null&&string.Equals(Path.GetFullPath(file),pack.Assets.FilePath,StringComparison.OrdinalIgnoreCase))continue;
                    yield return new EveContentDocument(file, File.ReadAllText(file), pack.Namespace,
                        dependencies: pack.Dependencies.Keys);
                }
            }
        }

        private static void AddPack(Dictionary<string, EvePack> packs, string directory)
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Content directory not found: {directory}");
            string manifestPath = Path.Combine(directory, "manifest.json");
            EvePackManifest manifest;
            try
            {
                manifest = JsonConvert.DeserializeObject<EvePackManifest>(File.ReadAllText(manifestPath),
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
                if (manifest == null || manifest.schemaVersion != 1) throw new FormatException("Expected schemaVersion 1.");
                ValidateName(manifest.Namespace);
                if (manifest.Namespace == "base") throw new FormatException("base is reserved for game-owned public concepts.");
                if (!Version.TryParse(manifest.version, out _)) throw new FormatException("Expected a numeric pack version, for example 1.0.0.");
                foreach (var dependency in manifest.dependencies ?? new Dictionary<string, string>())
                {
                    ValidateName(dependency.Key);
                    if (!Version.TryParse(dependency.Value, out _)) throw new FormatException($"Invalid dependency version for '{dependency.Key}'.");
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException)
            {
                throw new FormatException($"Invalid manifest '{manifestPath}': {ex.Message}", ex);
            }
            if (packs.ContainsKey(manifest.Namespace)) throw new FormatException($"Duplicate pack namespace '{manifest.Namespace}'.");
            packs.Add(manifest.Namespace, new EvePack { Directory = directory, Manifest = manifest });
        }

        private static void Visit(EvePack pack, Dictionary<string, EvePack> packs, HashSet<string> visiting,
            HashSet<string> visited, List<EvePack> ordered)
        {
            string id = pack.Manifest.Namespace;
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new FormatException($"Cyclic pack dependency involving '{id}'.");
            foreach (var dependency in (pack.Manifest.dependencies ?? new Dictionary<string, string>())
                .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!packs.TryGetValue(dependency.Key, out EvePack required))
                    throw new FormatException($"Pack '{id}' requires enabled pack '{dependency.Key}'.");
                if (!string.Equals(required.Manifest.version, dependency.Value, StringComparison.Ordinal))
                    throw new FormatException($"Pack '{id}' requires '{dependency.Key}' version {dependency.Value}, found {required.Manifest.version}.");
                Visit(required, packs, visiting, visited, ordered);
            }
            visiting.Remove(id);
            visited.Add(id);
            ordered.Add(pack);
        }

        private static void ValidateName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > EveId.MaxNamespaceLength || value == "." || value == ".." ||
                value.Any(c => !((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')))
                throw new FormatException($"Invalid pack namespace or folder '{value}'. Use lowercase letters, digits, _, - or .");
        }
    }
}
