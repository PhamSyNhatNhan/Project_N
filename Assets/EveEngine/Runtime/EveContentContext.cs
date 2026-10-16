using System;

namespace EveEngine
{
    // A loaded content generation outlives individual play sessions. It never owns entities or a Role.
    public sealed class EveContentContext : IDisposable
    {
        public Guid GenerationId { get; } = Guid.NewGuid();
        public EveRegistryHub Registry { get; private set; }
        public EveContentCompatibility Compatibility { get; private set; }
        public EveSessionState State { get; private set; } = EveSessionState.Starting;
        public bool IsReady => State == EveSessionState.Ready;
        internal EveCodeModSession Mods { get; }
        internal bool UnloadRequested { get; private set; }

        internal EveContentContext(EveCodeModSession mods)
        { Mods = mods ?? throw new ArgumentNullException(nameof(mods)); }

        internal void Publish(EveRegistryHub registry, EveContentCompatibility compatibility)
        {
            if (State != EveSessionState.Starting || registry == null || !registry.IsSealed || compatibility == null)
                throw new InvalidOperationException("A complete sealed content generation is required.");
            Registry = registry; Compatibility = compatibility; State = EveSessionState.Ready;
        }

        internal void RequestUnload() { UnloadRequested = true; State = EveSessionState.Stopping; }

        public void Dispose()
        {
            if (State == EveSessionState.Stopped) return;
            if (Mods.HasActivePlay) throw new InvalidOperationException("End the play session before unloading content.");
            RequestUnload();
            Mods.Dispose(); // Retain registry and instances if disposal fails; the owner can retry.
            Registry = null; Compatibility = null; State = EveSessionState.Stopped;
        }
    }
}
