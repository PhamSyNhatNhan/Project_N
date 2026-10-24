using System;
using EveEngine;

// EN: Who caused something, in which session. Base of every source; things that need no combat (items, ...) use it directly.
// VI: Ai gây ra, ở phiên nào. Lớp cha của mọi nguồn; thứ không cần chiến đấu (item, ...) dùng trực tiếp.
public class PNSource
{
    public Guid SessionId { get; }
    public EveEntityId EntityId { get; }
    public PNSource(Guid sessionId, EveEntityId entityId)
    { if (sessionId == Guid.Empty || !entityId.IsValid) throw new ArgumentException("Source requires session and stable entity/system attribution."); SessionId = sessionId; EntityId = entityId; }
}
