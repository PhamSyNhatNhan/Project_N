using System;
using System.Collections.Generic;

// EN: The pose evaluator of one rig puppet (Docs/VisualPlan.md 3.4), pure C# so the standalone smoke suite checks it. It owns no Unity objects: gameplay feeds it a state, a
//     phase, an overlay, an expression and a facing; Advance(dt) takes entity-clock seconds; Evaluate() fills the output arrays (bone and slot affines in metres, y up).
//     Channels: one exclusive base motion (blended from the pose being shown), one additive overlay on a bone mask, the expression (face), and automatic modifiers (breathe, blink).
//     Nothing is allocated per frame after construction (motions compile once, on first use).
// VI: Bộ tính tư thế của một rig. Không dùng Unity. Bốn kênh: chuyển động nền, lớp chồng, biểu cảm, bộ điều chỉnh tự động.
public sealed class PNVisualAnimator
{
    private const int Kinds = PNVisualLimits.TrackKinds; // Rot, X, Y, Sx, Sy, Bend, Sway (the last two are additive, in degrees)
    private sealed class Compiled
    {
        public PNVisualMotionDefinition Motion; public int[] TrackBone, TrackKind; public PNVisualCurve[] TrackCurve; public bool[] InMask;
        public int[] SlotIndex; public PNVisualSlotTrack[] SlotTracks;
        public int[] DeformSlot, DeformPoint, DeformAxis; public PNVisualCurve[] DeformCurve;        // lattice deformation tracks: slot, control point (row * 6 + column), 0 = x / 1 = y, curve
        public int[] IkIndex; public PNVisualCurve[] IkCurve;        // the motion's own mix curves: which constraint of the rig, and the 0..1 curve
    }

    private readonly PNVisualResolvedRig rig;
    private readonly PNVisualMotionSetDefinition set;
    private readonly Func<EveEngine.EveId, PNVisualMotionDefinition> motions;
    private readonly PNVisualBuildDefinition build;
    private readonly Dictionary<PNVisualMotionDefinition, Compiled> compiled = new Dictionary<PNVisualMotionDefinition, Compiled>();
    private readonly float metersPerPixel;
    private readonly int bones, slots, sockets;
    private readonly float[] rest, baseTarget, baseFrom, baseShown, current, delta;
    private readonly float[] scaleX, scaleY;                 // draw scale of every bone (rest scale x build group x track scale)
    private readonly float[] groupX, groupY;                 // build group scale of every bone
    private readonly float[] world;                          // per bone px affine, y down: A B C D TX TY
    private readonly int blinkSlots;
    private readonly PNVisualIkConstraint[] ikCons; private readonly int[] ikFirst, ikSecond, ikTarget;                            // IK constraints (R2): bone indices, -1 = no second bone
    private readonly PNVisualDynamicsEntry[] dynEntries; private readonly int[] dynOfBone; private readonly PNVisualSpring[] springs;     // spring chains (R1): one state per entry, the entry of each bone or -1

    // ---- outputs (read after Evaluate) ----
    public int SlotCount => slots;
    public int BoneCount => bones;
    public float[] BoneAffine { get; }        // 6 floats per bone: local px (y down) -> metres (y up), mirrored when facing left
    public float[] BoneScale { get; }         // 2 floats per bone: draw scale
    public float[] BoneBend { get; }          // 2 floats per bone: bend and sway in degrees (PNVisualBend), 0 when the bone is rigid
    public float[] SlotAffine { get; }        // 6 floats per slot, the part pivot is the local origin
    public float[] SlotAlpha { get; }
    public string[] SlotVariant { get; }      // "" = the part's default image
    public float[] SocketPosition { get; }    // 2 floats per socket: metres (y up), mirrored when facing left
    // R3: the lattice offsets of every slot, DeformStride floats per slot (6 x 6 control points, x then y, art px of the picture); null when no motion of the motion set deforms anything.
    public const int DeformStride = PNVisualLimits.MaxLattice * PNVisualLimits.MaxLattice * 2;
    public float[] SlotDeform { get; }
    private readonly float[] defTarget, defFrom, defShown;

