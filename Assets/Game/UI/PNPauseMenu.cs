#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Local menu ownership is separate from gameplay locks and from future global TimeStop.
internal sealed class PNPauseMenu : IDisposable
{
    private readonly PNUiRuntime owner;
    private readonly VisualElement overlay, surface, card, safe, system, core, aura, sky, frontStar, backStar;
    private readonly VisualElement frontStarSecond,backStarSecond;
    private readonly Button toggle, resume;
    private readonly Button leave;
    private readonly List<Part> parts=new List<Part>();
    private readonly Texture2D lightTexture, coreTexture;
    private readonly List<PNPlayerMove> moves=new List<PNPlayerMove>();
    private readonly List<PNPlayerSkill> skills=new List<PNPlayerSkill>();
    private readonly InputAction menu;
    private bool closing, ownsTime, disposed;
    private float previousTime, elapsed;
    private float surfaceAlpha, closeAlpha, orbitTime;
    private sealed class SkyGlint {internal VisualElement Element;internal Vector2 Center;internal float Start,Life;}
    private readonly List<SkyGlint> skyGlints=new List<SkyGlint>();
    private readonly System.Random skyRandom=new System.Random();
    internal const float EnterDuration=1f, ExitDuration=.58f;
    private Guid session;
    public bool IsOpen { get; private set; }
    // The safe-area layer and the star's side, so a button can sit beside the star (the lobby character button).
    internal VisualElement SafeArea => safe;
    internal float ToggleSize { get { float w = toggle.resolvedStyle.width; return float.IsFinite(w) && w > 0 ? w : 56; } }

