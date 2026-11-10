using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: The world features a run gives the worlds (rooms) it creates (Docs/EntityModulePlan.md step E5). Two sources, one function so that a new run and a continued run work out the same set: the features the run DECLARES in its
//     `features` list (for a capability that has no data block of its own: a mode or a mod), and the features that follow from a data block the run HAS. A feature that follows from a block is never declared (the block is its only
//     source: two places that must agree would disagree one day): today only `project_n:rogue_buff` (the run has a `rogue` block).
// VI: Feature của world mà một run đưa cho các world (phòng) nó tạo. Hai nguồn, một hàm duy nhất để run mới và run tiếp tục ra cùng tập: feature run KHAI trong `features` (khả năng không có khối dữ liệu riêng) và feature suy ra từ khối dữ liệu
//     run CÓ. Feature suy ra từ khối thì không được khai tay: hiện chỉ có `project_n:rogue_buff` (run có khối `rogue`).
public static class PNRunFeatures
{
    public static readonly EveId RogueBuff = EveId.Parse("project_n:rogue_buff");
    public const int MaxDeclared = 16;

    // The features a run's data blocks imply. Declaring one of these in `features` is refused.
    public static IReadOnlyList<EveId> Implied(PNRunDefinition run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        return run.Rogue != null ? new[] { RogueBuff } : Array.Empty<EveId>();
    }

    public static bool IsImpliedByABlock(EveId feature) => feature == RogueBuff;

    public static PNWorldFeatures Of(PNRunDefinition run) => new PNWorldFeatures(run.Features.Concat(Implied(run)));
}
