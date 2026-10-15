using System;
using EveEngine;

// EN: Defines a damage category and how much final defense applies to it.
// VI: Định nghĩa loại sát thương và tỷ lệ DEF cuối áp dụng cho loại đó.
public class PNDamageTypeDefinition : EveIIdentified
{
    public virtual EveId Id { get; }
    public virtual double DefenseFactor { get; }

    public PNDamageTypeDefinition(EveId id, double defenseFactor = 1d)
    {
        if (!id.IsValid) throw new ArgumentException("Damage type ID is required.", nameof(id));
        if (double.IsNaN(defenseFactor) || double.IsInfinity(defenseFactor) || defenseFactor < 0d)
            throw new ArgumentOutOfRangeException(nameof(defenseFactor));
        Id = id;
        DefenseFactor = defenseFactor;
    }
}
