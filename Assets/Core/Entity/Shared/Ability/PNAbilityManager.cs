using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using Newtonsoft.Json.Linq;

// EN: Entity load step and independent timer driver; disabling a skill does not disable this component.
// VI: Bước load entity và cập nhật timer riêng; tắt skill không tắt component này.
[DisallowMultipleComponent]
public class PNAbilityManager : MonoBehaviour, EveIEntityLoadStep
{
    [SerializeField] private List<PNAbilitySettings> initialAbilities = new List<PNAbilitySettings>();
#if UNITY_5_3_OR_NEWER
    [SerializeField] private TextAsset definitionsAsset;
#endif
    private IReadOnlyList<PNAbilityDefinition> configured;
    private PNAbilityCollection abilities;
    private EveTimeScale clock;
    private EveEntityLoadContext context;
    public virtual int LoadOrder => 0;
    public virtual IEnumerable<Type> Dependencies => Array.Empty<Type>();
    public bool IsReady => abilities != null && !abilities.IsDisposed && context != null && context.CanUse;
    public PNAbilityCollection Abilities => IsReady || abilities != null && context != null && context.AllowsModuleAccess ? abilities : throw new InvalidOperationException("Entity abilities are not published.");
    public IReadOnlyList<PNAbilitySettings> InitialAbilities => initialAbilities.AsReadOnly();
    public event Action<PNAbilityChange> Changed;

    // EN: Code/JSON configuration is independent from skills. Runtime grants can be defined after load.
    // VI: Cấu hình code/JSON độc lập với skill; có thể khai báo trạng thái runtime sau load.
    public void Configure(IEnumerable<PNAbilityDefinition> definitions)
    {
        if (abilities != null) throw new InvalidOperationException("Unload before replacing initial ability definitions.");
        if (definitions == null) throw new ArgumentNullException(nameof(definitions));
        var copy = definitions.ToArray();
        if (copy.Any(d => d == null) || copy.Select(d => d.Id).Distinct().Count() != copy.Length)
            throw new ArgumentException("Initial ability IDs must be unique.", nameof(definitions));
        configured = Array.AsReadOnly(copy);
    }
    public void ConfigureJson(string json) => Configure(PNAbilityDefinition.Parse(json));
    public virtual void Load(EveEntityLoadContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (abilities != null) throw new InvalidOperationException("Ability manager is already loaded.");
        IEnumerable<PNAbilityDefinition> definitions = configured ?? initialAbilities.Select(s => s.Create()).ToArray();
#if UNITY_5_3_OR_NEWER
        if (configured == null && definitionsAsset != null)
            definitions = definitions.Concat(PNAbilityDefinition.Parse(definitionsAsset.text));
#endif
        // EN: Entity-owned definitions authored in selected character skill envelopes.
        // VI: Định nghĩa thuộc entity được khai báo trong dữ liệu nhân vật đã chọn.
        var content = new List<PNAbilityDefinition>();
        var characterId = context.Identity.CharacterId;
        if (!characterId.IsValid)
        {
            // EN: Legacy scene entities use the same Inspector fallback as EveSkill/EveStat.
            // VI: Entity trong scene cũ dùng ID dự phòng từ Inspector như EveSkill/EveStat.
            string fallback = GetComponent<EveSkill>()?.CharacterDefinitionId;
            if (string.IsNullOrWhiteSpace(fallback)) fallback = GetComponent<EveStat>()?.CharacterDefinitionId;
            if (!string.IsNullOrWhiteSpace(fallback)) characterId = EveId.Parse(fallback);
        }
        var selectedSkills=new List<EveId>();
        if(characterId.IsValid)selectedSkills.AddRange(context.Registry.Characters.Get(characterId).SkillIds);
        var skill=GetComponent<PNSkill>();if(skill!=null)selectedSkills.AddRange(skill.BuiltInSkillIds);
        foreach(var skillId in selectedSkills.Select(context.Registry.Skills.ResolveIdentity).Distinct())
        {
            var payload=context.Registry.Skills.GetEffective(skillId).Data as JObject;
            var entries=(payload?["skill"] as JObject)?["abilities"];
            if(entries!=null)content.AddRange(PNAbilityDefinition.Parse(entries.ToString()));
        }
        definitions = definitions.Concat(content);
        abilities = new PNAbilityCollection(definitions, () => context.CanMutate || context.IsValid && context.AllowsModuleAccess);
        this.context = context; clock = GetComponent<EveTimeScale>();
        abilities.Changed += ForwardChange;
        abilities.ObserverFailed += ReportObserver;
    }
    protected virtual void Update()
    { if (IsReady && context.CanMutate) Advance(Time.deltaTime); }
    public void Advance(double gameDeltaTime)
    {
        PNAbilityDefinition.RequireSeconds(gameDeltaTime, true);
        if (!IsReady) throw new InvalidOperationException("Cannot advance unloaded entity abilities.");
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy) return;
        bool hasClock = clock != null && clock.isActiveAndEnabled;
        abilities.Advance(gameDeltaTime, hasClock ? clock.Scale : 1, hasClock ? clock.RawScale : 1);
    }
    private void ReportObserver(Exception error) => Debug.LogException(error, this);
    private void ForwardChange(PNAbilityChange change)
    {
        var listeners = Changed; if (listeners == null) return;
        foreach (Action<PNAbilityChange> listener in listeners.GetInvocationList())
            try { listener(change); } catch (Exception error) { ReportObserver(error); }
    }
    // EN: Death policies are explicit calls by entity/skill logic. No HP/death subscription here.
    // VI: Logic entity/skill chủ động quyết định khi chết; không theo dõi HP/death tại đây.
    public virtual void Unload()
    {
        abilities?.Dispose(); abilities = null; context = null; clock = null; Changed = null;
    }
    protected virtual void OnDestroy() => Unload();
}
