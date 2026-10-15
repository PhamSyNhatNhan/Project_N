using System;
using System.Collections.Generic;
using System.Linq;

namespace EveEngine
{
    // EN: Named JSON document supplied to the content loader by the game.
    // VI: Tài liệu JSON có tên do game cung cấp cho bộ nạp nội dung.
    public sealed class EveContentDocument
    {
        public string Name { get; }
        public string Json { get; }
        public string ContentNamespace { get; }
        public bool IsMetadata { get; }
        // Null means the caller did not supply pack dependency metadata.
        public IReadOnlyCollection<string> Dependencies { get; }

        public EveContentDocument(string name, string json, string contentNamespace = null, bool isMetadata = false,
            IEnumerable<string> dependencies = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Document name is required.", nameof(name)) : name;
            Json = string.IsNullOrWhiteSpace(json) ? throw new ArgumentException("Document JSON is required.", nameof(json)) : json;
            ContentNamespace = contentNamespace;
            IsMetadata = isMetadata;
            Dependencies = dependencies?.ToArray();
        }
    }
}
