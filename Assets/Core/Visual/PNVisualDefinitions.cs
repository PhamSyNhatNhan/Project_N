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
public enum PNVisualTrackKind { Rot, X, Y, Sx, Sy }

public static class PNVisualLimits
{
    public const int MaxBones = 64, MaxSlots = 64, MaxSockets = 32, MaxKeys = 256, MaxTracks = 64, MaxEvents = 32, MaxFrames = 256, MaxVariants = 16, MaxFree = 32, MaxMask = 64;
    public const float MinPixelsPerMeter = 10f, MaxPixelsPerMeter = 10000f, MaxCoordinate = 100000f, MaxAngle = 1080f, MaxScale = 8f, MaxDuration = 600f, MaxBlend = .5f, MaxFrequency = 20f;
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
    public IReadOnlyList<PNVisualBone> Bones { get; } public IReadOnlyList<PNVisualSlot> Slots { get; } public IReadOnlyList<PNVisualSocket> Sockets { get; }
    public PNVisualRigDefinition(EveId id, float pixelsPerMeter, float capsuleHeight, float capsuleRadius, EveId? extends, IEnumerable<PNVisualBone> bones, IEnumerable<PNVisualSlot> slots, IEnumerable<PNVisualSocket> sockets)
    {
        if (!id.IsValid) throw new FormatException("rig needs a valid id.");
        Id = id; PixelsPerMeter = PNVisualLimits.InRange(pixelsPerMeter, PNVisualLimits.MinPixelsPerMeter, PNVisualLimits.MaxPixelsPerMeter, "pixelsPerMeter");
        CapsuleHeight = PNVisualLimits.InRange(capsuleHeight, .01f, 100f, "capsule height"); CapsuleRadius = PNVisualLimits.InRange(capsuleRadius, .01f, 100f, "capsule radius");
        Extends = extends;
        Bones = (bones ?? throw new ArgumentNullException(nameof(bones))).ToArray(); Slots = (slots ?? Enumerable.Empty<PNVisualSlot>()).ToArray(); Sockets = (sockets ?? Enumerable.Empty<PNVisualSocket>()).ToArray();
        if (Bones.Count > PNVisualLimits.MaxBones || Slots.Count > PNVisualLimits.MaxSlots || Sockets.Count > PNVisualLimits.MaxSockets) throw new FormatException("rig " + id + " has too many bones, slots or sockets.");
        if (Extends == null && Bones.Count == 0) throw new FormatException("rig " + id + " needs at least one bone.");
    }

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
        var bones = new List<PNVisualBone>(); var slots = new List<PNVisualSlot>(); var sockets = new List<PNVisualSocket>();
        foreach (var rig in chain) { bones.AddRange(rig.Bones); slots.AddRange(rig.Slots); sockets.AddRange(rig.Sockets); }
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
        return new PNVisualResolvedRig(this, order, slots, sockets);
    }
}

// A rig after resolving `extends`: bones parents-first with index lookups.
public sealed class PNVisualResolvedRig
{
    public PNVisualRigDefinition Definition { get; }
    public IReadOnlyList<PNVisualBone> Bones { get; } public IReadOnlyList<PNVisualSlot> Slots { get; } public IReadOnlyList<PNVisualSocket> Sockets { get; }
    public int[] ParentIndex { get; }
    private readonly Dictionary<string, int> boneIndex = new Dictionary<string, int>(), slotIndex = new Dictionary<string, int>(), socketIndex = new Dictionary<string, int>();
    public PNVisualResolvedRig(PNVisualRigDefinition definition, IReadOnlyList<PNVisualBone> bones, IReadOnlyList<PNVisualSlot> slots, IReadOnlyList<PNVisualSocket> sockets)
    {
        Definition = definition; Bones = bones; Slots = slots; Sockets = sockets; ParentIndex = new int[bones.Count];
        for (int i = 0; i < bones.Count; i++) boneIndex[bones[i].Id] = i;
        for (int i = 0; i < bones.Count; i++) ParentIndex[i] = bones[i].Parent.Length == 0 ? -1 : boneIndex[bones[i].Parent];
        for (int i = 0; i < slots.Count; i++) slotIndex[slots[i].Id] = i;
        for (int i = 0; i < sockets.Count; i++) socketIndex[sockets[i].Id] = i;
    }
    public int BoneIndex(string id) => boneIndex.TryGetValue(id, out var i) ? i : -1;
    public int SlotIndex(string id) => slotIndex.TryGetValue(id, out var i) ? i : -1;
    public int SocketIndex(string id) => socketIndex.TryGetValue(id, out var i) ? i : -1;
}

