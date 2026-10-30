#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Caller owns pause/input and ticks the view with unscaled time; no global scheduler.
internal sealed class PNSettingsView : IDisposable
{
    public VisualElement Root {get;}
    private readonly PNMenuBackdrop backdrop;
    private readonly PNSparkleArtwork sparkles;
    private readonly PNSettingsDecoration decoration;
    private readonly VisualElement rows;
    private readonly List<VisualElement> row=new List<VisualElement>();
    private readonly List<PNSettingsDropdown> dropdowns=new List<PNSettingsDropdown>();
    private readonly ScrollView rowScroll;
    private readonly Label error;
    private readonly PNMenuCorner corners;
    private readonly PNSettingsDropdown language,fps,autoTarget,paperShadow,ink;
    // The view edits a draft; nothing changes until Save (one write, everything applied together).
    private PNUserSettings draft;
    private readonly Button saveButton,discardButton,keepButton;private readonly Label saveStatus,lookReason,confirmText;private readonly VisualElement confirm;
    private float cornerFrom;
    private readonly PNSettingsDropdown icons,palette,pcControls,bindingLabels;
    private readonly List<VisualElement> animated=new List<VisualElement>();
    private readonly List<VisualElement> fields=new List<VisualElement>();
    private readonly List<float> animationExit=new List<float>();
    private readonly PNSettingsScrollFade scrollFade;
    private readonly Action back,closeAll,closeAllStarted;
    private bool disposed,exiting,closeWhole;
    private float elapsed,clock,artProgress,exitFrom;
    private float exitAlpha;
    private int cancelFrame=-1;
    internal bool IsExiting=>exiting;
    internal float Progress=>artProgress;
    public PNSettingsView(VisualElement parent,Action back,Action closeAll,Action closeAllStarted=null,bool mobileOptions=false)
    {
        PNSettingsService.EnsureLoaded();draft=PNSettingsService.Current;this.back=back;this.closeAll=closeAll;this.closeAllStarted=closeAllStarted;
        Root=new VisualElement{name="pn-settings-view",focusable=true};Stretch(Root);Root.AddToClassList("pn-settings-view");Root.style.opacity=0;
        var font=Resources.Load<Font>("PNFont/Philosopher-Regular");if(font!=null)Root.style.unityFontDefinition=FontDefinition.FromFont(font);
        sparkles=new PNSparkleArtwork();backdrop=new PNMenuBackdrop{SurfaceAlpha=1,DecorationAlpha=.32f};backdrop.Root.style.backgroundColor=new Color(184f/255,192f/255,196f/255);Root.Add(backdrop.Root);
        decoration=new PNSettingsDecoration();Root.Add(decoration);
        rowScroll=new ScrollView(ScrollViewMode.Vertical){name="pn-settings-rows",horizontalScrollerVisibility=ScrollerVisibility.Hidden,verticalScrollerVisibility=ScrollerVisibility.Auto};rowScroll.AddToClassList("pn-settings-rows");Root.Add(rowScroll);rows=rowScroll.contentContainer;
        bool showMobile=mobileOptions||PNMobilePlatform.IsMobile(PNMobilePlatform.UsesSimulatedScreen)||((Debug.isDebugBuild||Application.isEditor)&&Array.Exists(Environment.GetCommandLineArgs(),a=>a.StartsWith("-pn-mobile-style-review=")));
        bool desktop=!PNMobilePlatform.IsMobile(PNMobilePlatform.UsesSimulatedScreen);
        var general=Section("general",false);var combat=Section("combat",true);var appearance=Section("interface",true);
        var languageRow=AddRow(general,"language","language");var fpsRow=AddRow(general,"fps","fps");var shadowRow=AddRow(general,"paper-shadow","paper_shadow");var inkRow=AddRow(general,"ink","ink");
        var targetRow=AddRow(combat,"auto-target","auto_target");
        language=new PNSettingsDropdown("pn-settings-language",languageRow,Root,Opening,code=>Edit(d=>d.WithLocale(PNLocalizationCatalog.Code(code))));
        fps=new PNSettingsDropdown("pn-settings-fps",fpsRow,Root,Opening,code=>Edit(d=>d.WithFrameRate(int.Parse(code))));
        autoTarget=new PNSettingsDropdown("pn-settings-auto-target",targetRow,Root,Opening,id=>Edit(d=>d.WithAutoTargetMode(PNTargetRule.ParseMode(id))));
        paperShadow=new PNSettingsDropdown("pn-settings-paper-shadow",shadowRow,Root,Opening,id=>Edit(d=>d.WithPaperShadow(PNPaperShadowModes.Parse(id))));
        ink=new PNSettingsDropdown("pn-settings-ink",inkRow,Root,Opening,id=>Edit(d=>d.WithInkMode(PNInkModes.Parse(id))));
        dropdowns.Add(language);dropdowns.Add(fps);dropdowns.Add(autoTarget);dropdowns.Add(paperShadow);dropdowns.Add(ink);
        // The look rows only change in the Title and the Hall: a short reason under the section when they are locked.
        lookReason=new Label{name="pn-settings-look-reason"};lookReason.AddToClassList("pn-settings-reason");general.parent.Add(lookReason);
        if(desktop)
        {
            pcControls=new PNSettingsDropdown("pn-settings-pc-controls",AddRow(appearance,"pc-controls","pc_controls"),Root,Opening,id=>Edit(d=>d.WithShowPcControls(id=="on")));
            bindingLabels=new PNSettingsDropdown("pn-settings-pc-binding-labels",AddRow(appearance,"pc-binding-labels","pc_binding_labels"),Root,Opening,id=>Edit(d=>d.WithShowPcBindingLabels(id=="on")));
            dropdowns.Add(pcControls);dropdowns.Add(bindingLabels);
        }
        if(showMobile||desktop)
        {
            icons=new PNSettingsDropdown("pn-settings-mobile-icons",AddRow(appearance,"mobile-icons","mobile_icons"),Root,Opening,id=>Edit(d=>{if(!PNMobileStyleCatalog.HasIconSet(id))throw new ArgumentException("Unknown mobile icon set.");return d.WithMobileStyle(id,d.MobilePalette);}));
            palette=new PNSettingsDropdown("pn-settings-mobile-palette",AddRow(appearance,"mobile-palette","mobile_palette"),Root,Opening,id=>Edit(d=>{if(!PNMobileStyleCatalog.HasPalette(id))throw new ArgumentException("Unknown mobile palette.");return d.WithMobileStyle(d.MobileIconSet,id);}));
            dropdowns.Add(icons);dropdowns.Add(palette);
        }
        scrollFade=new PNSettingsScrollFade(rowScroll);
        error=new Label{name="pn-settings-error"};error.AddToClassList("pn-settings-error");rows.Add(error);
        // One Save for every setting, at the END of the list (inside the scroll, not pinned).
        var saveRow=new VisualElement{name="pn-settings-save-row"};saveRow.AddToClassList("pn-settings-save-row");rows.Add(saveRow);Animate(saveRow);
        saveButton=new Button(Save){name="pn-settings-save"};saveButton.AddToClassList("pn-settings-save");saveRow.Add(saveButton);
        saveStatus=new Label{name="pn-settings-save-status"};saveStatus.AddToClassList("pn-settings-save-status");saveRow.Add(saveStatus);
        confirm=new VisualElement{name="pn-settings-unsaved"};confirm.AddToClassList("pn-settings-confirm");confirm.style.display=DisplayStyle.None;Root.Add(confirm);
        confirmText=new Label();confirmText.AddToClassList("pn-settings-confirm-text");confirm.Add(confirmText);
        var confirmRow=new VisualElement();confirmRow.AddToClassList("pn-settings-confirm-row");confirm.Add(confirmRow);
        discardButton=new Button(()=>{draft=PNSettingsService.Current;confirm.style.display=DisplayStyle.None;RequestClose(closeWholePending,true);}){name="pn-settings-discard"};discardButton.AddToClassList("pn-settings-save");confirmRow.Add(discardButton);
        keepButton=new Button(KeepEditing){name="pn-settings-keep"};keepButton.AddToClassList("pn-settings-save");confirmRow.Add(keepButton);
        corners=new PNMenuCorner(()=>RequestClose(false),()=>RequestClose(true));Root.Add(corners);corners.Back.focusable=corners.Close.focusable=true;
        Root.RegisterCallback<PointerDownEvent>(e=>{if(dropdowns.Any(d=>d.IsOpen)&&!dropdowns.Any(d=>d.Trigger.worldBound.Contains(e.position)||(d.IsOpen&&d.Popup.worldBound.Contains(e.position)))){foreach(var d in dropdowns)d.Close(false);e.StopImmediatePropagation();}},TrickleDown.TrickleDown);
        Root.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape){Cancel();e.StopImmediatePropagation();}});
        Root.RegisterCallback<NavigationCancelEvent>(e=>{Cancel();e.StopImmediatePropagation();});
        PNLocalization.Changed+=Refresh;PNSettingsService.Changed+=Refresh;PNMobileStyleCatalog.Changed+=Refresh;parent.Add(Root);Refresh();Tick(0);language.Trigger.Focus();
    }
    private VisualElement Section(string id,bool divider)
    {
        if(divider){var line=new PNSettingsRule(true);line.AddToClassList("pn-settings-divider");rows.Add(line);Animate(line);}
        var section=new VisualElement{name="pn-settings-section-"+id};section.AddToClassList("pn-settings-section");rows.Add(section);
        var heading=new VisualElement();heading.AddToClassList("pn-settings-heading");var title=new Label();PNLocalizationUI.Bind(title,"project_n:settings.section."+id);heading.Add(title);
        var underline=new PNSettingsRule(false);underline.AddToClassList("pn-settings-heading-line");heading.Add(underline);title.RegisterCallback<GeometryChangedEvent>(e=>underline.style.width=e.newRect.width);section.Add(heading);Animate(heading);
        var grid=new VisualElement();grid.AddToClassList("pn-settings-fields");section.Add(grid);fields.Add(grid);return grid;
    }
    private VisualElement AddRow(VisualElement grid,string name,string key)
    {
        var item=new VisualElement{name="pn-settings-"+name+"-row"};item.AddToClassList("pn-settings-row");row.Add(item);grid.Add(item);Animate(item);
        var label=new Label();label.AddToClassList("pn-settings-label");PNLocalizationUI.Bind(label,"project_n:settings."+key);item.Add(label);return item;
    }
    private void Animate(VisualElement element){animated.Add(element);animationExit.Add(0);}
    internal static void Stretch(VisualElement e){e.style.position=Position.Absolute;e.style.left=0;e.style.right=0;e.style.top=0;e.style.bottom=0;}
    internal static float EaseOut(float t){t=1-Mathf.Clamp01(t);return 1-t*t*t;}
    internal static float Ease(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
    private void Opening(PNSettingsDropdown opened){foreach(var d in dropdowns)if(d!=opened)d.Close(false);}
    private bool Dirty=>draft.ToJsonOrNull()!=PNSettingsService.Current.ToJsonOrNull();
    private bool closeWholePending;
    // A dropdown writes the draft only; the file, the language and the frame rate stay as they are until Save.
    private void Edit(Func<PNUserSettings,PNUserSettings> change)
    {
        try{draft=change(draft);error.text="";}
        catch(Exception e){Debug.LogWarning("Settings selection: "+e.Message);error.text=PNLocalization.Text("project_n:ui.language.failed");}
        Refresh();
    }
    private void Save()
    {
        if(disposed||!Dirty)return;
        try{bool saved=PNSettingsService.Apply(draft);error.text=saved?"":PNLocalization.Text("project_n:settings.save_failed");}
        catch(Exception e){Debug.LogWarning("Settings save: "+e.Message);error.text=PNLocalization.Text("project_n:ui.language.failed");}
        draft=PNSettingsService.Current;Refresh();
    }
    private void KeepEditing(){confirm.style.display=DisplayStyle.None;rows.SetEnabled(true);corners.SetEnabled(true);}
    private void Refresh()
    {
        if(disposed)return;
        var langs=PNLocalization.Languages;language.SetOptions(langs.Select(l=>l.Code).ToArray(),langs.Select(l=>l.NativeName).ToArray(),draft.Locale??PNLocalization.Selected);
        string[] ids={"30","60","90","120","144","0"};fps.SetOptions(ids,ids.Select(s=>s=="0"?PNLocalization.Text("project_n:settings.unlimited"):s).ToArray(),draft.TargetFrameRate.ToString());
        language.Trigger.tooltip=PNLocalization.Text("project_n:settings.language");fps.Trigger.tooltip=PNLocalization.Text("project_n:settings.fps");
        string[] modes={PNTargetRule.NearestId,PNTargetRule.LowestHealthId,PNTargetRule.LowestHealthPercentId};
        autoTarget.SetOptions(modes,modes.Select(m=>PNLocalization.Text("project_n:settings.auto_target."+m)).ToArray(),PNTargetRule.ModeId(draft.AutoTargetMode));
        autoTarget.Trigger.tooltip=PNLocalization.Text("project_n:settings.auto_target");
        // Overlap shadow between the parts of a puppet (five choices) and the ink look (four). Independent of each other; both change only in the Title and the Hall.
        string[] shadowIds={PNPaperShadowModes.OffId,PNPaperShadowModes.KeyActorsId,PNPaperShadowModes.ActorsId,PNPaperShadowModes.AllId,PNPaperShadowModes.BakedId};
        paperShadow.SetOptions(shadowIds,shadowIds.Select(m=>PNLocalization.Text("project_n:settings.paper_shadow."+m)).ToArray(),PNPaperShadowModes.Id(draft.PaperShadow));
        string[] inks={PNInkModes.BakedId,PNInkModes.LiveKeyActorsId,PNInkModes.LiveActorsId,PNInkModes.LiveAllId};
        ink.SetOptions(inks,inks.Select(m=>PNLocalization.Text("project_n:settings.ink."+m)).ToArray(),PNInkModes.Id(draft.InkMode));
        ink.Trigger.tooltip=PNLocalization.Text("project_n:settings.ink");
        bool lookLocked=!PNSettingsService.VisualLookChangeAllowed;paperShadow.Trigger.SetEnabled(!lookLocked);ink.Trigger.SetEnabled(!lookLocked);
        lookReason.text=lookLocked?PNLocalization.Text("project_n:settings.locked_outside_hall"):"";lookReason.style.display=lookLocked?DisplayStyle.Flex:DisplayStyle.None;
        paperShadow.Trigger.tooltip=PNLocalization.Text("project_n:settings.paper_shadow");
        if(icons!=null)
        {
            var sets=PNMobileStyleCatalog.IconSets;var palettes=PNMobileStyleCatalog.Palettes;var settings=draft;
            icons.SetOptions(sets.Select(s=>s.Id.ToString()).ToArray(),sets.Select(s=>PNLocalization.Text(s.DisplayNameKey)).ToArray(),PNMobileStyleCatalog.ResolveIconSet(settings.MobileIconSet).Id.ToString());
            palette.SetOptions(palettes.Select(s=>s.Id.ToString()).ToArray(),palettes.Select(s=>PNLocalization.Text(s.DisplayNameKey)).ToArray(),PNMobileStyleCatalog.ResolvePalette(settings.MobilePalette).Id.ToString());
        }
        pcControls?.SetOptions(new[]{"off","on"},new[]{PNLocalization.Text("project_n:settings.off"),PNLocalization.Text("project_n:settings.on")},draft.ShowPcControls?"on":"off");
        bindingLabels?.SetOptions(new[]{"off","on"},new[]{PNLocalization.Text("project_n:settings.off"),PNLocalization.Text("project_n:settings.on")},draft.ShowPcBindingLabels?"on":"off");
        corners.Back.tooltip=PNLocalization.Text("project_n:ui.back");corners.Close.tooltip=PNLocalization.Text("project_n:settings.close");
        bool dirty=Dirty;saveButton.text=PNLocalization.Text("project_n:settings.save");saveButton.SetEnabled(dirty);
        bool rebuild=dirty&&PNSettingsService.VisualLookChangeAllowed&&PNSettingsService.LookChangedBy(draft)&&EveEngine.EveRegistrySession.IsReady;
        saveStatus.text=!dirty?"":PNLocalization.Text(rebuild?"project_n:settings.unsaved_rebuild":"project_n:settings.unsaved");
        confirmText.text=PNLocalization.Text("project_n:settings.unsaved_ask");discardButton.text=PNLocalization.Text("project_n:settings.discard");keepButton.text=PNLocalization.Text("project_n:settings.keep_editing");
        if(PNSettingsService.SaveError!=null)error.text=PNLocalization.Text("project_n:settings.save_failed");
    }
    public void Cancel()
    {
        if(disposed||cancelFrame==Time.frameCount)return;cancelFrame=Time.frameCount;
        if(confirm.style.display==DisplayStyle.Flex){KeepEditing();return;}
        if(dropdowns.Any(d=>d.IsOpen)){foreach(var d in dropdowns)d.Close();return;}RequestClose(false);
    }
    private void RequestClose(bool whole,bool force=false)
    {
        if(disposed||exiting)return;
        if(!force&&Dirty){closeWholePending=whole;foreach(var d in dropdowns)d.Close(false);confirm.style.display=DisplayStyle.Flex;rows.SetEnabled(false);corners.SetEnabled(false);discardButton.Focus();return;}
        cornerFrom=corners.resolvedStyle.opacity;exitAlpha=Root.resolvedStyle.opacity;for(int i=0;i<animated.Count;i++)animationExit[i]=animated[i].resolvedStyle.opacity;exiting=true;closeWhole=whole;elapsed=0;exitFrom=artProgress;
        foreach(var d in dropdowns)d.Close(false);rows.SetEnabled(false);corners.SetEnabled(false);
        if(whole)closeAllStarted?.Invoke();
    }
    public void Tick(float dt)
    {
        if(disposed)return;elapsed+=dt;clock+=dt;
        float width=Root.resolvedStyle.width,height=Root.resolvedStyle.height;if(!float.IsFinite(width)||width<=0||!float.IsFinite(height)||height<=0)return;
        artProgress=exiting?exitFrom*(1-Ease(elapsed/.7f)):Ease(elapsed/.76f);
        // Exit: the content (corner buttons, rows, headings, confirm) goes early; only the backdrop and the star art last the whole 0.7 s. The entrance is unchanged.
        Root.style.opacity=exiting?1:Ease(elapsed/.76f);
        if(exiting){float fade=exitAlpha*(1-Ease(elapsed/.7f));backdrop.Root.style.opacity=fade;decoration.style.opacity=fade;confirm.style.opacity=1-EaseOut(elapsed/.2f);}
        backdrop.Tick(dt);decoration.Animate(clock,artProgress,width,height);
        float insetLeft=0,insetRight=0,insetTop=0;var area=Screen.safeArea;
        if(Screen.width>0&&Screen.height>0){insetLeft=width*area.xMin/Screen.width;insetRight=width*(1-area.xMax/Screen.width);insetTop=height*(1-area.yMax/Screen.height);}
        corners.Inset(insetLeft,insetRight,insetTop);
        bool narrow=width<900;rowScroll.style.left=Mathf.Max(insetLeft+20,width*.07f);rowScroll.style.right=Mathf.Max(insetRight+20,width*.07f);
        rowScroll.style.top=Mathf.Max(insetTop+88,88);rowScroll.style.bottom=78;
        float contentWidth=rowScroll.contentViewport.resolvedStyle.width;
        if(float.IsFinite(contentWidth)&&contentWidth>0)
        {
            float gridWidth=Mathf.Floor(Mathf.Min(1160,contentWidth-10)),cell=narrow?gridWidth:(gridWidth-36)/2;
            foreach(var grid in fields){grid.style.width=gridWidth;int index=0;foreach(var item in grid.Children()){item.style.width=narrow?cell:Mathf.Min(502,cell);item.style.marginRight=!narrow&&index%2==0?Mathf.Max(0,gridWidth-Mathf.Min(502,cell)*2-2):0;index++;}}
        }
        foreach(var item in row)item.EnableInClassList("pn-settings-row-narrow",narrow);
        for(int i=0;i<animated.Count;i++)
        {
            float p=exiting?animationExit[i]*(1-EaseOut((elapsed-Mathf.Min(i*.015f,.05f))/.3f)):Ease((elapsed-Mathf.Min(i*.015f,.03f))/.73f);
            animated[i].style.opacity=p;animated[i].style.translate=new Translate(0,10*(1-p));
        }
        corners.style.opacity=exiting?cornerFrom*(1-EaseOut(elapsed/.2f)):artProgress;scrollFade.Tick(dt);
        foreach(var d in dropdowns)d.Tick(dt,Root);
        if(exiting&&elapsed>=.7f){Action callback=closeWhole?closeAll:back;Dispose();callback?.Invoke();}
    }
    public void Dispose()
    {if(disposed)return;disposed=true;PNLocalization.Changed-=Refresh;PNSettingsService.Changed-=Refresh;PNMobileStyleCatalog.Changed-=Refresh;Root.RemoveFromHierarchy();backdrop.Dispose();sparkles.Dispose();}
}
// Fading rules; only section dividers carry a star, headings have an underline alone.
internal sealed class PNSettingsRule : VisualElement
{
    private readonly bool divider;
    internal PNSettingsRule(bool divider)
    {
        this.divider=divider;pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;
        if(divider){var star=new PNPauseDrawing(PNPauseDrawing.Shape.Star){pickingMode=PickingMode.Ignore};star.style.position=Position.Absolute;star.style.left=Length.Percent(50);star.style.marginLeft=-6;star.style.top=3;star.style.width=12;star.style.height=14;Add(star);}
    }
    private void Draw(MeshGenerationContext context)
    {
        float w=contentRect.width,y=divider?10:1;if(w<=0)return;var p=context.painter2D;p.lineWidth=1;
        for(int i=0;i<48;i++){float a=(float)i/48,b=(float)(i+1)/48;if(divider&&Mathf.Abs((a+b)*.5f-.5f)*w<20)continue;
            p.strokeColor=new Color(1,1,1,Mathf.Sin((a+b)*.5f*Mathf.PI)*(divider?.55f:.85f));p.BeginPath();p.MoveTo(new Vector2(w*a,y));p.LineTo(new Vector2(w*b,y));p.Stroke();}
    }
}
// Owned by a settings scroll view; no scheduled callbacks or effects survive its view.
internal sealed class PNSettingsScrollFade
{
    private readonly ScrollView scroll;private Vector2 previous;private float idle=1,alpha;private bool initialized;
    internal PNSettingsScrollFade(ScrollView scroll){this.scroll=scroll;scroll.verticalScroller.style.opacity=0;}
    internal void Tick(float dt)
    {
        var offset=scroll.scrollOffset;if(initialized&&(offset-previous).sqrMagnitude>.001f)idle=0;else idle+=dt;previous=offset;initialized=true;
        float target=idle<.6f?.5f:0;alpha=Mathf.MoveTowards(alpha,target,dt/.15f*.5f);scroll.verticalScroller.style.opacity=alpha;
        scroll.verticalScroller.pickingMode=alpha>0?PickingMode.Position:PickingMode.Ignore;
    }
}
#endif
