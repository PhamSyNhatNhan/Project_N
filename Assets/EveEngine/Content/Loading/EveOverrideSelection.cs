using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EveEngine
{
    // EN: Explicit user choices are applied before sealing and never hot-swap a live session.
    // VI: Lựa chọn người dùng áp trước khi khóa, không đổi nóng phiên đang chạy.
    public sealed class EveOverrideSelection
    {
        public string Registry { get; }
        public EveId Id { get; }
        public string Provider { get; }

        public EveOverrideSelection(string registry, EveId id, string provider)
        {
            if (string.IsNullOrWhiteSpace(registry) || !id.IsValid || id.Namespace != "base" || string.IsNullOrWhiteSpace(provider))
                throw new ArgumentException("Registry, base concept and provider are required.");
            Registry = registry; Id = id; Provider = provider;
        }

        public static IReadOnlyList<EveOverrideSelection> Read(string path)
        {
            if (!File.Exists(path)) return Array.AsReadOnly(Array.Empty<EveOverrideSelection>());
            using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))))
            {
                var root = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }) as JObject;
                if (root == null || reader.Read() || root["schemaVersion"]?.Type != JTokenType.Integer ||
                    root["schemaVersion"].Value<int>() != 1 || !(root["selections"] is JArray selections))
                    throw new FormatException("Override selection requires schemaVersion 1 and selections array.");
                CheckFields(root, "schemaVersion", "selections");
                var values = new List<EveOverrideSelection>();
                foreach (var token in selections)
                {
                    if (!(token is JObject entry)) throw new FormatException("Override selection must be an object.");
                    CheckFields(entry, "registry", "id", "provider");
                    foreach (var name in new[] { "registry", "id", "provider" })
                        if (entry[name]?.Type != JTokenType.String) throw new FormatException($"Selection {name} must be a string.");
                    values.Add(new EveOverrideSelection(entry["registry"].Value<string>(), EveId.Parse(entry["id"].Value<string>()), entry["provider"].Value<string>()));
                }
                return values.AsReadOnly();
            }
        }

        private static void CheckFields(JObject obj, params string[] fields)
        {
            foreach (var property in obj.Properties())
                if (Array.IndexOf(fields, property.Name) < 0) throw new FormatException($"Unknown selection field {property.Name}.");
        }
    }
}
