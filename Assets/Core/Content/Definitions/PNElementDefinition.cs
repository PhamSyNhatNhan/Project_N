using System;
using EveEngine;

// EN: Identifies an optional element independently from damage type and status effects.
// VI: Định danh nguyên tố tùy chọn, độc lập với loại sát thương và hiệu ứng trạng thái.
public class PNElementDefinition : EveIIdentified
{
    public virtual EveId Id { get; }

    public PNElementDefinition(EveId id)
    {
        if (!id.IsValid) throw new ArgumentException("Element ID is required.", nameof(id));
        Id = id;
    }
}
