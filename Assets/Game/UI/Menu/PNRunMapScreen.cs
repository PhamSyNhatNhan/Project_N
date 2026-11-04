#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// EN: Screen after "Play alone" / "Host a room" (Docs/RunSavePlan.md 5.8): the maps (run definitions that are not hidden) as a column of choice buttons, the one played last focused first. It
//     always shows, even with a single map. Only the host (or a player alone) sees it; a client joins the map the host chose. Names are localization keys of the run, so a mod's map appears
//     the same way. A long list scrolls (no visible bar). The art and description of a run are read by the content and shown later; this screen is names only.
// VI: Màn sau "Chơi đơn" / "Mở phòng": các map (run không ẩn) dạng cột nút, map chơi lần trước được chọn sẵn. Luôn hiện, kể cả khi chỉ có một map.
internal sealed class PNRunMapScreen : IPNMenuScreen
{
    private const int MaxVisible = 6;
    private readonly VisualElement root, column;
    private readonly List<VisualElement> items = new List<VisualElement>();
    private readonly IReadOnlyList<PNRunDefinition> runs;
    private readonly Action<PNRunDefinition> onChoose;
    private readonly Color tint;
    private readonly int focusIndex;

    internal void Choose(int index) => onChoose(runs[index]);

    public PNRunMapScreen(IReadOnlyList<PNRunDefinition> runs, string lastRun, Color tint, Action<PNRunDefinition> onChoose)
    {
        if (runs == null || runs.Count == 0) throw new ArgumentException("There is no map to choose.", nameof(runs));
        this.runs = runs; this.onChoose = onChoose ?? throw new ArgumentNullException(nameof(onChoose)); this.tint = tint;
        root = new VisualElement { name = "pn-map-screen", pickingMode = PickingMode.Ignore };
        root.style.position = Position.Absolute; root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
        column = PNMenuButtons.Column("pn-map-column"); root.Add(column);
        int visible = Math.Min(runs.Count, MaxVisible);
        column.style.height = visible * 82; column.style.marginTop = -visible * 41;
        VisualElement host = column;
        if (runs.Count > MaxVisible)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "pn-map-scroll", verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.height = visible * 82; column.Add(scroll); host = scroll;
        }
        for (int i = 0; i < runs.Count; i++)
        {
            int index = i;
            var button = PNMenuButtons.Choice("pn-map-" + runs[i].Id.ToString().Replace(':', '-'), runs[i].Name, () => Choose(index));
            PNLocalizationUI.Bind(button, runs[i].Name);
            host.Add(button); items.Add(button);
        }
        focusIndex = Math.Max(0, runs.ToList().FindIndex(x => x.Id.ToString() == lastRun));
    }

    public VisualElement Root => root;
    public IReadOnlyList<VisualElement> Items => items;
    public bool UsesBackdrop => true;
    public Color Tint => tint;
    public bool HandleBack() => false;
    public void Tick(float dt) { }
    public void Entered() { if (items.Count > 0) items[focusIndex].Focus(); }
    public void Animate(bool entering, float elapsed) { }
    public void Dispose() { root.RemoveFromHierarchy(); }
}
#endif
