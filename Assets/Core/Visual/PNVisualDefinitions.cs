using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: Data of the Visual (rig puppet) system, Docs/VisualPlan.md section 3.3. Everything is plain data (JSON + PNG) interpreted by code at run time: no Animator, no clips,
//     no bone GameObjects. Authoring units are art pixels (y down, like the drawing canvas); a rig's pixelsPerMeter converts them once at load.
// VI: Dữ liệu của hệ Visual (rig búp bê giấy). Toàn bộ là dữ liệu thuần (JSON + PNG), không Animator, không clip, không GameObject xương.

public enum PNVisualTint { None, Skin, Eye, Hair, Dye1, Dye2 }
public enum PNVisualMotionKind { Pose, Flipbook }
public enum PNVisualClock { Time, Phase }
public enum PNVisualChannel { Base, Overlay }
public enum PNVisualEase { Linear, Smooth, Step }
public enum PNVisualTrackKind { Rot, X, Y, Sx, Sy, Bend, Sway }
public enum PNVisualBendAxis { Y, X }
public enum PNVisualDynamicsChannel { Rot, Bend }

public static class PNVisualLimits
{
    public const int MaxGridCells = 16, MaxGridVertices = 289, MinLattice = 2, MaxLattice = 6, MaxSkinBones = 8, MaxSkinVerticesPerActor = 4096, MaxDeform = 64, MaxIk = 8, MaxDynamics = 32, MaxBones = 64, MaxSlots = 64, MaxSockets = 32, MaxKeys = 256, MaxTracks = 64, MaxEvents = 32, MaxFrames = 256, MaxVariants = 16, MaxFree = 32, MaxMask = 64, MaxSegments = 16, TrackKinds = 7;
    public const float MinPixelsPerMeter = 10f, MaxPixelsPerMeter = 10000f, MaxCoordinate = 100000f, MaxAngle = 1080f, MaxScale = 8f, MaxDuration = 600f, MaxBlend = .5f, MaxFrequency = 20f, MaxBendAngle = 360f, MaxSpringStiffness = 2000f, MaxSpringDamping = 50f, MaxSpringGravity = 5000f, MaxSpringInertia = 4f, MaxSpringLimit = 180f, MaxSpringTip = 2000f;
    public const int MaxLayer = 1000;
    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static float Coordinate(float value, string what)
    {
        if (!Finite(value) || Math.Abs(value) > MaxCoordinate) throw new FormatException(what + " must be a finite number within +-" + MaxCoordinate + ".");
        return value;
    }
    public static float InRange(float value, float minimum, float maximum, string what)
    {
        if (!Finite(value) || value < minimum || value > maximum) throw new FormatException(what + " must be between " + minimum + " and " + maximum + ".");
        return value;
    }
    public static string Name(string value, string what)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64) throw new FormatException(what + " must be 1..64 characters.");
        foreach (var c in value) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_')) throw new FormatException(what + " allows letters, digits and _ only: " + value);
        return value;
    }
    // Image paths are relative to the pack folder: no root, no "..", no backslash, a .png.
    public static string ImagePath(string value, string what)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 200) throw new FormatException(what + " image path must be 1..200 characters.");
        if (value.StartsWith("/") || value.Contains("\\") || value.Contains("..") || value.Contains(":") || !value.EndsWith(".png", StringComparison.Ordinal))
            throw new FormatException(what + " image path must be a relative .png path without '..': " + value);
        return value;
    }
}

// A spring chain link: the end of the bone (`tip`, in the bone's frame, art px) trails the bone's animated pose like a mass on a spring and the bone turns (Rot) or its bendable part curves (Bend)
// by the angle between the animated direction and the trailing one. Simulated by PNVisualAnimator in unmirrored art px with a fixed step; presentation only.
public sealed class PNVisualDynamicsEntry
{
    public string Bone { get; } public PNVisualDynamicsChannel Channel { get; } public float TipX { get; } public float TipY { get; }
    public float Stiffness { get; } public float Damping { get; } public float Gravity { get; } public float Inertia { get; } public float Limit { get; }
    public PNVisualDynamicsEntry(string bone, PNVisualDynamicsChannel channel, float tipX, float tipY, float stiffness, float damping, float gravity, float inertia, float limit)
    {
        Bone = PNVisualLimits.Name(bone, "dynamics bone");
        if (!Enum.IsDefined(typeof(PNVisualDynamicsChannel), channel)) throw new FormatException("dynamics channel is unknown.");
        Channel = channel; TipX = PNVisualLimits.Coordinate(tipX, "dynamics tip x"); TipY = PNVisualLimits.Coordinate(tipY, "dynamics tip y");
        float length = (float)Math.Sqrt(TipX * TipX + TipY * TipY);
        if (length < 1f || length > PNVisualLimits.MaxSpringTip) throw new FormatException("dynamics tip of " + bone + " must be 1.." + PNVisualLimits.MaxSpringTip + " px from the joint.");
        Stiffness = PNVisualLimits.InRange(stiffness, 0f, PNVisualLimits.MaxSpringStiffness, "dynamics stiffness"); Damping = PNVisualLimits.InRange(damping, 0f, PNVisualLimits.MaxSpringDamping, "dynamics damping");
        Gravity = PNVisualLimits.InRange(gravity, -PNVisualLimits.MaxSpringGravity, PNVisualLimits.MaxSpringGravity, "dynamics gravity"); Inertia = PNVisualLimits.InRange(inertia, 0f, PNVisualLimits.MaxSpringInertia, "dynamics inertia");
        Limit = PNVisualLimits.InRange(limit, 0f, PNVisualLimits.MaxSpringLimit, "dynamics limit");
    }
}

