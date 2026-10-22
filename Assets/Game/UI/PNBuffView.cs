#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Events update card contents. Per-frame work animates layout, never queries Buff values.
internal sealed class PNBuffView : IDisposable
{
    private readonly VisualElement root;
    private readonly PNUiRuntime owner;
    private readonly bool upwards;
    private PNIBuffPresentationSource collection;
    private readonly Dictionary<long, Card> cards = new Dictionary<long, Card>();
    public float Height { get; private set; }
    public const float Width = 200;
    private long sequence;
    private Card[] ordered=Array.Empty<Card>();
    private bool layoutDirty;
    public PNBuffView(PNUiRuntime owner, VisualElement root, bool upwards)
    { this.owner=owner; this.root=root; this.upwards=upwards; root.Clear(); root.AddToClassList("buff-grid"); root.pickingMode=PickingMode.Ignore; root.style.width=Width; }
    public void Bind(PNBuffManager manager)
    {
        var next=manager!=null && manager.IsReady ? manager.Presentation : null;
        if(ReferenceEquals(next,collection))return;
        Dispose(); collection=next; sequence=0;
        if(collection==null)return;
        collection.PresentationChanged+=Changed;
        foreach(var state in collection.PresentationSnapshot())Apply(state,false);
    }
    private void Changed(PNBuffPresentationChange change)
    {
        if(change.Sequence<=sequence)return; sequence=change.Sequence;
        if(change.Kind==PNBuffPresentationChangeKind.Reset){root.Clear();cards.Clear();ordered=Array.Empty<Card>();layoutDirty=true;return;}
        if(change.Kind==PNBuffPresentationChangeKind.Removed){if(cards.TryGetValue(change.State.InstanceId,out var old))old.Hide();return;}
        Apply(change.State,change.Kind==PNBuffPresentationChangeKind.Added && !owner.MenuOpen && root.resolvedStyle.display!=DisplayStyle.None);
    }
    private void Apply(PNBuffPresentationSnapshot state,bool appear)
    {
        if(!state.ShowInUI){if(cards.TryGetValue(state.InstanceId,out var hidden))hidden.Hide();return;}
        if(!cards.TryGetValue(state.InstanceId,out var card)){card=new Card(owner,state,appear);cards.Add(state.InstanceId,card);root.Add(card.Root);layoutDirty=true;}
        card.Refresh(state);
    }
    public void Animate()
    {
        List<long> retired=null;
        foreach(var pair in cards)if(pair.Value.Retired){if(retired==null)retired=new List<long>();retired.Add(pair.Key);pair.Value.Root.RemoveFromHierarchy();}
        if(retired!=null){foreach(var handle in retired)cards.Remove(handle);layoutDirty=true;}
        if(layoutDirty)
        {
            ordered=cards.Values.OrderBy(x=>x.State.GroupOrder).ThenBy(x=>x.State.InstanceId).ToArray();
            Height=ordered.Length==0?0:(float)Math.Ceiling(ordered.Length/5d)*34;root.style.height=Height;
            for(int i=0;i<ordered.Length;i++){float y=upwards?-(i/5+1)*34+2:i/5*34+2;ordered[i].Place(new Vector2(5+i%5*40,y),upwards?Height:0);}
            layoutDirty=false;
        }
        foreach(var card in ordered)card.Animate();
    }
    public void Dispose(){if(collection!=null)collection.PresentationChanged-=Changed;collection=null;cards.Clear();ordered=Array.Empty<Card>();root.Clear();Height=0;layoutDirty=true;}

    private sealed class Card
    {
        public readonly VisualElement Root=new VisualElement();
        public PNBuffPresentationSnapshot State {get;private set;}
        private readonly PNUiRuntime owner;
        private const string FallbackIcon="PNBuffIcons/PNPlaceholder";   // a Buff without art of its own
        private readonly VisualElement image,glow;
        private readonly PNHudGlow halo;
        private readonly Label[] labels=new Label[4];
        private PNBuffNumbers numbers;
        private bool hasNumbers,placed;
        private float appearAt=-10,hideAt=-1,moveAt=-10;
        private Vector2 target,from;
        public bool Retired=>hideAt>=0 && Time.unscaledTime-hideAt>=.1f;
        public Card(PNUiRuntime owner,PNBuffPresentationSnapshot state,bool appear)
        {
            this.owner=owner;Root.AddToClassList("buff-card");Root.name="buff-"+state.InstanceId;Root.pickingMode=PickingMode.Ignore;
            glow=new VisualElement();glow.AddToClassList("buff-glow");glow.pickingMode=PickingMode.Ignore;Root.Add(glow);
            halo=new PNHudGlow(true);halo.pickingMode=PickingMode.Ignore;Root.Add(halo);
            image=new VisualElement();image.AddToClassList("buff-image");image.pickingMode=PickingMode.Ignore;Root.Add(image);
            for(int i=0;i<4;i++){var badge=new PNHudNumberBadge();badge.AddToClassList("buff-corner-"+i);Root.Add(badge);labels[i]=new Label();labels[i].pickingMode=PickingMode.Ignore;badge.Add(labels[i]);}
            if(appear)appearAt=Time.unscaledTime;Refresh(state);
        }
        public void Refresh(PNBuffPresentationSnapshot state)
        {
            if(State==null || State.IconPath!=state.IconPath)
            {
                string path=owner.Image(state.IconPath)!=null?state.IconPath:FallbackIcon; var sprite=owner.Image(path);
                image.style.backgroundImage=sprite==null?StyleKeyword.None:new StyleBackground(sprite);
                var texture=owner.Glow(path);glow.style.backgroundImage=texture==null?StyleKeyword.None:new StyleBackground(Background.FromTexture2D(texture));
            }
            if(State==null || State.Glow!=state.Glow)glow.style.unityBackgroundImageTintColor=PNBuffGlowArt.Tint(state.Glow);
            State=state;hideAt=-1;image.style.opacity=state.Suspended?.45f:1;glow.style.opacity=state.Suspended?.45f:1;
            if(!hasNumbers || !numbers.Equals(state.Numbers))
            {
                numbers=state.Numbers;hasNumbers=true;
                var values=new[]{numbers.TopLeft,numbers.TopRight,numbers.BottomLeft,numbers.BottomRight};
                for(int i=0;i<4;i++){string text=values[i]?.ToString(CultureInfo.InvariantCulture)??"";if(labels[i].text!=text)labels[i].text=text;labels[i].parent.style.display=values[i].HasValue?DisplayStyle.Flex:DisplayStyle.None;}
            }
        }
        public void Hide(){if(hideAt<0)hideAt=Time.unscaledTime;}
        private Vector2 Offset()=>Vector2.Lerp(from,Vector2.zero,Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-moveAt)/.2f)));
        public void Place(Vector2 next,float origin){if(placed && next!=target){from=Offset()+target-next;moveAt=Time.unscaledTime;}placed=true;target=next;Root.style.left=next.x;Root.style.top=next.y+origin;}
        public void Animate()
        {
            float age=Time.unscaledTime-appearAt;
            float wave=age<0 || age>=.2f?0:age<.04f?Mathf.SmoothStep(0,1,age/.04f):1-Mathf.SmoothStep(0,1,(age-.04f)/.16f);
            image.style.scale=new Scale(Vector3.one*(1+3f/35*wave));halo.style.opacity=wave;
            Root.style.opacity=hideAt>=0?1-Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-hideAt)/.1f)):Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.12f));
            var offset=Offset();Root.style.translate=new Translate(offset.x,offset.y);
        }
    }
}
#endif

