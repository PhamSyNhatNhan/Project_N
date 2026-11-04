#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// EN: Screen shown after "Play alone" / "Host a room" when a run is saved (Docs/RunSavePlan.md Q12): Continue, or Sum up now (end that run as it stands, show the summary, free the slot). A new run
//     is never started over a saved one, so choosing one of the two is always first. When the saved run cannot be played with the content loaded now, Continue is off and the reason is told
//     (an error line, nothing else: this menu shows no information).
// VI: Màn khi có run đã lưu sau "Chơi đơn" / "Mở phòng": Tiếp tục hoặc Tổng kết ngay. Nếu bản lưu không chơi được với nội dung đang nạp, Tiếp tục tắt và nêu lý do.
internal sealed class PNResumeScreen : IPNMenuScreen
{
    private readonly VisualElement root, column;
    private readonly List<VisualElement> items = new List<VisualElement>();
    private readonly Button continueButton, sumUpButton;
    private readonly Color tint;

    internal void Continue() { if (continueButton.enabledSelf) onContinue(); }
    internal void SumUp() => onSumUp();
    internal bool CanContinue => continueButton.enabledSelf;
    private readonly Action onContinue, onSumUp;

    public PNResumeScreen(PNRunSaveProblem problem, Color tint, Action onContinue, Action onSumUp)
    {
        this.tint = tint; this.onContinue = onContinue; this.onSumUp = onSumUp;
        root = new VisualElement { name = "pn-resume-screen", pickingMode = PickingMode.Ignore };
        root.style.position = Position.Absolute; root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
        column = PNMenuButtons.Column("pn-resume-column"); root.Add(column);
        bool error = problem != PNRunSaveProblem.None;
        float height = 2 * 82 + (error ? 56 : 0);
        column.style.height = height; column.style.marginTop = -height / 2;
        continueButton = PNMenuButtons.Choice("pn-resume-continue", "Tiếp tục", Continue, primary: true);
        PNLocalizationUI.Bind(continueButton, "project_n:run.resume.continue");
        continueButton.SetEnabled(!error);
        sumUpButton = PNMenuButtons.Choice("pn-resume-sumup", "Tổng kết ngay", SumUp);
        PNLocalizationUI.Bind(sumUpButton, "project_n:run.resume.sumup");
        column.Add(continueButton); column.Add(sumUpButton); items.Add(continueButton); items.Add(sumUpButton);
        if (error)
        {
            var line = new Label { name = "pn-resume-error", pickingMode = PickingMode.Ignore };
            PNLocalizationUI.Bind(line, "project_n:run.save.problem." + problem.ToString().ToLowerInvariant());
            line.style.color = Color.white; line.style.fontSize = 15; line.style.unityTextAlign = TextAnchor.UpperCenter; line.style.whiteSpace = WhiteSpace.Normal; line.style.marginTop = 10;
            line.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 2, color = new Color(0, 0, 0, .6f) };
            column.Add(line);
        }
    }

    public VisualElement Root => root;
    public IReadOnlyList<VisualElement> Items => items;
    public bool UsesBackdrop => true;
    public Color Tint => tint;
    public bool HandleBack() => false;
    public void Tick(float dt) { }
    public void Entered() { (continueButton.enabledSelf ? continueButton : sumUpButton).Focus(); }
    public void Animate(bool entering, float elapsed) { }
    public void Dispose() { root.RemoveFromHierarchy(); }
}
#endif
