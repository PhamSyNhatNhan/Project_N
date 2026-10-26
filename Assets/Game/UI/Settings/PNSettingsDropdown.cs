#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class PNSettingsDropdown
{
    internal readonly Button Trigger;
    internal readonly VisualElement Popup;
    private readonly Label value;
    private readonly VisualElement chevron;
    private sealed class Chevron : VisualElement
    {
        internal Chevron(){pickingMode=PickingMode.Ignore;generateVisualContent+=ctx=>{var p=ctx.painter2D;Vector2 c=contentRect.center;p.strokeColor=new Color(.4f,.47f,.5f);p.lineWidth=1.5f;p.lineJoin=LineJoin.Round;p.lineCap=LineCap.Round;p.BeginPath();p.MoveTo(c+new Vector2(-5,-2));p.LineTo(c+new Vector2(0,3));p.LineTo(c+new Vector2(5,-2));p.Stroke();};}
    }
    private readonly ScrollView scroll;
    private readonly PNSettingsScrollFade scrollFade;
    private readonly List<Button> options=new List<Button>();
    private readonly Action<PNSettingsDropdown> opening;
    private readonly Action<string> selected;
    private string[] codes=Array.Empty<string>();
    private float progress;
    internal bool IsOpen {get;private set;}
    internal PNSettingsDropdown(string name,VisualElement row,VisualElement overlay,Action<PNSettingsDropdown> opening,Action<string> selected)
    {
        this.opening=opening;this.selected=selected;
        Trigger=new Button(Toggle){name=name+"-trigger"};Trigger.AddToClassList("pn-settings-trigger");
        value=new Label();value.AddToClassList("pn-settings-value");Trigger.Add(value);
        chevron=new Chevron();chevron.AddToClassList("pn-settings-chevron");Trigger.Add(chevron);row.Add(Trigger);
        Popup=new VisualElement{name=name+"-options",usageHints=UsageHints.DynamicTransform};Popup.AddToClassList("pn-settings-options");Popup.style.display=DisplayStyle.None;overlay.Add(Popup);
        scroll=new ScrollView(ScrollViewMode.Vertical){horizontalScrollerVisibility=ScrollerVisibility.Hidden,verticalScrollerVisibility=ScrollerVisibility.Auto};scroll.AddToClassList("pn-settings-scroll");Popup.Add(scroll);scrollFade=new PNSettingsScrollFade(scroll);
        Popup.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());
        Trigger.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.DownArrow||e.keyCode==KeyCode.UpArrow){Open();FocusOption(e.keyCode==KeyCode.DownArrow?0:options.Count-1);e.StopPropagation();}});
    }
    internal void SetOptions(string[] ids,string[] labels,string current)
    {
        bool rebuild=ids.Length!=codes.Length;for(int i=0;!rebuild&&i<ids.Length;i++)rebuild=ids[i]!=codes[i];
        if(rebuild)
        {
            scroll.Clear();options.Clear();codes=(string[])ids.Clone();
            for(int i=0;i<ids.Length;i++)
            {
                int index=i;string code=ids[i];var button=new Button(()=>{selected(code);Close();}){name=Trigger.name.Replace("-trigger","-option-")+code};button.AddToClassList("pn-settings-option");
                button.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.DownArrow||e.keyCode==KeyCode.UpArrow){FocusOption((index+(e.keyCode==KeyCode.DownArrow?1:-1)+options.Count)%options.Count);e.StopPropagation();}});
                options.Add(button);scroll.Add(button);
            }
        }
        string shown=current;
        for(int i=0;i<ids.Length;i++){bool match=ids[i]==current;options[i].text=labels[i]+(match?"   ✓":"");options[i].EnableInClassList("pn-settings-option-selected",match);if(match)shown=labels[i];}
        value.text=shown;
        if(rebuild&&IsOpen)FocusOption(Math.Max(0,Array.IndexOf(ids,current)));
    }
    private void FocusOption(int index){if(options.Count==0)return;index=Mathf.Clamp(index,0,options.Count-1);options[index].Focus();scroll.ScrollTo(options[index]);}
    private void Toggle(){if(IsOpen)Close();else Open();}
    internal void Open()
    {
        opening(this);IsOpen=true;Popup.style.display=DisplayStyle.Flex;Popup.pickingMode=PickingMode.Position;chevron.style.rotate=new Rotate(180);Trigger.tooltip=value.text;
    }
    internal void Close(bool focus=true)
    { if(!IsOpen)return;IsOpen=false;Popup.pickingMode=PickingMode.Ignore;chevron.style.rotate=new Rotate(0);if(focus)Trigger.Focus(); }
    internal void Tick(float dt,VisualElement root)
    {
        scrollFade.Tick(dt);
        float target=IsOpen?1:0;progress=Mathf.MoveTowards(progress,target,dt/(IsOpen?.24f:.16f));
        if(progress<=0){Popup.style.display=DisplayStyle.None;return;}
        Rect anchor=Trigger.worldBound;Vector2 pos=root.WorldToLocal(new Vector2(anchor.xMin,anchor.yMax));
        float h=root.resolvedStyle.height,w=root.resolvedStyle.width;
        if(!float.IsFinite(w)||!float.IsFinite(h))return;
        float height=Mathf.Min(266,options.Count*46+12);float top=pos.y+9;
        if(top+height>h-24)top=Mathf.Max(76,root.WorldToLocal(new Vector2(anchor.xMin,anchor.yMin)).y-height-9);
        Popup.style.left=Mathf.Clamp(pos.x,16,Mathf.Max(16,w-anchor.width-16));Popup.style.top=top;
        Popup.style.width=Mathf.Min(anchor.width,w-32);Popup.style.height=Mathf.Min(height,h-top-20);
        Popup.style.opacity=PNSettingsView.Ease(progress);Popup.style.translate=new Translate(0,-7*(1-progress));Popup.style.scale=new Scale(new Vector2(1,.97f+.03f*progress));
    }
}
#endif