public sealed class PNVisualPartDefinition : EveIIdentified
{
    public EveId Id { get; } public string Image { get; } public float PivotX { get; } public float PivotY { get; } public PNVisualTint Tint { get; }
    public IReadOnlyDictionary<string, string> Variants { get; }
    public PNVisualPartDefinition(EveId id, string image, float pivotX, float pivotY, PNVisualTint tint, IDictionary<string, string> variants)
    {
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
    public PNVisualCurve(float[] times, float[] values, PNVisualEase ease, bool startsFromCurrent)
    {
        if (times == null || values == null || times.Length != values.Length || times.Length == 0 || times.Length > PNVisualLimits.MaxKeys) throw new FormatException("curve needs 1.." + PNVisualLimits.MaxKeys + " keys.");
        for (int i = 0; i < times.Length; i++)
        {
            if (!PNVisualLimits.Finite(times[i]) || times[i] < 0f || times[i] > PNVisualLimits.MaxDuration) throw new FormatException("key time out of range.");
            if (i > 0 && times[i] <= times[i - 1]) throw new FormatException("key times must increase.");
            if (!(startsFromCurrent && i == 0)) PNVisualLimits.Coordinate(values[i], "key value");
        }
        if (!Enum.IsDefined(typeof(PNVisualEase), ease)) throw new FormatException("unknown ease.");
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
    public PNVisualTrack(string bone, PNVisualTrackKind kind, PNVisualCurve curve) { Bone = PNVisualLimits.Name(bone, "track bone"); Kind = kind; Curve = curve ?? throw new ArgumentNullException(nameof(curve)); }
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
    public PNVisualMotionDefinition(EveId id, EveId rig, PNVisualMotionKind kind, PNVisualClock clock, float duration, bool loop, float blendIn, float blendOut, PNVisualChannel channel, bool noModifiers, float speedScale,
        IEnumerable<string> mask, IEnumerable<PNVisualTrack> tracks, IEnumerable<PNVisualSlotTrack> slotTracks, IEnumerable<PNVisualEvent> events, IEnumerable<string> frames, float fps)
    {
        if (!id.IsValid || !rig.IsValid) throw new FormatException("motion needs a valid id and rig.");
        if (!Enum.IsDefined(typeof(PNVisualMotionKind), kind) || !Enum.IsDefined(typeof(PNVisualClock), clock) || !Enum.IsDefined(typeof(PNVisualChannel), channel)) throw new FormatException("motion kind, clock or channel is unknown.");
        Id = id; Rig = rig; Kind = kind; Clock = clock; Loop = loop; NoModifiers = noModifiers; Channel = channel;
        Duration = PNVisualLimits.InRange(duration, .01f, PNVisualLimits.MaxDuration, "motion duration");
        BlendIn = PNVisualLimits.InRange(blendIn, 0f, PNVisualLimits.MaxBlend, "blendIn"); BlendOut = PNVisualLimits.InRange(blendOut, 0f, PNVisualLimits.MaxBlend, "blendOut");
        SpeedScale = PNVisualLimits.InRange(speedScale, 0f, 10f, "speedScale"); Fps = PNVisualLimits.InRange(fps, 0f, 120f, "fps");
        Mask = (mask ?? Enumerable.Empty<string>()).Select(x => PNVisualLimits.Name(x, "mask bone")).ToArray();
        Tracks = (tracks ?? Enumerable.Empty<PNVisualTrack>()).ToArray(); SlotTracks = (slotTracks ?? Enumerable.Empty<PNVisualSlotTrack>()).ToArray();
        Events = (events ?? Enumerable.Empty<PNVisualEvent>()).ToArray(); Frames = (frames ?? Enumerable.Empty<string>()).Select(x => PNVisualLimits.ImagePath(x, "motion " + id + " frame")).ToArray();
        if (Mask.Count > PNVisualLimits.MaxMask || Tracks.Count > PNVisualLimits.MaxTracks * 5 || SlotTracks.Count > PNVisualLimits.MaxTracks || Events.Count > PNVisualLimits.MaxEvents || Frames.Count > PNVisualLimits.MaxFrames) throw new FormatException("motion " + id + " has too many masks, tracks, events or frames.");
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
    public PNVisualAppearanceDefinition(EveId id, EveId rig, EveId motionSet, EveId build, EveId expression, IDictionary<string, PNVisualColor> colors, IDictionary<string, EveId> slots,
        IEnumerable<PNVisualFreePart> free, IEnumerable<string> hide, float blushAlpha, IDictionary<string, float[]> socketOverrides)
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