    // ---- inputs ----
    public bool FacingLeft { get; set; }
    public float RateMultiplier { get; set; } = 1f;
    public string BaseState { get; private set; } = "";
    public string OverlayState { get; private set; } = "";
    public float BaseTime { get; private set; }
    public bool BaseFinished => baseMotion != null && !baseMotion.Motion.Loop && baseMotion.Motion.Clock == PNVisualClock.Time && BaseTime >= baseMotion.Motion.Duration;
    public bool OverlayActive => overlay != null;

    private Compiled baseMotion, overlay;
    private float overlayTime, blendTime, blendDuration, blinkClock, breatheClock, phase;
    private bool blending;
    private PNVisualExpressionDefinition expression;
    private float[] exprSx, exprSy, exprOx, exprOy, exprRot; private string[] exprVariant;

    public PNVisualAnimator(PNVisualResolvedRig rig, PNVisualMotionSetDefinition set, Func<EveEngine.EveId, PNVisualMotionDefinition> motions, PNVisualBuildDefinition build)
    {
        this.rig = rig ?? throw new ArgumentNullException(nameof(rig)); this.set = set ?? throw new ArgumentNullException(nameof(set));
        this.motions = motions ?? throw new ArgumentNullException(nameof(motions)); this.build = build ?? throw new ArgumentNullException(nameof(build));
        metersPerPixel = 1f / rig.Definition.PixelsPerMeter; bones = rig.Bones.Count; slots = rig.Slots.Count; sockets = rig.Sockets.Count;
        rest = new float[bones * Kinds]; for (int i = 0; i < bones; i++) { rest[i * Kinds + 3] = 1f; rest[i * Kinds + 4] = 1f; }
        baseTarget = new float[rest.Length]; baseFrom = new float[rest.Length]; baseShown = new float[rest.Length]; current = new float[rest.Length]; delta = new float[rest.Length];
        Array.Copy(rest, baseShown, rest.Length);
        scaleX = new float[bones]; scaleY = new float[bones]; groupX = new float[bones]; groupY = new float[bones]; world = new float[bones * 6];
        for (int i = 0; i < bones; i++) build.Scale(rig.Bones[i].ScaleGroup, out groupX[i], out groupY[i]);
        BoneAffine = new float[bones * 6]; BoneScale = new float[bones * 2]; BoneBend = new float[bones * 2]; SlotAffine = new float[slots * 6]; SlotAlpha = new float[slots]; SlotVariant = new string[slots]; SocketPosition = new float[sockets * 2];
        for (int i = 0; i < slots; i++) { SlotAlpha[i] = 1f; SlotVariant[i] = ""; if (rig.Slots[i].Blink) blinkSlots++; }
        var entries = rig.Dynamics; dynEntries = new PNVisualDynamicsEntry[entries.Count]; springs = new PNVisualSpring[entries.Count]; dynOfBone = new int[bones];
        for (int i = 0; i < bones; i++) dynOfBone[i] = -1;
        for (int k = 0; k < entries.Count; k++) { dynEntries[k] = entries[k]; dynOfBone[rig.BoneIndex(entries[k].Bone)] = k; }
        bool anyDeform = false;
        foreach (var stateId in set.States.Values) { var m = motions(stateId); if (m != null && m.Deform.Count > 0) { anyDeform = true; break; } }
        if (anyDeform) { SlotDeform = new float[slots * DeformStride]; defTarget = new float[SlotDeform.Length]; defFrom = new float[SlotDeform.Length]; defShown = new float[SlotDeform.Length]; }
        var ik = rig.Ik; ikCons = new PNVisualIkConstraint[ik.Count]; ikFirst = new int[ik.Count]; ikSecond = new int[ik.Count]; ikTarget = new int[ik.Count];
        for (int k = 0; k < ik.Count; k++) { ikCons[k] = ik[k]; ikFirst[k] = rig.BoneIndex(ik[k].Bones[0]); ikSecond[k] = ik[k].Bones.Count > 1 ? rig.BoneIndex(ik[k].Bones[1]) : -1; ikTarget[k] = rig.BoneIndex(ik[k].Target); }
        exprSx = new float[slots]; exprSy = new float[slots]; exprOx = new float[slots]; exprOy = new float[slots]; exprRot = new float[slots]; exprVariant = new string[slots];
        SetExpression(null);
    }