// An inverse kinematics constraint: one or two bones are turned so the tip of the last one (`tip`, in its frame, art px) reaches the goal, the position of the bone `target`. `mix` 0..1 is how far the
// solution is applied (a motion may change it over time, see PNVisualIkMix). Solved by PNVisualAnimator before the spring chains; see PNVisualIk.
public sealed class PNVisualIkConstraint
{
    public string Id { get; } public IReadOnlyList<string> Bones { get; } public float TipX { get; } public float TipY { get; } public string Target { get; } public bool BendPositive { get; } public float Mix { get; }
    public PNVisualIkConstraint(string id, IEnumerable<string> bones, float tipX, float tipY, string target, bool bendPositive, float mix)
    {
        Id = PNVisualLimits.Name(id, "ik id"); var list = new List<string>(); foreach (var b in bones ?? throw new ArgumentNullException(nameof(bones))) list.Add(PNVisualLimits.Name(b, "ik bone"));
        if (list.Count < 1 || list.Count > 2) throw new FormatException("ik " + id + " needs one or two bones.");
        if (list.Count == 2 && list[0] == list[1]) throw new FormatException("ik " + id + " repeats a bone.");
        Bones = list; TipX = PNVisualLimits.Coordinate(tipX, "ik tip x"); TipY = PNVisualLimits.Coordinate(tipY, "ik tip y");
        if (TipX * TipX + TipY * TipY < 1f) throw new FormatException("ik " + id + " tip must be at least 1 px from the joint.");
        Target = PNVisualLimits.Name(target, "ik target"); BendPositive = bendPositive; Mix = PNVisualLimits.InRange(mix, 0f, 1f, "ik mix");
    }
}

// A motion's own mix curve (0..1) for one constraint of the rig: the constraint is blended in and out over time.
public sealed class PNVisualIkMix
{
    public string Id { get; } public PNVisualCurve Curve { get; }
    public PNVisualIkMix(string id, PNVisualCurve curve)
    {
        Id = PNVisualLimits.Name(id, "ik mix id"); Curve = curve ?? throw new ArgumentNullException(nameof(curve));
        if (curve.StartsFromCurrent) throw new FormatException("an ik mix curve cannot start from the current pose.");
        if (!curve.IsWave) for (int i = 0; i < curve.Values.Length; i++) PNVisualLimits.InRange(curve.Values[i], 0f, 1f, "ik mix key");
    }
}

// Skin weights of a gridded part (R4): every vertex of the grid follows up to four bones of the rig. `Bones` names the bones (the part does not know the rig; the appearance checks they exist).
// Auto = the weights are computed when the puppet is bound, from the distance of each vertex to each bone in the rest pose (`Falloff`: how fast a bone's pull dies out). Explicit weights come
// as four (bone index, weight) pairs per vertex, vertices in the order of the picture (left to right, top row first), each vertex summing to 1 (+-0.01).
public sealed class PNVisualSkin
{
    public IReadOnlyList<string> Bones { get; } public bool Auto { get; } public float Falloff { get; } public int[] Influence { get; } public float[] Weight { get; }
    public const int PerVertex = 4;
    public PNVisualSkin(IEnumerable<string> bones, bool auto, float falloff, int[] influence, float[] weight)
    {
        var list = new List<string>(); foreach (var b in bones ?? throw new ArgumentNullException(nameof(bones))) { var name = PNVisualLimits.Name(b, "skin bone"); if (list.Contains(name)) throw new FormatException("skin repeats bone " + name + "."); list.Add(name); }
        if (list.Count < 1 || list.Count > PNVisualLimits.MaxSkinBones) throw new FormatException("skin needs 1.." + PNVisualLimits.MaxSkinBones + " bones.");
        Bones = list; Auto = auto; Falloff = PNVisualLimits.InRange(falloff, 1f, 8f, "skin falloff");
        if (auto) return;
        if (influence == null || weight == null || influence.Length != weight.Length || influence.Length == 0 || influence.Length % PerVertex != 0) throw new FormatException("skin weights need four influences per vertex.");
        for (int v = 0; v < influence.Length / PerVertex; v++)
        {
            float sum = 0f;
            for (int k = 0; k < PerVertex; k++)
            {
                int i = v * PerVertex + k; float w = weight[i];
                if (!PNVisualLimits.Finite(w) || w < 0f || w > 1f) throw new FormatException("a skin weight must be 0..1.");
                if (w > 0f && (influence[i] < 0 || influence[i] >= list.Count)) throw new FormatException("a skin weight names a bone that is not in the skin.");
                sum += w;
            }
            if (Math.Abs(sum - 1f) > .01f) throw new FormatException("the skin weights of a vertex must add up to 1 (+-0.01).");
        }
        Influence = influence; Weight = weight;
    }
    public int VertexCount => Influence == null ? 0 : Influence.Length / PerVertex;
}

public sealed class PNVisualBone
{
    public string Id { get; } public string Parent { get; } public float X { get; } public float Y { get; } public float Scale { get; } public string ScaleGroup { get; }
    public PNVisualBone(string id, string parent, float x, float y, float scale, string scaleGroup)
    {
        Id = PNVisualLimits.Name(id, "bone id"); Parent = string.IsNullOrEmpty(parent) ? "" : PNVisualLimits.Name(parent, "bone parent");
        X = PNVisualLimits.Coordinate(x, "bone x"); Y = PNVisualLimits.Coordinate(y, "bone y");
        Scale = PNVisualLimits.InRange(scale, .01f, PNVisualLimits.MaxScale, "bone scale"); ScaleGroup = string.IsNullOrEmpty(scaleGroup) ? "" : PNVisualLimits.Name(scaleGroup, "scale group");
    }
}

public sealed class PNVisualSlot
{
    public string Id { get; } public string Bone { get; } public int Layer { get; } public bool Face { get; } public bool Blink { get; }
    // Where the slot's part sits in its bone's frame (art px); the bone's own origin when 0, 0 (a face slot hangs on the head bone at its own spot).
    public float X { get; } public float Y { get; }
    public PNVisualSlot(string id, string bone, int layer, bool face, bool blink, float x = 0f, float y = 0f)
    {
        X = PNVisualLimits.Coordinate(x, "slot x"); Y = PNVisualLimits.Coordinate(y, "slot y");
        Id = PNVisualLimits.Name(id, "slot id"); Bone = PNVisualLimits.Name(bone, "slot bone");
        if (layer < -PNVisualLimits.MaxLayer || layer > PNVisualLimits.MaxLayer) throw new FormatException("slot layer out of range.");
        Layer = layer; Face = face; Blink = blink;
    }
}

public sealed class PNVisualSocket
{
    public string Id { get; } public string Bone { get; } public float X { get; } public float Y { get; }
    public PNVisualSocket(string id, string bone, float x, float y)
    { Id = PNVisualLimits.Name(id, "socket id"); Bone = PNVisualLimits.Name(bone, "socket bone"); X = PNVisualLimits.Coordinate(x, "socket x"); Y = PNVisualLimits.Coordinate(y, "socket y"); }
}

