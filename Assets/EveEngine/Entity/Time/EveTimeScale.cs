using System;
using System.Collections.Generic;
using UnityEngine;

namespace EveEngine
{
    // EN: Optional entity clock. Consumers opt in; this component never changes Animator or Rigidbody.
    // VI: Đồng hồ entity tùy chọn. Nơi sử dụng tự nối; component không sửa Animator hoặc Rigidbody.
    [DisallowMultipleComponent]
    public class EveTimeScale : MonoBehaviour, EveIEntityLoadStep
    {
        [SerializeField, Range(0f, 1f)] private float minScale;
        private readonly EveTimeScaleState state = new EveTimeScaleState();
        private readonly Dictionary<long, UnityEngine.Object> owners = new Dictionary<long, UnityEngine.Object>();
        private readonly List<long> expiredOwners = new List<long>();
        private float publishedScale = 1f;
        private bool clearing;
        private EveEntityLoadContext context;

        public virtual int LoadOrder => 0;
        public virtual IEnumerable<Type> Dependencies => Array.Empty<Type>();
        public event Action<float> ScaleChanged;
        public virtual float MinScale
        {
            get => minScale;
            set { state.MinScale = value; minScale = value; Publish(); }
        }
        public virtual float Scale { get { state.MinScale = minScale; return state.Scale; } }
        public virtual float RawScale => state.RawScale;
        public virtual float EffectiveMinScale { get { state.MinScale = minScale; return state.EffectiveMinScale; } }
        public virtual float DeltaTime => Time.deltaTime * Scale;
        public virtual float FixedDeltaTime => Time.fixedDeltaTime * Scale;
        public virtual IReadOnlyList<EveTimeModifier> Modifiers => state.Snapshot();

        public virtual long AddTimedModifier(EveId source, EveTimeModifierKind kind, float value, float duration)
        {
            if (context != null && !context.CanMutate) throw new InvalidOperationException("Clock mutation requires published authority.");
            if (clearing) throw new InvalidOperationException("Cannot add modifiers during clock reset.");
            long id = state.Add(source, kind, value, duration);
            Publish();
            return id;
        }

        // EN: Persistent effects require a live owner; release explicitly when an effect ends earlier.
        // VI: Hiệu ứng không thời hạn cần owner còn hoạt động; gỡ chủ động nếu hiệu ứng kết thúc sớm.
        public virtual long AddOwnedModifier(EveId source, EveTimeModifierKind kind, float value, UnityEngine.Object owner)
        {
            if (context != null && !context.CanMutate) throw new InvalidOperationException("Clock mutation requires published authority.");
            if (clearing) throw new InvalidOperationException("Cannot add modifiers during clock reset.");
            if (!(owner is Component) && !(owner is GameObject)) throw new ArgumentException("Owner must be a component or GameObject.", nameof(owner));
            if (!OwnerActive(owner)) throw new ArgumentException("Owner must be active.", nameof(owner));
            long id = state.Add(source, kind, value, null);
            owners.Add(id, owner);
            Publish();
            return id;
        }

        public virtual bool RemoveModifier(long id)
        {
            owners.Remove(id);
            bool removed = state.Remove(id);
            if (removed) Publish();
            return removed;
        }

        public virtual int RemoveSource(EveId source)
        {
            int removed = state.RemoveSource(source);
            PruneOwners();
            Publish();
            return removed;
        }

        public virtual void ClearModifiers()
        {
            if (clearing) return;
            clearing = true;
            try { state.Clear(); owners.Clear(); Publish(); }
            finally { clearing = false; }
        }

        protected virtual void Update() { if (context == null || context.CanMutate) Advance(Time.deltaTime); }
        protected virtual void OnDisable() => ClearModifiers();
        protected virtual void OnValidate() { minScale = Mathf.Clamp01(minScale); Publish(); }
        public virtual void Load(EveEntityLoadContext context)
        { this.context = context ?? throw new ArgumentNullException(nameof(context)); ClearModifiers(); }
        public virtual void Unload() { ClearModifiers(); ScaleChanged = null; context = null; }

        // EN: Also usable with an explicitly driven clock when this component's Update is overridden.
        // VI: Có thể dùng đồng hồ điều khiển tường minh khi ghi đè Update của component.
        public virtual void Advance(float gameDeltaTime)
        {
            if (context != null && !context.CanMutate) return;
            state.Advance(gameDeltaTime);
            PruneOwners();
            Publish();
        }

        private void PruneOwners()
        {
            expiredOwners.Clear();
            foreach (var pair in owners)
                if (!state.TryGet(pair.Key, out _) || !OwnerActive(pair.Value)) expiredOwners.Add(pair.Key);
            foreach (long id in expiredOwners) { owners.Remove(id); state.Remove(id); }
        }

        private static bool OwnerActive(UnityEngine.Object owner)
        {
            if (owner == null) return false;
            if (owner is Behaviour behaviour) return behaviour.isActiveAndEnabled;
            if (owner is Component component) return component.gameObject.activeInHierarchy;
            return owner is GameObject go && go.activeInHierarchy;
        }

        private void Publish()
        {
            if (context != null && !context.CanUse) return;
            float scale = Scale;
            if (scale == publishedScale) return;
            publishedScale = scale;
            if (ScaleChanged == null) return;
            foreach (Action<float> listener in ScaleChanged.GetInvocationList())
                try { listener(scale); } catch (Exception error) { Debug.LogException(error, this); }
        }
    }
}
