#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// EN: Screen 1 of the flow: the ring of mode gates (Docs/ModeMenuPlan.md, M3). Up to five gates show at once; the ring turns without ends by
//     dragging, wheel, arrows / A D, gamepad d-pad, or a click on a side gate; picking the front gate (click, Enter / Space, gamepad A) calls
//     `onPick`. The shared backdrop behind it (orbits, star, aura tinted by the front gate) comes from PNMenuBackdrop.
// VI: Màn 1: vòng cổng chế độ (tối đa 5 thẻ, quay không có đầu cuối) với hai quỹ đạo nghiêng và sao chạy phía sau.
internal sealed class PNModeScreen : IPNMenuScreen
{
    private readonly IReadOnlyList<PNListedMode> modes;
    private readonly Action<PNListedMode> onPick;
    private readonly VisualElement root, ringHost;
    private readonly List<PNGateView> gates = new List<PNGateView>();
    private readonly List<Texture2D> loaded = new List<Texture2D>();
    private readonly int[] mixed = new int[3];
    private readonly int[] order, zs;
    private readonly int[][] accents;
    private readonly VisualElement[] items;
    private InputAction left, right, confirm;
    private float pos;
    private int target;
    private bool dragging, disposed;
    private Vector2 downPoint, lastPoint; private float downPos, lastTime, velocity, moved;
    private Color tint = Color.white;
    private Rect[] hit;

    public PNModeScreen(IReadOnlyList<PNListedMode> modes, Action<PNListedMode> onPick, int startIndex = 0)
    {
        if (modes == null || modes.Count == 0) throw new ArgumentException("At least one mode is needed.");
        this.modes = modes; this.onPick = onPick;
        root = new VisualElement { name = "pn-mode-screen", pickingMode = PickingMode.Position };
        Stretch(root);
        ringHost = new VisualElement { name = "pn-mode-ring", pickingMode = PickingMode.Ignore }; Stretch(ringHost); root.Add(ringHost);
        order = new int[modes.Count]; zs = new int[modes.Count]; accents = new int[modes.Count][]; items = new[] { ringHost };
        for (int i = 0; i < modes.Count; i++)
        {
            var listing = modes[i].Listing; Texture2D art = null;
            if (!string.IsNullOrEmpty(listing.Art)) { art = Resources.Load<Texture2D>(listing.Art); if (art != null) loaded.Add(art); }
            var gate = new PNGateView(listing.Name, listing.AccentRgb, art); gate.Localize(modes[i].Id.ToString(),listing.Name); gate.name = "pn-gate-" + modes[i].Id.Path; gates.Add(gate); ringHost.Add(gate);
            order[i] = i; accents[i] = listing.AccentRgb;
        }
        target = PNMenuMath.Mod(startIndex, modes.Count); pos = target;
        root.RegisterCallback<PointerDownEvent>(OnDown); root.RegisterCallback<PointerMoveEvent>(OnMove);
        root.RegisterCallback<PointerUpEvent>(OnUp); root.RegisterCallback<PointerCancelEvent>(_ => dragging = false);
        root.RegisterCallback<WheelEvent>(OnWheel);
        hit = new Rect[modes.Count];
    }

    private static void Stretch(VisualElement e) { e.style.position = Position.Absolute; e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0; }
    public VisualElement Root => root;
    // The whole ring fades and rises with the flow's item motion; the gates inside keep their own layout transforms.
    public IReadOnlyList<VisualElement> Items => items;
    public bool UsesBackdrop => true;
    public Color Tint => tint;
    public bool HandleBack() => false;
    public int FrontIndex => PNMenuMath.Mod(target, modes.Count);
    public PNListedMode Front => modes[FrontIndex];

    public void Entered()
    {
        if (left != null) return;
        left = new InputAction("Mode left", InputActionType.Button); left.AddBinding("<Keyboard>/leftArrow"); left.AddBinding("<Keyboard>/a"); left.AddBinding("<Gamepad>/dpad/left");
        right = new InputAction("Mode right", InputActionType.Button); right.AddBinding("<Keyboard>/rightArrow"); right.AddBinding("<Keyboard>/d"); right.AddBinding("<Gamepad>/dpad/right");
        confirm = new InputAction("Mode confirm", InputActionType.Button); confirm.AddBinding("<Keyboard>/enter"); confirm.AddBinding("<Keyboard>/space"); confirm.AddBinding("<Gamepad>/buttonSouth");
        left.performed += _ => Turn(-1); right.performed += _ => Turn(1); confirm.performed += _ => Pick();
        left.Enable(); right.Enable(); confirm.Enable();
    }

    private static float Rubber(float d) => Mathf.Sign(d) * .35f * (1f - Mathf.Exp(-Mathf.Abs(d) * 1.2f));
    public void Turn(int steps) { if (!disposed && modes.Count > 1) target = Mathf.RoundToInt(pos) + steps; }
    public void Pick() { if (!disposed && Mathf.Abs(pos - target) < .35f) onPick?.Invoke(Front); }

