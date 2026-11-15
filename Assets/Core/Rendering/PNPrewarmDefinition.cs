using System;
using EveEngine;

// EN: Registered request to build something ahead of its first use, under the transition cover (docs: Assets/Game/Runtime/PNShaderWarmupREADME.md). Pure data plus the step to run; the runtime part is
//     PNShaderWarmup (Game). Shaders have their own definition (PNShaderWarmupDefinition); this is for everything else a system builds lazily: materials, meshes, textures painted in code, pools,
//     particle systems, the first run of a code path. A skill, an Action, a Buff visual, a game system or a mod registers its own entry in its content module; nothing central has to be edited.
//     `Step(budgetMs)` is called once per frame until it returns true (done). It may do all its work in one call and return true, or spread it and use the budget (milliseconds left this frame; at least
//     one call is always made). A step that throws is logged and dropped: it never blocks the session. Presentation only: no gameplay rule or network message depends on it.
// VI: Yêu cầu đăng ký dựng sẵn thứ gì đó trước lần dùng đầu tiên, dưới màn che chuyển cảnh. Shader có định nghĩa riêng; đây là cho mọi thứ khác mà một hệ dựng lười (material, mesh, texture vẽ bằng code,
//     pool, particle, lần chạy đầu của một đường code). Skill / Action / Buff / hệ thống / mod tự đăng ký trong module nội dung của mình.
public sealed class PNPrewarmDefinition : EveIIdentified
{
    public const int MaximumOrder = 1000;
    public EveId Id { get; }
    // Entries run in ascending order, then by id: a lower number runs first (default 0).
    public int Order { get; }
    // Called each frame with the milliseconds left in this frame's warm-up budget; true when this entry is finished.
    public Func<double, bool> Step { get; }
    public PNPrewarmDefinition(EveId id, Func<double, bool> step, int order = 0)
    {
        if (!id.IsValid || id.ToString().Length > 128) throw new ArgumentException("Prewarm id is invalid.");
        if (step == null) throw new ArgumentNullException(nameof(step));
        if (order < -MaximumOrder || order > MaximumOrder) throw new ArgumentOutOfRangeException(nameof(order));
        Id = id; Step = step; Order = order;
    }
    // The common case: one call does all the work.
    public PNPrewarmDefinition(EveId id, Action work, int order = 0) : this(id, WrapOnce(work), order) { }
    private static Func<double, bool> WrapOnce(Action work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        return _ => { work(); return true; };
    }
}
