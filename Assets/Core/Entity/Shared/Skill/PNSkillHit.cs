using System;

// EN: What `PNSkillActionContext.Hit` did with one hit (Docs/SkillNetworkFrameworkPlan.md 4.2, G2).
//     Applied: the authority dealt the damage; `Result` is the real outcome.
//     Skipped: this machine does not deal damage (a guess hands the hit to the report sink, an observer replays pictures). `Reported` says whether a sink took it.
//     Reading `Result` of a skipped hit is an error: logic that branches on damage or a kill belongs in `PNSkill.OnHitConfirmed`, which only the authority runs.
// VI: Kết quả của một lần trúng. Applied: authority đã gây sát thương. Skipped: máy này không gây sát thương; đọc `Result` của hit bị bỏ qua là lỗi,
//     logic phản ứng theo trúng / hạ gục phải viết trong `OnHitConfirmed` (chỉ authority chạy).
public enum PNHitStatus { Applied, Skipped }

public readonly struct PNHitOutcome
{
    private readonly PNDamageResult result;
    public PNHitStatus Status { get; }
    public bool Reported { get; }
    public bool Applied => Status == PNHitStatus.Applied;
    public PNDamageResult Result => Status == PNHitStatus.Applied ? result : throw new InvalidOperationException("The result of a hit is known only where the hit was applied: react to hits in OnHitConfirmed.");
    private PNHitOutcome(PNHitStatus status, PNDamageResult result, bool reported) { Status = status; this.result = result; Reported = reported; }
    public static PNHitOutcome Hit(PNDamageResult result) => new PNHitOutcome(PNHitStatus.Applied, result ?? throw new ArgumentNullException(nameof(result)), false);
    public static PNHitOutcome Skip(bool reported) => new PNHitOutcome(PNHitStatus.Skipped, null, reported);
}