public sealed class PNVisualRigDefinition : EveIIdentified
{
    public EveId Id { get; } public float PixelsPerMeter { get; } public float CapsuleHeight { get; } public float CapsuleRadius { get; }
    public EveId? Extends { get; }
    // Optional drawing box in metres for a rig whose parts reach far outside its capsule (a boss, orbiting pieces): width, height and the part below the feet. 0 = computed from the rest pose.
    public float DrawWidth { get; } public float DrawHeight { get; } public float DrawBottom { get; }
    public IReadOnlyList<PNVisualDynamicsEntry> Dynamics { get; } public IReadOnlyList<PNVisualIkConstraint> Ik { get; }
    public IReadOnlyList<PNVisualBone> Bones { get; } public IReadOnlyList<PNVisualSlot> Slots { get; } public IReadOnlyList<PNVisualSocket> Sockets { get; }
    public PNVisualRigDefinition(EveId id, float pixelsPerMeter, float capsuleHeight, float capsuleRadius, EveId? extends, IEnumerable<PNVisualBone> bones, IEnumerable<PNVisualSlot> slots, IEnumerable<PNVisualSocket> sockets,
        float drawWidth = 0f, float drawHeight = 0f, float drawBottom = 0f, IEnumerable<PNVisualDynamicsEntry> dynamics = null, IEnumerable<PNVisualIkConstraint> ik = null)
    {
        Ik = (ik ?? Enumerable.Empty<PNVisualIkConstraint>()).ToArray(); if (Ik.Count > PNVisualLimits.MaxIk) throw new FormatException("rig " + id + " has too many ik constraints.");
        Dynamics = (dynamics ?? Enumerable.Empty<PNVisualDynamicsEntry>()).ToArray();
        if (Dynamics.Count > PNVisualLimits.MaxDynamics) throw new FormatException("rig " + id + " has too many dynamics entries.");
        DrawWidth = DrawBox(drawWidth, "drawBounds width"); DrawHeight = DrawBox(drawHeight, "drawBounds height"); DrawBottom = DrawBox(drawBottom, "drawBounds bottom");
        if (!id.IsValid) throw new FormatException("rig needs a valid id.");
        Id = id; PixelsPerMeter = PNVisualLimits.InRange(pixelsPerMeter, PNVisualLimits.MinPixelsPerMeter, PNVisualLimits.MaxPixelsPerMeter, "pixelsPerMeter");
        CapsuleHeight = PNVisualLimits.InRange(capsuleHeight, .01f, 100f, "capsule height"); CapsuleRadius = PNVisualLimits.InRange(capsuleRadius, .01f, 100f, "capsule radius");
        Extends = extends;
        Bones = (bones ?? throw new ArgumentNullException(nameof(bones))).ToArray(); Slots = (slots ?? Enumerable.Empty<PNVisualSlot>()).ToArray(); Sockets = (sockets ?? Enumerable.Empty<PNVisualSocket>()).ToArray();
        if (Bones.Count > PNVisualLimits.MaxBones || Slots.Count > PNVisualLimits.MaxSlots || Sockets.Count > PNVisualLimits.MaxSockets) throw new FormatException("rig " + id + " has too many bones, slots or sockets.");
        if (Extends == null && Bones.Count == 0) throw new FormatException("rig " + id + " needs at least one bone.");
    }

    private static float DrawBox(float value, string what) => value == 0f ? 0f : PNVisualLimits.InRange(value, .01f, 100f, what);

    // The rig with its base rig's bones/slots/sockets first; bones are returned parents-before-children. Throws on a cycle, a missing parent, a duplicate id or an unknown bone in a slot/socket.
    public PNVisualResolvedRig Resolve(Func<EveId, PNVisualRigDefinition> lookup)
    {
        var chain = new List<PNVisualRigDefinition>(); var seen = new HashSet<EveId>(); var current = this;
        while (current != null)
        {
            if (!seen.Add(current.Id)) throw new FormatException("rig " + Id + " extends itself through a cycle.");
            chain.Insert(0, current);
            if (chain.Count > 8) throw new FormatException("rig " + Id + " extends more than 8 levels.");
            current = current.Extends.HasValue ? lookup(current.Extends.Value) ?? throw new FormatException("rig " + current.Id + " extends unknown rig " + current.Extends.Value) : null;
        }
        var bones = new List<PNVisualBone>(); var slots = new List<PNVisualSlot>(); var sockets = new List<PNVisualSocket>(); var dynamics = new List<PNVisualDynamicsEntry>(); var ik = new List<PNVisualIkConstraint>();
        foreach (var rig in chain) { bones.AddRange(rig.Bones); slots.AddRange(rig.Slots); sockets.AddRange(rig.Sockets); dynamics.AddRange(rig.Dynamics); ik.AddRange(rig.Ik); }
        if (ik.Count > PNVisualLimits.MaxIk) throw new FormatException("rig " + Id + " resolves to too many ik constraints.");
        if (dynamics.Count > PNVisualLimits.MaxDynamics) throw new FormatException("rig " + Id + " resolves to too many dynamics entries.");
        if (bones.Count > PNVisualLimits.MaxBones || slots.Count > PNVisualLimits.MaxSlots || sockets.Count > PNVisualLimits.MaxSockets) throw new FormatException("rig " + Id + " resolves to too many bones, slots or sockets.");
        var byId = new Dictionary<string, PNVisualBone>();
        foreach (var bone in bones) if (!byId.ContainsKey(bone.Id)) byId.Add(bone.Id, bone); else throw new FormatException("rig " + Id + " repeats bone " + bone.Id + ".");
        int roots = 0; var order = new List<PNVisualBone>(); var placed = new HashSet<string>();
        foreach (var bone in bones)
        {
            if (bone.Parent.Length == 0) { roots++; continue; }
            if (!byId.ContainsKey(bone.Parent)) throw new FormatException("rig " + Id + " bone " + bone.Id + " has unknown parent " + bone.Parent + ".");
        }
        if (roots != 1) throw new FormatException("rig " + Id + " needs exactly one root bone (no parent).");
        while (order.Count < bones.Count)
        {
            int before = order.Count;
            foreach (var bone in bones) if (!placed.Contains(bone.Id) && (bone.Parent.Length == 0 || placed.Contains(bone.Parent))) { order.Add(bone); placed.Add(bone.Id); }
            if (order.Count == before) throw new FormatException("rig " + Id + " has a parent cycle among its bones.");
        }
        var slotIds = new HashSet<string>();
        foreach (var slot in slots) { if (!byId.ContainsKey(slot.Bone)) throw new FormatException("rig " + Id + " slot " + slot.Id + " uses unknown bone " + slot.Bone + "."); if (!slotIds.Add(slot.Id)) throw new FormatException("rig " + Id + " repeats slot " + slot.Id + "."); }
        var socketIds = new HashSet<string>();
        foreach (var socket in sockets) { if (!byId.ContainsKey(socket.Bone)) throw new FormatException("rig " + Id + " socket " + socket.Id + " uses unknown bone " + socket.Bone + "."); if (!socketIds.Add(socket.Id)) throw new FormatException("rig " + Id + " repeats socket " + socket.Id + "."); }
        var dynamicBones = new HashSet<string>();
        foreach (var entry in dynamics) { if (!byId.ContainsKey(entry.Bone)) throw new FormatException("rig " + Id + " dynamics uses unknown bone " + entry.Bone + "."); if (!dynamicBones.Add(entry.Bone)) throw new FormatException("rig " + Id + " has two dynamics entries on bone " + entry.Bone + "."); }
        var ikIds = new HashSet<string>();
        foreach (var c in ik)
        {
            if (!ikIds.Add(c.Id)) throw new FormatException("rig " + Id + " repeats ik " + c.Id + ".");
            foreach (var b in c.Bones) if (!byId.ContainsKey(b)) throw new FormatException("rig " + Id + " ik " + c.Id + " uses unknown bone " + b + ".");
            if (!byId.ContainsKey(c.Target)) throw new FormatException("rig " + Id + " ik " + c.Id + " uses unknown target " + c.Target + ".");
            if (c.Bones.Count == 2 && byId[c.Bones[1]].Parent != c.Bones[0]) throw new FormatException("rig " + Id + " ik " + c.Id + ": the second bone must be a child of the first.");
            // the goal may not move with the chain it drives: the target is neither in the chain nor below its first bone
            for (string up = c.Target; up.Length > 0; up = byId[up].Parent) if (up == c.Bones[0]) throw new FormatException("rig " + Id + " ik " + c.Id + ": the target is part of, or below, the chain.");
        }
        return new PNVisualResolvedRig(this, order, slots, sockets, dynamics, ik);
    }
}

