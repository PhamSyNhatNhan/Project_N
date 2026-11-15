// EN: Where a cast runs relative to the authority (Docs/SkillNetworkFrameworkPlan.md 4.1, G1).
//     Authority: the machine that owns gameplay (host, or offline). The cast is real: damage, Buffs, spawns, rewards.
//     Predicted: the owner's client runs the cast ahead of the authority. It is a guess; it takes nothing the authority owns and can be rolled back.
//     Observed:  another client replays a cast the authority started, for its pictures only.
//     G1 only names the mode and carries it on the invocation and the action context; the services start to read it in G2.
// VI: Cast chạy ở đâu so với authority. Authority: máy sở hữu gameplay, cast thật. Predicted: client chủ nhân chạy trước, là một lần đoán.
//     Observed: client khác chạy lại cast của authority, chỉ để vẽ. G1 chỉ đặt tên và mang chế độ; dịch vụ bắt đầu đọc nó ở G2.
public enum PNSkillMode { Authority = 0, Predicted = 1, Observed = 2 }
