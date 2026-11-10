#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: The Roguelite Buff of ONE player entity (Docs/RogueBuffPlan.md section 5.1): an entity load step that builds the objects of the items the team owns when the entity is built and disposes them when it
//     is unloaded, so a new floor (a new entity) builds everything again from the run's set and nothing is saved on the entity. It reads the set ONCE, here, in `Load`: whatever happens to the team's
//     set while the entity lives (a pick, a purchase, a sale) takes effect from the next build (decision D6); there is deliberately no way to add or remove an item on a running entity.
//     It is attached by the entity module `project_n:rogue_buff_player` (Docs/EntityModulePlan.md), which the factory applies to a player in a world that has the `project_n:rogue_buff` feature, i.e. a room of a run with Roguelite Buff (not by the prefab, and not by `PNStat`, which lives in Core and cannot see the run). It does nothing without authority, outside a
//     run, or in a run whose definition has no `rogue` block. The entity is pooled, so everything is cleared in `Unload`. All the logic is in `PNRogueRuntime` (checked in the smoke suite); this is the
//     glue to the real entity.
// VI: Roguelite Buff của MỘT thực thể người chơi: bước nạp dựng đối tượng của các mục đội sở hữu lúc dựng thực thể và huỷ lúc dỡ. Đọc tập MỘT lần ở `Load`; thay đổi tập khi thực thể đang chạy chỉ có hiệu lực
//     từ lần dựng kế (D6). Gắn bởi mô-đun thực thể `project_n:rogue_buff_player` (world có feature `project_n:rogue_buff`). Không làm gì nếu không có quyền, ngoài run, hoặc run không có khối `rogue`.
[DisallowMultipleComponent]
public sealed class PNRogueBuffManager : MonoBehaviour, EveIEntityLoadStep
{
    private PNRogueRuntime runtime;
    private EveTimeScale clock;
    private EveEntityLoader loader;

    public int LoadOrder => 20;
    // Only the stat: a scene or test entity may have no skill, and the order after Skill/Buff comes from LoadOrder (Docs/RogueBuffPlan.md R0).
    public IEnumerable<Type> Dependencies => new[] { typeof(PNStat) };
    public bool IsActive => runtime != null;
    public PNRogueRuntime Runtime => runtime;

    public void Load(EveEntityLoadContext context)
    {
        if (runtime != null) throw new InvalidOperationException("Roguelite Buff manager already loaded.");
        if (!PNEntityRoles.IsPlayer(this) || !context.Session.HasAuthority) return;
        var director = PNRunDirector.Find(context.Session);
        var state = director?.State;
        if (state == null || !state.IsRunning || director.Definition?.Rogue == null) return;
        // A fault of this system must not stop the player from being built: it is reported and the player has no items this floor.
        try
        {
            var stat = GetComponent<PNStat>();
            var catalog = PNRogueCatalog.Build(context.Registry);
            var set = new PNRogueSet(catalog, state.Loadout);
            var binding = new PNRogueBinding(gameObject, stat, GetComponent<PNSkill>(), context.Session, state.Floor, set.Snapshot());
            var made = new PNRogueRuntime(context.Registry, catalog, new StatSink(stat), Report);
            runtime = made; clock = GetComponent<EveTimeScale>(); loader = GetComponent<EveEntityLoader>();
            made.Build(binding);
        }
        catch (Exception error)
        {
            Debug.LogException(new InvalidOperationException("Roguelite Buff could not be built for " + name + "; this player has no items this floor.", error), this);
            Unload();
        }
    }

    public void Unload()
    {
        var made = runtime; runtime = null; clock = null; loader = null;
        if (made == null) return;
        try { made.Dispose(); } catch (Exception error) { Debug.LogException(error, this); }
    }

    private void Update()
    {
        if (runtime == null || loader == null || !loader.IsReady) return;
        runtime.Tick(clock != null ? clock.DeltaTime : Time.deltaTime);
        if (runtime.DisplayChanged) { GetComponent<PNRogueEntityView>()?.Touch(); runtime.AcknowledgeDisplays(); }   // the numbers go to the `rogue_view` block once per change
    }

    private static void Report(string what, Exception error)
    {
        if (error == null) Debug.LogWarning(what);
        else Debug.LogException(new InvalidOperationException(what, error));
    }

    // The modifier sources of the items go straight onto the entity's stat: they are not Buffs, so nothing an enemy does to Buffs (cleansing, dispelling) removes them.
    private sealed class StatSink : IPNRogueStatSink
    {
        private readonly PNStat stat;
        public StatSink(PNStat stat) { this.stat = stat; }
        public void SetSource(EveId sourceId, IReadOnlyList<EveStatModifier> modifiers) { stat.SetSource(sourceId, modifiers); }
        public void RemoveSource(EveId sourceId) { stat.RemoveSource(sourceId); }
    }
}
#endif
