using System;
using Newtonsoft.Json.Linq;

// EN: Reusable work executed through an action; completion policy belongs to that action.
// VI: Công việc dùng lại được chạy qua action; cách chờ hoàn tất thuộc action đó.
public abstract class PNSkillOperation : IDisposable
{
    public abstract void Execute(PNSkillActionContext context, JToken data);
    public virtual void Cancel() { }
    public virtual void Dispose() { }
}
