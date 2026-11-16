using System;

namespace ProjectN.Coop
{
    internal enum PNBuffViewRole { Unresolved, Host, Presentation }

    // Binding-owned client view cursor. It never advances authority or changes gameplay clocks.
    internal sealed class PNBuffViewTimeline
    {
        internal PNBuffViewRole Active { get; private set; }
        internal PNBuffViewRole Target { get; private set; }
        internal bool Transition { get; private set; }
        internal bool Frozen { get; private set; }
        internal double Deadline { get; private set; }
        internal double PublishedStamp { get; private set; }
        internal double CutStamp { get; private set; }
        internal ulong Candidate { get; private set; }
        internal double CapturedRate { get; private set; }
        internal bool Prepared => Active != PNBuffViewRole.Unresolved && !Transition;
        internal bool Waiting => !Prepared;

        internal static PNBuffViewRole Resolve(bool hasMove, bool attached, PNMoveNetworkRole role)
            => !hasMove ? PNBuffViewRole.Presentation : !attached ? PNBuffViewRole.Unresolved :
                role == PNMoveNetworkRole.Owner ? PNBuffViewRole.Host :
                role == PNMoveNetworkRole.Observer ? PNBuffViewRole.Presentation : PNBuffViewRole.Unresolved;

        internal bool Observe(PNBuffViewRole role, double now, double budget)
        {
            PNTimeMath.Nonnegative(now); PNTimeMath.Nonnegative(budget);
            if (Active == PNBuffViewRole.Unresolved && !Transition)
            {
                Target = role;
                if (role != PNBuffViewRole.Unresolved) { Active = role; Deadline = 0; }
                else if (Deadline == 0) Deadline = now + budget;
                return false;
            }
            if (role == Target) return false;
            Target = role;
            if (Transition) return false; // keep the candidate and original deadline on repeated switches
            Transition = true; Deadline = now + budget;
            return true;
        }
        internal double Select(PNTimeSample sample, bool target = false)
            => (target ? Target : Active) == PNBuffViewRole.Host ? sample.Host : sample.Presentation;
        internal void Published(double stamp)
        { PNTimeMath.Nonnegative(stamp); PublishedStamp = Math.Max(PublishedStamp, stamp); }
        internal void Snapshot(double rate)
        { PNTimeMath.Nonnegative(rate); CapturedRate = rate; }
        internal bool HoldBaseline(ulong transfer, double captured, PNTimeSample sample)
        {
            if (!Transition) return false;
            if (!Frozen)
            {
                Candidate = transfer; CutStamp = Math.Max(PublishedStamp, captured); Frozen = true;
            }
            if (Candidate != transfer) throw new InvalidOperationException("Buff view cut candidate changed before publication.");
            return !sample.Ready || Target == PNBuffViewRole.Unresolved || Select(sample, true) < CutStamp;
        }
        internal void CommitBaseline(ulong transfer, PNTimeSample sample)
        {
            if (!Transition) return;
            if (!Frozen || Candidate != transfer || Target == PNBuffViewRole.Unresolved || !sample.Ready || Select(sample, true) < CutStamp)
                throw new InvalidOperationException("Buff view cut is not ready.");
            Active = Target; PublishedStamp = Select(sample, true);
            Transition = Frozen = false; Candidate = 0; Deadline = CutStamp = 0;
        }
        // An authenticated unready recovery baseline is its own coherent seek. It
        // must not wait on the input gate whose fence requires that same baseline.
        internal void RecoveryBaseline(PNBuffViewRole role)
        {
            Active = Target = role; Transition = Frozen = false;
            Candidate = 0; CutStamp = PublishedStamp = Deadline = CapturedRate = 0;
        }
        internal bool Expired(double now) => Waiting && Deadline != 0 && now >= Deadline;
    }
}