// A rig after resolving `extends`: bones parents-first with index lookups.
public sealed class PNVisualResolvedRig
{
    public PNVisualRigDefinition Definition { get; }
    public IReadOnlyList<PNVisualBone> Bones { get; } public IReadOnlyList<PNVisualSlot> Slots { get; } public IReadOnlyList<PNVisualSocket> Sockets { get; }
    public IReadOnlyList<PNVisualDynamicsEntry> Dynamics { get; } public IReadOnlyList<PNVisualIkConstraint> Ik { get; }
    public int[] ParentIndex { get; }
    private readonly Dictionary<string, int> boneIndex = new Dictionary<string, int>(), slotIndex = new Dictionary<string, int>(), socketIndex = new Dictionary<string, int>();
    public PNVisualResolvedRig(PNVisualRigDefinition definition, IReadOnlyList<PNVisualBone> bones, IReadOnlyList<PNVisualSlot> slots, IReadOnlyList<PNVisualSocket> sockets, IReadOnlyList<PNVisualDynamicsEntry> dynamics = null, IReadOnlyList<PNVisualIkConstraint> ik = null)
    {
        Definition = definition; Bones = bones; Slots = slots; Sockets = sockets; Dynamics = dynamics ?? new PNVisualDynamicsEntry[0]; Ik = ik ?? new PNVisualIkConstraint[0]; ParentIndex = new int[bones.Count];
        for (int i = 0; i < bones.Count; i++) boneIndex[bones[i].Id] = i;
        for (int i = 0; i < bones.Count; i++) ParentIndex[i] = bones[i].Parent.Length == 0 ? -1 : boneIndex[bones[i].Parent];
        for (int i = 0; i < slots.Count; i++) slotIndex[slots[i].Id] = i;
        for (int i = 0; i < sockets.Count; i++) socketIndex[sockets[i].Id] = i;
    }
    public int BoneIndex(string id) => boneIndex.TryGetValue(id, out var i) ? i : -1;
    public int IkIndex(string id) { for (int i = 0; i < Ik.Count; i++) if (Ik[i].Id == id) return i; return -1; }
    public int SlotIndex(string id) => slotIndex.TryGetValue(id, out var i) ? i : -1;
    public int SocketIndex(string id) => socketIndex.TryGetValue(id, out var i) ? i : -1;
}

