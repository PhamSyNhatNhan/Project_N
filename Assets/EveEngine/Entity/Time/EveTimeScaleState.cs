using System;
using System.Collections.Generic;

namespace EveEngine
{
    // EN: Optional clock arithmetic with independent modifier lifetimes; no Unity physics dependency.
    // VI: Tính đồng hồ tùy chọn với thời hạn modifier độc lập; không phụ thuộc vật lý Unity.
    public class EveTimeScaleState
    {
        private readonly Dictionary<long, EveTimeModifier> modifiers = new Dictionary<long, EveTimeModifier>();
        private readonly List<long> workIds = new List<long>();
        private long nextId;
        private float minScale;
        public virtual float MinScale
        {
            get => minScale;
            set { RequireFinite(value, nameof(value)); if (value < 0f || value > 1f) throw new ArgumentOutOfRangeException(nameof(value)); minScale = value; }
        }
        public virtual int Count => modifiers.Count;
        public virtual float RawScale => (float)(1d + Sum(EveTimeModifierKind.Scale));
        public virtual float EffectiveMinScale => (float)Math.Max(0d, minScale - Sum(EveTimeModifierKind.MinScaleReduction));
        public virtual float Scale => Math.Max(EffectiveMinScale, RawScale);

        public virtual long Add(EveId source, EveTimeModifierKind kind, float value, double? duration)
        {
            if (!source.IsValid) throw new ArgumentException("A source ID is required.", nameof(source));
            if (kind != EveTimeModifierKind.Scale && kind != EveTimeModifierKind.MinScaleReduction)
                throw new ArgumentOutOfRangeException(nameof(kind));
            RequireFinite(value, nameof(value));
            if (kind == EveTimeModifierKind.MinScaleReduction && value < 0f) throw new ArgumentOutOfRangeException(nameof(value));
            if (duration.HasValue && (double.IsNaN(duration.Value) || double.IsInfinity(duration.Value) || duration.Value <= 0d))
                throw new ArgumentOutOfRangeException(nameof(duration));
            double sum = Sum(kind) + value;
            if (Math.Abs(sum) > float.MaxValue || (kind == EveTimeModifierKind.Scale && Math.Abs(1d + sum) > float.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(value), "Modifier total exceeds the supported range.");
            long id = checked(++nextId);
            modifiers.Add(id, new EveTimeModifier(id, source, kind, value, duration));
            return id;
        }

        public virtual bool Remove(long id) => modifiers.Remove(id);
        public virtual bool TryGet(long id, out EveTimeModifier modifier) => modifiers.TryGetValue(id, out modifier);
        public virtual IReadOnlyList<EveTimeModifier> Snapshot() => new List<EveTimeModifier>(modifiers.Values).AsReadOnly();

        public virtual int RemoveSource(EveId source)
        {
            workIds.Clear();
            foreach (var item in modifiers.Values) if (item.Source == source) workIds.Add(item.Id);
            foreach (long id in workIds) modifiers.Remove(id);
            return workIds.Count;
        }

        // EN: Caller supplies game time before local scaling; a stopped entity still expires modifiers.
        // VI: Nguồn gọi truyền thời gian game trước hệ số entity; entity dừng vẫn hết hạn modifier.
        public virtual void Advance(double gameDeltaTime)
        {
            if (double.IsNaN(gameDeltaTime) || double.IsInfinity(gameDeltaTime) || gameDeltaTime < 0d)
                throw new ArgumentOutOfRangeException(nameof(gameDeltaTime));
            if (gameDeltaTime == 0d) return;
            workIds.Clear();
            workIds.AddRange(modifiers.Keys);
            foreach (long id in workIds)
            {
                var item = modifiers[id];
                if (!item.RemainingTime.HasValue) continue;
                double remaining = item.RemainingTime.Value - gameDeltaTime;
                if (remaining <= 0d) modifiers.Remove(id);
                else modifiers[id] = new EveTimeModifier(id, item.Source, item.Kind, item.Value, remaining);
            }
        }

        // EN: IDs are not reused after reset, so old handles cannot remove new modifiers.
        // VI: Không tái sử dụng ID sau reset để handle cũ không xóa nhầm modifier mới.
        public virtual void Clear() => modifiers.Clear();

        private double Sum(EveTimeModifierKind kind)
        {
            double sum = 0d;
            foreach (var item in modifiers.Values) if (item.Kind == kind) sum += item.Value;
            return sum;
        }

        private static void RequireFinite(float value, string name)
        { if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(name); }
    }
}
