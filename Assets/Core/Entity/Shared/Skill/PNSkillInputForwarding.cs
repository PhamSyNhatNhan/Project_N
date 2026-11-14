using EveEngine;

// EN: The owner's client does not run a skill (it has no gameplay authority): it forwards the slot press to the authority, which decides and runs it (Docs/SkillNetworkingAPlan.md S2).
//     Set on the skill by the component that owns the wire (Game/Sync/PNSkillRemoteInput); the skill only asks `IsActive` and calls `Forward`.
// VI: Client chủ nhân không chạy skill (không có authority): gửi lệnh bấm ô lên authority, authority quyết và chạy.
public interface IPNSkillInputForwarder
{
    // True while forwarding can work (bound to the sync layer, authority state known).
    bool IsActive { get; }
    // Sends one slot input to the authority; false when it cannot be sent.
    bool Forward(EveId slot, PNSkillInputEvent input);
}