public sealed class PNVisualPartDefinition : EveIIdentified
{
    public EveId Id { get; } public string Image { get; } public float PivotX { get; } public float PivotY { get; } public PNVisualTint Tint { get; }
    public IReadOnlyDictionary<string, string> Variants { get; }
    // Bendable part: the image is drawn as `Segments` strips along `Axis` (the pivot is where the bend starts); 1 = a plain rigid quad. See PNVisualBend.
    public int Segments { get; } public PNVisualBendAxis Axis { get; }
    // R3/R4: a gridded part is drawn as GridCols x GridRows cells (0 = no grid); the lattice (LatticeCols x LatticeRows control points, 0 = none) is moved by the motions' deform tracks; Skin ties the grid to bones.
    public int GridCols { get; } public int GridRows { get; } public int LatticeCols { get; } public int LatticeRows { get; } public PNVisualSkin Skin { get; }
    public bool HasGrid => GridCols > 0;
    public PNVisualPartDefinition(EveId id, string image, float pivotX, float pivotY, PNVisualTint tint, IDictionary<string, string> variants, int segments = 1, PNVisualBendAxis axis = PNVisualBendAxis.Y,
        int gridCols = 0, int gridRows = 0, int latticeCols = 0, int latticeRows = 0, PNVisualSkin skin = null)
    {
        if (gridCols != 0 || gridRows != 0)
        {
            if (gridCols < 1 || gridCols > PNVisualLimits.MaxGridCells || gridRows < 1 || gridRows > PNVisualLimits.MaxGridCells) throw new FormatException("part " + id + " grid must be 1.." + PNVisualLimits.MaxGridCells + " cells each way.");
            if (segments != 1) throw new FormatException("part " + id + " cannot be both a bendable part and a grid.");
        }
        if (latticeCols != 0 || latticeRows != 0)
        {
            if (gridCols == 0) throw new FormatException("part " + id + " lattice needs a grid.");
            if (latticeCols < PNVisualLimits.MinLattice || latticeCols > PNVisualLimits.MaxLattice || latticeRows < PNVisualLimits.MinLattice || latticeRows > PNVisualLimits.MaxLattice) throw new FormatException("part " + id + " lattice must be " + PNVisualLimits.MinLattice + ".." + PNVisualLimits.MaxLattice + " points each way.");
        }
        if (skin != null)
        {
            if (gridCols == 0) throw new FormatException("part " + id + " skin needs a grid.");
            if (latticeCols != 0) throw new FormatException("part " + id + " cannot have both a lattice and a skin.");
            if (!skin.Auto && skin.VertexCount != (gridCols + 1) * (gridRows + 1)) throw new FormatException("part " + id + " skin has " + skin.VertexCount + " weighted vertices, the grid has " + (gridCols + 1) * (gridRows + 1) + ".");
        }
        GridCols = gridCols; GridRows = gridRows; LatticeCols = latticeCols; LatticeRows = latticeRows; Skin = skin;
        if (segments < 1 || segments > PNVisualLimits.MaxSegments) throw new FormatException("part " + id + " segments must be 1.." + PNVisualLimits.MaxSegments + ".");
        if (!Enum.IsDefined(typeof(PNVisualBendAxis), axis)) throw new FormatException("part bend axis is unknown.");
        Segments = segments; Axis = axis;
        if (!id.IsValid) throw new FormatException("part needs a valid id.");
        Id = id; Image = PNVisualLimits.ImagePath(image, "part " + id); PivotX = PNVisualLimits.Coordinate(pivotX, "part pivot x"); PivotY = PNVisualLimits.Coordinate(pivotY, "part pivot y");
        if (!Enum.IsDefined(typeof(PNVisualTint), tint)) throw new FormatException("part tint is unknown.");
        Tint = tint;
        var copy = new Dictionary<string, string>();
        if (variants != null)
        {
            if (variants.Count > PNVisualLimits.MaxVariants) throw new FormatException("part " + id + " has too many variants.");
            foreach (var pair in variants) copy.Add(PNVisualLimits.Name(pair.Key, "variant name"), PNVisualLimits.ImagePath(pair.Value, "part " + id + " variant " + pair.Key));
        }
        Variants = copy;
    }
}

public sealed class PNVisualBuildDefinition : EveIIdentified
{
    public EveId Id { get; } public float CapsuleHeight { get; } public float CapsuleRadius { get; } public float Shadow { get; }
    private readonly Dictionary<string, float[]> groups;
    public IReadOnlyDictionary<string, float[]> ScaleGroups => groups;
    public PNVisualBuildDefinition(EveId id, IDictionary<string, float[]> scaleGroups, float capsuleHeight, float capsuleRadius, float shadow)
    {
        if (!id.IsValid) throw new FormatException("build needs a valid id.");
        Id = id; CapsuleHeight = PNVisualLimits.InRange(capsuleHeight, .25f, 4f, "build capsule height"); CapsuleRadius = PNVisualLimits.InRange(capsuleRadius, .25f, 4f, "build capsule radius"); Shadow = PNVisualLimits.InRange(shadow, .25f, 4f, "build shadow");
        groups = new Dictionary<string, float[]>();
        if (scaleGroups != null)
            foreach (var pair in scaleGroups)
            {
                if (pair.Value == null || pair.Value.Length != 2) throw new FormatException("build scale group needs [x, y].");
                groups.Add(PNVisualLimits.Name(pair.Key, "scale group"), new[] { PNVisualLimits.InRange(pair.Value[0], .25f, 4f, "build scale x"), PNVisualLimits.InRange(pair.Value[1], .25f, 4f, "build scale y") });
            }
    }
    public void Scale(string group, out float x, out float y)
    {
        if (group != null && group.Length > 0 && groups.TryGetValue(group, out var v)) { x = v[0]; y = v[1]; } else { x = 1f; y = 1f; }
    }
}

// A value over time: either keys (t, v) eased, or a repeating wave. Key 0 with `Current` starts from the pose being shown (skill strikes).
public sealed class PNVisualCurve
{
    public float[] Times { get; } public float[] Values { get; } public PNVisualEase Ease { get; } public bool StartsFromCurrent { get; }
    public bool IsWave { get; } public float Amp { get; } public float Freq { get; } public float Phase { get; } public float Offset { get; } public bool Abs { get; }
    // Bezier handles of the segment that STARTS at key i (docs/VisualCurveHandlesPlan.md): Handles[4 i ..] = cx1, dy1, cx2, dy2 with cx a fraction (0..1) of the segment's length in time and dy the value
    // offset of each control point from the key it belongs to (dy1 from key i, dy2 from key i + 1). null when no key has a handle: such a curve is sampled exactly as before. HasHandle[i] says which keys carry one.
    public float[] Handles { get; } public bool[] HasHandle { get; }
    public PNVisualCurve(float[] times, float[] values, PNVisualEase ease, bool startsFromCurrent) : this(times, values, ease, startsFromCurrent, null, null) { }
    public PNVisualCurve(float[] times, float[] values, PNVisualEase ease, bool startsFromCurrent, float[] handles, bool[] hasHandle)
    {
        if (times == null || values == null || times.Length != values.Length || times.Length == 0 || times.Length > PNVisualLimits.MaxKeys) throw new FormatException("curve needs 1.." + PNVisualLimits.MaxKeys + " keys.");
        for (int i = 0; i < times.Length; i++)
        {
            if (!PNVisualLimits.Finite(times[i]) || times[i] < 0f || times[i] > PNVisualLimits.MaxDuration) throw new FormatException("key time out of range.");
            if (i > 0 && times[i] <= times[i - 1]) throw new FormatException("key times must increase.");
            if (!(startsFromCurrent && i == 0)) PNVisualLimits.Coordinate(values[i], "key value");
        }
        if (!Enum.IsDefined(typeof(PNVisualEase), ease)) throw new FormatException("unknown ease.");
        if (handles != null || hasHandle != null)
        {
            if (handles == null || hasHandle == null || hasHandle.Length != times.Length || handles.Length != times.Length * 4) throw new FormatException("curve handles do not match the keys.");
            bool any = false;
            for (int i = 0; i < times.Length; i++)
            {
                if (!hasHandle[i]) continue;
                any = true;
                if (i == times.Length - 1) throw new FormatException("the last key has no segment after it, so it cannot carry a handle.");
                float cx1 = handles[i * 4], cx2 = handles[i * 4 + 2];
                if (!PNVisualLimits.Finite(cx1) || !PNVisualLimits.Finite(cx2) || cx1 < 0f || cx1 > 1f || cx2 < 0f || cx2 > 1f) throw new FormatException("a handle's time (cx) must be 0..1 of the segment.");
                PNVisualLimits.Coordinate(handles[i * 4 + 1], "handle dy"); PNVisualLimits.Coordinate(handles[i * 4 + 3], "handle dy");
            }
            if (any) { Handles = handles; HasHandle = hasHandle; }
        }
        Times = times; Values = values; Ease = ease; StartsFromCurrent = startsFromCurrent;
    }
    public PNVisualCurve(float amp, float freq, float phase, float offset, bool abs)
    {
        IsWave = true; Amp = PNVisualLimits.Coordinate(amp, "wave amp"); Freq = PNVisualLimits.InRange(freq, 0f, PNVisualLimits.MaxFrequency, "wave freq");
        Phase = PNVisualLimits.Coordinate(phase, "wave phase"); Offset = PNVisualLimits.Coordinate(offset, "wave offset"); Abs = abs;
        Times = Array.Empty<float>(); Values = Array.Empty<float>();
    }
}

