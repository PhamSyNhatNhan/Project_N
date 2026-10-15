using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace EveEngine
{
    // Schema-neutral extension batches validate without mutation, then commit alongside built-in content.
    public interface EveIContentExtension
    {
        string ExtensionKey { get; }
        EveIContentExtensionBatch PrepareExtensions(IReadOnlyList<EveContentExtensionDocument> documents, EveRegistryHub hub, IReadOnlyList<EveIIdentified> pendingDefinitions);
    }
    public interface EveIContentExtensionBatch
    {
        IEnumerable<EveIIdentified> Definitions { get; }
        void Validate();
        void Commit();
    }
    public sealed class EveContentExtensionDocument
    {
        private readonly JObject extensions;
        public string Name { get; }
        public string ContentNamespace { get; }
        public IReadOnlyCollection<string> Dependencies { get; }
        public JObject Extensions => (JObject)extensions.DeepClone();
        public EveContentExtensionDocument(string name, string contentNamespace, IReadOnlyCollection<string> dependencies, JObject extensions)
        { Name = name; ContentNamespace = contentNamespace; Dependencies = dependencies; this.extensions = (JObject)extensions.DeepClone(); }
    }
}
