using System;
using System.Collections.Generic;
using System.Linq;

namespace EveEngine
{
    // EN: Owns base stats and source contributions, then evaluates effective values.
    // VI: Giữ chỉ số nền và đóng góp theo nguồn, rồi tính giá trị hiệu lực.
    public class EveStatSet
    {
        private readonly EveRegistry<EveStatDefinition> definitions;
        private readonly Dictionary<EveId, EveBaseStat> bases = new Dictionary<EveId, EveBaseStat>();
        private readonly Dictionary<EveId, List<EveStatModifier>> sources = new Dictionary<EveId, List<EveStatModifier>>();
        private readonly Dictionary<EveId, double> singleValueCache = new Dictionary<EveId, double>();
        private readonly Dictionary<string, double> combinedValueCache = new Dictionary<string, double>();

        public EveStatSet(EveRegistry<EveStatDefinition> definitions)
        {
            this.definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            if (!definitions.IsSealed) throw new InvalidOperationException("Stat definitions must be sealed before use.");
        }

        // EN: Derived sets can inspect definitions without mutating the registry.
        // VI: Stat set kế thừa có thể đọc định nghĩa mà không sửa registry.
        protected EveRegistry<EveStatDefinition> Definitions => definitions;
        public virtual EveId ResolveIdentity(EveId id) => definitions.ResolveIdentity(id);

        public static EveStatSet FromCharacter(EveCharacterDefinition character, EveRegistry<EveStatDefinition> definitions)
        {
            var result = new EveStatSet(definitions);
            result.LoadCharacter(character);
            return result;
        }

        // EN: Override this to create game-specific base stat instances.
        // VI: Ghi đè hàm này để tạo các chỉ số nền riêng của game.
        public virtual void LoadCharacter(EveCharacterDefinition character)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            foreach (EveInitialStat initial in character.Stats)
            {
                EveStatDefinition definition = definitions.GetEffective(ResolveIdentity(initial.Id));
                if (definition.Kind == EveStatKind.Resource)
                    AddBase(new EveResourceStat(initial.Id, initial.BaseValue, initial.InitialCurrent, definition.MaxChangePolicy));
                else
                {
                    if (initial.InitialCurrent.HasValue)
                        throw new InvalidOperationException($"Non-resource stat {initial.Id} cannot have initialCurrent.");
                    AddBase(new EveBaseStat(initial.Id, initial.BaseValue));
                }
            }
        }

        public virtual void AddBase(EveBaseStat stat)
        {
            if (stat == null) throw new ArgumentNullException(nameof(stat));
            if (sources.Count != 0) throw new InvalidOperationException("Base stats must be added before contributions.");
            EveId identity = ResolveIdentity(stat.Id);
            EveStatDefinition definition = definitions.GetEffective(identity);
            if ((definition.Kind == EveStatKind.Resource) != (stat is EveResourceStat))
                throw new InvalidOperationException($"Stat {stat.Id} has the wrong runtime type.");
            if (bases.ContainsKey(identity)) throw new InvalidOperationException($"Duplicate base stat {stat.Id}.");
            bases.Add(identity, stat);
            InvalidateCache();
        }

        public virtual bool TryGetBase(EveId id, out EveBaseStat stat) => bases.TryGetValue(ResolveIdentity(id), out stat);
        public virtual IReadOnlyDictionary<EveId, EveBaseStat> BaseSnapshot() => new Dictionary<EveId, EveBaseStat>(bases);

