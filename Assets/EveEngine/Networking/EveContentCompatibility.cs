using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EveEngine
{
    // EN: Compares protocol, game rules and canonical content before admitting a peer.
    // VI: So sánh giao thức, luật game và nội dung chuẩn hóa trước khi nhận peer.
    public sealed class EveContentCompatibility
    {
        public const int CurrentProtocol = 1;
        public int ProtocolVersion { get; }
        public string RulesVersion { get; }
        public IReadOnlyDictionary<string, string> ContentHashes { get; }

        public EveContentCompatibility(int protocolVersion, string rulesVersion, IDictionary<string, string> contentHashes)
        {
            if (string.IsNullOrWhiteSpace(rulesVersion)) throw new ArgumentException("Rules version required.");
            ProtocolVersion = protocolVersion;
            RulesVersion = rulesVersion;
            ContentHashes = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(contentHashes ?? throw new ArgumentNullException(nameof(contentHashes)), StringComparer.Ordinal));
        }

        public static EveContentCompatibility Create(EveRegistryHub registry, IEnumerable<EveContentDocument> documents, string rulesVersion,
            IReadOnlyDictionary<string, string> codeHashes = null, IReadOnlyDictionary<string, string> assetHashes = null)
        {
            if (registry == null || !registry.IsSealed) throw new ArgumentException("Sealed registry required.");
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in codeHashes ?? new Dictionary<string, string>())
                hashes.Add("code:" + pair.Key, pair.Value);
            foreach (var pair in assetHashes ?? new Dictionary<string, string>())
                hashes.Add("asset:" + pair.Key, pair.Value);
            foreach (var group in documents.GroupBy(doc => doc.ContentNamespace ?? "unscoped"))
            {
                // EN: Ignore machine paths and whitespace; retain array order and every manifest field.
                // VI: Bỏ đường dẫn máy và khoảng trắng; giữ thứ tự mảng và mọi trường manifest.
                var values = group.Select(doc => Canonical(JToken.Parse(doc.Json)).ToString(Formatting.None))
                    .OrderBy(value => value, StringComparer.Ordinal);
                hashes.Add("pack:" + group.Key, Hash(new JArray(values).ToString(Formatting.None)));
            }
            foreach (var pair in registry.Snapshot())
            {
                var definitions = pair.Value.Definitions.OrderBy(value => value.Id.ToString(), StringComparer.Ordinal)
                    .Select(value => Canonical(JToken.FromObject(value)));
                hashes.Add("registry:" + pair.Key.FullName, Hash(new JArray(definitions).ToString(Formatting.None)));
                if (pair.Value is EveIBindingRegistry bindings)
                    hashes.Add("bindings:" + pair.Key.FullName, Hash(Canonical(JToken.FromObject(bindings.BindingSnapshot())).ToString(Formatting.None)));
            }
            return new EveContentCompatibility(CurrentProtocol, rulesVersion, hashes);
        }

        public void Validate(EveContentCompatibility remote)
        {
            if (remote == null) throw new ArgumentNullException(nameof(remote));
            if (ProtocolVersion != remote.ProtocolVersion) throw new InvalidOperationException("Protocol version mismatch.");
            if (!string.Equals(RulesVersion, remote.RulesVersion, StringComparison.Ordinal))
                throw new InvalidOperationException("Game rules version mismatch.");
            foreach (string key in ContentHashes.Keys.Union(remote.ContentHashes.Keys).OrderBy(value => value, StringComparer.Ordinal))
                if (!ContentHashes.TryGetValue(key, out string local) || !remote.ContentHashes.TryGetValue(key, out string other) || local != other)
                    throw new InvalidOperationException($"Content mismatch: {key}.");
        }

        private static JToken Canonical(JToken token)
        {
            if (token is JObject obj) return new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new JProperty(p.Name, Canonical(p.Value))));
            if (token is JArray array) return new JArray(array.Select(Canonical));
            return token.DeepClone();
        }
        private static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }
}
