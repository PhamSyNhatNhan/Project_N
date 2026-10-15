namespace EveEngine
{
    // EN: Signals readiness without carrying or owning session data.
    // VI: Thông báo sẵn sàng, không mang hoặc sở hữu dữ liệu của phiên.
    public readonly struct EveRuntimeReady { }
    public readonly struct EveEntityReady
    {
        public EveEntityLoader Loader { get; }
        public EveEntityLoadContext Context { get; }
        public EveEntityReady(EveEntityLoader loader, EveEntityLoadContext context) { Loader = loader; Context = context; }
    }

    // EN: Engine-owned local events for the runtime lifecycle.
    // VI: Event nội bộ của engine cho vòng đời runtime.
    public static class EveRuntimeEventIds
    {
        public static readonly EveId Ready = EveId.Parse("eve_engine:runtime_ready");
        public static readonly EveId EntityReady = EveId.Parse("eve_engine:entity_ready");
    }
}
