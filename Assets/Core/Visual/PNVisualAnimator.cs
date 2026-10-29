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
        if (motion.BlendIn > 0f && baseMotion != null) { Array.Copy(baseShown, baseFrom, baseShown.Length); blending = true; blendTime = 0f; blendDuration = motion.BlendIn; } else blending = false;
        // "current" keys start from the pose being shown at this instant
        Array.Copy(baseShown, current, baseShown.Length);
        baseMotion = c; BaseState = state; BaseTime = 0f; phase = 0f;
        return true;
    }

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
        blinkClock += dt; breatheClock += dt;
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
        bool modifiers = baseMotion == null || !baseMotion.Motion.NoModifiers;
        if (modifiers && set.BreatheBone.Length > 0 && set.BreatheAmp > 0f)
        {
            int b = rig.BoneIndex(set.BreatheBone);
            if (b >= 0) v[b * Kinds + 2] += set.BreatheAmp * (float)Math.Sin(breatheClock * 2.0 * Math.PI / set.BreathePeriod);
        }
        // 3. scales, then the bone chain (child offset uses the parent's draw scale, never an accumulated one)
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
            double rad = v[i * Kinds] * Math.PI / 180.0; float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
            // local = T(lx, ly) * R(rot): x' = cos x - sin y + lx ; y' = sin x + cos y + ly
            float a = cos, b2 = sin, c = -sin, d = cos;
            if (p < 0) { world[i * 6] = a; world[i * 6 + 1] = b2; world[i * 6 + 2] = c; world[i * 6 + 3] = d; world[i * 6 + 4] = lx; world[i * 6 + 5] = ly; }
            else
            {
                int o = p * 6; float pa = world[o], pb = world[o + 1], pc = world[o + 2], pd = world[o + 3], ptx = world[o + 4], pty = world[o + 5];
                world[i * 6] = pa * a + pc * b2; world[i * 6 + 1] = pb * a + pd * b2; world[i * 6 + 2] = pa * c + pc * d; world[i * 6 + 3] = pb * c + pd * d;
                world[i * 6 + 4] = pa * lx + pc * ly + ptx; world[i * 6 + 5] = pb * lx + pd * ly + pty;
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
        switch (curve.Ease)
        {
            case PNVisualEase.Step: return a;
            case PNVisualEase.Smooth: k = k * k * (3f - 2f * k); break;
        }
        return a + (b - a) * k;
    }
}
