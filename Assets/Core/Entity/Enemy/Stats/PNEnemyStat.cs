// EN: Extension point for stat behavior shared by this game's enemies.
// VI: Điểm kế thừa cho hành vi chỉ số chung của các kẻ địch trong game.
using System;
using EveEngine;

public class PNEnemyStat : PNStat
{
    private PNEnemyTypeDefinition enemyType;
    public PNEnemyTypeDefinition EnemyType => IsReady && enemyType != null ? enemyType
        : throw new InvalidOperationException("Enemy classification requires initialized character content.");
    public EveId EnemyTypeId => EnemyType.Id;
    public override PNCombatTeam CombatTeam => PNCombatTeam.Enemy;

    public override void Initialize(EveCharacterDefinition character, EveRegistryHub registry)
    {
        if (IsInitialized) throw new InvalidOperationException("Enemy stats are already initialized.");
        if (character == null) throw new ArgumentNullException(nameof(character));
        if (registry == null || !registry.IsSealed) throw new ArgumentException("Sealed registry required.", nameof(registry));
        var selected = PNEnemyContent.ResolveType(character.Data, registry);
        base.Initialize(character, registry);
        enemyType = selected;
    }

    // Direct stat-set initialization has no character metadata, so it uses registered Normal.
    public override void Initialize(EveStatSet statSet)
    {
        if (IsInitialized) throw new InvalidOperationException("Enemy stats are already initialized.");
        if (!(statSet is PNStatSet gameSet)) throw new ArgumentException("Enemy stats require a PNStatSet.", nameof(statSet));
        var selected = PNEnemyContent.ResolveType(null, gameSet.Registry);
        base.Initialize(statSet);
        enemyType = selected;
    }

    public override void Unload()
    {
        enemyType = null;
        base.Unload();
    }
}
