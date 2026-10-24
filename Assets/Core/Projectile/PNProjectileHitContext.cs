using EveEngine;

// EN: Detached skill context of one projectile: no live skill, attribution is the launch's frozen source and the world is the world
//     the projectile flies in. It lets the frozen OnHit effects (PNSkillHitSnapshot) run after the shooter is gone.
// VI: Ngữ cảnh skill tách rời của một viên đạn: không có skill sống, nguồn là nguồn đã đóng băng lúc bắn.
public sealed class PNProjectileHitContext : PNSkillActionContext
{
    private readonly PNCombatSource source;
    private readonly EveWorldContext world;
    public PNProjectileHitContext(EveId entrySkillId, PNCombatSource source, EveWorldContext world, EveRegistryHub registry)
        : base(entrySkillId, null, registry, null, null, null, null) { this.source = source; this.world = world; }
    public override PNCombatSource Source => source;
    public override EveWorldContext World => world;
}
