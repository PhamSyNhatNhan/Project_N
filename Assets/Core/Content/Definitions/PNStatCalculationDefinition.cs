using System;
using EveEngine;
using Newtonsoft.Json;

// EN: Registers a calculation factory; implementation identity participates in coop content hashes.
// VI: Đăng ký factory đường tính; định danh triển khai tham gia hash nội dung coop.
public class PNStatCalculationDefinition : EveIIdentified
{
    [JsonIgnore] private readonly Type implementationType;
    internal bool IsCurrentIndependentBuiltIn => GetType() == typeof(PNStatCalculationDefinition) &&
        (implementationType == typeof(PNScaleCalculation) || implementationType == typeof(PNMitigationCalculation) || implementationType == typeof(PNPenetrationCalculation));
    public virtual EveId Id { get; }
    public virtual string Implementation => implementationType.FullName;

    public PNStatCalculationDefinition(EveId id, Type implementationType)
    {
        if (!id.IsValid) throw new ArgumentException("Calculation ID is required.", nameof(id));
        if (implementationType == null || !typeof(PNStatCalculation).IsAssignableFrom(implementationType) ||
            implementationType.IsAbstract || implementationType.ContainsGenericParameters ||
            implementationType.GetConstructor(Type.EmptyTypes) == null)
            throw new ArgumentException("Calculation implementation must be concrete with a public parameterless constructor.", nameof(implementationType));
        Id = id;
        this.implementationType = implementationType;
    }

    public virtual PNStatCalculation Create()
    {
        // Exact built-ins still produce fresh instances through the selected factory.
        if (implementationType == typeof(PNScaleCalculation)) return new PNScaleCalculation();
        if (implementationType == typeof(PNMitigationCalculation)) return new PNMitigationCalculation();
        if (implementationType == typeof(PNPenetrationCalculation)) return new PNPenetrationCalculation();
        return (PNStatCalculation)Activator.CreateInstance(implementationType);
    }
}