    public PNPauseMenu(PNUiRuntime owner, VisualElement root)
    {
        this.owner=owner;
        safe=new VisualElement { name="menu-safe-area", pickingMode=PickingMode.Ignore };
        safe.AddToClassList("menu-safe-area"); root.Add(safe);
        toggle=new Button(Toggle) { name="toggle-pause", tooltip="Menu" };
        toggle.AddToClassList("pause-toggle"); safe.Add(toggle);
        var icon=new PNPauseDrawing(PNPauseDrawing.Shape.Icon);icon.AddToClassList("pause-sparkle-icon");toggle.Add(icon);
        overlay=new VisualElement { name="pause-overlay" }; overlay.AddToClassList("pause-overlay"); root.Add(overlay);
        surface=new VisualElement { name="pause-surface", pickingMode=PickingMode.Ignore };
        surface.AddToClassList("pause-surface"); surface.AddToClassList("pn-panel-surface"); overlay.Add(surface);
        lightTexture=PNPauseArtwork.Gradient(false);coreTexture=PNPauseArtwork.Gradient(true);
        system=new VisualElement { name="pause-system",pickingMode=PickingMode.Ignore };system.AddToClassList("pause-system");surface.Add(system);
        aura=Box("pause-soft-light",-110,-110,820,820);aura.style.backgroundImage=lightTexture;system.Add(aura);
        var dial=new PNPauseDrawing(PNPauseDrawing.Shape.Dial) { name="pause-dial" };Place(dial,0,0,600,600);system.Add(dial);
        AddPart(dial,2,.23f,.76f,.01f,new Vector2(.23f,.23f));
        var rearMain=Orbit(false,0);var rearSoft=Orbit(false,1);system.Add(rearMain);system.Add(rearSoft);
        backStar=new PNPauseDrawing(PNPauseDrawing.Shape.Star) { name="pause-star-back" };Place(backStar,0,0,24,28);system.Add(backStar);
        backStarSecond=new PNPauseDrawing(PNPauseDrawing.Shape.Star) {name="pause-star-second-back"};Place(backStarSecond,0,0,24,28);system.Add(backStarSecond);
        core=Box("pause-core",171,171,258,258);core.AddToClassList("pause-core");core.style.backgroundImage=coreTexture;system.Add(core);
        AddPart(core,0,0,.76f,0,new Vector2(.16f,.16f));
        var frontMain=Orbit(true,0);var frontSoft=Orbit(true,1);system.Add(frontMain);system.Add(frontSoft);
        frontStar=new PNPauseDrawing(PNPauseDrawing.Shape.Star) { name="pause-star-front" };Place(frontStar,0,0,24,28);frontStar.style.overflow=Overflow.Visible;system.Add(frontStar);
        frontStarSecond=new PNPauseDrawing(PNPauseDrawing.Shape.Star) {name="pause-star-second-front"};Place(frontStarSecond,0,0,24,28);frontStarSecond.style.overflow=Overflow.Visible;system.Add(frontStarSecond);
        AddPart(rearMain,1,.09f,.76f,.06f,new Vector2(.16f,.04f));AddPart(frontMain,1,.09f,.76f,.06f,new Vector2(.16f,.04f));
        AddPart(rearSoft,1,.17f,.76f,.03f,new Vector2(.12f,.03f));AddPart(frontSoft,1,.17f,.76f,.03f,new Vector2(.12f,.03f));
        sky=new VisualElement { name="pause-sky",pickingMode=PickingMode.Ignore };sky.AddToClassList("pause-sky");surface.Insert(0,sky);
        var positions=new[]{new Vector2(13,17),new Vector2(32,28),new Vector2(42,69),new Vector2(9,77),new Vector2(52,15),new Vector2(28,85),new Vector2(82,17),new Vector2(91,78)};
        for(int i=0;i<positions.Length;i++)
        {
            var star=new PNPauseDrawing(PNPauseDrawing.Shape.Star);Place(star,0,0,i==0?18:12,i==0?22:16);
            star.style.left=Length.Percent(positions[i].x);star.style.top=Length.Percent(positions[i].y);sky.Add(star);
            var glint=new SkyGlint {Element=star,Center=positions[i]};skyGlints.Add(glint);ResetSky(glint,-(float)skyRandom.NextDouble()*3);
        }
        card=new VisualElement { name="pause-card", pickingMode=PickingMode.Ignore }; card.AddToClassList("pause-card"); overlay.Add(card);
        resume=new Button(Close) { name="pause-continue", text="Tiếp tục" }; resume.AddToClassList("pause-choice"); resume.AddToClassList("pause-primary"); card.Add(resume);
        foreach(var item in new[]{new[]{"pause-settings","Cài đặt"},new[]{"pause-leave","Rời phòng"}})
        { var button=new Button { name=item[0], text=item[1] }; button.AddToClassList("pause-choice"); card.Add(button); PNLocalizationUI.Bind(button,item[0]=="pause-settings"?"project_n:ui.settings":"project_n:ui.leave");if(item[0]=="pause-settings")button.clicked+=OpenLanguages; if(item[0]=="pause-leave"){leave=button;button.clicked+=Leave;button.SetEnabled(false);} }
        PNLocalizationUI.Bind(resume,"project_n:ui.resume");
        int choiceIndex=0;
        foreach(var button in card.Children())
        {
            var halo=new PNPauseDrawing(PNPauseDrawing.Shape.ButtonHalo);halo.AddToClassList("pause-button-halo");button.Insert(0,halo);
            AddPart(button,3,.26f+choiceIndex*.07f,.56f,(2-choiceIndex)*.045f,Vector2.one);choiceIndex++;
        }
        overlay.style.display=DisplayStyle.None;
        menu=new InputAction("Local menu",InputActionType.Button);
        menu.AddBinding("<Keyboard>/escape"); menu.AddBinding("<Gamepad>/start");
        menu.performed+=OnMenu; menu.Enable();
    }
    // Leaves the lobby/run for the Hall. Closing first keeps the menu animation and time scale consistent with a normal resume.
    private void Leave(){ if(!IsOpen||PNGameFlow.Busy)return; Close(); PNGameFlow.ReturnToHall(); }
    private PNSettingsView settingsView;
    private void OpenLanguages()
    {
        if(settingsView!=null||closing||!IsOpen)return;
        card.style.display=DisplayStyle.None;
        settingsView=new PNSettingsView(overlay,
            ()=>{settingsView=null;card.style.display=DisplayStyle.Flex;resume.Focus();},
            ()=>{settingsView=null;FinishClose();card.style.display=DisplayStyle.Flex;surface.style.visibility=Visibility.Visible;},
            ()=>{surface.style.visibility=Visibility.Hidden;},owner.ShowMobileControls);
    }
    private void OnMenu(InputAction.CallbackContext _) => Toggle();
    private void Toggle() { if(IsOpen)Close();else Open(); }
    public void Open()
    {
        if(disposed || IsOpen || owner.FlowOpen || !EveRegistrySession.IsReady)return;
        session=EveRegistrySession.Context.SessionId;
        IsOpen=true; closing=false; elapsed=0;surfaceAlpha=0;
        owner.ResetMobileInput(); AcquireLocalInput();
        if(EveRegistrySession.Context.Role==EveSessionRole.Offline)
        { previousTime=Time.timeScale; ownsTime=true; Time.timeScale=0; }
        overlay.style.display=DisplayStyle.Flex; toggle.style.display=DisplayStyle.None;
        Animate(); resume.Focus();
    }
    public void Close()
    {
        if(!IsOpen || closing)return;
        if(settingsView!=null){settingsView.Cancel();return;}
        closing=true;elapsed=0;closeAlpha=surfaceAlpha;
        foreach(var part in parts)part.Capture();
    }
    public void Tick()
    {
        if(disposed)return;
        FitSafeArea();
        if(IsOpen && (!EveRegistrySession.IsReady || EveRegistrySession.Context.SessionId!=session))
        { Abort();return; }
        if(IsOpen)
        {
            settingsView?.Tick(Time.unscaledDeltaTime);
            AcquireLocalInput();
            elapsed+=Time.unscaledDeltaTime;
            if(closing)
            {
                Animate();
                if(elapsed>=ExitDuration)FinishClose();
            }
            else Animate();
        }
        else ReleaseNeutralInput();
        toggle.SetEnabled(EveRegistrySession.IsReady);
        var phase=PNGameFlow.Phase;
        leave?.SetEnabled(EveRegistrySession.IsReady&&(phase==PNFlowPhase.Lobby||phase==PNFlowPhase.Run)&&!PNGameFlow.Busy);
    }
    private sealed class Part
    {
        internal VisualElement Element;internal int Kind;internal float DelayIn,DurationIn,DelayOut,Alpha,FromAlpha,Y,FromY;
        internal Vector2 Initial,Scale,FromScale;
        internal void Capture(){FromScale=Scale;FromAlpha=Alpha;FromY=Y;}
    }
    private static void Place(VisualElement e,float x,float y,float w,float h)
    {e.style.position=Position.Absolute;e.style.left=x;e.style.top=y;e.style.width=w;e.style.height=h;}
    private static VisualElement Box(string name,float x,float y,float w,float h)
    {var e=new VisualElement { name=name,pickingMode=PickingMode.Ignore };Place(e,x,y,w,h);return e;}
    private VisualElement Orbit(bool front,int index)
    {
        var e=new PNPauseDrawing(front?PNPauseDrawing.Shape.FrontOrbit:PNPauseDrawing.Shape.Orbit) { name=front?"pause-front-ring-"+index:"pause-ring-"+index };
        if(index==0)Place(e,40,181,520,236);else Place(e,-13,104,626,390);
        e.style.rotate=new Rotate(index==0?-27:28);if(index==1)e.style.opacity=.52f;return e;
    }
    private void AddPart(VisualElement e,int kind,float delay,float duration,float outDelay,Vector2 initial)
    {parts.Add(new Part { Element=e,Kind=kind,DelayIn=delay,DurationIn=duration,DelayOut=outDelay,Initial=initial });}
    private void Animate()
    {
        surfaceAlpha=closing?closeAlpha*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((elapsed-.308f)/.252f))):Mathf.Clamp01(elapsed/.3f);
        surface.style.opacity=surfaceAlpha;card.style.opacity=1;
        // Menus remain outside the decoration's transform/clipping hierarchy.
        foreach(var part in parts)
        {
            if(!closing)
            {
                float t=Mathf.Clamp01((elapsed-part.DelayIn)/part.DurationIn);
                float eased=PNPauseArtwork.Curve(t,.22f,.72f,.18f,1);
                part.Scale=Vector2.Lerp(part.Initial,Vector2.one,eased);part.Alpha=Mathf.Clamp01(t*1.8f);part.Y=part.Kind==3?16*(1-eased):0;
            }
            else
            {
                float duration=part.Kind==3?.26f:.56f-part.DelayOut;
                float t=Mathf.Clamp01((elapsed-part.DelayOut)/duration);
                if(part.Kind==0)
                {
                    float p=PNPauseArtwork.Curve(t,.4f,.1f,.38f,1);
                    float size=p<.78f?Mathf.Lerp(1,.18f,p/.78f):Mathf.Lerp(.18f,.08f,(p-.78f)/.22f);
                    part.Scale=part.FromScale*size;part.Alpha=part.FromAlpha*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((p-.78f)/.22f)));
                }
                else if(part.Kind==3)
                {float p=Mathf.SmoothStep(0,1,t);part.Scale=Vector2.Lerp(part.FromScale,part.FromScale*.92f,p);part.Alpha=part.FromAlpha*(1-Mathf.Clamp01(t*1.3f));part.Y=Mathf.Lerp(part.FromY,-10,p);}
                else
                {
                    float p=PNPauseArtwork.Curve(t,.55f,0,.25f,1);
                    float x=p<.4f?Mathf.Lerp(1,.85f,p/.4f):Mathf.Lerp(.85f,.04f,(p-.4f)/.6f);
                    float y=p<.4f?Mathf.Lerp(1,.12f,p/.4f):Mathf.Lerp(.12f,.015f,(p-.4f)/.6f);
                    part.Scale=Vector2.Scale(part.FromScale,new Vector2(x,y));part.Alpha=part.FromAlpha*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((p-.76f)/.24f)));part.Y=0;
                }
            }
            part.Element.style.scale=new Scale(part.Scale);part.Element.style.opacity=part.Alpha*(part.Element.name.Contains("ring-1")?.52f:1);
            part.Element.style.translate=new Translate(0,part.Y);
        }
        float skyAlpha=closing?1-Mathf.Clamp01(elapsed/.22f):Mathf.Clamp01((elapsed-.3f)/.5f);
        sky.style.opacity=skyAlpha;
        foreach(var glint in skyGlints)
        {
            float t=(orbitTime-glint.Start)/glint.Life;
            if(t>=1){ResetSky(glint,orbitTime+.25f+(float)skyRandom.NextDouble()*.9f);t=-1;}
            float alpha=t<0?0:Mathf.Pow(Mathf.Sin(Mathf.PI*Mathf.Clamp01(t)),2)*.75f;
            glint.Element.style.opacity=alpha;float size=.65f+alpha*.5f;glint.Element.style.scale=new Scale(new Vector2(size,size));
        }
        orbitTime+=Time.unscaledDeltaTime;
        float angle=(.14f+orbitTime/26)*Mathf.PI*2;
        var point=new Vector2(260*Mathf.Cos(angle),118*Mathf.Sin(angle));float tilt=-27*Mathf.Deg2Rad;
        var position=new Vector2(point.x*Mathf.Cos(tilt)-point.y*Mathf.Sin(tilt),point.x*Mathf.Sin(tilt)+point.y*Mathf.Cos(tilt));
        var main=parts.Find(p=>p.Element.name=="pause-ring-0");
        // Use the ring's local scale before tilt, and keep each complete star unmasked.
        point=Vector2.Scale(point,main.Scale);position=new Vector2(point.x*Mathf.Cos(tilt)-point.y*Mathf.Sin(tilt),point.x*Mathf.Sin(tilt)+point.y*Mathf.Cos(tilt));
        var starPosition=new Translate(300+position.x-12,299+position.y-14);
        frontStar.style.translate=starPosition;backStar.style.translate=starPosition;
        frontStar.style.opacity=main.Alpha;backStar.style.opacity=main.Alpha;
        frontStar.style.visibility=Mathf.Sin(angle)>=0?Visibility.Visible:Visibility.Hidden;
        backStar.style.visibility=Mathf.Sin(angle)<0?Visibility.Visible:Visibility.Hidden;
        AnimateSecondStar();
    }
    private void AnimateSecondStar()
    {
        float angle=(.62f+orbitTime/34)*Mathf.PI*2,tilt=28*Mathf.Deg2Rad;
        var ring=parts.Find(part=>part.Element.name=="pause-ring-1");
        var point=Vector2.Scale(new Vector2(313*Mathf.Cos(angle),195*Mathf.Sin(angle)),ring.Scale);
        var position=new Vector2(point.x*Mathf.Cos(tilt)-point.y*Mathf.Sin(tilt),point.x*Mathf.Sin(tilt)+point.y*Mathf.Cos(tilt));
        var translation=new Translate(300+position.x-12,299+position.y-14);
        frontStarSecond.style.translate=translation;backStarSecond.style.translate=translation;
        frontStarSecond.style.opacity=ring.Alpha*.8f;backStarSecond.style.opacity=ring.Alpha*.8f;
        frontStarSecond.style.visibility=Mathf.Sin(angle)>=0?Visibility.Visible:Visibility.Hidden;
        backStarSecond.style.visibility=Mathf.Sin(angle)<0?Visibility.Visible:Visibility.Hidden;
    }
    private void ResetSky(SkyGlint glint,float start)
    {
        glint.Start=start;glint.Life=2.4f+(float)skyRandom.NextDouble()*3;
        glint.Element.style.opacity=0;
        glint.Element.style.left=Length.Percent(Mathf.Clamp(glint.Center.x+((float)skyRandom.NextDouble()-.5f)*12,3,97));
        glint.Element.style.top=Length.Percent(Mathf.Clamp(glint.Center.y+((float)skyRandom.NextDouble()-.5f)*12,3,97));
    }
    private void FitSafeArea()
    {
        bool simulatedScreen=PNMobilePlatform.UsesSimulatedScreen;
        var area=simulatedScreen?UnityEngine.Device.Screen.safeArea:Screen.safeArea;
        float sw=simulatedScreen?UnityEngine.Device.Screen.width:Screen.width;
        float sh=simulatedScreen?UnityEngine.Device.Screen.height:Screen.height;
        float w=owner.HudRoot.resolvedStyle.width,h=owner.HudRoot.resolvedStyle.height;
        if(sw<=0 || sh<=0 || !float.IsFinite(w) || !float.IsFinite(h))return;
        if(area.width<=0 || area.height<=0 || area.xMin<0 || area.yMin<0 || area.xMax>sw+.5f || area.yMax>sh+.5f)area=new Rect(0,0,sw,sh);
        safe.style.top=h*(1-area.yMax/sh);safe.style.right=w*(1-area.xMax/sw);
        float available=w*area.width/sw;
        card.style.width=Mathf.Min(300,available-48);
        card.style.right=w*(1-area.xMax/sw)+Mathf.Max(24,available*.09f);
        card.style.top=h< w*.8f?h*.5f:h*.57f;card.style.translate=new Translate(0,-123);
        system.style.left=w*(available<700?.09f:.17f)-300;system.style.top=h*(h<w*.8f?.54f:.36f)-300;
        float artScale=Mathf.Clamp(h/520,.8f,1.5f);system.style.scale=new Scale(new Vector2(artScale,artScale));
        bool mobile=PNMobilePlatform.IsMobile(simulatedScreen) || owner.ShowMobileControls;
        toggle.style.width=mobile?64:56;toggle.style.height=mobile?64:56;
    }
    private void AcquireLocalInput()
    {
        foreach(var loader in EveRegistrySession.Context.Entities.Snapshot())
        {
            var identity=loader.GetComponent<EveEntityIdentity>();
            if(identity==null || identity.OwnerId!=owner.LocalOwnerId || loader.GetComponent<PNPlayerStat>()==null)continue;
            var move=loader.GetComponent<PNPlayerMove>();
            if(move!=null && !moves.Contains(move)){moves.Add(move);move.MenuInputBlocked=true;move.SetMoveDirection(Vector2.zero);}
            var skill=loader.GetComponent<PNPlayerSkill>();
            if(skill!=null && !skills.Contains(skill)){skills.Add(skill);skill.MenuInputBlocked=true;}
        }
    }
    private static bool Held(InputAction action)
    {
        if(action==null)return false;
        foreach(var control in action.controls)if(control.EvaluateMagnitude()>.2f)return true;
        return false;
    }
    private static bool Held(PNPlayerSkill skill)
    {
        var c=skill.PC?.Controller;
        return c!=null && (Held(c.Attack)||Held(c.Skill)||Held(c.Ulti)||Held(c.Dash)||Held(c.Burst));
    }
    private void ReleaseNeutralInput()
    {
        // Ignore presses made under the overlay; rearm only after physical release.
        for(int i=moves.Count-1;i>=0;i--)
            if(moves[i]==null || !moves[i].HasHeldMenuInput)
            { if(moves[i]!=null)moves[i].MenuInputBlocked=false;moves.RemoveAt(i); }
        for(int i=skills.Count-1;i>=0;i--)
            if(skills[i]==null || !Held(skills[i]))
            { if(skills[i]!=null)skills[i].MenuInputBlocked=false;skills.RemoveAt(i); }
    }
    private void RestoreTime() { if(!ownsTime)return; ownsTime=false;Time.timeScale=previousTime; }
    private void FinishClose()
    {
        IsOpen=false;closing=false;overlay.style.display=DisplayStyle.None;toggle.style.display=DisplayStyle.Flex;
        RestoreTime(); owner.ResetMobileInput(); ReleaseNeutralInput(); toggle.Focus();
    }
    private void Abort()
    {
        settingsView?.Dispose();settingsView=null;card.style.display=DisplayStyle.Flex;surface.style.visibility=Visibility.Visible;
        IsOpen=false;closing=false;overlay.style.display=DisplayStyle.None;toggle.style.display=DisplayStyle.Flex;
        RestoreTime();owner.ResetMobileInput();
        foreach(var move in moves)if(move!=null)move.MenuInputBlocked=false;
        foreach(var skill in skills)if(skill!=null)skill.MenuInputBlocked=false;
        moves.Clear();skills.Clear();
    }
    public void Dispose()
    {
        settingsView?.Dispose();settingsView=null;
        if(disposed)return;disposed=true;Abort();menu.performed-=OnMenu;menu.Disable();menu.Dispose();
        overlay.RemoveFromHierarchy();safe.RemoveFromHierarchy();
        PNUiRuntime.Retire(lightTexture);PNUiRuntime.Retire(coreTexture);
    }
}
#endif
