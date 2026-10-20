using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Per-enemy lorica tuning from the character's `data.lorica` object (every field optional, the object itself too):
//       share: part of every hit that lorica takes while it is up (0 < share <= 1, default .6); health takes the rest,
//       breakSeconds: how long the break lasts (> 0, default 7), recoverySeconds: how long lorica stays locked afterwards (>= 0, default 5).
//     The lorica amount itself is the enemy's `base:lorica` stat (absent or 0 = the enemy has no lorica).
// VI: Thông số lorica của từng enemy trong `data.lorica` của character (mọi trường và cả object đều tuỳ chọn).
public sealed class PNLoricaSettings
{
    public const double DefaultShare = .6d, DefaultBreakSeconds = 7d, DefaultRecoverySeconds = 5d;
    public static readonly PNLoricaSettings Default = new PNLoricaSettings(DefaultShare, DefaultBreakSeconds, DefaultRecoverySeconds);
    public double Share { get; }
    public double BreakSeconds { get; }
    public double RecoverySeconds { get; }
    public PNLoricaSettings(double share, double breakSeconds, double recoverySeconds)
    {
        if (double.IsNaN(share) || share <= 0d || share > 1d) throw new FormatException("lorica share must be > 0 and at most 1.");
        if (double.IsNaN(breakSeconds) || double.IsInfinity(breakSeconds) || breakSeconds <= 0d || breakSeconds > 3600d) throw new FormatException("lorica breakSeconds must be > 0 and at most 3600.");
        if (double.IsNaN(recoverySeconds) || double.IsInfinity(recoverySeconds) || recoverySeconds < 0d || recoverySeconds > 3600d) throw new FormatException("lorica recoverySeconds must be 0..3600.");
        Share = share; BreakSeconds = breakSeconds; RecoverySeconds = recoverySeconds;
    }
    // `data` is the character's game-owned data object (null or without `lorica` = defaults).
    public static PNLoricaSettings Parse(object data)
    {
        if (data == null || data is JToken empty && empty.Type == JTokenType.Null) return Default;
        if (!(data is JObject root)) throw new FormatException("Project_N character data must be an object.");
        var token = root["lorica"];
        if (token == null) return Default;
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "share", "breakSeconds", "recoverySeconds");
        return new PNLoricaSettings(obj["share"] == null ? DefaultShare : PNBuffContentBatch.Number(obj, "share"),
            obj["breakSeconds"] == null ? DefaultBreakSeconds : PNBuffContentBatch.Number(obj, "breakSeconds"),
            obj["recoverySeconds"] == null ? DefaultRecoverySeconds : PNBuffContentBatch.Number(obj, "recoverySeconds"));
    }
}
