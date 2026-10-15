using System;

namespace EveEngine
{
    // EN: Common ID and value carried by base stats and modifiers.
    // VI: ID và giá trị dùng chung cho chỉ số nền và đóng góp.
    public abstract class EveStatEntry
    {
        public virtual EveId Id { get; }
        public virtual double Value { get; }

        protected EveStatEntry(EveId id, double value)
        {
            if (!id.IsValid) throw new ArgumentException("Stat ID is required.", nameof(id));
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Id = id;
            Value = value;
        }
    }

    // EN: Group-0 base value owned by one character or entity.
    // VI: Giá trị nền nhóm 0 thuộc về một nhân vật hoặc entity.
    public class EveBaseStat : EveStatEntry
    {
        public EveBaseStat(EveId id, double value) : base(id, value) { }
    }

    // EN: Base resource that also tracks current value and maximum-change policy.
    // VI: Chỉ số tài nguyên nền có thêm giá trị hiện tại và quy tắc đổi giới hạn.
    public class EveResourceStat : EveBaseStat
    {
        public virtual double Current { get; protected set; }
        public virtual EveMaxChangePolicy MaxChangePolicy { get; }

        public EveResourceStat(EveId id, double baseMax, double? initialCurrent, EveMaxChangePolicy policy)
            : base(id, baseMax)
        {
            if (baseMax < 0d) throw new ArgumentOutOfRangeException(nameof(baseMax));
            MaxChangePolicy = policy;
            Current = ClampCurrent(initialCurrent ?? baseMax, baseMax);
        }

        protected internal virtual void SetCurrent(double value, double effectiveMax)
        {
            Current = ClampCurrent(value, effectiveMax);
        }

        protected internal virtual void OnMaxChanged(double oldMax, double newMax)
        {
            if (MaxChangePolicy == EveMaxChangePolicy.PreserveRatio)
                SetCurrent(oldMax > 0d ? Current / oldMax * newMax : newMax, newMax);
            else
                SetCurrent(Current, newMax);
        }

        private static double ClampCurrent(double value, double effectiveMax)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            return Math.Max(0d, Math.Min(value, Math.Max(0d, effectiveMax)));
        }
    }

    // EN: One removable contribution from a buff, equipment or other source.
    // VI: Một đóng góp có thể gỡ từ buff, trang bị hoặc nguồn khác.
    public class EveStatModifier : EveStatEntry
    {
        public virtual int Group { get; }
        // EN: Optional routing ID interpreted by the game; the engine defines no calculation targets.
        // VI: ID định tuyến tùy chọn do game xử lý; engine không định nghĩa đích tính toán.
        public virtual EveId CalculationId { get; }
        public virtual EveId ContributionId { get; }
        // Group0 is flat; the owning game calculator interprets optional contribution metadata.
        // EN: Values in groups above 0 are additive percentages within each group.
        // VI: Giá trị ở nhóm lớn hơn 0 là phần trăm cộng dồn trong từng nhóm.
        public EveStatModifier(EveId id, double value, int group, EveId calculationId = default)
            : this(id, value, group, calculationId, default) { }
        public EveStatModifier(EveId id, double value, int group, EveId calculationId, EveId contributionId) : base(id, value)
        {
            if (group < 0) throw new ArgumentOutOfRangeException(nameof(group));
            Group = group;
            CalculationId = calculationId;
            if (group != 0 && contributionId.IsValid) throw new ArgumentException("Contribution ID is only valid for group 0.", nameof(contributionId));
            ContributionId = contributionId;
        }

        // EN: Preserve routing when copying; derived modifiers can preserve additional data here.
        // VI: Giữ định tuyến khi sao chép; modifier kế thừa có thể giữ dữ liệu bổ sung tại đây.
        public virtual EveStatModifier Clone() => new EveStatModifier(Id, Value, Group, CalculationId, ContributionId);
    }
}