public sealed class PNVisualTrack
{
    public string Bone { get; } public PNVisualTrackKind Kind { get; } public PNVisualCurve Curve { get; }
    public PNVisualTrack(string bone, PNVisualTrackKind kind, PNVisualCurve curve)
    {
        Bone = PNVisualLimits.Name(bone, "track bone"); Kind = kind; Curve = curve ?? throw new ArgumentNullException(nameof(curve));
        if (!Enum.IsDefined(typeof(PNVisualTrackKind), kind)) throw new FormatException("track kind is unknown.");
        if (kind == PNVisualTrackKind.Bend || kind == PNVisualTrackKind.Sway)
        {
            // a bend is a turn in degrees along the whole part; refused (not clamped) beyond +-MaxBendAngle
            float peak = 0f;
            if (curve.IsWave) peak = Math.Abs(curve.Offset) + Math.Abs(curve.Amp);
            else for (int i = 0; i < curve.Values.Length; i++) if (!(curve.StartsFromCurrent && i == 0)) peak = Math.Max(peak, Math.Abs(curve.Values[i]));
            if (peak > PNVisualLimits.MaxBendAngle) throw new FormatException("bone " + bone + " " + kind + " must stay within +-" + PNVisualLimits.MaxBendAngle + " degrees.");
        }
    }
}

// The move of one lattice control point of a slot's part over time (R3): column and row count from the top left of the picture, offsets in art px of the picture before the bone's scale.
public sealed class PNVisualDeformTrack
{
    public string Slot { get; } public int Col { get; } public int Row { get; } public PNVisualCurve X { get; } public PNVisualCurve Y { get; }
    public PNVisualDeformTrack(string slot, int col, int row, PNVisualCurve x, PNVisualCurve y)
    {
        Slot = PNVisualLimits.Name(slot, "deform slot");
        if (col < 0 || col >= PNVisualLimits.MaxLattice || row < 0 || row >= PNVisualLimits.MaxLattice) throw new FormatException("deform point must be 0.." + (PNVisualLimits.MaxLattice - 1) + " each way.");
        if (x == null && y == null) throw new FormatException("a deform point needs an x or a y curve.");
        foreach (var c in new[] { x, y }) if (c != null && c.StartsFromCurrent) throw new FormatException("a deform curve cannot start from the current pose.");
        Col = col; Row = row; X = x; Y = y;
    }
}

// A slot's variant over time (step) and an alpha curve, for faces inside a motion.
public sealed class PNVisualSlotTrack
{
    public string Slot { get; } public float[] Times { get; } public string[] Variants { get; } public PNVisualCurve Alpha { get; } public PNVisualCurve Sy { get; }
    public PNVisualSlotTrack(string slot, float[] times, string[] variants, PNVisualCurve alpha, PNVisualCurve sy = null)
    {
        Slot = PNVisualLimits.Name(slot, "slot track slot"); Times = times ?? Array.Empty<float>(); Variants = variants ?? Array.Empty<string>(); Alpha = alpha; Sy = sy;
        if (Times.Length != Variants.Length || Times.Length > PNVisualLimits.MaxKeys) throw new FormatException("slot track variants need one time per variant.");
        for (int i = 0; i < Times.Length; i++)
        {
            if (!PNVisualLimits.Finite(Times[i]) || Times[i] < 0f || (i > 0 && Times[i] <= Times[i - 1])) throw new FormatException("slot track times must increase from 0.");
            if (Variants[i] != "") PNVisualLimits.Name(Variants[i], "slot track variant");
        }
    }
}

public sealed class PNVisualEvent
{
    public float At { get; } public string Name { get; }
    public PNVisualEvent(float at, string name) { At = PNVisualLimits.InRange(at, 0f, PNVisualLimits.MaxDuration, "event time"); Name = PNVisualLimits.Name(name, "event name"); }
}

