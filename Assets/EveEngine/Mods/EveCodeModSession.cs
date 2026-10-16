using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EveEngine
{
    // EN: Modules instantiate lazily during bootstrap so failed registration remains owned for cleanup.
    // VI: Module tạo instance khi bootstrap để đăng ký lỗi vẫn có owner dọn tài nguyên.
    public sealed class EveCodeModSession : IDisposable
    {
        private sealed class Module : EveIContentModule
        {
            internal EveContentPack Pack;
            internal Type Type;
            internal EveICodeMod Instance;
            internal bool Started, Ended, Disposed;
            public void Register(EveRegistryHub hub)
            {
                if (Instance != null) throw new InvalidOperationException($"Mod {Pack.Namespace} cannot register twice.");
                try
                {
                    Instance = (EveICodeMod)Activator.CreateInstance(Type);
                    Instance.Register(new EveModRegistrationContext(hub, Pack));
                }
                catch (Exception error) { throw new InvalidOperationException($"Mod {Pack.Namespace} registration failed.", error); }
            }
        }
        private readonly Module[] modules;
        private EveSessionContext session;
        private bool isDisposed;
        private bool invoking;
        internal bool HasActivePlay => session != null;
        internal int PendingPlayCleanupCount => modules.Count(module => module.Started && !module.Ended);
        public IEnumerable<EveIContentModule> ContentModules => modules.ToArray();
        public IReadOnlyDictionary<string, string> Fingerprints { get; }
        public int PendingCleanupCount => modules.Count(module => module.Instance != null && !module.Disposed);

        internal EveCodeModSession(IEnumerable<(EveContentPack pack, Type type)> entries, IDictionary<string, string> fingerprints)
        {
            modules = entries.Select(entry => new Module { Pack = entry.pack, Type = entry.type }).ToArray();
            Fingerprints = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(fingerprints, StringComparer.Ordinal));
        }

        public static EveCodeModSession Empty() => new EveCodeModSession(Array.Empty<(EveContentPack, Type)>(), new Dictionary<string, string>());

        internal void Start(EveSessionContext context)
        {
            if (isDisposed || invoking || session != null || context == null) throw new InvalidOperationException("Invalid mod session start.");
            session = context; invoking = true;
            try
            {
                foreach (var module in modules)
                {
                    module.Started = false; module.Ended = false;
                }
                foreach (var module in modules)
                {
                    if (module.Instance == null || module.Disposed) throw new InvalidOperationException($"Mod {module.Pack.Namespace} was not registered.");
                    module.Started = true;
                    try { module.Instance.OnSessionStarted(context); }
                    catch (Exception error) { throw new InvalidOperationException($"Mod {module.Pack.Namespace} session start failed.", error); }
                }
            }
            finally { invoking = false; }
        }

        // End a play without disposing the instances captured by registered definitions/factories.
        internal void EndPlay()
        {
            if (invoking) throw new InvalidOperationException("A mod lifecycle callback is running.");
            if (session == null) return;
            if (session.IsActive) throw new InvalidOperationException("Invalidate the play session before ending mods.");
            invoking = true;
            var errors = new List<Exception>();
            try
            {
                foreach (var module in modules.Reverse())
                {
                    if (!module.Started || module.Ended) continue;
                    try { module.Instance.OnSessionEnded(session); module.Ended = true; }
                    catch (Exception error) { errors.Add(new InvalidOperationException($"Mod {module.Pack.Namespace} session cleanup failed.", error)); }
                }
                if (errors.Count != 0) throw new AggregateException("Mod play cleanup is incomplete; retry before starting a new session.", errors);
                session = null;
            }
            finally { invoking = false; }
        }

        // EN: Retain failed cleanup for retry; successful hooks/disposals are never repeated.
        // VI: Giữ cleanup lỗi để thử lại; hook/dispose thành công không chạy lại.
        public void Dispose()
        {
            if (isDisposed) return;
            EndPlay();
            if (invoking) throw new InvalidOperationException("A mod lifecycle callback is running.");
            invoking = true;
            var errors = new List<Exception>();
            try
            {
                foreach (var module in modules.Reverse())
                {
                    if (module.Instance == null || module.Disposed) continue;
                    try
                    {
                        module.Instance.Dispose();
                        module.Disposed = true;
                    }
                    catch (Exception error) { errors.Add(new InvalidOperationException($"Mod {module.Pack.Namespace} cleanup failed.", error)); }
                }
                if (errors.Count != 0) throw new AggregateException("Mod cleanup is incomplete; retry before starting a new session.", errors);
                isDisposed = true;
            }
            finally { invoking = false; }
        }
    }
}
