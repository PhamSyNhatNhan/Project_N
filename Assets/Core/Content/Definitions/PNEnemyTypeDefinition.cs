using System;
using EveEngine;

// Classification vocabulary only. Consumers own buffs, rewards and game-mode rules.
public class PNEnemyTypeDefinition : EveIIdentified
{
    public virtual EveId Id { get; }
    public virtual string DisplayName { get; }

    public PNEnemyTypeDefinition(EveId id, string displayName)
    {
        if (!id.IsValid) throw new ArgumentException("Enemy type ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Enemy type display name is required.", nameof(displayName));
        Id = id;
        DisplayName = displayName;
    }
}
