using System;
using Newtonsoft.Json.Linq;

// EN: Reusable work executed through an action; completion policy belongs to that action.
// VI: Công việc dùng lại được chạy qua action; cách chờ hoàn tất thuộc action đó.
public abstract class PNSkillOperation : IDisposable
{
    public abstract void Execute(PNSkillActionContext context, JToken data);
    // EN: A skill's effect trigger runs every Operation in a Predicted cast too (a guess on a machine without authority; plan 4.6a). The Operation does not know that: whatever only the authority may do goes through a
    //     door that looks at `context.Mode` (the Buff and heal doors, `PNSkillObjectSpawn.Admit`, the Control lease), so a guess makes only what a guess may make. PNObjectSyncO4EditorChecks lists every Operation and
    //     fails for one that is not covered.
    // VI: Effect cua skill chay moi Operation ca trong cast doan (plan 4.6a). Operation khong biet dieu do: viec chi authority duoc lam di qua cua xem `context.Mode`; check O4 liet ke moi Operation va that bai neu co cai chua duoc xet.
    public virtual void Cancel() { }
    public virtual void Dispose() { }
}
