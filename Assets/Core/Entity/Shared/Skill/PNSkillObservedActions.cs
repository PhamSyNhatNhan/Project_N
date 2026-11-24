using EveEngine;
using UnityEngine;

// EN: Since PV8 (Docs/ObservedViewPlan.md) another player's machine never runs an Action: it draws the run by its registered view (`PNSkillActionViews.cs`). The legacy replay of an Action in an Observed
//     context (G8b/G8c) is gone. What is left here is the teardown: everything an observer drew stops with the cast, the entity or the session.
// VI: Từ PV8 máy người chơi khác không chạy Action: chỉ vẽ run bằng view đã đăng ký. Đường phát lại cũ (G8b/G8c) đã gỡ; chỉ còn dọn khi cast/entity/session kết thúc.
public partial class PNSkill
{
    public void EndObservedActions()
    {
        ClearObservedActionPresentation();
        ClearLocalActionViews();
    }
}