    public bool HasState(string state) => set.States.ContainsKey(state);

    // Plays a state of the motion set. A base motion replaces the current one (blended from the pose being shown over its blendIn; a hard cut when blendIn is 0); an overlay
    // replaces the running overlay and adds on its bone mask. Playing the base state already playing does nothing unless `restart`.
    public bool Play(string state, bool restart = false)
    {
        if (!set.States.TryGetValue(state, out var id)) return false;
        var motion = motions(id); if (motion == null) throw new InvalidOperationException("Motion " + id + " is not registered.");
        var c = Compile(motion);
        if (motion.Channel == PNVisualChannel.Overlay) { overlay = c; overlayTime = 0f; OverlayState = state; return true; }
        if (baseMotion == c && !restart) return true;
        if (motion.BlendIn > 0f && baseMotion != null) { Array.Copy(baseShown, baseFrom, baseShown.Length); if (defShown != null) Array.Copy(defShown, defFrom, defShown.Length); blending = true; blendTime = 0f; blendDuration = motion.BlendIn; } else blending = false;
        // "current" keys start from the pose being shown at this instant
        Array.Copy(baseShown, current, baseShown.Length);
        baseMotion = c; BaseState = state; BaseTime = 0f; phase = 0f;
        return true;
    }

    // The puppet's own movement in the world (metres, y up; the actor feeds the change of position since the last tick, in the puppet's plane): what a spring chain is left behind by.
    // Unmirrored px (y down) inside; the sign of x follows the facing so a puppet that runs to the left trails its cloth to the right.
    public void FeedBodyMotion(float dxMetres, float dyMetres)
    {
        if (float.IsNaN(dxMetres) || float.IsInfinity(dxMetres) || float.IsNaN(dyMetres) || float.IsInfinity(dyMetres)) return;
        float ppm = 1f / metersPerPixel; bodyDx += (FacingLeft ? -dxMetres : dxMetres) * ppm; bodyDy += -dyMetres * ppm;
    }
    // A teleport or a long gap: forget the motion in flight and restart every spring from its animated pose.
    public void ResetDynamics() { bodyDx = bodyDy = 0f; pendingDt = 0f; dynamicsReset = true; }
    private float bodyDx, bodyDy, pendingDt; private bool dynamicsReset;

    public void StopOverlay() { overlay = null; OverlayState = ""; }
    public void SetPhase(float value) { phase = value < 0f ? 0f : value > 1f ? 1f : value; }

    public void SetExpression(PNVisualExpressionDefinition value)
    {
        expression = value;
        for (int i = 0; i < slots; i++) { exprSx[i] = 1f; exprSy[i] = 1f; exprOx[i] = 0f; exprOy[i] = 0f; exprRot[i] = 0f; exprVariant[i] = ""; }
        if (value == null) return;
        foreach (var s in value.Slots)
        {
            int i = rig.SlotIndex(s.Slot); if (i < 0) continue;
            exprSx[i] = s.Sx; exprSy[i] = s.Sy; exprOx[i] = s.Ox; exprOy[i] = s.Oy; exprRot[i] = s.Rot; exprVariant[i] = s.Variant;
        }
    }

    public void Advance(float dt)
    {
        if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return;
        blinkClock += dt; breatheClock += dt; pendingDt += dt;
        if (baseMotion != null)
        {
            var m = baseMotion.Motion;
            if (m.Clock == PNVisualClock.Time) { float rate = (m.SpeedScale <= 0f ? 1f : m.SpeedScale) * RateMultiplier; BaseTime += dt * (rate < 0f ? 0f : rate); if (m.Loop && m.Duration > 0f) BaseTime %= m.Duration; else if (BaseTime > m.Duration) BaseTime = m.Duration; }
            else BaseTime = phase * m.Duration;
        }
        if (blending) { blendTime += dt; if (blendTime >= blendDuration) blending = false; }
        if (overlay != null) { var m = overlay.Motion; overlayTime += dt * (m.SpeedScale <= 0f ? 1f : m.SpeedScale); if (!m.Loop && overlayTime >= m.Duration + 0f) { overlay = null; OverlayState = ""; } else if (m.Loop && m.Duration > 0f) overlayTime %= m.Duration; }
    }

