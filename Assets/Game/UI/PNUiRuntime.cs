#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

// Combat presentation reads live entity values; the separate local menu owns pause/input gates.
public sealed class PNUiRuntime : MonoBehaviour
{
    [SerializeField] private long localOwnerId;
    [SerializeField] private Vector3 playerOffset = new Vector3(0, .85f, 0);
    [SerializeField] private Vector3 enemyOffset = new Vector3(0, .75f, 0);
    // Relative to HUD reference pixels; camera zoom must not explode the labels.
    [SerializeField] private float worldScale = 1f;
    [SerializeField, Range(1,8)] private int abilityColumns = 4;
    [SerializeField, Range(12,96)] private float abilitySize = 35;
    [SerializeField, Range(0,31)] private int worldLayer = 31;
    [SerializeField] private bool showMobileControls;
    public bool ShowMobileControls { get=>showMobileControls; set=>showMobileControls=value; }
    private PNMobileControls mobile;
    private PNPauseMenu pause;
    public bool MenuOpen => pause!=null && pause.IsOpen;
    public void OpenMenu()=>pause?.Open();
    public void CloseMenu()=>pause?.Close();
    internal void ResetMobileInput()=>mobile?.Reset();
    public long LocalOwnerId { get=>EveRegistrySession.IsReady && !EveRegistrySession.Context.HasAuthority
        ? EveRegistrySession.Context.LocalOwnerId.GetValueOrDefault(-1) : localOwnerId; set=>localOwnerId=value; }
    private bool IsLocalPlayer(PNStat stat)=>stat is PNPlayerStat && stat.GetComponent<EveEntityIdentity>().OwnerId==LocalOwnerId;
    public EveEntityId LocalPlayerId { get; set; }
    public Camera ViewCamera { get; set; }
    public PNStat HudPlayer { get; private set; }
    public int WorldCount => worlds.Count;
    public VisualElement HudRoot => hud?.rootVisualElement;
    public bool TestExpanded { get; private set; }
    private UIDocument hud;
    private PanelSettings overlaySettings, worldSettings;
    private VisualTreeAsset worldTemplate;
    private StyleSheet styles;
    private readonly Dictionary<EveEntityId,WorldView> worlds=new Dictionary<EveEntityId,WorldView>();
    private readonly Dictionary<string,Sprite> images=new Dictionary<string,Sprite>();
    private readonly Dictionary<Sprite,Sprite> portraits=new Dictionary<Sprite,Sprite>();
    private readonly List<PNStat> entities=new List<PNStat>();
    private readonly List<EveEntityId> selectionIds=new List<EveEntityId>();
    private Camera attachedCamera, uiCamera;
    private UniversalAdditionalCameraData cameraData;
    private bool originalUiBit;
    private Guid session;
    private EveEntityId selected;
    private DropdownField entitySelect;
    private Label details;
    private Label statsDetails, abilityDetails;
    private ProgressBar playerHp;
    private PNBuffView hudBuffs;
    private VisualElement portrait;
    private VisualElement portraitImage;
    private PNHudGlow healthGlow;
    private PNSparkleArtwork sparkleArtwork;
    private PNHudSparkles avatarSparkles,healthSparkles;
    private PNStat sparklePlayer;
    private double previousHealth;
    private Button testButton;
    private string selectionKey="";

