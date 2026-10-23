#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// EN: A dial layer of the character screen's background: feathered arcs (each arc is a run of 4-5 degree segments whose alpha fades in and out over
//     six segments at both ends, like the pause menu's dial). Drawn once per size, turned by style.rotate.
// VI: Một lớp cung chia độ ở nền màn nhân vật: các cung mờ dần hai đầu như dial của pause; vẽ một lần, xoay bằng style.
internal sealed class PNArcLayer : VisualElement
{
    private readonly float[][] arcs; // radius (in a 600 box), from degrees, segments, step degrees, alpha
    public PNArcLayer(float[][] arcs)
    { this.arcs = arcs; pickingMode = PickingMode.Ignore; generateVisualContent += Draw; style.position = Position.Absolute; }
    private void Draw(MeshGenerationContext context)
    {
        var p = context.painter2D; var c = contentRect.center; float f = contentRect.width / 600f;
        p.lineWidth = 1.2f; p.lineCap = LineCap.Round;
        foreach (var a in arcs)
        {
            int segments = (int)a[2];
            for (int k = 0; k < segments; k++)
            {
                float fade = Mathf.Min(1f, Mathf.Min(k + 1, segments - k) / 6f);
                p.strokeColor = new Color(1, 1, 1, a[4] * fade);
                p.BeginPath(); p.Arc(c, a[0] * f, Angle.Degrees(a[1] + k * a[3]), Angle.Degrees(a[1] + (k + 1) * a[3])); p.Stroke();
            }
        }
    }
}

// EN: Screen 3 of the flow (Docs/ModeMenuPlan.md, M7): a scrollable row of round character buttons on top (only characters the player can use),
//     the character picture in the middle, the team on the left, name / description / divider / base stats on the right, "Tiếp tục" at the
//     bottom right. It draws its own background (dial arcs in two counter-rotating layers, rising motes tinted by the character); the shared
//     decoration fades away. Used from the Hall flow and, alone, from the lobby to change the character.
// VI: Màn 3: hàng tròn nhân vật kéo được, ảnh ở giữa, đội bên trái, thông tin và chỉ số bên phải, nút Tiếp tục; tự dựng nền.
internal sealed class PNCharacterScreen : IPNMenuScreen
{
    private sealed class Mote { internal VisualElement Element; internal float X, Y, Speed, Size, Phase; }
    private readonly IReadOnlyList<PNPlayableEntry> entries;
    private readonly Action<PNPlayableEntry> onContinue;
    private readonly Func<IReadOnlyList<KeyValuePair<string, string>>> team;
    private readonly VisualElement root, aura, dial, motes, strip, track, portrait, portraitInner, teamColumn, info, statsBox;
    private readonly PNArcLayer layerA, layerB;
    private readonly Button continueButton;
    private readonly Label nameLabel, descriptionLabel, emptyLabel;
    private readonly Texture2D light;
    private Texture2D feather;
    private readonly List<VisualElement> items = new List<VisualElement>();
    private readonly List<Button> circles = new List<Button>();
    private readonly List<Mote> moteList = new List<Mote>();
    private readonly System.Random random = new System.Random();
    private int index = -1;
    private Color tint = new Color(.8f, .8f, .8f, 1f);
    private float time, stripX, stripFrom, stripMoved, teamTimer;
    private bool stripDown, disposed;
    private string teamSignature = "?";
    private IVisualElementScheduledItem fade;

