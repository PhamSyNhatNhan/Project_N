using System;
using System.Collections.Generic;
using System.Linq;

namespace EveEngine.Movement
{
    // Seconds on the host monotonic simulation clock; metres and physical metres/second.
    public readonly struct MoveVector
    {
        public readonly float X, Y, Z;
        public MoveVector(float x, float y, float z)
        {
            if (!MoveOrder.Finite(x) || !MoveOrder.Finite(y) || !MoveOrder.Finite(z)) throw new ArgumentOutOfRangeException();
            X = x; Y = y; Z = z;
        }
        public static MoveVector Lerp(MoveVector a, MoveVector b, float t) =>
            new MoveVector(a.X + (b.X-a.X)*t, a.Y + (b.Y-a.Y)*t, a.Z + (b.Z-a.Z)*t);
        public bool Near(MoveVector other, float epsilon = .0001f) =>
            Math.Abs(X-other.X) <= epsilon && Math.Abs(Y-other.Y) <= epsilon && Math.Abs(Z-other.Z) <= epsilon;
        internal void Bound(float limit)
        { if (Math.Abs(X)>limit || Math.Abs(Y)>limit || Math.Abs(Z)>limit) throw new ArgumentOutOfRangeException(); }
    }
    public static class MoveOrder
    {
        // Half-range serial arithmetic; a live lifetime must not span 2^31 unobserved updates.
        public static bool Newer(uint value, uint old) => unchecked((int)(value-old)) > 0;
        public static bool AtLeast(uint value, uint old) => value == old || Newer(value, old);
        public static uint Next(uint value) { unchecked { value++; } return value == 0 ? 1u : value; }
        internal static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        internal static void Time(double x) { if (double.IsNaN(x) || double.IsInfinity(x) || x < 0) throw new ArgumentOutOfRangeException(); }
    }
    public readonly struct MoveIdentity : IEquatable<MoveIdentity>
    {
        public readonly Guid Session;
        public readonly long Entity, Generation;
        public MoveIdentity(Guid session, long entity, long generation)
        { if(session==Guid.Empty || entity<=0 || generation<=0)throw new ArgumentException("Move identity."); Session=session; Entity=entity; Generation=generation; }
        public bool Equals(MoveIdentity other) => Session==other.Session && Entity==other.Entity && Generation==other.Generation;
        public override bool Equals(object other) => other is MoveIdentity id && Equals(id);
        public override int GetHashCode() => Session.GetHashCode() ^ Entity.GetHashCode()*397 ^ Generation.GetHashCode();
        internal void Validate() { if(Session==Guid.Empty || Entity<=0 || Generation<=0)throw new ArgumentException("Move identity."); }
    }
    public readonly struct MovementState
    {
        public readonly MoveVector Position, Velocity;
        public readonly int Facing;
        public readonly bool Grounded;
        // View only: 0 grounded, 1 rising, 2 falling. Not a producer vocabulary.
        public readonly byte VerticalView;
        public MovementState(MoveVector position, MoveVector velocity, int facing=1, bool grounded=false, byte verticalView=2)
        {
            position.Bound(1000000); velocity.Bound(100000);
            if((facing!=1 && facing!=-1) || verticalView>2 || grounded != (verticalView==0))throw new ArgumentException("Movement view.");
            Position=position; Velocity=velocity; Facing=facing; Grounded=grounded; VerticalView=verticalView;
        }
        internal void Validate() { new MovementState(Position,Velocity,Facing,Grounded,VerticalView); }
        public MovementState WithVelocity(MoveVector value) => new MovementState(Position,value,Facing,Grounded,VerticalView);
    }
    [Flags] public enum MoveRights : byte { None=0, Walk=1, Jump=2, FaceControl=4, FaceSkill=8, All=15 }
    public sealed class MoveMirror
    {
        public MoveIdentity Identity { get; }
        public uint Revision { get; }
        public long LifeRevision { get; }
        public bool Dead { get; }
        public MoveRights Rights { get; }
        public float MoveSpeed { get; }
        public float JumpSpeed { get; }
        public float Scale { get; }
        public float RawScale { get; }
        public IReadOnlyList<string> MotionPermissions { get; }
        public MoveMirror(MoveIdentity id,uint revision,long lifeRevision,bool dead,MoveRights rights,float moveSpeed,float jumpSpeed,float scale,float rawScale,IEnumerable<string> motionPermissions=null)
        {
            id.Validate();
            if(revision==0 || lifeRevision<0 || (rights & ~MoveRights.All)!=0 || !MoveOrder.Finite(moveSpeed) || moveSpeed<0 || moveSpeed>100000 ||
                !MoveOrder.Finite(jumpSpeed) || jumpSpeed<0 || jumpSpeed>100000 || !MoveOrder.Finite(scale) || scale<0 || scale>1000 || !MoveOrder.Finite(rawScale) || Math.Abs(rawScale)>1000)
                throw new ArgumentOutOfRangeException("Move mirror.");
            var permissions=(motionPermissions ?? Array.Empty<string>()).ToArray();
            if(permissions.Length>64 || permissions.Any(p=>p==null || System.Text.Encoding.UTF8.GetByteCount(p)>128 || !EveId.TryParse(p,out _)) || permissions.Distinct().Count()!=permissions.Length)
                throw new ArgumentException("Motion permissions.");
            Array.Sort(permissions,StringComparer.Ordinal);
            Identity=id; Revision=revision; LifeRevision=lifeRevision; Dead=dead; Rights=rights; MoveSpeed=moveSpeed; JumpSpeed=jumpSpeed; Scale=scale; RawScale=rawScale;
            MotionPermissions=Array.AsReadOnly(permissions);
        }
        public bool AllowsMotion(string permission) => !Dead && MotionPermissions.Contains(permission);
        public bool SameValues(MoveMirror other) => other!=null && Identity.Equals(other.Identity) && LifeRevision==other.LifeRevision && Dead==other.Dead && Rights==other.Rights &&
            MoveSpeed==other.MoveSpeed && JumpSpeed==other.JumpSpeed && Scale==other.Scale && RawScale==other.RawScale && MotionPermissions.SequenceEqual(other.MotionPermissions);
    }
    public readonly struct MoveRecovery
    {
        public readonly MoveVector ResumeVelocity, ResumeAngularVelocity;
        public readonly bool AwaitingGroundDeparture;
        public MoveRecovery(MoveVector velocity,MoveVector angular,bool awaitingGroundDeparture=false)
        { velocity.Bound(100000); angular.Bound(100000); ResumeVelocity=velocity; ResumeAngularVelocity=angular; AwaitingGroundDeparture=awaitingGroundDeparture; }
    }
    public abstract class MoveMessage
    {
        public MoveIdentity Identity { get; }
        protected MoveMessage(MoveIdentity id) { id.Validate(); Identity=id; }
    }
    public sealed class MovePoseReport : MoveMessage
    {
        public uint Sequence { get; }
        public uint Epoch { get; }
        public uint MirrorRevision { get; }
        public uint CorrectionId { get; }
        public MovementState State { get; }
        public MovePoseReport(MoveIdentity id,uint sequence,uint epoch,uint mirror,uint correction,MovementState state):base(id)
        { if(sequence==0 || epoch==0 || mirror==0 || correction==0)throw new ArgumentException("Report fence."); state.Validate(); Sequence=sequence; Epoch=epoch; MirrorRevision=mirror; CorrectionId=correction; State=state; }
    }
    public enum MoveCorrectionReason : byte { Baseline, Constraint, Teleport, RejectedBranch, Death, Authority }
    public sealed class MoveBaselineRequest : MoveMessage
    {
        public uint RequestId { get; }
        public MoveBaselineRequest(MoveIdentity id,uint requestId):base(id)
        { if(requestId==0)throw new ArgumentException("Move baseline request.");RequestId=requestId; }
    }
    public sealed class MoveCorrection : MoveMessage
    {
        public uint Id { get; }
        public uint Epoch { get; }
        public uint MirrorRevision { get; }
        public MovementState State { get; }
        public MoveRecovery Recovery { get; }
        public MoveCorrectionReason Reason { get; }
        public uint ReportSequenceFloor { get; }
        public uint BaselineRequestId { get; }
        public MoveCorrection(MoveIdentity identity,uint id,uint epoch,uint mirror,MovementState state,MoveRecovery recovery,MoveCorrectionReason reason,uint reportSequenceFloor=0,uint baselineRequestId=0):base(identity)
        { if(id==0 || epoch==0 || mirror==0 || reason>MoveCorrectionReason.Authority)throw new ArgumentException("Correction fence."); state.Validate(); Id=id; Epoch=epoch; MirrorRevision=mirror; State=state; Recovery=recovery; Reason=reason; ReportSequenceFloor=reportSequenceFloor; BaselineRequestId=baselineRequestId; }
    }
    public sealed class MovePoseState : MoveMessage
    {
        public uint Sequence { get; }
        public uint Epoch { get; }
        public double SampleTime { get; }
        public MovementState State { get; }
        public MovePoseState(MoveIdentity id,uint sequence,uint epoch,double time,MovementState state):base(id)
        { if(sequence==0 || epoch==0)throw new ArgumentException("State fence."); MoveOrder.Time(time); state.Validate(); Sequence=sequence; Epoch=epoch; SampleTime=time; State=state; }
    }
}