    public static void Attach(EveGameRuntime runtime)
    {
        if(runtime.GetComponent<PNUiRuntime>()==null) runtime.gameObject.AddComponent<PNUiRuntime>();
    }
    private void OnEnable()
    {
        try { Build(); } catch { DisposeUI(); throw; }
    }
    private void Build()
    {
        if(hud!=null) return;
        var template=Resources.Load<VisualTreeAsset>("UI/Combat/CombatHud");
        worldTemplate=Resources.Load<VisualTreeAsset>("UI/Combat/CombatWorld");
        styles=Resources.Load<StyleSheet>("UI/Combat/CombatUI");
        var theme=Resources.Load<ThemeStyleSheet>("UI/Combat/RuntimeTheme");
        if(template==null || worldTemplate==null || styles==null || theme==null)
            throw new InvalidOperationException("Combat UI Resources are missing.");
        overlaySettings=ScriptableObject.CreateInstance<PanelSettings>(); overlaySettings.name="Combat overlay settings";
        overlaySettings.themeStyleSheet=theme; overlaySettings.scaleMode=PanelScaleMode.ScaleWithScreenSize;
        overlaySettings.referenceResolution=new Vector2Int(1280,720); overlaySettings.match=.5f;
        overlaySettings.sortingOrder=100;
        worldSettings=ScriptableObject.CreateInstance<PanelSettings>(); worldSettings.name="Combat world settings";
        worldSettings.themeStyleSheet=theme; worldSettings.renderMode=PanelRenderMode.WorldSpace;
        var obj=new GameObject("Combat HUD"); obj.transform.SetParent(transform,false);
        hud=obj.AddComponent<UIDocument>(); hud.panelSettings=overlaySettings; hud.visualTreeAsset=template;
        var root=hud.rootVisualElement; root.styleSheets.Add(styles); root.pickingMode=PickingMode.Ignore;
        root.Q("combat-hud").pickingMode=PickingMode.Ignore;
        IgnorePicking(root.Q("player-hud"));
        playerHp=root.Q<ProgressBar>("player-hp"); portrait=root.Q("portrait");
        hudBuffs=new PNBuffView(this,root.Q("player-buffs"),false);
        portraitImage=root.Q("portrait-image");
        root.Q("portrait-host").Insert(0,new PNHudGlow(true));
        healthGlow=new PNHudGlow(false); root.Q("hud-health").Insert(0,healthGlow);
        sparkleArtwork=new PNSparkleArtwork();
        avatarSparkles=new PNHudSparkles(root.Q("portrait-host"),"hud-avatar-sparkles",28);
        healthSparkles=new PNHudSparkles(root.Q("hud-health"),"hud-health-sparkles",40);
        testButton=root.Q<Button>("toggle-test"); testButton.clicked+=ToggleTest;
        entitySelect=root.Q<DropdownField>("test-entity"); entitySelect.RegisterValueChangedCallback(SelectEntity);
        details=root.Q<Label>("test-details");
        statsDetails=root.Q<Label>("test-stats"); abilityDetails=root.Q<Label>("test-abilities");
        SetTestExpanded(false);
        mobile=new PNMobileControls(this,root);
        pause=new PNPauseMenu(this,root);
    }
    public void SetTestExpanded(bool value)
    {
        TestExpanded=value;
        if(hud==null) return;
        hud.rootVisualElement.Q("test-body").style.display=value?DisplayStyle.Flex:DisplayStyle.None;
        testButton.text=value?"Test panel ▾":"Test panel ▸";
    }
    private void ToggleTest()=>SetTestExpanded(!TestExpanded);
    private static void IgnorePicking(VisualElement element)
    {
        element.pickingMode=PickingMode.Ignore;
        foreach(var child in element.Children()) IgnorePicking(child);
    }
    private void SelectEntity(ChangeEvent<string> change)
    {
        int index=entitySelect.choices.IndexOf(change.newValue);
        if(index>=0 && index<selectionIds.Count) selected=selectionIds[index];
    }
    private void LateUpdate()=>RefreshUI();
    public void RefreshUI()
    {
        if(hud==null) return;
        pause?.Tick();
        hud.rootVisualElement.Q("test-panel").style.display=MenuOpen?DisplayStyle.None:DisplayStyle.Flex;
        var camera=ViewCamera!=null?ViewCamera:Camera.main;
        if(camera!=attachedCamera) ConnectCamera(camera);
        if(uiCamera!=null && camera!=null)
        {
            uiCamera.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
            uiCamera.orthographic=camera.orthographic; uiCamera.orthographicSize=camera.orthographicSize;
            uiCamera.fieldOfView=camera.fieldOfView; uiCamera.nearClipPlane=camera.nearClipPlane;
            uiCamera.farClipPlane=camera.farClipPlane; uiCamera.rect=camera.rect;
            uiCamera.projectionMatrix=camera.projectionMatrix;
        }
        if(!EveRegistrySession.IsReady)
        {
            ClearWorlds(); entities.Clear(); HudPlayer=null; mobile?.Refresh(null); UpdateHud(); UpdateSelection(); UpdateDetails(); return;
        }
        if(session!=EveRegistrySession.Context.SessionId)
        { ClearWorlds(); ClearPortraits(); selected=default; session=EveRegistrySession.Context.SessionId; images.Clear(); }
        entities.Clear();
        foreach(var loader in EveRegistrySession.Context.Entities.Snapshot())
        {
            var stat=loader.GetComponent<PNStat>();
            if(stat==null || !stat.IsReady) continue;
            entities.Add(stat);
            if(!(stat is PNPlayerStat) && !(stat is PNEnemyStat)) continue;
            if(!worlds.TryGetValue(stat.EntityId,out var view))
            { view=new WorldView(this,stat); worlds.Add(stat.EntityId,view); }
        }
        foreach(var id in worlds.Keys.Where(id=>!entities.Any(s=>s.EntityId==id)).ToArray())
        { worlds[id].Dispose(); worlds.Remove(id); }
        HudPlayer=entities.FirstOrDefault(s=>s is PNPlayerStat && s.isActiveAndEnabled &&
            s.gameObject.activeInHierarchy && s.GetComponent<EveEntityIdentity>().OwnerId==LocalOwnerId &&
            (!LocalPlayerId.IsValid || s.EntityId==LocalPlayerId));
        foreach(var view in worlds.Values) view.Refresh(camera);
        mobile?.Refresh(HudPlayer);
        UpdateHud(); UpdateSelection(); UpdateDetails();
    }
    private void UpdateHud()
    {
        bool ready=PNVitals.TryRead(HudPlayer,PNContentIds.Health,out var currentHealth,out _,out _);
        hudBuffs.Bind(ready?HudPlayer.GetComponent<PNBuffManager>():null);hudBuffs.Animate();
        hud.rootVisualElement.Q("player-hud").style.display=ready&&!MenuOpen?DisplayStyle.Flex:DisplayStyle.None;
        if(!ready){sparklePlayer=null;avatarSparkles.Clear();healthSparkles.Clear();return;}
        Health(playerHp,HudPlayer,false);
        healthGlow.Fill=playerHp.value/100;
        // currentHealth comes from the same authoritative presentation read as the bar.
        if(sparklePlayer!=HudPlayer){sparklePlayer=HudPlayer;previousHealth=currentHealth;avatarSparkles.Clear();healthSparkles.Clear();}
        if(MenuOpen){avatarSparkles.Clear();healthSparkles.Clear();}
        else if(currentHealth!=previousHealth)
        {
            // Latest demo replaces a prior HP burst so rapid loss/heal cannot mix colors.
            avatarSparkles.Clear();healthSparkles.Clear();
            float red=1-playerHp.value/100;
            float light=1-red*.42f,chroma=(1-Mathf.Abs(2*light-1))*red*.9f;
            var glow=currentHealth>previousHealth?new Color(.3525f,.9475f,.6004f):new Color(light+chroma*.5f,light-chroma*.5f,light-chroma*.5f);
            avatarSparkles.Ring(7,glow,Time.unscaledTime,false);healthSparkles.Health(glow,Time.unscaledTime);
        }
        previousHealth=currentHealth;avatarSparkles.Tick(Time.unscaledTime);healthSparkles.Tick(Time.unscaledTime);
        var definition=EveRegistrySession.Current.Characters.Get(HudPlayer.CharacterId);
        var sprite=Image(definition.PortraitPath);
        if(sprite!=null && !portraits.ContainsKey(sprite))portraits.Add(sprite,PNHudPortrait.TrimTransparentMargin(sprite));
        portraitImage.style.backgroundImage=sprite==null?StyleKeyword.None:new StyleBackground(portraits[sprite]);
        portrait.Q("portrait-placeholder").style.display=sprite==null?DisplayStyle.Flex:DisplayStyle.None;
    }
    private void UpdateSelection()
    {
        string key=string.Join("|",entities.Select(s=>s.EntityId.ToString()));
        if(key!=selectionKey)
        {
            selectionKey=key; selectionIds.Clear(); selectionIds.AddRange(entities.Select(s=>s.EntityId));
            entitySelect.choices=entities.Select(s=>Name(s)+" #"+s.EntityId).ToList();
        }
        if(!selectionIds.Contains(selected)) selected=HudPlayer!=null?HudPlayer.EntityId:selectionIds.FirstOrDefault();
        int index=selectionIds.IndexOf(selected);
        entitySelect.SetValueWithoutNotify(index>=0?entitySelect.choices[index]:"No entity");
    }
    private void UpdateDetails()
    {
        if(!TestExpanded) return;
        var stat=entities.FirstOrDefault(s=>s.EntityId==selected);
        if(stat==null) { details.text="Waiting for a Ready entity."; statsDetails.text="—"; abilityDetails.text="—"; return; }
        var text=new StringBuilder();
        text.AppendLine(PNVitals.TryRead(stat,PNContentIds.Health,out var hp,out var hpMax,out _) ? "HP  "+Number(hp)+" / "+Number(hpMax) : "HP  —");
        text.AppendLine("Iframe  "+stat.IFrameRemaining.ToString("F3",CultureInfo.InvariantCulture)+"s · Game");
        var time=stat.GetComponent<EveTimeScale>();
        text.AppendLine("Time  "+(time!=null?"scale "+Number(time.Scale)+" · raw "+Number(time.RawScale)+" · min "+Number(time.EffectiveMinScale):"1"));
        var eve=stat.GetComponent<EveCharacterSkill>();
        if(eve!=null) text.AppendLine("Cast  "+eve.CurrentCast+" / "+eve.CurrentPhase);
        details.text=text.ToString().TrimEnd(); text.Clear();
        foreach(var pair in stat.Set.BaseSnapshot().OrderBy(p=>p.Key))
            text.AppendLine(pair.Key.Path+"  "+Number(stat.Evaluate(pair.Key))+" / "+Number(pair.Value.Value));
        statsDetails.text=text.ToString().TrimEnd(); text.Clear();
        var manager=stat.GetComponent<PNAbilityManager>();
        if(manager!=null && manager.IsReady)
            foreach(var ability in manager.Abilities.Instances)
                text.AppendLine(ability.Id.Path+" #"+ability.Handle.Instance+"\n  "+ability.Definition.Kind+" · "+ability.Definition.Clock+
                    " · UI "+ability.Definition.ShowInUI+"\n  amount "+(ability.Amount?.ToString()??"—")+" / "+(ability.Maximum?.ToString()??"—")+
                    " · active "+ability.IsActive+"\n  cooldown "+Number(ability.CooldownRemaining)+"s · duration "+
                    (ability.DurationRemaining.HasValue?Number(ability.DurationRemaining.Value)+"s":"∞ / inactive"));
        else text.AppendLine("No skill Ability collection.");
        abilityDetails.text=text.ToString().TrimEnd();
    }
    private void ConnectCamera(Camera value)
    {
        DisconnectCamera(); attachedCamera=value;
        if(value==null) return;
        // Native world panels get their own URP overlay camera with cleared depth, so terrain cannot occlude them.
        cameraData=value.GetUniversalAdditionalCameraData();
        if(cameraData.renderType!=CameraRenderType.Base)
            throw new InvalidOperationException("Combat UI needs a URP Base camera.");
        originalUiBit=(value.cullingMask&(1<<worldLayer))!=0; value.cullingMask&=~(1<<worldLayer);
        var obj=new GameObject("Combat world camera"); obj.transform.SetParent(transform,false);
        uiCamera=obj.AddComponent<Camera>(); uiCamera.CopyFrom(value); uiCamera.cullingMask=1<<worldLayer;
        var data=uiCamera.GetUniversalAdditionalCameraData(); data.renderType=CameraRenderType.Overlay;
        var pipeline=UniversalRenderPipeline.asset;
        for(int index=0; index<pipeline.rendererDataList.Length; index++)
            if(pipeline.GetRenderer(index)==cameraData.scriptableRenderer) { data.SetRenderer(index); break; }
        data.renderPostProcessing=false;
        cameraData.cameraStack.Add(uiCamera);
    }
    private void DisconnectCamera()
    {
        if(cameraData!=null && uiCamera!=null) cameraData.cameraStack.Remove(uiCamera);
        if(attachedCamera!=null) attachedCamera.cullingMask=originalUiBit?attachedCamera.cullingMask|(1<<worldLayer):attachedCamera.cullingMask&~(1<<worldLayer);
        if(uiCamera!=null) Retire(uiCamera.gameObject);
        uiCamera=null; attachedCamera=null; cameraData=null;
    }
    internal Sprite Image(string path)
    {
        if(string.IsNullOrEmpty(path)) return null;
        if(images.TryGetValue(path,out var result)) return result;
        result=Resources.Load<Sprite>(path); images.Add(path,result);
        if(result==null) Debug.LogWarning("Combat UI Sprite missing from Resources: "+path,this);
        return result;
    }
    private void ClearWorlds() { foreach(var view in worlds.Values) view.Dispose(); worlds.Clear(); }
    private void ClearPortraits()
    {
        foreach(var pair in portraits)if(pair.Value!=pair.Key)Retire(pair.Value);
        portraits.Clear();
    }
    private void OnDisable()=>DisposeUI();
    private void OnApplicationFocus(bool focus) { if(!focus)mobile?.Reset(); }
    private void OnApplicationPause(bool paused) { if(paused)mobile?.Reset(); }
    private void OnDestroy()=>DisposeUI();
    private void DisposeUI()
    {
        hudBuffs?.Dispose();hudBuffs=null;
        avatarSparkles?.Clear();healthSparkles?.Clear();avatarSparkles=null;healthSparkles=null;sparklePlayer=null;
        pause?.Dispose(); pause=null;
        mobile?.Dispose(); mobile=null;
        sparkleArtwork?.Dispose();sparkleArtwork=null;
        ClearWorlds(); ClearPortraits(); DisconnectCamera();
        if(testButton!=null) testButton.clicked-=ToggleTest;
        if(entitySelect!=null) entitySelect.UnregisterValueChangedCallback(SelectEntity);
        if(hud!=null) Retire(hud.gameObject); hud=null;
        if(overlaySettings!=null) Retire(overlaySettings); if(worldSettings!=null) Retire(worldSettings);
        overlaySettings=null; worldSettings=null; entities.Clear(); images.Clear(); selectionIds.Clear();
        selectionKey=""; session=Guid.Empty; HudPlayer=null;
    }
    internal static void Retire(UnityEngine.Object value) { if(Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    public static string Number(double value)=>value.ToString("0.##",CultureInfo.InvariantCulture);
    private static string Name(PNStat stat)=>stat.CharacterId.Path;
    private static void Health(ProgressBar bar, PNStat stat, bool showText)
    {
        if(!PNVitals.TryRead(stat,PNContentIds.Health,out var current,out var max,out _)){bar.value=0;bar.title="";return;}
        bar.value=max>0?(float)(100*current/max):0;
        bar.title=showText?Number(current)+" / "+Number(max):"";
    }
    private sealed class WorldView : IDisposable
    {
        private readonly PNUiRuntime owner;
        private readonly PNStat stat;
        private readonly GameObject obj;
        private readonly UIDocument document;
        private readonly VisualElement root, grid;
        private readonly ProgressBar health;
        private readonly SpriteRenderer characterSprite;
        private readonly PNBuffView buffs;
        private readonly Dictionary<EveId,AbilityCard> cards=new Dictionary<EveId,AbilityCard>();
        private bool initialSnapshot=true;
        public WorldView(PNUiRuntime owner, PNStat stat)
        {
            this.owner=owner; this.stat=stat;
            obj=new GameObject("Combat UI "+stat.EntityId); obj.layer=owner.worldLayer; obj.transform.SetParent(owner.transform,false);
            document=obj.AddComponent<UIDocument>(); document.panelSettings=owner.worldSettings;
            document.worldSpaceSizeMode=WorldSpaceSizeMode.Fixed; document.worldSpaceSize=new Vector2(260,175);
            document.pivot=Pivot.BottomCenter; document.visualTreeAsset=owner.worldTemplate;
            root=document.rootVisualElement; root.styleSheets.Add(owner.styles); root.pickingMode=PickingMode.Ignore;
            IgnorePicking(root);
            // UIDocument creates an interaction collider; combat labels never participate in physics/input.
            var collider=obj.GetComponent<BoxCollider>(); if(collider!=null) { collider.isTrigger=true; collider.enabled=false; }
            characterSprite=stat.GetComponentInChildren<SpriteRenderer>();
            if(stat is PNPlayerStat)
            {
                root.Q("world-root").AddToClassList("player-world");
            }
            else
            {
                root.Q("world-root").AddToClassList("enemy-world");
                document.worldSpaceSize=new Vector2(96,48);
            }
            root.Q("enemy-vitals").style.display=stat is PNEnemyStat || stat is PNPlayerStat && !owner.IsLocalPlayer(stat)?DisplayStyle.Flex:DisplayStyle.None;
            grid=root.Q("ability-grid"); grid.style.display=owner.IsLocalPlayer(stat)?DisplayStyle.Flex:DisplayStyle.None;
            health=root.Q<ProgressBar>("enemy-hp");
            if(stat is PNEnemyStat || stat is PNPlayerStat && !owner.IsLocalPlayer(stat))buffs=new PNBuffView(owner,root.Q("enemy-buffs"),true);
        }
        public void Refresh(Camera camera)
        {
            if(buffs!=null){buffs.Bind(stat.GetComponent<PNBuffManager>());buffs.Animate();root.Q("world-root").style.width=PNBuffView.Width;document.worldSpaceSize=new Vector2(PNBuffView.Width,Math.Max(14,buffs.Height+18));}
            var interactionCollider=obj.GetComponent<BoxCollider>();
            if(interactionCollider!=null) { interactionCollider.isTrigger=true; interactionCollider.enabled=false; }
            // Rebuilt Toolkit documents have no resolved layout until a panel update.
            // Hide for that frame rather than project a NaN anchor or invent a scale.
            float referenceHeight=owner.HudRoot.resolvedStyle.height;
            if(float.IsNaN(referenceHeight) || float.IsInfinity(referenceHeight) || referenceHeight<=0)
            { root.style.display=DisplayStyle.None; return; }
            bool visible=!owner.MenuOpen && camera!=null && stat.IsReady && stat.isActiveAndEnabled && stat.gameObject.activeInHierarchy && PNVitals.TryRead(stat,PNContentIds.Health,out _,out _,out var dead) && !dead;
            var position=stat.transform.position+(stat is PNPlayerStat?owner.playerOffset:owner.enemyOffset);
            if(camera!=null && characterSprite!=null && characterSprite.enabled && characterSprite.sprite!=null)
            {
                var anchor=camera.WorldToScreenPoint(stat.transform.position);
                var bounds=characterSprite.sprite.bounds;
                float top=anchor.y;
                float left=float.PositiveInfinity, right=float.NegativeInfinity;
                for(int corner=0;corner<4;corner++)
                {
                    var point=bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,0));
                    if(characterSprite.flipX)point.x=-point.x;
                    if(characterSprite.flipY)point.y=-point.y;
                    var projected=camera.WorldToScreenPoint(characterSprite.transform.TransformPoint(point));
                    top=Mathf.Max(top,projected.y);
                    left=Mathf.Min(left,projected.x); right=Mathf.Max(right,projected.x);
                }
                float gap=6*camera.pixelHeight/referenceHeight;
                anchor.x=(left+right)*.5f; anchor.y=top+gap; position=camera.ScreenToWorldPoint(anchor);
            }
            if(visible)
            {
                var screen=camera.WorldToViewportPoint(position);
                visible=screen.z>0 && screen.x>=0 && screen.x<=1 && screen.y>=0 && screen.y<=1;
            }
            root.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            if(!visible) return;
            obj.transform.SetPositionAndRotation(position,camera.transform.rotation);
            float depth=camera.WorldToViewportPoint(position).z;
            float visibleHeight=camera.orthographic?2*camera.orthographicSize:2*depth*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
            // Native world mesh uses centimeters. Match the overlay's logical pixel scale.
            float pixelsToWorld=visibleHeight/Math.Max(1,referenceHeight);
            obj.transform.localScale=Vector3.one*(100*pixelsToWorld*owner.worldScale);
            if(stat is PNEnemyStat || stat is PNPlayerStat && !owner.IsLocalPlayer(stat)) Health(health,stat,false);
            else
            {
                var manager=stat.GetComponent<PNAbilityManager>();
                IReadOnlyList<PNAbilityDisplay> rows=manager!=null && manager.IsReady
                    ?PNAbilityDisplay.Read(manager.Abilities)
                        .Where(row=>row.IsAvailable || manager.Abilities.Find(row.Definition.Id).Any(ability=>ability.CooldownRemaining>0)).ToArray()
                    :Array.Empty<PNAbilityDisplay>();
                var ids=new HashSet<EveId>(rows.Select(r=>r.Definition.Id));
                foreach(var card in cards.Values)if(!ids.Contains(card.Definition.Id))card.Hide();
                foreach(var row in rows)
                {
                    if(!cards.TryGetValue(row.Definition.Id,out var card))
                    { card=new AbilityCard(owner,row.Definition);IgnorePicking(card.Root);grid.Add(card.Root);cards.Add(row.Definition.Id,card);if(!initialSnapshot)card.Appear(); }
                    card.Refresh(row);
                }
                foreach(var pair in cards.ToArray())if(pair.Value.IsRetired)
                { pair.Value.Root.RemoveFromHierarchy();cards.Remove(pair.Key); }
                var ordered=cards.Values.OrderBy(c=>c.Definition.DisplayOrder).ThenBy(c=>c.Definition.Id).ToArray();
                float width=(owner.abilitySize+10)*owner.abilityColumns;
                float height=Math.Max(owner.abilitySize+4,(float)Math.Ceiling((double)ordered.Length/owner.abilityColumns)*(owner.abilitySize+4));
                grid.style.width=width; root.Q("world-root").style.width=width;
                grid.style.height=height;document.worldSpaceSize=new Vector2(width,height);
                for(int i=0;i<ordered.Length;i++)
                {
                    ordered[i].Place(new Vector2(5+i%owner.abilityColumns*(owner.abilitySize+10),
                        height-(i/owner.abilityColumns+1)*(owner.abilitySize+4)+2),height);
                    ordered[i].Animate();
                }
                initialSnapshot=false;
            }
        }
        public void Dispose(){buffs?.Dispose();Retire(obj);}
    }
    private sealed class AbilityCard
    {
        public readonly VisualElement Root;
        public readonly PNAbilityDefinition Definition;
        private readonly Label charge,counter,time;
        private readonly VisualElement image;
        private readonly PNHudGlow flash;
        private PNAbilityDisplay previous;
        private float pulseAt=-10,numberAt=-10,hideAt=-1,moveAt=-10;
        private float appearAt=-10,appearFrom=1,hideFrom=1;
        private Vector2 target,moveFrom;
        private bool placed;
        public bool IsRetired=>hideAt>=0 && Time.unscaledTime-hideAt>=.1f;
        public AbilityCard(PNUiRuntime owner, PNAbilityDefinition definition)
        {
            Definition=definition;
            Root=new VisualElement(); Root.AddToClassList("ability-card"); Root.name=definition.Id.ToString();
            Root.style.position=Position.Absolute;Root.style.marginLeft=0;Root.style.marginRight=0;Root.style.marginTop=0;Root.style.marginBottom=0;
            Root.style.width=owner.abilitySize; Root.style.height=owner.abilitySize;
            Root.tooltip=definition.Id+" · "+definition.Kind;
            image=new VisualElement(); image.AddToClassList("ability-image");
            var sprite=owner.Image(definition.IconPath)??owner.Image("UI/Combat/AbilityPlaceholder");
            flash=new PNHudGlow(true) { name="ability-flash" };flash.style.opacity=0;Root.Add(flash);
            if(sprite!=null) image.style.backgroundImage=new StyleBackground(sprite); Root.Add(image);
            charge=AddNumber("ability-charge");
            counter=AddNumber("ability-counter");
            time=AddNumber("ability-time");
        }
        private Label AddNumber(string corner)
        {
            var badge=new PNHudNumberBadge(); badge.AddToClassList(corner); Root.Add(badge);
            badge.style.transformOrigin=new TransformOrigin(Length.Percent(corner=="ability-charge"?100:0),Length.Percent(50),0);
            var label=new Label(); label.AddToClassList(corner); badge.Add(label); return label;
        }
        public void Refresh(PNAbilityDisplay row)
        {
            bool returning=hideAt>=0;
            hideAt=-1;
            if(returning)Appear(Root.style.opacity.value);
            if(previous!=null && (returning || !previous.IsAvailable&&row.IsAvailable ||
                row.Definition.Kind==PNAbilityKind.State&&row.ActiveInstances>previous.ActiveInstances ||
                row.Definition.Kind==PNAbilityKind.Cooldown&&previous.Remaining>0&&row.Remaining<=0&&row.IsAvailable))Pulse();
            if(previous!=null && row.Amount>previous.Amount && Time.unscaledTime-numberAt>=.3f)numberAt=Time.unscaledTime;
            previous=row;
            image.style.opacity=row.IsAvailable?1:.45f;
            SetNumber(charge,row.Definition.Kind==PNAbilityKind.Cooldown && row.Maximum>1?row.Amount.ToString():"");
            SetNumber(counter,row.Definition.Kind==PNAbilityKind.Counter && row.Maximum>0?row.Amount.ToString():"");
            SetNumber(time,row.Remaining>0?Math.Ceiling(row.Remaining).ToString(CultureInfo.InvariantCulture):"");
        }
        public void Pulse(){if(Time.unscaledTime-pulseAt>=.2f)pulseAt=Time.unscaledTime;}
        public void Appear(float from=0){appearFrom=from;appearAt=Time.unscaledTime;Pulse();}
        public void Hide(){if(hideAt<0){hideFrom=Root.style.opacity.value;hideAt=Time.unscaledTime;}}
        public void Place(Vector2 next,float panelHeight)
        {
            // Track positions relative to the document bottom, which stays anchored at the head.
            var bottomRelative=new Vector2(next.x,next.y-panelHeight);
            if(!placed){target=bottomRelative;placed=true;}
            else if(bottomRelative!=target)
            {
                moveFrom=MoveOffset()+target-bottomRelative;
                moveAt=Time.unscaledTime;target=bottomRelative;
            }
            Root.style.left=next.x;Root.style.top=next.y;
        }
        public void Animate()
        {
            float pulse=Wave(pulseAt,.04f,.16f),number=Wave(numberAt,.06f,.18f);
            image.style.scale=new Scale(Vector3.one*(1+3f/35*pulse));
            flash.style.opacity=pulse;
            AnimateNumber(charge,number);AnimateNumber(counter,number);
            Root.style.opacity=hideAt<0?Mathf.Lerp(appearFrom,1,Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-appearAt)/.12f))):
                hideFrom*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-hideAt)/.1f)));
            var offset=MoveOffset();
            Root.style.translate=new Translate(offset.x,offset.y);
        }
        private Vector2 MoveOffset()=>Vector2.Lerp(moveFrom,Vector2.zero,
            Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-moveAt)/.2f)));
        private static float Wave(float start,float rise,float fall)
        {
            float t=Time.unscaledTime-start;
            if(t<0||t>=rise+fall)return 0;
            return t<rise?Mathf.SmoothStep(0,1,t/rise):1-Mathf.SmoothStep(0,1,(t-rise)/fall);
        }
        private static void AnimateNumber(Label label,float wave)
        {label.parent.style.scale=new Scale(Vector3.one*(1+.05f*wave));}
        private static void SetNumber(Label label,string value)
        {
            label.text=value;
            label.parent.style.display=value.Length>0?DisplayStyle.Flex:DisplayStyle.None;
        }
    }
}
#endif
