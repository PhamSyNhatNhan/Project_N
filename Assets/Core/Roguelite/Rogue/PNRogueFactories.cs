using System;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Which kinds of definition a registered implementation may serve.
[Flags] public enum PNRogueKinds { None = 0, Constellation = 1, Spark = 2, Lodestar = 4 }

// EN: Up to four numbers (and nothing else) that an item or a constellation shows on the HUD of its owner, laid out like the four corners of a Buff card (`PNBuffNumbers`). An absent number
//     is not shown; a shown zero is shown. The class sets `Display` whenever the numbers change; the manager reads it, it is never polled by the UI per frame.
public readonly struct PNRogueDisplay : IEquatable<PNRogueDisplay>
{
    public long? TopLeft { get; }
    public long? TopRight { get; }
    public long? BottomLeft { get; }
    public long? BottomRight { get; }
    public PNRogueDisplay(long? topLeft = null, long? topRight = null, long? bottomLeft = null, long? bottomRight = null)
    { TopLeft = topLeft; TopRight = topRight; BottomLeft = bottomLeft; BottomRight = bottomRight; }
    public bool IsEmpty => !TopLeft.HasValue && !TopRight.HasValue && !BottomLeft.HasValue && !BottomRight.HasValue;
    public bool Equals(PNRogueDisplay other) => TopLeft == other.TopLeft && TopRight == other.TopRight && BottomLeft == other.BottomLeft && BottomRight == other.BottomRight;
    public override bool Equals(object other) => other is PNRogueDisplay display && Equals(display);
    public override int GetHashCode() => TopLeft.GetHashCode() ^ (TopRight.GetHashCode() * 3) ^ (BottomLeft.GetHashCode() * 7) ^ (BottomRight.GetHashCode() * 13);
}

// EN: What the framework hands to the code of one definition when a player entity is built (Docs/RogueBuffPlan.md section 5.2). These are plain references: the class uses the game's own
//     APIs (PNStat, PNSkill.Effects, projectiles, heal, Buff) directly, there is no toolbox. `Set` is the frozen set of items the team owns at the moment the entity is built.
public sealed class PNRogueBinding
{
    public GameObject Entity { get; }
    public PNStat Stat { get; }
    // May be null: a scene entity or a test entity without a skill component.
    public PNSkill Skill { get; }
    public EveSessionContext Session { get; }
    // The index of the floor being played.
    public int Floor { get; }
    public PNRogueSnapshot Set { get; }
    public PNRogueBinding(GameObject entity, PNStat stat, PNSkill skill, EveSessionContext session, int floor, PNRogueSnapshot set)
    { Entity = entity; Stat = stat; Skill = skill; Session = session; Floor = floor; Set = set ?? throw new ArgumentNullException(nameof(set)); }
}

// EN: The code class of a constellation, Spark or Lodestar at run time. A definition reaches its class through a `PNRogueFactoryDefinition`: the definition holds the numbers (`Data`), the class
//     holds the behavior. One object per definition per entity; it lives from the moment the entity is built until it is unloaded (a new floor builds new objects). Rules for the class:
//     no static state; runs only on the authority; does not modify skills or hang on a button; numbers come from `Data`, never constants in code; everything it registers (events, timers) is
//     undone in `Dispose()`; no allocation per frame in `Tick`. The framework applies the shared `stats` block as one modifier source and removes it, so a class with no effect of its own is empty.
// VI: Lớp mã của chòm/Tia sáng/Sao dẫn lúc chạy. Định nghĩa giữ số (`Data`), lớp giữ hành vi. Mỗi định nghĩa một đối tượng cho mỗi thực thể; sống từ lúc dựng đến lúc dỡ thực thể.
public abstract class PNRogueLogic
{
    private JObject data;
    public PNRogueDefinition Definition { get; internal set; }
    public PNRogueBinding Binding { get; private set; }
    // The definition's `data` (a copy made once; it does not change).
    public JObject Data => data ?? (data = Definition.Data);
    public GameObject Entity => Binding.Entity;
    public PNStat Stat => Binding.Stat;
    public PNSkill Skill => Binding.Skill;
    public EveSessionContext Session => Binding.Session;
    public int Floor => Binding.Floor;
    public PNRogueSnapshot Set => Binding.Set;
    // What this object shows on the HUD of its owner; the class sets it when the numbers change. Empty = nothing shown.
    public PNRogueDisplay Display { get; protected set; }