    // ---- evaluation ----
    public void Evaluate()
    {
        // 1. base motion (absolute track values), blended from the previous pose
        Array.Copy(rest, baseTarget, rest.Length);
        if (baseMotion != null) EvalTracks(baseMotion, BaseTime, baseTarget, true);
        if (blending && blendDuration > 0f)
        {
            float k = blendTime / blendDuration; k = k <= 0f ? 0f : k >= 1f ? 1f : k * k * (3f - 2f * k);
            for (int i = 0; i < baseShown.Length; i++) baseShown[i] = baseFrom[i] + (baseTarget[i] - baseFrom[i]) * k;
        }
        else Array.Copy(baseTarget, baseShown, baseShown.Length);
        // 2. final values = shown base, plus overlay, plus modifiers
        var v = delta; Array.Copy(baseShown, v, v.Length);
        float weight = 0f;
        if (overlay != null)
        {
            var m = overlay.Motion; weight = 1f;
            if (m.BlendIn > 0f && overlayTime < m.BlendIn) weight = overlayTime / m.BlendIn;
            if (!m.Loop && m.BlendOut > 0f && overlayTime > m.Duration - m.BlendOut) weight = Math.Min(weight, (m.Duration - overlayTime) / m.BlendOut);
            weight = weight < 0f ? 0f : weight > 1f ? 1f : weight;
            EvalOverlay(overlay, overlayTime, v, weight);
        }
        if (SlotDeform != null) EvalDeform(weight);
        bool modifiers = baseMotion == null || !baseMotion.Motion.NoModifiers;
        if (modifiers && set.BreatheBone.Length > 0 && set.BreatheAmp > 0f)
        {
            int b = rig.BoneIndex(set.BreatheBone);
            if (b >= 0) v[b * Kinds + 2] += set.BreatheAmp * (float)Math.Sin(breatheClock * 2.0 * Math.PI / set.BreathePeriod);
        }
        // 2b. inverse kinematics: turn the bones of each constraint so the tip reaches its goal (the rotations in `v` are adjusted, the chain below is solved as usual)
        if (ikCons.Length > 0) SolveIk(v);
        // 3. scales, then the bone chain (child offset uses the parent's draw scale, never an accumulated one). A bone with a spring chain is solved the moment its animated pose is known, so its
        //    children see it already turned.
        float dynDt = pendingDt, dynBodyX = bodyDx, dynBodyY = bodyDy; bool dynReset = dynamicsReset; float dynWeight = baseMotion != null ? baseMotion.Motion.DynamicsWeight : 1f;
        if (dynEntries.Length > 0) { pendingDt = 0f; bodyDx = bodyDy = 0f; dynamicsReset = false; }
        for (int i = 0; i < bones; i++)
        {
            var bone = rig.Bones[i];
            scaleX[i] = bone.Scale * groupX[i] * v[i * Kinds + 3]; scaleY[i] = bone.Scale * groupY[i] * v[i * Kinds + 4];
            BoneScale[i * 2] = scaleX[i]; BoneScale[i * 2 + 1] = scaleY[i];
            BoneBend[i * 2] = v[i * Kinds + 5]; BoneBend[i * 2 + 1] = v[i * Kinds + 6];
            int p = rig.ParentIndex[i];
            float lx = bone.X + v[i * Kinds + 1], ly = bone.Y + v[i * Kinds + 2];
            if (p >= 0) { lx = bone.X * scaleX[p] + v[i * Kinds + 1]; ly = bone.Y * scaleY[p] + v[i * Kinds + 2]; }
            else { lx = bone.X * scaleX[i] + v[i * Kinds + 1]; ly = bone.Y * scaleY[i] + v[i * Kinds + 2]; }
            float rot = v[i * Kinds];
            ComputeWorld(i, p, lx, ly, rot);
            int k = dynOfBone[i];
            if (k >= 0)
            {
                var e = dynEntries[k]; int o = i * 6; float tipX = e.TipX * scaleX[i], tipY = e.TipY * scaleY[i];
                float px = world[o + 4], py = world[o + 5], tx = world[o] * tipX + world[o + 2] * tipY + px, ty = world[o + 1] * tipX + world[o + 3] * tipY + py;
                float theta = PNVisualDynamics.Advance(ref springs[k], e, px, py, tx, ty, dynDt, dynBodyX, dynBodyY, dynReset) * dynWeight;
                if (theta != 0f)
                {
                    if (e.Channel == PNVisualDynamicsChannel.Rot) ComputeWorld(i, p, lx, ly, rot + theta);
                    else BoneBend[i * 2] += 2f * theta;                       // the chord of a curve leaves the tangent by half its total turn
                }
            }
            Output(world, i * 6, BoneAffine, i * 6);
        }
        // 4. slots: face (expression, motion slot tracks, blink), then the affine of every slot
        float blink = modifiers && expressionBlink() && set.BlinkEvery > 0f ? BlinkAmount() : 0f;
        for (int i = 0; i < slots; i++)
        {
            var slot = rig.Slots[i]; int b = rig.BoneIndex(slot.Bone);
            string variant = exprVariant[i]; float alpha = 1f, sy = exprSy[i], sx = exprSx[i], ox = exprOx[i], oy = exprOy[i], rot = exprRot[i];
            if (baseMotion != null) ApplySlotTracks(baseMotion, BaseTime, i, ref variant, ref alpha, ref sy, 1f);
            if (overlay != null) ApplySlotTracks(overlay, overlayTime, i, ref variant, ref alpha, ref sy, weight);
            if (slot.Blink && blink > 0f && variant != "closed") sy *= 1f - .92f * blink;
            SlotVariant[i] = variant; SlotAlpha[i] = alpha;
            // slot matrix = bone world * T(slot at * bone scale + expression offset) * R(expression rot) * S(bone scale * expression/track scale)
            float tx = slot.X * scaleX[b] + ox, ty = slot.Y * scaleY[b] + oy;
            double rr = rot * Math.PI / 180.0; float cs = (float)Math.Cos(rr), sn = (float)Math.Sin(rr);
            float la = cs * scaleX[b] * sx, lb = sn * scaleX[b] * sx, lc = -sn * scaleY[b] * sy, ld = cs * scaleY[b] * sy;
            int o = b * 6; float wa = world[o], wb = world[o + 1], wc = world[o + 2], wd = world[o + 3], wtx = world[o + 4], wty = world[o + 5];
            slotTmp[0] = wa * la + wc * lb; slotTmp[1] = wb * la + wd * lb; slotTmp[2] = wa * lc + wc * ld; slotTmp[3] = wb * lc + wd * ld;
            slotTmp[4] = wa * tx + wc * ty + wtx; slotTmp[5] = wb * tx + wd * ty + wty;
            Output(slotTmp, 0, SlotAffine, i * 6);
        }
        for (int i = 0; i < sockets; i++)
        {
            var s = rig.Sockets[i]; int b = rig.BoneIndex(s.Bone); int o = b * 6;
            float x = s.X * scaleX[b], y = s.Y * scaleY[b];
            float px = world[o] * x + world[o + 2] * y + world[o + 4], py = world[o + 1] * x + world[o + 3] * y + world[o + 5];
            SocketPosition[i * 2] = (FacingLeft ? -px : px) * metersPerPixel; SocketPosition[i * 2 + 1] = -py * metersPerPixel;
        }
    }
    private readonly float[] slotTmp = new float[6];

