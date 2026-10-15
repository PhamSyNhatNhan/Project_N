using System;
namespace EveEngine
{
    // Opt-in measured profile; no production defaults.
    public sealed class EveEntityCommitPolicy
    {
        public double SoftMilliseconds { get; }
        public double HardMilliseconds { get; }
        public int ConsecutiveLimit { get; }
        public EveEntityCommitPolicy(double softMilliseconds, double hardMilliseconds, int consecutiveLimit)
        {
            if (double.IsNaN(softMilliseconds) || double.IsInfinity(softMilliseconds) || softMilliseconds <= 0 || double.IsNaN(hardMilliseconds) || double.IsInfinity(hardMilliseconds) || hardMilliseconds <= softMilliseconds || consecutiveLimit < 1) throw new ArgumentOutOfRangeException();
            SoftMilliseconds = softMilliseconds; HardMilliseconds = hardMilliseconds; ConsecutiveLimit = consecutiveLimit;
        }
    }
}