public sealed class PNVisualMotionDefinition : EveIIdentified
{
    public EveId Id { get; } public EveId Rig { get; } public PNVisualMotionKind Kind { get; } public PNVisualClock Clock { get; } public float Duration { get; } public bool Loop { get; }
    public float BlendIn { get; } public float BlendOut { get; } public PNVisualChannel Channel { get; } public bool NoModifiers { get; } public float SpeedScale { get; }
    public IReadOnlyList<string> Mask { get; } public IReadOnlyList<PNVisualTrack> Tracks { get; } public IReadOnlyList<PNVisualSlotTrack> SlotTracks { get; } public IReadOnlyList<PNVisualEvent> Events { get; }
    public IReadOnlyList<string> Frames { get; } public float Fps { get; }
    // 0..1: how much of the rig's spring chains this motion lets act (1 = all). A motion that must hit exact hand-keyed poses can ask for less.
    public float DynamicsWeight { get; }
    public IReadOnlyList<PNVisualIkMix> IkMix { get; } public IReadOnlyList<PNVisualDeformTrack> Deform { get; }
    public PNVisualMotionDefinition(EveId id, EveId rig, PNVisualMotionKind kind, PNVisualClock clock, float duration, bool loop, float blendIn, float blendOut, PNVisualChannel channel, bool noModifiers, float speedScale,
        IEnumerable<string> mask, IEnumerable<PNVisualTrack> tracks, IEnumerable<PNVisualSlotTrack> slotTracks, IEnumerable<PNVisualEvent> events, IEnumerable<string> frames, float fps, float dynamicsWeight = 1f, IEnumerable<PNVisualIkMix> ikMix = null, IEnumerable<PNVisualDeformTrack> deform = null)
    {
        Deform = (deform ?? Enumerable.Empty<PNVisualDeformTrack>()).ToArray(); if (Deform.Count > PNVisualLimits.MaxDeform) throw new FormatException("motion has too many deform tracks.");
        IkMix = (ikMix ?? Enumerable.Empty<PNVisualIkMix>()).ToArray();
        DynamicsWeight = PNVisualLimits.InRange(dynamicsWeight, 0f, 1f, "motion dynamics");
        if (!id.IsValid || !rig.IsValid) throw new FormatException("motion needs a valid id and rig.");
        if (!Enum.IsDefined(typeof(PNVisualMotionKind), kind) || !Enum.IsDefined(typeof(PNVisualClock), clock) || !Enum.IsDefined(typeof(PNVisualChannel), channel)) throw new FormatException("motion kind, clock or channel is unknown.");
        Id = id; Rig = rig; Kind = kind; Clock = clock; Loop = loop; NoModifiers = noModifiers; Channel = channel;
        Duration = PNVisualLimits.InRange(duration, .01f, PNVisualLimits.MaxDuration, "motion duration");
        BlendIn = PNVisualLimits.InRange(blendIn, 0f, PNVisualLimits.MaxBlend, "blendIn"); BlendOut = PNVisualLimits.InRange(blendOut, 0f, PNVisualLimits.MaxBlend, "blendOut");
        SpeedScale = PNVisualLimits.InRange(speedScale, 0f, 10f, "speedScale"); Fps = PNVisualLimits.InRange(fps, 0f, 120f, "fps");
        Mask = (mask ?? Enumerable.Empty<string>()).Select(x => PNVisualLimits.Name(x, "mask bone")).ToArray();
        Tracks = (tracks ?? Enumerable.Empty<PNVisualTrack>()).ToArray(); SlotTracks = (slotTracks ?? Enumerable.Empty<PNVisualSlotTrack>()).ToArray();
        Events = (events ?? Enumerable.Empty<PNVisualEvent>()).ToArray(); Frames = (frames ?? Enumerable.Empty<string>()).Select(x => PNVisualLimits.ImagePath(x, "motion " + id + " frame")).ToArray();
        if (Mask.Count > PNVisualLimits.MaxMask || Tracks.Count > PNVisualLimits.MaxTracks * PNVisualLimits.TrackKinds || SlotTracks.Count > PNVisualLimits.MaxTracks || Events.Count > PNVisualLimits.MaxEvents || Frames.Count > PNVisualLimits.MaxFrames) throw new FormatException("motion " + id + " has too many masks, tracks, events or frames.");
        if (Kind == PNVisualMotionKind.Flipbook && (Frames.Count == 0 || Fps <= 0f)) throw new FormatException("flipbook motion " + id + " needs frames and fps.");
        if (Kind == PNVisualMotionKind.Pose && Frames.Count > 0) throw new FormatException("pose motion " + id + " cannot have frames.");
        if (Channel == PNVisualChannel.Overlay && Mask.Count == 0) throw new FormatException("overlay motion " + id + " needs a mask.");
        if (Channel == PNVisualChannel.Overlay && Kind != PNVisualMotionKind.Pose) throw new FormatException("an overlay motion must be a pose motion.");
        foreach (var track in Tracks) if (track.Curve.StartsFromCurrent && Channel == PNVisualChannel.Overlay) throw new FormatException("an overlay cannot start from the current pose.");
    }
}

public sealed class PNVisualMotionSetDefinition : EveIIdentified
{
    public EveId Id { get; } public EveId Rig { get; }
    public IReadOnlyDictionary<string, EveId> States { get; }
    public string BreatheBone { get; } public float BreatheAmp { get; } public float BreathePeriod { get; } public float BlinkEvery { get; } public float BlinkDuration { get; }
    public PNVisualMotionSetDefinition(EveId id, EveId rig, IDictionary<string, EveId> states, string breatheBone, float breatheAmp, float breathePeriod, float blinkEvery, float blinkDuration)
    {
        if (!id.IsValid || !rig.IsValid) throw new FormatException("motion set needs a valid id and rig.");
        Id = id; Rig = rig;
        var copy = new Dictionary<string, EveId>();
        if (states == null || states.Count == 0 || states.Count > 32) throw new FormatException("motion set " + id + " needs 1..32 states.");
        foreach (var pair in states) { if (!pair.Value.IsValid) throw new FormatException("motion set state needs a motion id."); copy.Add(PNVisualLimits.Name(pair.Key, "state name"), pair.Value); }
        States = copy;
        BreatheBone = string.IsNullOrEmpty(breatheBone) ? "" : PNVisualLimits.Name(breatheBone, "breathe bone");
        BreatheAmp = PNVisualLimits.InRange(breatheAmp, 0f, 50f, "breathe amp"); BreathePeriod = PNVisualLimits.InRange(breathePeriod, .1f, 60f, "breathe period");
        BlinkEvery = PNVisualLimits.InRange(blinkEvery, 0f, 60f, "blink every"); BlinkDuration = PNVisualLimits.InRange(blinkDuration, .02f, 2f, "blink duration");
    }
}