    // Called once by the framework before `Initialize`.
    public void Attach(PNRogueBinding binding)
    {
        if (Binding != null) throw new InvalidOperationException("A Roguelite Buff object is attached once.");
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
    }
    // Runs once, when the entity is built, after the constellation (for an item) and the items before it in the fixed order exist. Read `Set` here for "if A then B" effects.
    public virtual void Initialize() { }
    // Once per frame while the entity runs; `deltaSeconds` follows the entity's own clock (a time stop slows it).
    public virtual void Tick(double deltaSeconds) { }
    // Undo everything this object registered. Called when the entity is unloaded, in the reverse order of `Initialize`; also after a failure in `Initialize` or `Tick`.
    public virtual void Dispose() { }
}

// A constellation's class: owns the base mechanism and the state shared by the items of that constellation on this entity (for example a counter). Items reach it through `Constellation`.
public abstract class PNRogueConstellationLogic : PNRogueLogic { }

// A Spark's or a Lodestar's class. `Constellation` is the object of the constellation it belongs to (cast it to the concrete class to call its methods).
public abstract class PNRogueItemLogic : PNRogueLogic
{
    public PNRogueConstellationLogic Constellation { get; internal set; }
}

// EN: A registered implementation: its id (what a definition's `factoryId` names), the kinds it serves, the validator of the `data` block and the creator of the run-time class. Registered in code by a
//     content module, never named by a CLR class in JSON (safe under IL2CPP stripping). A constellation factory creates a `PNRogueConstellationLogic`, a Spark/Lodestar factory a `PNRogueItemLogic`.
//     `validate` runs at content load (structure) and again on the sealed registry (references to other content).
// VI: Một triển khai đã đăng ký: id mà `factoryId` của định nghĩa trỏ tới, các loại nó phục vụ, bộ kiểm khối `data` và bộ tạo lớp lúc chạy. Đăng ký bằng mã.
public sealed class PNRogueFactoryDefinition : EveIIdentified
{
    private readonly Func<PNRogueLogic> create;
    private readonly Action<JObject, EveRegistryHub> validate;
    public EveId Id { get; }
    public PNRogueKinds Kinds { get; }
    public string Implementation => ImplementationType.FullName;
    [JsonIgnore] public Type ImplementationType { get; }

    public PNRogueFactoryDefinition(EveId id, PNRogueKinds kinds, Type implementation, Func<PNRogueLogic> create, Action<JObject, EveRegistryHub> validate = null)
    {
        if (!id.IsValid || id.ToString().Length > PNRogueLimits.MaxText) throw new ArgumentException("A valid factory id of at most 128 characters is required.", nameof(id));
        if (kinds == PNRogueKinds.None || (kinds & ~(PNRogueKinds.Constellation | PNRogueKinds.Spark | PNRogueKinds.Lodestar)) != 0) throw new ArgumentException("A factory serves at least one known kind.", nameof(kinds));
        if (implementation == null || implementation.IsAbstract || implementation.IsInterface || implementation.ContainsGenericParameters || !typeof(PNRogueLogic).IsAssignableFrom(implementation))
            throw new ArgumentException("A factory implementation must be a concrete PNRogueLogic class.", nameof(implementation));
        bool forConstellation = (kinds & PNRogueKinds.Constellation) != 0, forItems = (kinds & (PNRogueKinds.Spark | PNRogueKinds.Lodestar)) != 0;
        if (forConstellation && forItems) throw new ArgumentException("A factory serves a constellation or items, not both.", nameof(kinds));
        if (forConstellation && !typeof(PNRogueConstellationLogic).IsAssignableFrom(implementation)) throw new ArgumentException("A constellation factory creates a PNRogueConstellationLogic.", nameof(implementation));
        if (forItems && !typeof(PNRogueItemLogic).IsAssignableFrom(implementation)) throw new ArgumentException("A Spark or Lodestar factory creates a PNRogueItemLogic.", nameof(implementation));
        Id = id; Kinds = kinds; ImplementationType = implementation; this.create = create ?? throw new ArgumentNullException(nameof(create)); this.validate = validate;
    }

    public bool Serves(PNRogueKind kind) => (Kinds & (PNRogueKinds)(1 << (int)kind)) != 0;

    // Throws FormatException when the block is not what this implementation accepts. `hub` is the registry being built or the sealed one.
    public void ValidateData(JObject data, EveRegistryHub hub) { validate?.Invoke(data ?? new JObject(), hub); }

    public PNRogueLogic Create(PNRogueDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (definition.FactoryId != Id || !Serves(definition.Kind)) throw new InvalidOperationException("Definition " + definition.Id + " does not use factory " + Id + ".");
        var made = create();
        if (made == null || made.GetType() != ImplementationType) throw new InvalidOperationException("Factory " + Id + " must create a " + ImplementationType.Name + ".");
        made.Definition = definition;
        return made;
    }
}