    public PNCharacterScreen(IReadOnlyList<PNPlayableEntry> entries, string lastCharacter, Func<IReadOnlyList<KeyValuePair<string, string>>> team, Action<PNPlayableEntry> onContinue)
    {
        this.entries = entries ?? throw new ArgumentNullException(nameof(entries)); this.team = team; this.onContinue = onContinue;
        root = new VisualElement { name = "pn-character-screen", pickingMode = PickingMode.Ignore }; Stretch(root);
        light = PNPauseArtwork.Gradient(false);
        aura = Absolute("pn-char-aura"); aura.style.left = Length.Percent(50); aura.style.top = Length.Percent(54); aura.style.width = 1000; aura.style.height = 1000; aura.style.marginLeft = -500; aura.style.marginTop = -500;
        aura.style.backgroundImage = light; root.Add(aura);
        dial = Absolute("pn-char-dial"); dial.style.left = Length.Percent(50); dial.style.top = Length.Percent(52); root.Add(dial);
        layerA = new PNArcLayer(new[] { new float[] { 248, -110, 48, 5, .42f }, new float[] { 205, 40, 36, 5, .28f }, new float[] { 168, -30, 28, 5, .24f } });
        layerB = new PNArcLayer(new[] { new float[] { 228, 60, 40, 5, .3f }, new float[] { 266, -150, 34, 5, .22f }, new float[] { 312, 20, 26, 4, .16f } });
        foreach (var layer in new[] { layerA, layerB }) { layer.style.left = 0; layer.style.top = 0; layer.style.right = 0; layer.style.bottom = 0; dial.Add(layer); }
        motes = Absolute("pn-char-motes"); Stretch(motes); motes.pickingMode = PickingMode.Ignore; root.Add(motes);
        for (int i = 0; i < 18; i++)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore }; e.style.position = Position.Absolute; e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = 8;
            motes.Add(e);
            moteList.Add(new Mote { Element = e, X = (float)random.NextDouble(), Y = (float)random.NextDouble(), Speed = .012f + (float)random.NextDouble() * .022f, Size = 1.6f + (float)random.NextDouble() * 2.4f, Phase = (float)random.NextDouble() * 6f });
        }

        // top row
        strip = Absolute("pn-char-strip"); strip.style.top = Length.Percent(5); strip.style.left = Length.Percent(50); strip.style.height = 92; strip.style.overflow = Overflow.Hidden; strip.pickingMode = PickingMode.Position;
        track = new VisualElement { name = "pn-char-track", pickingMode = PickingMode.Ignore }; track.style.flexDirection = FlexDirection.Row; track.style.position = Position.Absolute; track.style.top = 14; track.style.left = StripPad; strip.Add(track);
        for (int i = 0; i < entries.Count; i++)
        {
            int captured = i; var entry = entries[i];
            var button = new Button(() => { if (stripMoved <= 6f) Select(captured, true); }) { name = "pn-char-" + entry.Settings.Name, text = entry.Settings.Name, focusable = false };
            PNLocalizationUI.Content(button,entry.Id,"character","name",entry.Settings.Name);
            button.style.width = 64; button.style.height = 64; button.style.marginRight = 18; button.style.fontSize = 13; button.style.paddingLeft = button.style.paddingRight = 2;
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius = button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 32;
            button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 1.5f;
            button.style.unityTextAlign = TextAnchor.MiddleCenter; button.style.whiteSpace = WhiteSpace.NoWrap; button.style.overflow = Overflow.Hidden;
            track.Add(button); circles.Add(button);
        }
        strip.RegisterCallback<PointerDownEvent>(e => { stripDown = true; stripMoved = 0; stripFrom = e.localPosition.x; strip.CapturePointer(e.pointerId); stripAnchor = stripX; });
        strip.RegisterCallback<PointerMoveEvent>(e => { if (!stripDown) return; float dx = e.localPosition.x - stripFrom; stripMoved = Mathf.Max(stripMoved, Mathf.Abs(dx)); if (stripMoved > 6f) { stripX = ClampStrip(stripAnchor + dx); ApplyStrip(); } });
        strip.RegisterCallback<PointerUpEvent>(e => { stripDown = false; if (strip.HasPointerCapture(e.pointerId)) strip.ReleasePointer(e.pointerId); });
        strip.RegisterCallback<WheelEvent>(e => { stripX = ClampStrip(stripX - (Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) ? e.delta.x : e.delta.y) * 20f); ApplyStrip(); e.StopPropagation(); });
        root.Add(strip); items.Add(strip);

        // picture
        portrait = Absolute("pn-char-portrait"); portrait.style.left = Length.Percent(50); portrait.style.top = Length.Percent(53);
        portraitInner = new VisualElement { pickingMode = PickingMode.Ignore }; Stretch(portraitInner); portrait.Add(portraitInner); root.Add(portrait); items.Add(portrait);

        // team (left)
        teamColumn = Absolute("pn-char-team"); teamColumn.style.left = Length.Percent(5); teamColumn.style.top = Length.Percent(26); teamColumn.style.width = 190; root.Add(teamColumn); items.Add(teamColumn);

        // info (right)
        info = Absolute("pn-char-info"); info.style.right = Length.Percent(5); info.style.top = Length.Percent(16); info.style.bottom = Length.Percent(22); info.style.width = 280;
        // Name, description and stats scroll together (a long description must stay reachable); the slim thumb sits inside the card, no track.
        var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "pn-char-scroll" };
        scroll.style.flexGrow = 1; scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden; scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
        var scroller = scroll.verticalScroller; scroller.style.width = 5; scroller.style.marginLeft = 4;
        scroller.lowButton.style.display = DisplayStyle.None; scroller.highButton.style.display = DisplayStyle.None;
        var tracker = scroller.Q(className: "unity-base-slider__tracker"); if (tracker != null) { tracker.style.backgroundColor = Color.clear; tracker.style.borderTopWidth = tracker.style.borderBottomWidth = tracker.style.borderLeftWidth = tracker.style.borderRightWidth = 0; }
        var dragger = scroller.Q(className: "unity-base-slider__dragger"); if (dragger != null) { dragger.style.backgroundColor = new Color(1, 1, 1, .8f); dragger.style.width = 5; dragger.style.borderTopWidth = dragger.style.borderBottomWidth = dragger.style.borderLeftWidth = dragger.style.borderRightWidth = 0; dragger.style.borderTopLeftRadius = dragger.style.borderTopRightRadius = dragger.style.borderBottomLeftRadius = dragger.style.borderBottomRightRadius = 3; }
        var body = scroll.contentContainer; body.style.paddingRight = 10;
        nameLabel = new Label { name = "pn-char-name" }; nameLabel.style.fontSize = 30; nameLabel.style.color = new Color(.21f, .27f, .3f); body.Add(nameLabel);
        descriptionLabel = new Label { name = "pn-char-description" }; descriptionLabel.style.fontSize = 13; descriptionLabel.style.whiteSpace = WhiteSpace.Normal; descriptionLabel.style.color = new Color(.26f, .33f, .37f); descriptionLabel.style.marginTop = 6; body.Add(descriptionLabel);
        // The divider is a one pixel white line that fades out at both ends (the demo's feathered line), not a solid bar.
        feather = FeatherTexture();
        var divider = new VisualElement { name = "pn-char-divider", pickingMode = PickingMode.Ignore }; divider.style.height = 1; divider.style.marginTop = 14; divider.style.marginBottom = 8;
        divider.style.backgroundImage = feather; divider.style.unityBackgroundScaleMode = ScaleMode.StretchToFill; body.Add(divider);
        statsBox = new VisualElement { name = "pn-char-stats", pickingMode = PickingMode.Ignore }; body.Add(statsBox);
        emptyLabel = new Label("Chưa có nhân vật nào dùng được.") { name = "pn-char-empty" }; emptyLabel.style.color = new Color(.26f, .33f, .37f); emptyLabel.style.display = entries.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None; body.Add(emptyLabel);
        PNLocalizationUI.Bind(emptyLabel,"project_n:ui.empty_characters");
        info.Add(scroll); root.Add(info); items.Add(info);

        continueButton = PNMenuButtons.Choice("pn-char-continue", "Tiếp tục", Continue, true);
        PNLocalizationUI.Bind(continueButton,"project_n:ui.continue");
        continueButton.style.position = Position.Absolute; continueButton.style.right = Length.Percent(5); continueButton.style.bottom = Length.Percent(7); continueButton.style.width = 220;
        continueButton.SetEnabled(entries.Count > 0); root.Add(continueButton); items.Add(continueButton);

        if (entries.Count > 0) Select(PNPlayableCatalog.StartIndex(entries, lastCharacter), false);
        RefreshTeam();
    }

    private float stripAnchor;
    private static void Stretch(VisualElement e) { e.style.position = Position.Absolute; e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0; }
    private static VisualElement Absolute(string name) { var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore }; e.style.position = Position.Absolute; return e; }

    public VisualElement Root => root;
    public IReadOnlyList<VisualElement> Items => items;
    public bool UsesBackdrop => false;
    public Color Tint => tint;
    public bool HandleBack() => false;
    public PNPlayableEntry Selected => index >= 0 ? entries[index] : null;
    public int SelectedIndex => index;

    public void Entered() { }

    public void Continue() { if (!disposed && index >= 0) onContinue?.Invoke(entries[index]); }

    public void Select(int i, bool animate)
    {
        if (i < 0 || i >= entries.Count || i == index && animate) return;
        index = i; var entry = entries[i]; var a = entry.Settings.AccentRgb;
        tint = new Color(a[0] / 255f, a[1] / 255f, a[2] / 255f, 1f);
        aura.style.unityBackgroundImageTintColor = new Color(tint.r, tint.g, tint.b, .5f);
        PNLocalizationUI.Content(nameLabel,entry.Id,"character","name",entry.Settings.Name);
        PNLocalizationUI.Content(descriptionLabel,entry.Id,"character","description",entry.Settings.Description);
        statsBox.Clear();
        foreach (var row in PNPlayableCatalog.StatRows(entry.Definition))
        {
            var line = new VisualElement { pickingMode = PickingMode.Ignore }; line.style.flexDirection = FlexDirection.Row; line.style.justifyContent = Justify.SpaceBetween; line.style.paddingTop = 3; line.style.paddingBottom = 3;
            var k = new Label(row.Label); k.style.fontSize = 13; k.style.color = new Color(.26f, .33f, .37f);
            PNLocalizationUI.Bind(k,row.LocalizationKey,row.Label);
            var v = new Label(row.Value); v.style.fontSize = 13; v.style.color = new Color(.16f, .22f, .26f);
            line.Add(k); line.Add(v); statsBox.Add(line);
        }
        for (int c = 0; c < circles.Count; c++)
        {
            bool on = c == i;
            circles[c].style.backgroundColor = on ? new Color(.97f, .98f, .98f, 1f) : new Color(1, 1, 1, .32f);
            circles[c].style.color = on ? new Color(.16f, .22f, .26f) : new Color(.26f, .33f, .37f);
            circles[c].style.borderTopColor = circles[c].style.borderBottomColor = circles[c].style.borderLeftColor = circles[c].style.borderRightColor = new Color(1, 1, 1, on ? .95f : .6f);
        }
        var moteColor = Color.Lerp(tint, Color.white, .45f);
        foreach (var m in moteList) { m.Element.style.backgroundColor = moteColor; }
        ShowPortrait(entry, animate);
        CenterStrip(i);
        RefreshTeam(true);
    }

    private void ShowPortrait(PNPlayableEntry entry, bool animate)
    {
        fade?.Pause();
        void Apply()
        {
            portraitInner.Clear(); portraitInner.style.backgroundImage = StyleKeyword.None;
            Texture2D texture = string.IsNullOrEmpty(entry.Settings.Art) ? null : Resources.Load<Texture2D>(entry.Settings.Art);
            Sprite sprite = texture == null && !string.IsNullOrEmpty(entry.Definition.PortraitPath) ? Resources.Load<Sprite>(entry.Definition.PortraitPath) : null;
            if (texture != null) { portraitInner.style.backgroundImage = texture; portraitInner.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit; }
            else if (sprite != null) { portraitInner.style.backgroundImage = new StyleBackground(sprite); portraitInner.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit; }
            else portraitInner.Add(new Silhouette(Color.Lerp(tint, Color.black, .55f)));
        }
        if (!animate) { Apply(); portraitInner.style.opacity = 1; return; }
        // Cross-fade: out in 0.14 s, in over 0.26 s.
        float start = time; bool swapped = false;
        fade = portrait.schedule.Execute(() =>
        {
            float t = time - start;
            if (t < .14f) portraitInner.style.opacity = 1f - t / .14f;
            else { if (!swapped) { swapped = true; Apply(); } portraitInner.style.opacity = Mathf.Clamp01((t - .14f) / .26f); }
        }).Every(16).Until(() => time - start >= .4f);
    }

    private sealed class Silhouette : VisualElement
    {
        private readonly Color color;
        public Silhouette(Color color) { this.color = color; pickingMode = PickingMode.Ignore; style.position = Position.Absolute; style.left = 0; style.right = 0; style.top = 0; style.bottom = 0; generateVisualContent += Draw; }
        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D; var r = contentRect; float w = r.width, h = r.height;
            p.fillColor = color;
            p.BeginPath(); p.Arc(new Vector2(r.x + w * .5f, r.y + h * .22f), w * .19f, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill();
            p.BeginPath(); p.MoveTo(new Vector2(r.x + w * .12f, r.yMax)); p.BezierCurveTo(new Vector2(r.x + w * .12f, r.y + h * .45f), new Vector2(r.x + w * .3f, r.y + h * .42f), new Vector2(r.x + w * .5f, r.y + h * .42f));
            p.BezierCurveTo(new Vector2(r.x + w * .7f, r.y + h * .42f), new Vector2(r.x + w * .88f, r.y + h * .45f), new Vector2(r.x + w * .88f, r.yMax)); p.ClosePath(); p.Fill();
        }
    }

    private float stripLayoutWidth;
    // 128 x 1 white texture whose alpha rises from 0 at the ends to .9 in the middle.
    private static Texture2D FeatherTexture()
    {
        const int n = 128; var texture = new Texture2D(n, 1, TextureFormat.RGBA32, false) { name = "Character divider", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color[n]; for (int i = 0; i < n; i++) { float x = (i + .5f) / n, edge = Mathf.Min(x, 1f - x) * 2f; pixels[i] = new Color(1, 1, 1, .9f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge * 2.2f))); }
        texture.SetPixels(pixels); texture.Apply(false, true); return texture;
    }

    private float StripWidth => stripLayoutWidth;
    private const float StripPad = 14f;
    private float TrackWidth => circles.Count * (64f + 18f) - 18f;
    private float ClampStrip(float x) { float w = StripWidth, t = TrackWidth; return t + 2 * StripPad <= w ? (w - t) * .5f - StripPad : Mathf.Clamp(x, w - t - 2 * StripPad, 0f); }
    private void ApplyStrip() { track.style.translate = new Translate(stripX, 0); }
    private void CenterStrip(int i)
    {
        if (circles.Count == 0) return;
        float center = i * (64f + 18f) + 32f; stripX = ClampStrip(StripWidth * .5f - center - StripPad); ApplyStrip();
    }

    private void RefreshTeam(bool force = false)
    {
        var rows = team?.Invoke();
        string signature = rows == null ? "" : string.Join("|", Join(rows));
        if (!force && signature == teamSignature) return;
        teamSignature = signature; teamColumn.Clear();
        if (rows == null) return;
        foreach (var row in rows)
        {
            var line = new VisualElement { pickingMode = PickingMode.Ignore }; line.style.flexDirection = FlexDirection.Row; line.style.alignItems = Align.Center; line.style.marginBottom = 12;
            var circle = new VisualElement { pickingMode = PickingMode.Ignore }; circle.style.width = 40; circle.style.height = 40; circle.style.borderTopLeftRadius = circle.style.borderTopRightRadius = circle.style.borderBottomLeftRadius = circle.style.borderBottomRightRadius = 20;
            circle.style.backgroundColor = new Color(1, 1, 1, .42f); circle.style.borderTopWidth = circle.style.borderBottomWidth = circle.style.borderLeftWidth = circle.style.borderRightWidth = 1.5f;
            circle.style.borderTopColor = circle.style.borderBottomColor = circle.style.borderLeftColor = circle.style.borderRightColor = new Color(1, 1, 1, .9f);
            var letter = new Label(row.Key.Length > 0 ? row.Key.Substring(0, 1).ToUpperInvariant() : "?"); letter.style.unityTextAlign = TextAnchor.MiddleCenter; letter.style.width = 40; letter.style.height = 40; letter.style.marginTop = letter.style.marginBottom = letter.style.marginLeft = letter.style.marginRight = 0; letter.style.paddingTop = letter.style.paddingBottom = letter.style.paddingLeft = letter.style.paddingRight = 0; letter.style.color = new Color(.21f, .27f, .3f); circle.Add(letter);
            var text = new VisualElement { pickingMode = PickingMode.Ignore }; text.style.marginLeft = 10;
            var name = new Label(row.Key); name.style.fontSize = 14; name.style.color = new Color(.16f, .22f, .26f);
            var status = new Label(row.Value); status.style.fontSize = 12; status.style.color = new Color(.3f, .38f, .42f);
            text.Add(name); text.Add(status); line.Add(circle); line.Add(text); teamColumn.Add(line);
        }
    }
    private static IEnumerable<string> Join(IReadOnlyList<KeyValuePair<string, string>> rows) { foreach (var r in rows) yield return r.Key + "=" + r.Value; }

    public void Tick(float dt)
    {
        if (disposed) return;
        time += dt;
        float w = root.resolvedStyle.width, h = root.resolvedStyle.height;
        if (!float.IsFinite(w) || !float.IsFinite(h) || w < 2 || h < 2) return;
        // Layout that depends on the stage size.
        float stripWidth = Mathf.Min(w * .64f, 540f);
        if (Mathf.Abs(strip.resolvedStyle.width - stripWidth) > .5f || strip.style.width.value.value != stripWidth)
        { stripLayoutWidth = stripWidth; strip.style.width = stripWidth; strip.style.marginLeft = -stripWidth * .5f; if (index >= 0) CenterStrip(index); }
        float pw = h * .6f * .55f, ph = h * .6f;
        portrait.style.width = pw; portrait.style.height = ph; portrait.style.marginLeft = -pw * .5f; portrait.style.marginTop = -ph * .5f;
        float D = Mathf.Max(700f, h * 1.9f); dial.style.width = D; dial.style.height = D; dial.style.marginLeft = -D * .5f; dial.style.marginTop = -D * .5f;
        layerA.style.rotate = new Rotate(time / 220f * 360f); layerB.style.rotate = new Rotate(-time / 340f * 360f);
        // Rising motes in the character's lightened accent.
        foreach (var m in moteList)
        {
            m.Y -= m.Speed * dt; m.Phase += dt; if (m.Y < -.05f) { m.Y = 1.05f; m.X = (float)random.NextDouble(); }
            float alpha = Mathf.Clamp01(Mathf.Min(m.Y, 1f - m.Y) * 6f) * .85f * motesAlpha;
            m.Element.style.left = (m.X + Mathf.Sin(m.Phase) * .012f) * w; m.Element.style.top = m.Y * h;
            m.Element.style.width = m.Size; m.Element.style.height = m.Size; m.Element.style.opacity = alpha;
        }
        teamTimer += dt; if (teamTimer >= .25f) { teamTimer = 0; RefreshTeam(); }
    }

    private float motesAlpha = 1f;
    public void Animate(bool entering, float elapsed)
    {
        PNMenuMotion.Dial(entering, elapsed, out var sx, out var sy, out var a);
        dial.style.scale = new Scale(new Vector2(sx, sy)); dial.style.opacity = a;
        motesAlpha = entering ? Mathf.Clamp01((elapsed - .3f) / .6f) : 1f - Mathf.Clamp01(elapsed / .3f);
        aura.style.opacity = entering ? Mathf.Clamp01(elapsed / .4f) : 1f - Mathf.Clamp01(elapsed / .3f);
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        fade?.Pause();
        root.RemoveFromHierarchy(); PNUiRuntime.Retire(light); if (feather != null) PNUiRuntime.Retire(feather);
    }
}
#endif
