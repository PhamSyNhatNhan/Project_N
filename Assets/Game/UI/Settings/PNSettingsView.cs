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
    private readonly PNSettingsDropdown language,fps;
    private readonly PNSettingsDropdown icons,palette;
    private readonly Action back,closeAll,closeAllStarted;
    private bool disposed,exiting,closeWhole;
    private float elapsed,clock,artProgress,exitFrom;
    private float exitAlpha;
    private readonly List<float> rowExitAlpha=new List<float>();
    private int cancelFrame=-1;
    internal bool IsExiting=>exiting;
    internal float Progress=>artProgress;
    public PNSettingsView(VisualElement parent,Action back,Action closeAll,Action closeAllStarted=null,bool mobileOptions=false)
    {
        PNSettingsService.EnsureLoaded();this.back=back;this.closeAll=closeAll;this.closeAllStarted=closeAllStarted;
        Root=new VisualElement{name="pn-settings-view",focusable=true};Stretch(Root);Root.AddToClassList("pn-settings-view");Root.style.opacity=0;
        var font=Resources.Load<Font>("PNFont/Philosopher-Regular");if(font!=null)Root.style.unityFontDefinition=FontDefinition.FromFont(font);
        sparkles=new PNSparkleArtwork();backdrop=new PNMenuBackdrop{SurfaceAlpha=1,DecorationAlpha=.32f};backdrop.Root.style.backgroundColor=new Color(184f/255,192f/255,196f/255);Root.Add(backdrop.Root);
        decoration=new PNSettingsDecoration();Root.Add(decoration);
        rowScroll=new ScrollView(ScrollViewMode.Vertical){name="pn-settings-rows",horizontalScrollerVisibility=ScrollerVisibility.Hidden,verticalScrollerVisibility=ScrollerVisibility.Auto};rowScroll.AddToClassList("pn-settings-rows");Root.Add(rowScroll);rows=rowScroll.contentContainer;
        bool showMobile=mobileOptions||PNMobilePlatform.IsMobile(PNMobilePlatform.UsesSimulatedScreen)||((Debug.isDebugBuild||Application.isEditor)&&Array.Exists(Environment.GetCommandLineArgs(),a=>a.StartsWith("-pn-mobile-style-review=")));
        string[] names={"language","fps","mobile-icons","mobile-palette"};string[] labels={"language","fps","mobile_icons","mobile_palette"};
        for(int i=0;i<(showMobile?4:2);i++){var item=new VisualElement{name="pn-settings-"+names[i]+"-row"};item.AddToClassList("pn-settings-row");row.Add(item);rowExitAlpha.Add(0);rows.Add(item);var label=new Label();label.AddToClassList("pn-settings-label");PNLocalizationUI.Bind(label,"project_n:settings."+labels[i]);item.Add(label);}
        // Overlay popups are outside the row's transform and clipping hierarchy.
        language=new PNSettingsDropdown("pn-settings-language",row[0],Root,Opening,code=>Apply(()=>PNSettingsService.SetLocale(code)));
        fps=new PNSettingsDropdown("pn-settings-fps",row[1],Root,Opening,code=>Apply(()=>PNSettingsService.SetFrameRate(int.Parse(code))));
        dropdowns.Add(language);dropdowns.Add(fps);
        if(showMobile){icons=new PNSettingsDropdown("pn-settings-mobile-icons",row[2],Root,Opening,id=>Apply(()=>PNSettingsService.SetMobileIconSet(id)));palette=new PNSettingsDropdown("pn-settings-mobile-palette",row[3],Root,Opening,id=>Apply(()=>PNSettingsService.SetMobilePalette(id)));dropdowns.Add(icons);dropdowns.Add(palette);}
        error=new Label{name="pn-settings-error"};error.AddToClassList("pn-settings-error");rows.Add(error);
        corners=new PNMenuCorner(()=>RequestClose(false),()=>RequestClose(true));Root.Add(corners);corners.Back.focusable=corners.Close.focusable=true;
        Root.RegisterCallback<PointerDownEvent>(e=>{if(dropdowns.Any(d=>d.IsOpen)&&!dropdowns.Any(d=>d.Trigger.worldBound.Contains(e.position)||(d.IsOpen&&d.Popup.worldBound.Contains(e.position)))){foreach(var d in dropdowns)d.Close(false);e.StopImmediatePropagation();}},TrickleDown.TrickleDown);
        Root.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape){Cancel();e.StopImmediatePropagation();}});
        Root.RegisterCallback<NavigationCancelEvent>(e=>{Cancel();e.StopImmediatePropagation();});
        PNLocalization.Changed+=Refresh;PNSettingsService.Changed+=Refresh;PNMobileStyleCatalog.Changed+=Refresh;parent.Add(Root);Refresh();Tick(0);language.Trigger.Focus();
    }
    internal static void Stretch(VisualElement e){e.style.position=Position.Absolute;e.style.left=0;e.style.right=0;e.style.top=0;e.style.bottom=0;}
    internal static float Ease(float t){t=Mathf.Clamp01(t);return t*t*t*(t*(t*6-15)+10);}
    private void Opening(PNSettingsDropdown opened){foreach(var d in dropdowns)if(d!=opened)d.Close(false);}
    private void Apply(Func<bool> change)
    {
        try{bool saved=change();error.text=saved?"":PNLocalization.Text("project_n:settings.save_failed");}
        catch(Exception e){Debug.LogWarning("Settings selection: "+e.Message);error.text=PNLocalization.Text("project_n:ui.language.failed");}
        Refresh();
    }
    private void Refresh()
    {
        if(disposed)return;
        var langs=PNLocalization.Languages;language.SetOptions(langs.Select(l=>l.Code).ToArray(),langs.Select(l=>l.NativeName).ToArray(),PNLocalization.Selected);
        string[] ids={"30","60","90","120","144","0"};fps.SetOptions(ids,ids.Select(s=>s=="0"?PNLocalization.Text("project_n:settings.unlimited"):s).ToArray(),PNSettingsService.Current.TargetFrameRate.ToString());
        language.Trigger.tooltip=PNLocalization.Text("project_n:settings.language");fps.Trigger.tooltip=PNLocalization.Text("project_n:settings.fps");
        if(icons!=null)
        {
            var sets=PNMobileStyleCatalog.IconSets;var palettes=PNMobileStyleCatalog.Palettes;var settings=PNSettingsService.Current;
            icons.SetOptions(sets.Select(s=>s.Id.ToString()).ToArray(),sets.Select(s=>PNLocalization.Text(s.DisplayNameKey)).ToArray(),PNMobileStyleCatalog.ResolveIconSet(settings.MobileIconSet).Id.ToString());
            palette.SetOptions(palettes.Select(s=>s.Id.ToString()).ToArray(),palettes.Select(s=>PNLocalization.Text(s.DisplayNameKey)).ToArray(),PNMobileStyleCatalog.ResolvePalette(settings.MobilePalette).Id.ToString());
        }
        corners.Back.tooltip=PNLocalization.Text("project_n:ui.back");corners.Close.tooltip=PNLocalization.Text("project_n:settings.close");
        if(PNSettingsService.SaveError!=null)error.text=PNLocalization.Text("project_n:settings.save_failed");
    }
    public void Cancel()
    {
        if(disposed||cancelFrame==Time.frameCount)return;cancelFrame=Time.frameCount;
        if(dropdowns.Any(d=>d.IsOpen)){foreach(var d in dropdowns)d.Close();return;}RequestClose(false);
    }
    private void RequestClose(bool whole)
    {
        if(disposed||exiting)return;exitAlpha=Root.resolvedStyle.opacity;for(int i=0;i<row.Count;i++)rowExitAlpha[i]=row[i].resolvedStyle.opacity;exiting=true;closeWhole=whole;elapsed=0;exitFrom=artProgress;
        foreach(var d in dropdowns)d.Close(false);rows.SetEnabled(false);corners.SetEnabled(false);
        if(whole)closeAllStarted?.Invoke();
    }
    public void Tick(float dt)
    {
        if(disposed)return;elapsed+=dt;clock+=dt;
        float width=Root.resolvedStyle.width,height=Root.resolvedStyle.height;if(!float.IsFinite(width)||width<=0||!float.IsFinite(height)||height<=0)return;
        artProgress=exiting?exitFrom*(1-Ease(elapsed/.9f)):Ease(elapsed/1.3f);
        Root.style.opacity=exiting?exitAlpha*(1-Ease((elapsed-.9f)/.18f)):Ease(elapsed/.32f);
        backdrop.Tick(dt);decoration.Animate(clock,artProgress,width,height);
        float insetLeft=0,insetRight=0,insetTop=0;var area=Screen.safeArea;
        if(Screen.width>0&&Screen.height>0){insetLeft=width*area.xMin/Screen.width;insetRight=width*(1-area.xMax/Screen.width);insetTop=height*(1-area.yMax/Screen.height);}
        corners.Inset(insetLeft,insetRight,insetTop);
        bool narrow=width<820;rowScroll.style.left=Mathf.Max(insetLeft+20,width*(narrow?.08f:.12f));rowScroll.style.top=Mathf.Max(insetTop+90,height*.23f);rowScroll.style.width=Mathf.Min(540,width-insetLeft-insetRight-(narrow?width*.16f:width*.24f));rowScroll.style.bottom=48;
        foreach(var item in row){item.EnableInClassList("pn-settings-row-narrow",narrow);}
        for(int i=0;i<row.Count;i++){float p=exiting?rowExitAlpha[i]*(1-Ease(elapsed/.24f)):Ease((elapsed-.1f-i*.07f)/.42f);row[i].style.opacity=p;row[i].style.translate=new Translate(-18*(1-p),0);}
        foreach(var d in dropdowns)d.Tick(dt,Root);
        if(exiting&&elapsed>=1.08f){Action callback=closeWhole?closeAll:back;Dispose();callback?.Invoke();}
    }
    public void Dispose()
    {if(disposed)return;disposed=true;PNLocalization.Changed-=Refresh;PNSettingsService.Changed-=Refresh;PNMobileStyleCatalog.Changed-=Refresh;Root.RemoveFromHierarchy();backdrop.Dispose();sparkles.Dispose();}
}
#endif
