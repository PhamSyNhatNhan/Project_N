using System;
using EveEngine;

// Metadata only: calculators decide which definitions they understand and how to use them.
public class PNStatContributionDefinition : EveIIdentified
{
    public EveId Id { get; }
    public PNStatContributionDefinition(EveId id)
    {
        if (!id.IsValid) throw new ArgumentException("Contribution ID is required.", nameof(id));
        Id = id;
    }
}