public sealed class PNVisualFaceSlot
{
    public string Slot { get; } public string Variant { get; } public float Sx { get; } public float Sy { get; } public float Ox { get; } public float Oy { get; } public float Rot { get; }
    public PNVisualFaceSlot(string slot, string variant, float sx, float sy, float ox, float oy, float rot)
    {
        Slot = PNVisualLimits.Name(slot, "expression slot"); Variant = string.IsNullOrEmpty(variant) ? "" : PNVisualLimits.Name(variant, "expression variant");
        Sx = PNVisualLimits.InRange(sx, 0f, PNVisualLimits.MaxScale, "expression sx"); Sy = PNVisualLimits.InRange(sy, 0f, PNVisualLimits.MaxScale, "expression sy");
        Ox = PNVisualLimits.Coordinate(ox, "expression ox"); Oy = PNVisualLimits.Coordinate(oy, "expression oy"); Rot = PNVisualLimits.InRange(rot, -PNVisualLimits.MaxAngle, PNVisualLimits.MaxAngle, "expression rot");
    }
}

public sealed class PNVisualExpressionDefinition : EveIIdentified
{
    public EveId Id { get; } public EveId Rig { get; } public bool Blink { get; } public IReadOnlyList<PNVisualFaceSlot> Slots { get; }
    public PNVisualExpressionDefinition(EveId id, EveId rig, bool blink, IEnumerable<PNVisualFaceSlot> slots)
    {
        if (!id.IsValid || !rig.IsValid) throw new FormatException("expression needs a valid id and rig.");
        Id = id; Rig = rig; Blink = blink; Slots = (slots ?? Enumerable.Empty<PNVisualFaceSlot>()).ToArray();
        if (Slots.Count > PNVisualLimits.MaxSlots) throw new FormatException("expression " + id + " has too many slots.");
    }
}

public sealed class PNVisualFreePart
{
    public EveId Part { get; } public string Bone { get; } public float X { get; } public float Y { get; } public int Layer { get; }
    public PNVisualFreePart(EveId part, string bone, float x, float y, int layer)
    {
        if (!part.IsValid) throw new FormatException("free part needs a part id.");
        Part = part; Bone = PNVisualLimits.Name(bone, "free part bone"); X = PNVisualLimits.Coordinate(x, "free part x"); Y = PNVisualLimits.Coordinate(y, "free part y");
        if (layer < -PNVisualLimits.MaxLayer || layer > PNVisualLimits.MaxLayer) throw new FormatException("free part layer out of range.");
        Layer = layer;
    }
}

public sealed class PNVisualAppearanceDefinition : EveIIdentified
{
    public EveId Id { get; } public EveId Rig { get; } public EveId MotionSet { get; } public EveId Build { get; } public EveId Expression { get; }
    public IReadOnlyDictionary<string, PNVisualColor> Colors { get; } public IReadOnlyDictionary<string, EveId> Slots { get; }
    public IReadOnlyList<PNVisualFreePart> Free { get; } public IReadOnlyList<string> Hide { get; } public float BlushAlpha { get; }
    public IReadOnlyDictionary<string, float[]> SocketOverrides { get; }
    // The strings the stars hang this puppet by (at most PNVisualStringLimits.MaxPerPuppet); empty for a puppet without strings.
    public IReadOnlyList<PNVisualStringEntry> Strings { get; }
    public PNVisualAppearanceDefinition(EveId id, EveId rig, EveId motionSet, EveId build, EveId expression, IDictionary<string, PNVisualColor> colors, IDictionary<string, EveId> slots,
        IEnumerable<PNVisualFreePart> free, IEnumerable<string> hide, float blushAlpha, IDictionary<string, float[]> socketOverrides, IEnumerable<PNVisualStringEntry> strings = null)
    {
        if (!id.IsValid || !rig.IsValid || !motionSet.IsValid || !build.IsValid) throw new FormatException("appearance needs an id, rig, motionSet and build.");
        Id = id; Rig = rig; MotionSet = motionSet; Build = build; Expression = expression;
        var c = new Dictionary<string, PNVisualColor>(); if (colors != null) foreach (var p in colors) c.Add(PNVisualLimits.Name(p.Key, "color channel"), p.Value);
        Colors = c;
        var s = new Dictionary<string, EveId>(); if (slots != null) foreach (var p in slots) { if (!p.Value.IsValid) throw new FormatException("appearance slot needs a part id."); s.Add(PNVisualLimits.Name(p.Key, "appearance slot"), p.Value); }
        if (s.Count > PNVisualLimits.MaxSlots) throw new FormatException("appearance " + id + " has too many slots.");
        Slots = s;
        Free = (free ?? Enumerable.Empty<PNVisualFreePart>()).ToArray(); if (Free.Count > PNVisualLimits.MaxFree) throw new FormatException("appearance " + id + " has too many free parts.");
        Hide = (hide ?? Enumerable.Empty<string>()).Select(x => PNVisualLimits.Name(x, "hidden slot")).ToArray();
        BlushAlpha = PNVisualLimits.InRange(blushAlpha, 0f, 1f, "blushAlpha");
        var o = new Dictionary<string, float[]>(); if (socketOverrides != null) foreach (var p in socketOverrides) { if (p.Value == null || p.Value.Length != 2) throw new FormatException("socket override needs [x, y]."); o.Add(PNVisualLimits.Name(p.Key, "socket"), new[] { PNVisualLimits.Coordinate(p.Value[0], "socket x"), PNVisualLimits.Coordinate(p.Value[1], "socket y") }); }
        SocketOverrides = o;
        Strings = (strings ?? Enumerable.Empty<PNVisualStringEntry>()).ToArray();
        if (Strings.Count > PNVisualStringLimits.MaxPerPuppet) throw new FormatException("appearance " + id + " has more than " + PNVisualStringLimits.MaxPerPuppet + " strings.");
    }
}

// Plain RGBA colour so Core data does not depend on UnityEngine.Color (the smoke suite has no Unity).
public readonly struct PNVisualColor
{
    public readonly float R, G, B, A;
    public PNVisualColor(float r, float g, float b, float a) { R = r; G = g; B = b; A = a; }
    public static PNVisualColor Parse(string text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '#' || (text.Length != 7 && text.Length != 9)) throw new FormatException("colour must be #RRGGBB or #RRGGBBAA: " + text);
        float Part(int i) { try { return Convert.ToInt32(text.Substring(i, 2), 16) / 255f; } catch (FormatException) { throw new FormatException("bad hex colour: " + text); } }
        return new PNVisualColor(Part(1), Part(3), Part(5), text.Length == 9 ? Part(7) : 1f);
    }
}
