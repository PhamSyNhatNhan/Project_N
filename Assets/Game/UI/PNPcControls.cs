#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Five display-only action channels. The skill owns input; this view owns visuals.
internal sealed class PNPcControls : IDisposable
{
    private sealed class Slot
    {
        internal VisualElement Button;
        internal Label Key;
        internal PNMobileIconView Icon;
        internal PNHudSparkles Sparkles;
        internal readonly PNPcBindingLabel Binding=new PNPcBindingLabel();
        internal bool Held;
        internal float NextShimmer;
    }
    private readonly PNUiRuntime owner;
    private readonly VisualElement root,cluster;
    private readonly PNSparkleGlyph centreStar;
    private readonly Slot[] slots=new Slot[5];
    private bool disposed;
    private PNPlayerSkillControls boundControls;
    internal PNPcControls(PNUiRuntime owner,VisualElement host)
    {
        this.owner=owner;
        root=new VisualElement{name="pc-controls",pickingMode=PickingMode.Ignore};root.AddToClassList("pc-controls");host.Insert(0,root);
        cluster=new VisualElement{name="pc-action-cluster",pickingMode=PickingMode.Ignore};cluster.AddToClassList("pc-action-cluster");root.Add(cluster);
        var font=Resources.Load<Font>("PNFont/Philosopher-Regular");if(font!=null)root.style.unityFontDefinition=FontDefinition.FromFont(font);
        // Same rounded star/owned halo as the pause toggle; no separate artwork lease.
        centreStar=new PNSparkleGlyph(true){name="pc-centre-star",pickingMode=PickingMode.Ignore};
        centreStar.style.position=Position.Absolute;centreStar.style.left=295;centreStar.style.top=255;
        centreStar.style.width=30;centreStar.style.height=40;centreStar.style.opacity=.78f;
        centreStar.SetGlow(new Color(.8f,.9f,1));cluster.Add(centreStar);
        Add(0,"attack",372,275,323,275);Add(1,"skill",310,194,310,243);
        Add(2,"ulti",248,275,297,275);Add(3,"dash",310,356,310,307);Add(4,"burst",370,109,370,158);
        PNSettingsService.Changed+=ApplyStyle;PNMobileStyleCatalog.Changed+=ApplyStyle;
        InputSystem.onActionChange+=ActionChanged;root.style.display=DisplayStyle.None;ApplyStyle();
    }
    private void Add(int index,string name,float x,float y,float kx,float ky)
    {
        var button=new VisualElement{name="pc-"+name,pickingMode=PickingMode.Ignore};button.AddToClassList("mobile-button");button.AddToClassList("pc-action-button");
        button.style.left=x-30;button.style.top=y-30;cluster.Add(button);
        var glow=new PNHudGlow(true){pickingMode=PickingMode.Ignore};glow.style.left=-11;glow.style.right=-11;glow.style.top=-11;glow.style.bottom=-11;button.Add(glow);
        var ring=new VisualElement{pickingMode=PickingMode.Ignore};ring.AddToClassList("mobile-outer-ring");button.Add(ring);
        // Dedicated viewport: 20% smaller than the mobile 68% viewport, leaving rings untouched.
        var icon=new PNMobileIconView();icon.AddToClassList("pc-action-icon");button.Add(icon);
        var key=new Label("—"){name="pc-"+name+"-binding",pickingMode=PickingMode.Ignore};key.AddToClassList("pc-action-binding");key.style.left=kx-10;key.style.top=ky-10;cluster.Add(key);
        slots[index]=new Slot{Button=button,Key=key,Icon=icon,Sparkles=new PNHudSparkles(button,"pc-"+name+"-sparkles",16)};
    }
    private void ActionChanged(object value,InputActionChange change)
    {if(change==InputActionChange.BoundControlsChanged)foreach(var slot in slots)slot.Binding.Invalidate();}
    private void ApplyStyle()
    {
        var prefs=PNSettingsService.Current;var icons=PNMobileStyleCatalog.ResolveIconSet(prefs.MobileIconSet);var palette=PNMobileStyleCatalog.ResolvePalette(prefs.MobilePalette);
        centreStar.style.display=prefs.ShowPcBindingLabels?DisplayStyle.None:DisplayStyle.Flex;
        for(int i=0;i<slots.Length;i++){slots[i].Icon.Set(icons.Icons[i],palette.Colors[i]);slots[i].Key.style.display=prefs.ShowPcBindingLabels?DisplayStyle.Flex:DisplayStyle.None;}
    }
    internal void Reset()
    {foreach(var slot in slots){slot.Held=false;slot.NextShimmer=0;slot.Button.RemoveFromClassList("mobile-pressed");slot.Sparkles.Clear();}}
    internal void Refresh(PNStat player)
    {
        if(disposed)return;
        var skill=player!=null?player.GetComponent<PNPlayerSkill>():null;var actions=skill?.PC?.Controller;
        if(!ReferenceEquals(boundControls,skill?.PC)){Reset();boundControls=skill?.PC;}
        bool dead=PNVitals.TryRead(player,PNContentIds.Health,out _,out _,out var isDead)&&isDead;
        bool show=PNSettingsService.Current.ShowPcControls&&!PNMobilePlatform.IsMobile(PNMobilePlatform.UsesSimulatedScreen)&&!owner.ShowMobileControls&&
            player!=null&&actions!=null&&skill.CanInput&&!dead&&!owner.MenuOpen&&!owner.TestExpanded&&Time.timeScale>0&&Application.isFocused;
        for(int i=0;i<slots.Length;i++)
        {
            InputAction action=actions==null?null:i==0?actions.Attack:i==1?actions.Skill:i==2?actions.Ulti:i==3?actions.Dash:actions.Burst;
            slots[i].Binding.Bind(action);
        }
        root.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        if(!show){Reset();return;}
        float now=Time.unscaledTime;
        foreach(var slot in slots)
        {
            bool held=slot.Binding.Refresh();
            if(slot.Key.text!=slot.Binding.Text){slot.Key.text=slot.Binding.Text;slot.Key.tooltip=slot.Binding.FullText;slot.Key.style.fontSize=slot.Key.text.Length>1?Mathf.Clamp(28f/slot.Key.text.Length,4,10):13;}
            if(held!=slot.Held)
            {
                slot.Held=held;slot.Button.EnableInClassList("mobile-pressed",held);
                if(held)slot.NextShimmer=now;else slot.Sparkles.CancelPending(now);
            }
            if(held&&now>=slot.NextShimmer){slot.Sparkles.Ring(3,new Color(1,1,1,.67f),now,true);slot.NextShimmer=now+.43f;}
            slot.Sparkles.Tick(now);
        }
        float width=owner.HudRoot.resolvedStyle.width,height=owner.HudRoot.resolvedStyle.height;
        if(!float.IsFinite(width)||!float.IsFinite(height)||width<=0||height<=0||Screen.width<=0||Screen.height<=0)return;
        var area=Screen.safeArea;if(area.width<=0||area.height<=0)area=new Rect(0,0,Screen.width,Screen.height);
        root.style.left=width*area.xMin/Screen.width;root.style.right=width*(1-area.xMax/Screen.width);
        root.style.top=height*(1-area.yMax/Screen.height);root.style.bottom=height*area.yMin/Screen.height;
        float scale=Mathf.Min(1,width*area.width/Screen.width*.48f/430,height*area.height/Screen.height*.68f/430);
        cluster.style.scale=new Scale(new Vector2(scale,scale));
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;PNSettingsService.Changed-=ApplyStyle;PNMobileStyleCatalog.Changed-=ApplyStyle;InputSystem.onActionChange-=ActionChanged;
        Reset();boundControls=null;foreach(var slot in slots)slot.Binding.Bind(null);root.RemoveFromHierarchy();
    }
}
#endif