    // The lattice offsets of every slot: the base motion's points (absolute, blended from the lattice being shown like the bones), plus the overlay's, weighted.
    private void EvalDeform(float overlayWeight)
    {
        Array.Clear(defTarget, 0, defTarget.Length);
        if (baseMotion != null) for (int i = 0; i < baseMotion.DeformCurve.Length; i++) defTarget[baseMotion.DeformSlot[i] * DeformStride + baseMotion.DeformPoint[i] * 2 + baseMotion.DeformAxis[i]] = Sample(baseMotion.DeformCurve[i], BaseTime, 0f);
        if (blending && blendDuration > 0f)
        {
            float k = blendTime / blendDuration; k = k <= 0f ? 0f : k >= 1f ? 1f : k * k * (3f - 2f * k);
            for (int i = 0; i < defShown.Length; i++) defShown[i] = defFrom[i] + (defTarget[i] - defFrom[i]) * k;
        }
        else Array.Copy(defTarget, defShown, defShown.Length);
        Array.Copy(defShown, SlotDeform, defShown.Length);
        if (overlay != null && overlayWeight > 0f)
            for (int i = 0; i < overlay.DeformCurve.Length; i++) SlotDeform[overlay.DeformSlot[i] * DeformStride + overlay.DeformPoint[i] * 2 + overlay.DeformAxis[i]] += Sample(overlay.DeformCurve[i], overlayTime, 0f) * overlayWeight;
    }

