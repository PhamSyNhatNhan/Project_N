using System;
using System.Collections.Generic;

namespace EveEngine
{
    public interface EveIRegistrationReferences
    {void ValidateRegistrationReferences(string owner,System.Collections.Generic.IReadOnlyCollection<string> dependencies);}
    public static class EveModApi
    {
        public const string Version = "6";
    }

    // EN: Constructors must be resource-free; registration/session cleanup must tolerate partial failure.
    // VI: Constructor không giữ tài nguyên; dọn đăng ký/phiên phải xử lý được lỗi giữa chừng.
    public interface EveICodeMod : IDisposable
    {
        // Once per content generation. Instances remain alive while their registered definitions are used.
        void Register(EveModRegistrationContext context);
        // May repeat on the same instance for successive plays; context and play-owned state must not leak.
        void OnSessionStarted(EveSessionContext context);
        void OnSessionEnded(EveSessionContext context);
    }

    public abstract class EveCodeMod : EveICodeMod
    {
        public abstract void Register(EveModRegistrationContext context);
        public virtual void OnSessionStarted(EveSessionContext context) { }
        public virtual void OnSessionEnded(EveSessionContext context) { }
        public virtual void Dispose() { }
    }

    // EN: Supported registration helpers bind provider identity/version/dependencies to its manifest.
    // VI: Helper đăng ký gắn danh tính/phiên bản/phụ thuộc provider với manifest của nó.
    public sealed class EveModRegistrationContext
    {
        public EveRegistryHub Registry { get; }
        public string Namespace { get; }
        public string Version { get; }
        public IReadOnlyDictionary<string, string> Dependencies { get; }

        internal EveModRegistrationContext(EveRegistryHub registry, EveContentPack pack)
        {
            Registry = registry; Namespace = pack.Namespace; Version = pack.Version; Dependencies = pack.Dependencies;
        }

        public void Register<T>(T definition) where T : class, EveIIdentified
        {
            if (definition == null || definition.Id.Namespace != Namespace)
                throw new ArgumentException($"Mod definitions must belong to {Namespace}.");
            (definition as EveIRegistrationReferences)?.ValidateRegistrationReferences(Namespace,new System.Collections.Generic.List<string>(Dependencies.Keys));
            Registry.GetOrCreate<T>().Register(definition);
        }

        public void Override<T>(EveId concept, EveId implementation) where T : class, EveIIdentified
            => Registry.Get<T>().RequestOverride(concept, implementation, Namespace, Version, Dependencies.Keys);
    }
}

