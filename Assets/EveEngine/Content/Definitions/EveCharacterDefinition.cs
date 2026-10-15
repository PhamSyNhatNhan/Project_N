using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace EveEngine
{
    // EN: One base value in a character definition, with optional initial current.
    // VI: Một giá trị nền của nhân vật, có thể kèm giá trị hiện tại ban đầu.
    public class EveInitialStat
    {
        public virtual EveId Id { get; }
        public virtual double BaseValue { get; }
        public virtual double? InitialCurrent { get; }

        public EveInitialStat(EveId id, double baseValue, double? initialCurrent = null)
        {
            if (!id.IsValid) throw new ArgumentException("Stat ID is required.", nameof(id));
            if (double.IsNaN(baseValue) || double.IsInfinity(baseValue)) throw new ArgumentOutOfRangeException(nameof(baseValue));
            if (initialCurrent.HasValue && (double.IsNaN(initialCurrent.Value) || double.IsInfinity(initialCurrent.Value)))
                throw new ArgumentOutOfRangeException(nameof(initialCurrent));
            Id = id;
            BaseValue = baseValue;
            InitialCurrent = initialCurrent;
        }
    }

    // EN: Registered character template containing its starting stat values.
    // VI: Mẫu nhân vật được đăng ký cùng các giá trị chỉ số khởi đầu.
    public class EveCharacterDefinition : EveIIdentified
    {
        public virtual EveId Id { get; }
        public virtual IReadOnlyList<EveInitialStat> Stats { get; }
        public virtual IReadOnlyList<EveId> SkillIds { get; }
        public virtual string PortraitPath { get; }
        private readonly object data;
        // Game-owned metadata; JSON is isolated both on input and on access.
        public virtual object Data => data is JToken json ? json.DeepClone() : data;

        public EveCharacterDefinition(EveId id, IReadOnlyList<EveInitialStat> stats, IReadOnlyList<EveId> skillIds = null)
            : this(id, stats, skillIds, string.Empty) { }

        public EveCharacterDefinition(EveId id, IReadOnlyList<EveInitialStat> stats, IReadOnlyList<EveId> skillIds, string portraitPath)
            : this(id, stats, skillIds, portraitPath, null) { }

        public EveCharacterDefinition(EveId id, IReadOnlyList<EveInitialStat> stats, IReadOnlyList<EveId> skillIds, string portraitPath, object data)
        {
            this.data = data is JToken json ? (json.Type == JTokenType.Null ? null : json.DeepClone()) : data;
            PortraitPath = EveResourcePath.Validate(portraitPath);
            if (!id.IsValid) throw new ArgumentException("Character ID is required.", nameof(id));
            Id = id;
            // EN: Copy and wrap so neither the caller's list nor a cast can change published content.
            // VI: Sao chép và bọc chỉ đọc để danh sách đầu vào hoặc ép kiểu không sửa được nội dung đã công bố.
            Stats = stats == null ? throw new ArgumentNullException(nameof(stats)) : new List<EveInitialStat>(stats).AsReadOnly();
            var ids = new List<EveId>(skillIds ?? Array.Empty<EveId>());
            var unique = new HashSet<EveId>();
            foreach (EveId skillId in ids)
                if (!skillId.IsValid || !unique.Add(skillId))
                    throw new ArgumentException("Character skill IDs must be valid and unique.", nameof(skillIds));
            SkillIds = ids.AsReadOnly();
        }
    }
}