    // The pose with the animation only (no IK, no springs): scales and world affines of every bone from the rotations and offsets in v.
    private void PreWorld(float[] v)
    {
        for (int i = 0; i < bones; i++)
        {
            var bone = rig.Bones[i]; int p = rig.ParentIndex[i];
            scaleX[i] = bone.Scale * groupX[i] * v[i * Kinds + 3]; scaleY[i] = bone.Scale * groupY[i] * v[i * Kinds + 4];
            float lx = p >= 0 ? bone.X * scaleX[p] + v[i * Kinds + 1] : bone.X * scaleX[i] + v[i * Kinds + 1], ly = p >= 0 ? bone.Y * scaleY[p] + v[i * Kinds + 2] : bone.Y * scaleY[i] + v[i * Kinds + 2];
            ComputeWorld(i, p, lx, ly, v[i * Kinds]);
        }
    }

    private void SolveIk(float[] v)
    {
        PreWorld(v);
        for (int c = 0; c < ikCons.Length; c++)
        {
            float mix = ikCons[c].Mix;
            if (baseMotion != null) for (int k = 0; k < baseMotion.IkIndex.Length; k++) if (baseMotion.IkIndex[k] == c) { float m = Sample(baseMotion.IkCurve[k], BaseTime, 1f); mix = m < 0f ? 0f : m > 1f ? 1f : m; }
            if (mix <= 0f) continue;
            int a = ikFirst[c], b = ikSecond[c], g = ikTarget[c], last = b >= 0 ? b : a, o = last * 6;
            float tipX = ikCons[c].TipX * scaleX[last], tipY = ikCons[c].TipY * scaleY[last];
            float tx = world[o] * tipX + world[o + 2] * tipY + world[o + 4], ty = world[o + 1] * tipX + world[o + 3] * tipY + world[o + 5];
            float gx = world[g * 6 + 4], gy = world[g * 6 + 5], ax = world[a * 6 + 4], ay = world[a * 6 + 5];
            if (b >= 0)
            {
                if (!PNVisualIk.TwoBone(ax, ay, world[b * 6 + 4], world[b * 6 + 5], tx, ty, gx, gy, ikCons[c].BendPositive, out float turnA, out float turnB)) continue;
                v[a * Kinds] += turnA * mix; v[b * Kinds] += turnB * mix;
            }
            else v[a * Kinds] += PNVisualIk.OneBone(ax, ay, tx, ty, gx, gy) * mix;
            if (c + 1 < ikCons.Length) PreWorld(v);                  // the next constraint sees this one's result
        }
    }

    // local = T(lx, ly) * R(rot): x' = cos x - sin y + lx ; y' = sin x + cos y + ly ; world = parent world * local
    private void ComputeWorld(int i, int p, float lx, float ly, float rotDegrees)
    {
        double rad = rotDegrees * Math.PI / 180.0; float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
        float a = cos, b2 = sin, c = -sin, d = cos;
        if (p < 0) { world[i * 6] = a; world[i * 6 + 1] = b2; world[i * 6 + 2] = c; world[i * 6 + 3] = d; world[i * 6 + 4] = lx; world[i * 6 + 5] = ly; return; }
        int o = p * 6; float pa = world[o], pb = world[o + 1], pc = world[o + 2], pd = world[o + 3], ptx = world[o + 4], pty = world[o + 5];
        world[i * 6] = pa * a + pc * b2; world[i * 6 + 1] = pb * a + pd * b2; world[i * 6 + 2] = pa * c + pc * d; world[i * 6 + 3] = pb * c + pd * d;
        world[i * 6 + 4] = pa * lx + pc * ly + ptx; world[i * 6 + 5] = pb * lx + pd * ly + pty;
    }