        // Caller has verified plain immutable entries and Current-independent formulas.
        // Shared entries are immutable; mutable resources and source lists remain private.
        protected void ImportSnapshot(EveStatSet original)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (bases.Count != 0 || sources.Count != 0) throw new InvalidOperationException("Snapshot destination must be empty.");
            foreach (var pair in original.bases)
            {
                if (pair.Value is EveResourceStat resource)
                {
                    if (resource.GetType() != typeof(EveResourceStat)) throw new InvalidOperationException("Custom resource requires ordinary capture.");
                    var copy = new EveResourceStat(pair.Key, resource.Value, 0, resource.MaxChangePolicy);
                    copy.SetCurrent(original.GetCurrent(pair.Key), original.GetMax(pair.Key)); bases.Add(pair.Key, copy);
                }
                else { if (pair.Value.GetType() != typeof(EveBaseStat)) throw new InvalidOperationException("Custom base requires ordinary capture."); bases.Add(pair.Key, pair.Value); }
            }
            foreach (var pair in original.sources)
            { if (pair.Value.Any(x => x.GetType() != typeof(EveStatModifier))) throw new InvalidOperationException("Custom modifier requires ordinary capture."); sources.Add(pair.Key, new List<EveStatModifier>(pair.Value)); }
            InvalidateCache();
        }

        // EN: Atomically replace contributions from one independently removable source.
        // VI: Thay toàn bộ đóng góp của một nguồn có thể gỡ độc lập trong một thao tác.
        // EN: A source ID identifies an equipment, buff or floor-effect instance.
        // VI: ID nguồn xác định một instance trang bị, buff hoặc hiệu ứng của ải.
        public virtual void SetSource(EveId sourceId, IEnumerable<EveStatModifier> modifiers)
        {
            if (!sourceId.IsValid) throw new ArgumentException("Source ID is required.", nameof(sourceId));
            if (modifiers == null) throw new ArgumentNullException(nameof(modifiers));
            var replacement = new List<EveStatModifier>(modifiers);
            foreach (EveStatModifier modifier in replacement)
            {
                if (modifier == null) throw new ArgumentException("Source contains a null modifier.", nameof(modifiers));
                ValidateModifier(modifier);
            }

            Dictionary<EveId, double> oldMaxima = CaptureResourceMaxima();
            Dictionary<EveId, double> oldCurrents = CaptureResourceCurrents();
            sources.TryGetValue(sourceId, out List<EveStatModifier> previous);
            if (replacement.Count == 0) sources.Remove(sourceId);
            else sources[sourceId] = replacement;
            InvalidateCache();
            try { UpdateResources(oldMaxima); }
            catch
            {
                if (previous == null) sources.Remove(sourceId);
                else sources[sourceId] = previous;
                InvalidateCache();
                RestoreResources(oldMaxima, oldCurrents);
                throw;
            }
        }

        public virtual bool RemoveSource(EveId sourceId)
        {
            if (!sourceId.IsValid) throw new ArgumentException("Source ID is required.", nameof(sourceId));
            if (!sources.ContainsKey(sourceId)) return false;
            Dictionary<EveId, double> oldMaxima = CaptureResourceMaxima();
            Dictionary<EveId, double> oldCurrents = CaptureResourceCurrents();
            List<EveStatModifier> previous = sources[sourceId];
            sources.Remove(sourceId);
            InvalidateCache();
            try { UpdateResources(oldMaxima); }
            catch
            {
                sources[sourceId] = previous;
                InvalidateCache();
                RestoreResources(oldMaxima, oldCurrents);
                throw;
            }
            return true;
        }

        public virtual IReadOnlyDictionary<EveId, IReadOnlyList<EveStatModifier>> SourceSnapshot()
        {
            var copy = new Dictionary<EveId, IReadOnlyList<EveStatModifier>>();
            foreach (var pair in sources) copy.Add(pair.Key, pair.Value.ToArray());
            return copy;
        }

        // EN: Games can validate routed contributions against their own content registries.
        // VI: Game có thể kiểm tra đóng góp định tuyến qua registry nội dung riêng.
        protected virtual void ValidateModifier(EveStatModifier modifier)
        {
            definitions.Get(modifier.Id);
            if (modifier.CalculationId.IsValid)
                throw new InvalidOperationException("Routed modifiers require a game-specific stat set.");
        }

        // EN: Games can include non-stat IDs in an expression without changing engine registries.
        // VI: Game có thể dùng ID ngoài stat trong biểu thức mà không đổi registry engine.
        protected virtual void ValidateEvaluationId(EveId id) => definitions.Get(id);

        // EN: Selected stats form one expression: group 0 sums base values.
        // VI: Các chỉ số được chọn tạo một biểu thức: nhóm 0 cộng giá trị nền.
        // EN: Percentages sum within each higher group; different groups multiply.
        // VI: Phần trăm cộng trong cùng nhóm lớn hơn 0; các nhóm khác nhau nhân nhau.
        public virtual double Evaluate(params EveId[] statIds) => Evaluate((IEnumerable<EveId>)statIds);

        public virtual double Evaluate(IEnumerable<EveId> statIds)
        {
            if (statIds == null) throw new ArgumentNullException(nameof(statIds));
            if (!CanCacheEvaluation)
            {
                var dynamicIds = new HashSet<EveId>(statIds.Select(ResolveIdentity));
                if (dynamicIds.Count == 0) throw new ArgumentException("At least one stat ID is required.", nameof(statIds));
                foreach (var id in dynamicIds) ValidateEvaluationId(id);
                return EvaluateUncached(dynamicIds);
            }
            if (statIds is IReadOnlyList<EveId> list && list.Count == 1 &&
                singleValueCache.TryGetValue(ResolveIdentity(list[0]), out double fastValue)) return fastValue;
            var selected = new HashSet<EveId>(statIds.Select(ResolveIdentity));
            if (selected.Count == 0) throw new ArgumentException("At least one stat ID is required.", nameof(statIds));
            if (selected.Count == 1)
            {
                EveId id = selected.First();
                ValidateEvaluationId(id);
                if (singleValueCache.TryGetValue(id, out double singleCached)) return singleCached;
                double singleValue = EvaluateUncached(selected);
                singleValueCache.Add(id, singleValue);
                return singleValue;
            }
            foreach (EveId id in selected) ValidateEvaluationId(id);

            // EN: A selection has the same result regardless of input order or repeated IDs.
            // VI: Cùng tập ID cho cùng kết quả bất kể thứ tự hay ID lặp lại.
            string key = string.Join("\u001d", selected.OrderBy(id => id).Select(id => id.ToString()));
            if (combinedValueCache.TryGetValue(key, out double cached)) return cached;
            double value = EvaluateUncached(selected);
            combinedValueCache.Add(key, value);
            return value;
        }

        // EN: Clear derived values after successful mutations and before resource maxima are recalculated.
        // VI: Xóa giá trị suy ra sau thay đổi và trước khi tính lại giới hạn resource.
        public virtual void InvalidateCache()
        {
            singleValueCache.Clear();
            combinedValueCache.Clear();
        }
        // Derived calculation layers may read Current, time or external mod state.
        protected virtual bool CanCacheEvaluation => true;

        protected virtual double EvaluateUncached(HashSet<EveId> selected)
        {
            double baseValue = 0d;
            foreach (EveId id in selected.OrderBy(id => id))
                if (bases.TryGetValue(id, out EveBaseStat stat)) baseValue += stat.Value;

            var groups = new SortedDictionary<int, double>();
            foreach (var source in sources.OrderBy(pair => pair.Key))
                foreach (EveStatModifier modifier in source.Value)
                {
                    if (!selected.Contains(ResolveIdentity(modifier.Id))) continue;
                    if (modifier.Group == 0) baseValue += modifier.Value;
                    else
                    {
                        groups.TryGetValue(modifier.Group, out double total);
                        groups[modifier.Group] = total + modifier.Value;
                    }
                }

            double result = baseValue;
            foreach (var group in groups) result *= ResolveGroupMultiplier(group.Key, group.Value);
            if (double.IsNaN(result) || double.IsInfinity(result) || result > double.MaxValue || result < -double.MaxValue)
                throw new OverflowException("Stat evaluation exceeded the double range.");
            return (double)result;
        }

        // EN: Games can bound a percentage group's multiplier without changing the engine formula.
        // VI: Game có thể giới hạn hệ số từng nhóm phần trăm mà không đổi công thức engine.
        protected virtual double ResolveGroupMultiplier(int group, double percent) => 1d + percent;

        public virtual double GetMax(EveId resourceId)
        {
            GetResource(resourceId);
            return Math.Max(0d, Evaluate(resourceId));
        }

        public virtual double GetCurrent(EveId resourceId) => GetResource(resourceId).Current;

        public virtual void SetCurrent(EveId resourceId, double value)
            => GetResource(resourceId).SetCurrent(value, GetMax(resourceId));

        protected virtual EveResourceStat GetResource(EveId id)
        {
            if (!bases.TryGetValue(ResolveIdentity(id), out EveBaseStat stat) || !(stat is EveResourceStat resource))
                throw new KeyNotFoundException($"Resource stat {id} is not present on this entity.");
            return resource;
        }

        protected virtual Dictionary<EveId, double> CaptureResourceMaxima()
        {
            var maxima = new Dictionary<EveId, double>();
            foreach (var pair in bases)
                if (pair.Value is EveResourceStat) maxima.Add(pair.Key, GetMax(pair.Key));
            return maxima;
        }

        protected virtual void UpdateResources(Dictionary<EveId, double> oldMaxima)
        {
            Dictionary<EveId, double> newMaxima = CaptureResourceMaxima();
            foreach (var pair in oldMaxima)
                if (pair.Value != newMaxima[pair.Key])
                    ((EveResourceStat)bases[pair.Key]).OnMaxChanged(pair.Value, newMaxima[pair.Key]);
        }

        protected virtual Dictionary<EveId, double> CaptureResourceCurrents()
        {
            var currents = new Dictionary<EveId, double>();
            foreach (var pair in bases)
                if (pair.Value is EveResourceStat resource) currents.Add(pair.Key, resource.Current);
            return currents;
        }

        protected virtual void RestoreResources(Dictionary<EveId, double> maxima, Dictionary<EveId, double> currents)
        {
            foreach (var pair in maxima)
                ((EveResourceStat)bases[pair.Key]).SetCurrent(currents[pair.Key], pair.Value);
        }
    }
}
