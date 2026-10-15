using System;
using EveEngine;

// EN: Registers a classification ID without prescribing values, damage or automatic behavior.
// VI: Đăng ký ID phân loại, không quy định value, damage hoặc hành vi tự động.
public class PNTagDefinition : EveIIdentified
{
    public virtual EveId Id { get; }

    public PNTagDefinition(EveId id)
    {
        if (!id.IsValid) throw new ArgumentException("Tag ID is required.", nameof(id));
        Id = id;
    }
}