    private bool expressionBlink() => expression == null || expression.Blink;

    // Triangular blink: closes over the first half of BlinkDuration and opens over the second, once every BlinkEvery seconds (at the end of each period).
    public float BlinkAmount()
    {
        if (set.BlinkEvery <= 0f) return 0f;
        float u = blinkClock % set.BlinkEvery, start = set.BlinkEvery - set.BlinkDuration;
        if (u < start) return 0f;
        float h = set.BlinkDuration * .5f, x = u - start; return x < h ? x / h : (set.BlinkDuration - x) / h;
    }

    // px (y down) affine -> metres (y up), mirrored on x when facing left.
    private void Output(float[] source, int s, float[] target, int t)
    {
        float m = metersPerPixel, f = FacingLeft ? -1f : 1f;
        target[t] = source[s] * m * f; target[t + 1] = -source[s + 1] * m; target[t + 2] = source[s + 2] * m * f; target[t + 3] = -source[s + 3] * m;
        target[t + 4] = source[s + 4] * m * f; target[t + 5] = -source[s + 5] * m;
    }

    // ---- compilation and curve evaluation ----
    private Compiled Compile(PNVisualMotionDefinition motion)
    {
        if (compiled.TryGetValue(motion, out var c)) return c;
        c = new Compiled { Motion = motion, InMask = new bool[bones] };
        var tb = new List<int>(); var tk = new List<int>(); var tc = new List<PNVisualCurve>();
        foreach (var track in motion.Tracks) { int b = rig.BoneIndex(track.Bone); if (b < 0) continue; tb.Add(b); tk.Add((int)track.Kind); tc.Add(track.Curve); }
        c.TrackBone = tb.ToArray(); c.TrackKind = tk.ToArray(); c.TrackCurve = tc.ToArray();
        foreach (var name in motion.Mask) { int b = rig.BoneIndex(name); if (b >= 0) c.InMask[b] = true; }
        var si = new List<int>(); var st = new List<PNVisualSlotTrack>();
        foreach (var track in motion.SlotTracks) { int s = rig.SlotIndex(track.Slot); if (s < 0) continue; si.Add(s); st.Add(track); }
        c.SlotIndex = si.ToArray(); c.SlotTracks = st.ToArray();
        var ds = new List<int>(); var dp = new List<int>(); var da = new List<int>(); var dc = new List<PNVisualCurve>();
        foreach (var track in motion.Deform)
        {
            int slot = rig.SlotIndex(track.Slot); if (slot < 0) continue;
            if (track.X != null) { ds.Add(slot); dp.Add(track.Row * PNVisualLimits.MaxLattice + track.Col); da.Add(0); dc.Add(track.X); }
            if (track.Y != null) { ds.Add(slot); dp.Add(track.Row * PNVisualLimits.MaxLattice + track.Col); da.Add(1); dc.Add(track.Y); }
        }
        c.DeformSlot = ds.ToArray(); c.DeformPoint = dp.ToArray(); c.DeformAxis = da.ToArray(); c.DeformCurve = dc.ToArray();
        var ii = new List<int>(); var ic = new List<PNVisualCurve>();
        foreach (var mix in motion.IkMix) { int k = rig.IkIndex(mix.Id); if (k < 0) continue; ii.Add(k); ic.Add(mix.Curve); }
        c.IkIndex = ii.ToArray(); c.IkCurve = ic.ToArray();
        compiled.Add(motion, c); return c;
    }

    private void EvalTracks(Compiled c, float time, float[] target, bool absolute)
    {
        for (int i = 0; i < c.TrackCurve.Length; i++)
        {
            int index = c.TrackBone[i] * Kinds + c.TrackKind[i];
            target[index] = Sample(c.TrackCurve[i], time, current[index]);
        }
    }

