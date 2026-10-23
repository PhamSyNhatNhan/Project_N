#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

internal static class PNLocalizationUI
{
    private sealed class Binding
    {
        internal Action Refresh;
        private bool subscribed;
        internal Binding(VisualElement element,Action refresh)
        {
            Refresh=refresh;
            element.RegisterCallback<AttachToPanelEvent>(_=>Attach());
            element.RegisterCallback<DetachFromPanelEvent>(_=>Detach());
            if(element.panel!=null)Attach();else Refresh();
        }
        private void Attach(){if(!subscribed){PNLocalization.Changed+=Run;subscribed=true;}Run();}
        private void Detach(){if(subscribed){PNLocalization.Changed-=Run;subscribed=false;}}
        private void Run()=>Refresh();
    }
    private static readonly ConditionalWeakTable<VisualElement,Binding> bindings=new ConditionalWeakTable<VisualElement,Binding>();
    internal static void Watch(VisualElement element,Action refresh)
    { if(bindings.TryGetValue(element,out var binding)){binding.Refresh=refresh;refresh();}else bindings.Add(element,new Binding(element,refresh)); }
    internal static void Bind(TextElement element,string key,string fallback=null)=>Watch(element,()=>element.text=PNLocalization.Text(key,null,fallback));
    internal static void Content(TextElement element,string id,string kind,string field,string fallback)=>Watch(element,()=>element.text=PNLocalization.ContentText(id,kind,field,fallback));
    internal static Label Label(string key){var label=new Label();Bind(label,key);return label;}
    // The same transparent, pause-style language view is used by Title and pause.
    internal static void ShowLanguages(VisualElement parent,Action closed)
    {
        var view=new VisualElement{name="pn-language-view"};view.style.position=Position.Absolute;view.style.left=0;view.style.right=0;view.style.top=0;view.style.bottom=0;
        var column=PNMenuButtons.Column("pn-language-column");column.style.top=Length.Percent(20);view.Add(column);
        VisualElement list;
        if(PNLocalization.Languages.Count<=4)list=new VisualElement();
        else {var scroll=new ScrollView(ScrollViewMode.Vertical){horizontalScrollerVisibility=ScrollerVisibility.Hidden};scroll.style.maxHeight=360;list=scroll;}
        column.Add(list);
        var error=new Label();error.style.color=new Color(.7f,.25f,.25f);column.Add(error);
        foreach(var language in PNLocalization.Languages)
        {
            var code=language.Code;var button=PNMenuButtons.Choice("pn-language-"+code,language.NativeName,()=>{try{PNLocalization.Select(code);error.text="";}catch(Exception e){Debug.LogWarning("Language selection: "+e);error.text=PNLocalization.Text("project_n:ui.language.failed");}});
            Watch(button,()=>button.text=(PNLocalization.Selected==code?"✓  ":"")+language.NativeName);list.Add(button);
        }
        var back=PNMenuButtons.Choice("pn-language-back","",()=>{view.RemoveFromHierarchy();closed?.Invoke();});Bind(back,"project_n:ui.back");column.Add(back);
        parent.Add(view);back.Focus();
    }
}
#endif
