#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem;

// Owns touch presentation and virtual input only. Skills interpret the five channels.
internal sealed class PNMobileControls : IDisposable
{
    private sealed class TouchControl
    {
        public VisualElement Element;
        public PNMobileVirtualControl Input;
        public int Pointer=-1;
        public string Channel, DefaultPath;
        public PNHudSparkles Sparkles;
        public float NextShimmer;
    }
    private readonly PNUiRuntime owner;
    private readonly VisualElement root, safe, zone, stick, thumb, buttons;
    private readonly PNMobileDirectionRing directionRing;
    private readonly GameObject transport;
    private readonly List<TouchControl> controls=new List<TouchControl>();
    private readonly Dictionary<GameObject,bool> legacy=new Dictionary<GameObject,bool>();
    private readonly TouchControl movement;
    private Vector2 origin;
    private bool visible, accepting;
    private PNStat player;
    public PNMobileControls(PNUiRuntime owner, VisualElement host)
    {
        this.owner=owner;
        root=new VisualElement { name="mobile-controls", pickingMode=PickingMode.Ignore };
        root.AddToClassList("mobile-controls"); host.Insert(0,root);
        safe=new VisualElement { name="mobile-safe-area", pickingMode=PickingMode.Ignore };
        safe.AddToClassList("mobile-safe-area"); root.Add(safe);
        zone=new VisualElement { name="mobile-move-zone" }; zone.AddToClassList("mobile-move-zone"); safe.Add(zone);
        stick=new VisualElement { name="mobile-stick", pickingMode=PickingMode.Ignore };
        stick.AddToClassList("mobile-stick"); zone.Add(stick);
        thumb=new VisualElement { pickingMode=PickingMode.Ignore }; thumb.AddToClassList("mobile-thumb"); stick.Add(thumb);
        directionRing=new PNMobileDirectionRing(); stick.Add(directionRing);
        buttons=new VisualElement { name="mobile-buttons", pickingMode=PickingMode.Ignore };
        buttons.AddToClassList("mobile-buttons"); safe.Add(buttons);
        transport=new GameObject("Toolkit mobile virtual controls"); transport.SetActive(false);
        transport.transform.SetParent(owner.transform,false);
        movement=Bind(zone,"<Gamepad>/leftStick",true);
        // Original ErMode cluster, scaled from2273px authored canvas to1280px reference.
        AddButton("Attack","buttonEast",365,201,128);
        AddButton("Skill","buttonWest",231,103,88);
        AddButton("Ulti","buttonNorth",352,57,88);
        AddButton("Dash","buttonSouth",197,230,88);
        AddButton("Burst","dpad/up",118,60,100);
        root.style.display=DisplayStyle.None; stick.style.display=DisplayStyle.None;
    }
    private void AddButton(string name,string path,float x,float y,float size)
    {
        var element=new VisualElement { name="mobile-"+name.ToLowerInvariant(), tooltip=name };
        element.AddToClassList("mobile-button");
        element.style.width=size; element.style.height=size;
        element.style.left=x-size/2; element.style.top=y-size/2;
        AddGlow(element);
        AddOuterRing(element);
        var image=new VisualElement { pickingMode=PickingMode.Ignore };
        image.AddToClassList("mobile-button-icon");
        image.style.backgroundImage=new StyleBackground(owner.Image("UI/Combat/AbilityPlaceholder"));
        element.Add(image); buttons.Add(element);
        var control=Bind(element,"<Gamepad>/"+path,false); control.Channel=name;
        control.Sparkles=new PNHudSparkles(element,"mobile-"+name.ToLowerInvariant()+"-sparkles",16);
    }
    private static void AddOuterRing(VisualElement host)
    {
        var ring=new VisualElement { pickingMode=PickingMode.Ignore };
        ring.AddToClassList("mobile-outer-ring"); host.Add(ring);
    }
    private static void AddGlow(VisualElement host)
    {
        var glow=new PNHudGlow(true) { name="mobile-glow" };
        glow.style.left=-11; glow.style.right=-11; glow.style.top=-11; glow.style.bottom=-11;
        host.Insert(0,glow);
    }
    private TouchControl Bind(VisualElement element,string path,bool isStick)
    {
        var control=new TouchControl { Element=element, Input=transport.AddComponent<PNMobileVirtualControl>(), DefaultPath=path };
        control.Input.Configure(path); controls.Add(control);
        element.RegisterCallback<PointerDownEvent>(e=>
        {
            if(!accepting || control.Pointer!=-1 || e.button!=0 || control.Input.control==null) return;
            if(isStick && owner.HudRoot.Q("player-hud").worldBound.Contains(e.position))return;
            control.Pointer=e.pointerId; element.CapturePointer(e.pointerId);
            if(isStick)
            {
                origin=zone.WorldToLocal(e.position);
                stick.style.left=origin.x-70; stick.style.top=origin.y-70;
                stick.style.display=DisplayStyle.Flex; Move(e.position);
            }
            else { control.Input.PushButton(true); element.AddToClassList("mobile-pressed");control.NextShimmer=Time.unscaledTime; }
            e.StopPropagation();
        });
        element.RegisterCallback<PointerMoveEvent>(e=>
        {
            if(control.Pointer!=e.pointerId)return;
            if(isStick)Move(e.position); e.StopPropagation();
        });
        element.RegisterCallback<PointerUpEvent>(e=> { if(control.Pointer==e.pointerId){Release(control);e.StopPropagation();} });
        element.RegisterCallback<PointerCancelEvent>(e=> { if(control.Pointer==e.pointerId)Release(control); });
        element.RegisterCallback<PointerCaptureOutEvent>(e=> { if(control.Pointer==e.pointerId)Release(control); });
        return control;
    }
    private void Move(Vector2 panelPosition)
    {
        var delta=Vector2.ClampMagnitude(zone.WorldToLocal(panelPosition)-origin,50);
        thumb.style.translate=new Translate(delta.x,delta.y);
        // Toolkit Y grows downward; gamepad Y grows upward. Keep the thumb in UI space.
        movement.Input.PushStick(new Vector2(delta.x,-delta.y)/50);
        directionRing.PullDistance=delta.magnitude;
        directionRing.style.rotate=new Rotate(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
    }
    private void Release(TouchControl control)
    {
        int pointer=control.Pointer; control.Pointer=-1;
        if(control==movement)
        { control.Input.PushStick(Vector2.zero); stick.style.display=DisplayStyle.None; thumb.style.translate=new Translate(0,0); directionRing.PullDistance=0; }
        else { control.Input.PushButton(false); control.Element.RemoveFromClassList("mobile-pressed");control.Sparkles?.CancelPending(Time.unscaledTime); }
        if(pointer!=-1 && control.Element.HasPointerCapture(pointer))control.Element.ReleasePointer(pointer);
    }
    public void Reset() { foreach(var control in controls){Release(control);control.Sparkles?.Clear();} }
    public void Refresh(PNStat current)
    {
        bool simulatedScreen=PNMobilePlatform.UsesSimulatedScreen;
        bool show=(PNMobilePlatform.IsMobile(simulatedScreen) || owner.ShowMobileControls) && PNVitals.TryRead(current,PNContentIds.Health,out _,out _,out var dead) && !dead;
        if(player!=current){Reset();player=current;}
        var skill=current!=null?current.GetComponent<PNPlayerSkill>():null;
        foreach(var control in controls)
        {
            if(control.Channel==null)continue;
            var actions=skill?.PC?.Controller;
            InputAction action=actions==null?null:control.Channel=="Attack"?actions.Attack:
                control.Channel=="Skill"?actions.Skill:control.Channel=="Ulti"?actions.Ulti:
                control.Channel=="Dash"?actions.Dash:actions.Burst;
            string path=action==null?control.DefaultPath:"";
            if(action!=null)foreach(var binding in action.bindings)
                if(!binding.isComposite && !binding.isPartOfComposite &&
                    !string.IsNullOrEmpty(binding.effectivePath) && binding.effectivePath.StartsWith("<Gamepad>/",StringComparison.Ordinal))
                { path=binding.effectivePath; break; }
            if(control.Input.controlPath!=path){Release(control);control.Input.Configure(path);}
        }
        if(show!=visible)
        {
            Reset(); visible=show; root.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            if(show)SuppressLegacy(); else RestoreLegacy();
            transport.SetActive(show);
        }
        accepting=show && !owner.MenuOpen && Time.timeScale>0 && (Application.isEditor || Application.isFocused) && !owner.TestExpanded;
        if(!accepting)Reset();
        foreach(var control in controls)
        {
            if(control.Sparkles==null)continue;
            if(control.Pointer!=-1 && accepting && Time.unscaledTime>=control.NextShimmer)
            {control.Sparkles.Ring(3,new Color(1,1,1,.67f),Time.unscaledTime,true);control.NextShimmer=Time.unscaledTime+.43f;}
            control.Sparkles.Tick(Time.unscaledTime);
        }
        // Keep the virtual device and legacy suppression alive under the menu.
        root.style.display=show&&!owner.MenuOpen?DisplayStyle.Flex:DisplayStyle.None;
        if(!show)return;
        SuppressLegacy();
        var area=simulatedScreen?UnityEngine.Device.Screen.safeArea:Screen.safeArea;
        int screenWidth=simulatedScreen?UnityEngine.Device.Screen.width:Screen.width;
        int screenHeight=simulatedScreen?UnityEngine.Device.Screen.height:Screen.height;
        // Read both coordinates from the selected view, including forced desktop previews.
        if(area.width<=0 || area.height<=0 || area.xMin<0 || area.yMin<0 ||
            area.xMax>screenWidth+.5f || area.yMax>screenHeight+.5f)
            area=new Rect(0,0,screenWidth,screenHeight);
        float width=owner.HudRoot.resolvedStyle.width, height=owner.HudRoot.resolvedStyle.height;
        if(width<=0 || height<=0 || screenWidth<=0 || screenHeight<=0)return;
        safe.style.left=width*area.xMin/screenWidth; safe.style.right=width*(1-area.xMax/screenWidth);
        safe.style.top=height*(1-area.yMax/screenHeight); safe.style.bottom=height*area.yMin/screenHeight;
        float scale=Mathf.Min(1,width*area.width/screenWidth*.48f/500,height*area.height/screenHeight*.52f/320);
        buttons.style.scale=new Scale(new Vector3(scale,scale,1));
    }
    private void SuppressLegacy()
    {
        foreach(var control in UnityEngine.Object.FindObjectsByType<OnScreenControl>(FindObjectsInactive.Include))
        {
            if(control is PNMobileVirtualControl)continue;
            for(var parent=control.transform;parent!=null;parent=parent.parent)
                if(parent.name=="Mobile")
                {
                    if(!legacy.ContainsKey(parent.gameObject))legacy.Add(parent.gameObject,parent.gameObject.activeSelf);
                    parent.gameObject.SetActive(false); break;
                }
        }
    }
    private void RestoreLegacy()
    { foreach(var pair in legacy)if(pair.Key!=null)pair.Key.SetActive(pair.Value); legacy.Clear(); }
    public void Dispose()
    {
        Reset(); transport.SetActive(false); RestoreLegacy(); root.RemoveFromHierarchy(); PNUiRuntime.Retire(transport);
    }
}

// Decorative direction rim, pointing right before Toolkit rotation is applied.
public sealed class PNMobileDirectionRing : VisualElement
{
    private float nubLength;
    public bool HasDirection=>nubLength>0;
    public float NubLength=>nubLength;
    public float PullDistance
    {
        set
        {
            float next=8*Mathf.InverseLerp(5,50,value);
            if(next==nubLength || (next>0 && nubLength>0 && Mathf.Abs(next-nubLength)<.005f))return;
            nubLength=next;MarkDirtyRepaint();
        }
    }
    public PNMobileDirectionRing()
    {
        name="mobile-direction-ring"; pickingMode=PickingMode.Ignore;
        AddToClassList("mobile-direction-ring");
        generateVisualContent+=Draw;
    }
    private void Draw(MeshGenerationContext context)
    {
        var p=context.painter2D;
        // The same path supplies the feathered halo and the visible rim, including its nub.
        for(int layer=0;layer<3;layer++)
        {
            p.lineWidth=12-4*layer;
            p.strokeColor=new Color(1,1,1,.012f+.008f*layer);
            Path(p); p.Stroke();
        }
        p.strokeColor=Color.white; p.lineWidth=1.5f;
        Path(p);p.Stroke();
    }
    private void Path(Painter2D p)
    {
        var center=contentRect.center;
        const float radius=76;
        p.BeginPath();
        if(!HasDirection)
        { p.Arc(center,radius,0,360);p.ClosePath();return; }
        float angle=12*Mathf.Deg2Rad;
        var lower=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
        var upper=center+new Vector2(Mathf.Cos(angle),-Mathf.Sin(angle))*radius;
        var tip=center+new Vector2(radius+nubLength,0);
        // Circle tangents at zero extension; a smooth rounded bump as the tip grows.
        float tangent=4f/3*Mathf.Tan(angle/4)*radius;
        p.Arc(center,radius,12,348);
        p.BezierCurveTo(upper+new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*tangent,tip+new Vector2(0,-tangent),tip);
        p.BezierCurveTo(tip+new Vector2(0,tangent),lower+new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle))*tangent,lower);
        p.ClosePath();
    }
}
#endif
