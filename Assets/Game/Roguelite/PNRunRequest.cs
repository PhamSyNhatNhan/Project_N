#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;

// EN: What the next run is (Docs/RunSavePlan.md 5.8): a new run on the map the host chose, or a saved run continued. The menu sets it (PNGameFlow.NextRun) before the play starts and the flow
//     takes it when the team leaves the lobby. Without one the run is the standard run.
// VI: Run kế tiếp là gì: run mới trên map host chọn, hoặc run đã lưu được tiếp tục. Menu đặt trước khi bắt đầu; flow lấy khi đội rời sảnh.
public sealed class PNRunRequest
{
    public EveId Run { get; }
    public PNRunSave Save { get; }
    private PNRunRequest(EveId run, PNRunSave save) { Run = run; Save = save; }
    public static PNRunRequest New(EveId run)
    {
        if (!run.IsValid) throw new ArgumentException("A run needs a map.", nameof(run));
        return new PNRunRequest(run, null);
    }
    public static PNRunRequest Continue(PNRunSave save)
    {
        if (save == null) throw new ArgumentNullException(nameof(save));
        return new PNRunRequest(save.Definition, save);
    }
    public bool IsContinue => Save != null;
}
#endif
