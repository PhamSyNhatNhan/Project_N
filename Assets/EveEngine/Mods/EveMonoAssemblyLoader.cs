using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace EveEngine
{
    // EN: Runtime-specific loading is isolated for the planned CoreCLR migration.
    // VI: Nạp theo runtime được tách riêng để chuyển sang CoreCLR sau này.
    public interface EveIModAssemblyLoader
    {
        bool IsSupported { get; }
        Assembly Load(string path, byte[] image);
    }

    public sealed class EveMonoAssemblyLoader : EveIModAssemblyLoader
    {
        public bool IsSupported
        {
            get
            {
#if ENABLE_IL2CPP
                return false;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return true;
#elif !UNITY_5_3_OR_NEWER
                return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
                return false;
#endif
            }
        }
        public Assembly Load(string path, byte[] image) => Assembly.Load(image);
    }

    // EN: Preflight all files before executing code; conflicting shared names fail rather than load-order bind.
    // VI: Kiểm tra mọi file trước chạy code; tên thư viện xung đột gây lỗi thay vì chọn theo thứ tự nạp.
    public sealed class EveCodeModLoader
    {
        private sealed class Image
        {
            internal string Path, Hash;
            internal AssemblyName Name;
            internal byte[] Bytes;
        }
        private sealed class Loaded
        {
            internal string Hash;
            internal Assembly Assembly;
        }
        private sealed class State
        {
            internal readonly Dictionary<string, Loaded> Loaded = new Dictionary<string, Loaded>(StringComparer.OrdinalIgnoreCase);
            internal string Plan;
            internal bool ResolverInstalled;
        }
        private static readonly State shared = new State();
        private readonly State state;
        private Dictionary<string, Loaded> loaded => state.Loaded;
        private string processPlan { get => state.Plan; set => state.Plan = value; }
        private bool resolverInstalled { get => state.ResolverInstalled; set => state.ResolverInstalled = value; }
        private readonly EveIModAssemblyLoader loader;
        private readonly string gameNamespace;
        public EveCodeModLoader(string gameNamespace, EveIModAssemblyLoader loader = null) : this(gameNamespace, loader, false) { }
        internal EveCodeModLoader(string gameNamespace, EveIModAssemblyLoader loader, bool isolated)
        {
            if (string.IsNullOrWhiteSpace(gameNamespace)) throw new ArgumentException("Game namespace is required.");
            this.gameNamespace = gameNamespace; this.loader = loader ?? new EveMonoAssemblyLoader();
            state = isolated ? new State() : shared;
        }

        public EveCodeModSession Load(IEnumerable<EveContentPack> packs)
        {
            if (packs == null) throw new ArgumentNullException(nameof(packs));
            var codePacks = packs.Where(pack => pack.Code != null).ToArray();
            if (codePacks.Length == 0)
            {
                if (processPlan != null) throw new InvalidOperationException("The active DLL mod set changed; restart the game.");
                return EveCodeModSession.Empty();
            }
            if (!loader.IsSupported) throw new NotSupportedException("DLL mods are supported only on Windows with the managed mod runtime (not IL2CPP).");
            var images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            var mainImages = new Dictionary<EveContentPack, Image>();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            var owners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pack in codePacks)
            {
                if (pack.IsBase) throw new FormatException("Base game code is built into the player, not loaded as a mod.");
                if (pack.Code.ApiVersion != EveModApi.Version || !pack.Dependencies.ContainsKey(gameNamespace))
                    throw new FormatException($"Code mod {pack.Namespace} requires API {EveModApi.Version} and a {gameNamespace} dependency.");
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var relative in new[] { pack.Code.Assembly }.Concat(pack.Code.Libraries))
                {
                    string path = ResolvePath(pack.Directory, relative);
                    if (!paths.Add(path)) throw new FormatException($"Mod {pack.Namespace} lists DLL {relative} twice.");
                    byte[] bytes = File.ReadAllBytes(path);
                    var image = new Image { Path = path, Bytes = bytes, Hash = Hash(bytes), Name = AssemblyName.GetAssemblyName(path) };
                    if (Hash(File.ReadAllBytes(path)) != image.Hash) throw new IOException($"DLL changed during preflight: {path}.");
                    string name = image.Name.Name;
                    if (images.TryGetValue(name, out var existing) && (existing.Hash != image.Hash || !Matches(existing.Name, image.Name)))
                        throw new InvalidOperationException($"Conflicting DLL name {name}; rename/use one compatible library. Restart after changing DLLs.");
                    images[name] = image;
                    if (!owners.TryGetValue(name, out var names)) owners.Add(name, names = new HashSet<string>(StringComparer.Ordinal));
                    names.Add(pack.Namespace);
                    if (relative == pack.Code.Assembly) mainImages.Add(pack, image);
                    hashes.Add($"dll:{pack.Namespace}/{relative.Replace('\\', '/')}", image.Hash);
                }
            }
            // EN: Never accept bundled copies of host/API assemblies, even when byte-identical.
            // VI: Không nhận bản sao assembly host/API đóng kèm, kể cả byte giống hệt.
            foreach (var image in images.Values)
            {
                if (loaded.TryGetValue(image.Name.Name, out var previous))
                {
                    if (previous.Hash != image.Hash || !Matches(previous.Assembly.GetName(), image.Name))
                        throw new InvalidOperationException($"Loaded DLL {image.Name.Name} changed; restart the game.");
                }
                else if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => string.Equals(assembly.GetName().Name, image.Name.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Mod DLL {image.Name.Name} duplicates a host/API assembly. Reference the host copy instead.");
            }
            string plan = Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", codePacks.Select(pack =>
                pack.Namespace + "|" + pack.Version + "|" + pack.Code.EntryPoint + "|" + pack.Code.ApiVersion).OrderBy(value => value, StringComparer.Ordinal)) + "\n" +
                string.Join("\n", hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value))));
            if (processPlan != null && processPlan != plan) throw new InvalidOperationException("The active DLL mod set changed; restart the game.");
            processPlan = plan;
            var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Func<Image, Assembly> load = null;
            load = image =>
            {
                if (loaded.TryGetValue(image.Name.Name, out var cached)) return cached.Assembly;
                if (!resolving.Add(image.Name.Name)) throw new InvalidOperationException($"Recursive dependency while loading {image.Name.Name}.");
                try
                {
                    Assembly assembly = loader.Load(image.Path, image.Bytes);
                    if (!Matches(assembly.GetName(), image.Name)) throw new InvalidOperationException($"Loaded assembly identity differs from preflight: {image.Path}.");
                    loaded.Add(image.Name.Name, new Loaded { Hash = image.Hash, Assembly = assembly });
                    return assembly;
                }
                finally { resolving.Remove(image.Name.Name); }
            };
            ResolveEventHandler resolver = (sender, args) =>
            {
                var requested = new AssemblyName(args.Name);
                if (!images.TryGetValue(requested.Name, out var image)) return null;
                if (!Matches(requested, image.Name)) throw new InvalidOperationException($"DLL dependency {args.Name} does not match {image.Name.FullName}.");
                return load(image);
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            try
            {
                foreach (var image in images.Values.OrderBy(value => value.Name.Name, StringComparer.OrdinalIgnoreCase)) load(image);
                foreach (var image in images.Values)
                    foreach (var reference in loaded[image.Name.Name].Assembly.GetReferencedAssemblies())
                    {
                        if (images.TryGetValue(reference.Name, out var dependency))
                        {
                            if (!Matches(reference, dependency.Name)) throw new InvalidOperationException($"Incompatible reference {reference.FullName} in {image.Name.Name}.");
                            foreach (var owner in owners[image.Name.Name])
                            {
                                var pack = codePacks.Single(value => value.Namespace == owner);
                                if (!owners[reference.Name].Contains(owner) && !owners[reference.Name].Any(provider => pack.Dependencies.ContainsKey(provider)))
                                    throw new InvalidOperationException($"Mod {owner} must declare a dependency on the provider of {reference.Name}.");
                            }
                        }
                        else
                        {
                            var host = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, reference.Name, StringComparison.OrdinalIgnoreCase));
                            if (host == null) host = Assembly.Load(reference);
                            if (!Matches(reference, host.GetName())) throw new InvalidOperationException($"Host/API reference {reference.FullName} does not match {host.FullName}.");
                        }
                    }
                var entries = new List<(EveContentPack, Type)>();
                foreach (var pack in codePacks)
                {
                    var assembly = loaded[mainImages[pack].Name.Name].Assembly;
                    Type type = assembly.GetType(pack.Code.EntryPoint, true, false);
                    if (!type.IsPublic || type.IsAbstract || type.ContainsGenericParameters || !typeof(EveICodeMod).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                        throw new InvalidOperationException($"Mod {pack.Namespace} entry point {pack.Code.EntryPoint} must be a public concrete EveICodeMod with a public parameterless constructor.");
                    entries.Add((pack, type));
                }
                if (!resolverInstalled)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += ResolveLoaded;
                    resolverInstalled = true;
                }
                return new EveCodeModSession(entries, hashes);
            }
            catch (Exception error) { throw new InvalidOperationException("DLL mod loading failed; restart after correcting the DLL/manifest. " + error.Message, error); }
            finally { AppDomain.CurrentDomain.AssemblyResolve -= resolver; }
        }

        private static bool Matches(AssemblyName expected, AssemblyName actual)
            => string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase) &&
                (expected.Version == null || expected.Version.Equals(actual.Version)) &&
                string.Equals(expected.CultureName ?? "", actual.CultureName ?? "", StringComparison.OrdinalIgnoreCase) &&
                (expected.GetPublicKeyToken() ?? Array.Empty<byte>()).SequenceEqual(actual.GetPublicKeyToken() ?? Array.Empty<byte>());

        private Assembly ResolveLoaded(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            if (!loaded.TryGetValue(requested.Name, out var value)) return null;
            if (!Matches(requested, value.Assembly.GetName())) throw new InvalidOperationException($"Loaded mod dependency differs from {args.Name}; restart the game.");
            return value.Assembly;
        }

        private static string ResolvePath(string directory, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || !relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                relative.Split('/', '\\').Any(part => part == ".." || part == "." || part.Length == 0))
                throw new FormatException($"Invalid mod DLL path {relative}.");
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new FormatException("DLL path leaves mod directory.");
            for (string current = path; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new FormatException("Mod DLL paths cannot use links/junctions.");
                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
            }
            return path;
        }
        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