    private void EvalOverlay(Compiled c, float time, float[] v, float weight)
    {
        for (int i = 0; i < c.TrackCurve.Length; i++)
        {
            int b = c.TrackBone[i]; if (!c.InMask[b]) continue;
            int index = b * Kinds + c.TrackKind[i]; float value = Sample(c.TrackCurve[i], time, 0f);
            if (c.TrackKind[i] == 3 || c.TrackKind[i] == 4) v[index] *= 1f + (value - 1f) * weight; else v[index] += value * weight;
        }
    }

    private void ApplySlotTracks(Compiled c, float time, int slot, ref string variant, ref float alpha, ref float sy, float weight)
    {
        for (int i = 0; i < c.SlotIndex.Length; i++)
        {
            if (c.SlotIndex[i] != slot) continue;
            var t = c.SlotTracks[i];
            if (t.Times.Length > 0 && weight >= .5f) { string chosen = null; for (int k = 0; k < t.Times.Length; k++) if (time >= t.Times[k]) chosen = t.Variants[k]; if (chosen != null) variant = chosen; }
            if (t.Alpha != null) alpha *= 1f + (Sample(t.Alpha, time, 1f) - 1f) * weight;
            if (t.Sy != null) sy *= 1f + (Sample(t.Sy, time, 1f) - 1f) * weight;
        }
    }

    // One bezier segment (docs/VisualCurveHandlesPlan.md 2.3): control points (cx1, a + dy1) and (cx2, b + dy2) between (0, a) and (1, b) in segment time. The time curve x(u) is solved for u = k with
    // Newton from u = k, at most six steps, keeping a bracket and bisecting when a step leaves it or the slope vanishes; the value is then y(u). The algorithm is part of the content contract (the editor
    // ports it line by line): changing it changes every authored motion, so it counts as a format change.
    public static float Bezier(float[] handles, int o, float a, float b, float k)
    {
        if (k <= 0f) return a; if (k >= 1f) return b;
        float cx1 = handles[o], cx2 = handles[o + 2], p1 = a + handles[o + 1], p2 = b + handles[o + 3];
        float ca = 3f * cx1 - 3f * cx2 + 1f, cb = 3f * cx2 - 6f * cx1, cc = 3f * cx1;           // x(u) = ((ca u + cb) u + cc) u
        float u = k, lowU = 0f, highU = 1f;
        for (int i = 0; i < 6; i++)
        {
            float x = ((ca * u + cb) * u + cc) * u - k;
            if (x < 1e-6f && x > -1e-6f) break;
            if (x > 0f) highU = u; else lowU = u;
            float slope = (3f * ca * u + 2f * cb) * u + cc;
            float next = (slope > 1e-6f || slope < -1e-6f) ? u - x / slope : -1f;
            u = next > lowU && next < highU ? next : (lowU + highU) * .5f;
        }
        float w = 1f - u;
        return w * w * w * a + 3f * w * w * u * p1 + 3f * w * u * u * p2 + u * u * u * b;
    }

    public static float Sample(PNVisualCurve curve, float time, float current)
    {
        if (curve.IsWave)
        {
            double s = Math.Sin(2.0 * Math.PI * curve.Freq * time + curve.Phase);
            return curve.Offset + curve.Amp * (float)(curve.Abs ? Math.Abs(s) : s);
        }
        var t = curve.Times; var v = curve.Values; int n = t.Length;
        float first = curve.StartsFromCurrent ? current : v[0];
        if (n == 1 || time <= t[0]) return first;
        if (time >= t[n - 1]) return v[n - 1];
        int hi = 1; while (hi < n - 1 && t[hi] < time) hi++;
        int lo = hi - 1; float a = lo == 0 ? first : v[lo], b = v[hi], k = (time - t[lo]) / (t[hi] - t[lo]);
        if (curve.HasHandle != null && curve.HasHandle[lo]) return Bezier(curve.Handles, lo * 4, a, b, k);
        switch (curve.Ease)
        {
            case PNVisualEase.Step: return a;
            case PNVisualEase.Smooth: k = k * k * (3f - 2f * k); break;
        }
        return a + (b - a) * k;
    }
}