    private void OnDown(PointerDownEvent e)
    {
        dragging = true; moved = 0; downPoint = lastPoint = (Vector2)e.position; downPos = pos; velocity = 0; lastTime = Time.unscaledTime;
        root.CapturePointer(e.pointerId);
    }

    private void OnMove(PointerMoveEvent e)
    {
        if (!dragging) return;
        float spacing = PNMenuMath.DragSpacing(gates[0].Width);
        var p = (Vector2)e.position; float dx = p.x - downPoint.x; moved = Mathf.Max(moved, Mathf.Abs(dx));
        if (moved > 6f)
        {
            // One mode only: nothing to turn to, so the gate follows the finger with resistance and settles back instead of wrapping round.
            pos = modes.Count == 1 ? downPos - Rubber(dx / spacing) : downPos - dx / spacing;
            float now = Time.unscaledTime, dt = Mathf.Max(.001f, now - lastTime);
            velocity = (lastPoint.x - p.x) / dt / spacing; lastPoint = p; lastTime = now;
        }
    }

    private void OnUp(PointerUpEvent e)
    {
        if (!dragging) return; dragging = false; if (root.HasPointerCapture(e.pointerId)) root.ReleasePointer(e.pointerId);
        if (moved < 6f)
        {
            var p = root.WorldToLocal((Vector2)e.position); int hitIndex = -1; float bestZ = -1;
            for (int i = 0; i < hit.Length; i++)
            {
                var slot = PNMenuMath.Layout(i, pos, modes.Count);
                if (slot.Visibility > .5f && hit[i].Contains(p) && slot.Z > bestZ) { bestZ = slot.Z; hitIndex = i; }
            }
            if (hitIndex >= 0)
            {
                int turn = PNMenuMath.TurnFor(hitIndex, Mathf.RoundToInt(target), modes.Count);
                if (turn == 0) onPick?.Invoke(modes[hitIndex]); else target = Mathf.RoundToInt(target) + turn;
            }
            return;
        }
        target = modes.Count == 1 ? Mathf.RoundToInt(target) : PNMenuMath.Fling(pos, velocity);
    }

    private void OnWheel(WheelEvent e)
    {
        float amount = Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) ? e.delta.x : e.delta.y;
        if (Mathf.Abs(amount) < .01f || modes.Count == 1) return;
        target = Mathf.RoundToInt(target) + (amount > 0 ? 1 : -1); e.StopPropagation();
    }

    public void Tick(float dt)
    {
        if (disposed) return;
        if (!dragging) pos = PNMenuMath.Settle(pos, target, dt);
        float w = root.resolvedStyle.width, h = root.resolvedStyle.height;
        if (!float.IsFinite(w) || !float.IsFinite(h) || w < 2 || h < 2) return;
        float gateW = Mathf.Max(90f, Mathf.Min(h * .5f / 2.2f, w * .2f)); // the demo's width: about a quarter of the stage height, at most a fifth of its width
        float cx = w * .5f, cy = h * .54f, rise = h * PNMenuMath.RiseOfStage;
        int n = modes.Count;
        for (int i = 0; i < n; i++)
        {
            var gate = gates[i]; var slot = PNMenuMath.Layout(i, pos, n);
            if (Mathf.Abs(gate.Width - gateW) > .5f) gate.SetWidth(gateW);
            float x = cx + slot.X * gateW, y = cy - slot.Rise * rise;
            gate.style.left = cx - gateW * .5f; gate.style.top = cy - gate.Height * .5f;
            gate.style.translate = new Translate(slot.X * gateW, -slot.Rise * rise);
            gate.style.scale = new Scale(new Vector2(slot.Scale, slot.Scale));
            gate.style.opacity = slot.Opacity; gate.style.display = slot.Visibility > .01f ? DisplayStyle.Flex : DisplayStyle.None;
            gate.SetFront(slot.Front);
            float sw = gateW * slot.Scale, sh = gate.Height * slot.Scale;
            hit[i] = new Rect(x - sw * .5f, y - sh * .5f, sw, sh);
        }
        // Painting order: far gates first. Re-sorted only when the order changes.
        for (int i = 0; i < n; i++) zs[i] = PNMenuMath.Layout(i, pos, n).Z;
        for (int i = 1; i < n; i++) { int v = order[i], j = i - 1; while (j >= 0 && zs[order[j]] > zs[v]) { order[j + 1] = order[j]; j--; } order[j + 1] = v; }
        for (int k = 0; k < n; k++) if (ringHost[k] != gates[order[k]]) gates[order[k]].BringToFront();
        // The aura follows the nearest two accents.
        float q = ((pos % n) + n) % n; int lo = Mathf.FloorToInt(q), hi = (lo + 1) % n;
        PNMenuMath.Mix(accents[lo], accents[hi], q - lo, mixed);
        tint = new Color(mixed[0] / 255f, mixed[1] / 255f, mixed[2] / 255f, 1f);
    }

    // The orbits and the travelling star belong to the shared backdrop (PNMenuBackdrop); this screen only draws the ring.
    public void Animate(bool entering, float elapsed) { }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var action in new[] { left, right, confirm }) { action?.Disable(); action?.Dispose(); }
        left = right = confirm = null;
        root.RemoveFromHierarchy();
    }
}
#endif
