using System;
using System.Linq;
using System.Text;
using EveEngine.Movement;
using Mirror;

namespace ProjectN.Coop
{
    public struct PNMoveStream : IEquatable<PNMoveStream>
    {
        public Guid Session;
        public uint NetId;
        public long Entity,Generation;
        public ulong Nonce;
        public MoveIdentity Identity=>new MoveIdentity(Session,Entity,Generation);
        public void Validate(){if(NetId==0||Nonce==0)throw new FormatException("Move stream.");_ = Identity;}
        public bool Equals(PNMoveStream s)=>Session==s.Session&&NetId==s.NetId&&Entity==s.Entity&&Generation==s.Generation&&Nonce==s.Nonce;
    }
    public struct PNMoveRequest : NetworkMessage { public PNMoveStream Stream; public uint Request; }
    public struct PNMovePacket : NetworkMessage
    {
        // 0 owner baseline, 1 owner control, 2 observer baseline/control, 3 observer pose, 4 owner report.
        public byte Kind;
        public uint Request;
        public PNMoveStream Stream;
        public long Revision,Life;
        public MoveMirror Mirror;
        public MoveCorrection Correction;
        public MovePoseState Pose;
        public MovePoseReport Report;
    }
    public static class PNMoveWire
    {
        public const int ControlBytes=16384,PoseBytes=512;
        private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        private static void Stream(NetworkWriter w,PNMoveStream s){s.Validate();w.WriteGuid(s.Session);w.WriteUInt(s.NetId);w.WriteLong(s.Entity);w.WriteLong(s.Generation);w.WriteULong(s.Nonce);}
        private static PNMoveStream Stream(NetworkReader r){var s=new PNMoveStream{Session=r.ReadGuid(),NetId=r.ReadUInt(),Entity=r.ReadLong(),Generation=r.ReadLong(),Nonce=r.ReadULong()};s.Validate();return s;}
        private static void Vector(NetworkWriter w,MoveVector v){w.WriteFloat(v.X);w.WriteFloat(v.Y);w.WriteFloat(v.Z);}
        private static MoveVector Vector(NetworkReader r)=>new MoveVector(r.ReadFloat(),r.ReadFloat(),r.ReadFloat());
        private static void State(NetworkWriter w,MovementState s){_ = new MovementState(s.Position,s.Velocity,s.Facing,s.Grounded,s.VerticalView);Vector(w,s.Position);Vector(w,s.Velocity);w.WriteByte((byte)(s.Facing==1?1:0));w.WriteByte(s.VerticalView);}
        private static MovementState State(NetworkReader r){var p=Vector(r);var v=Vector(r);byte face=r.ReadByte(),view=r.ReadByte();if(face>1||view>2)throw new FormatException("Move view.");return new MovementState(p,v,face==1?1:-1,view==0,view);}
        public static void WriteMoveRequest(this NetworkWriter w,PNMoveRequest v){Stream(w,v.Stream);if(v.Request==0)throw new FormatException("Move request.");w.WriteUInt(v.Request);}
        public static PNMoveRequest ReadMoveRequest(this NetworkReader r){var v=new PNMoveRequest{Stream=Stream(r),Request=r.ReadUInt()};if(v.Request==0)throw new FormatException("Move request.");return v;}
        private static void MirrorState(NetworkWriter w,MoveMirror m)
        {
            w.WriteUInt(m.Revision);w.WriteLong(m.LifeRevision);w.WriteByte((byte)(m.Dead?1:0));w.WriteByte((byte)m.Rights);w.WriteFloat(m.MoveSpeed);w.WriteFloat(m.JumpSpeed);w.WriteFloat(m.Scale);w.WriteFloat(m.RawScale);
            w.WriteByte((byte)m.MotionPermissions.Count);foreach(var id in m.MotionPermissions){var bytes=Utf8.GetBytes(id);if(bytes.Length<1||bytes.Length>128)throw new FormatException("Move permission size.");w.WriteByte((byte)bytes.Length);w.WriteBytes(bytes,0,bytes.Length);}
        }
        private static MoveMirror MirrorState(NetworkReader r,MoveIdentity id)
        {
            uint rev=r.ReadUInt();long life=r.ReadLong();byte dead=r.ReadByte(),rights=r.ReadByte();float speed=r.ReadFloat(),jump=r.ReadFloat(),scale=r.ReadFloat(),raw=r.ReadFloat();int count=r.ReadByte();if(dead>1||count>64)throw new FormatException("Move mirror bounds.");
            var permissions=new string[count];for(int i=0;i<count;i++){int size=r.ReadByte();if(size<1||size>128)throw new FormatException("Move permission size.");var bytes=r.ReadBytesSegment(size);permissions[i]=Utf8.GetString(bytes.Array,bytes.Offset,bytes.Count);}
            return new MoveMirror(id,rev,life,dead==1,(MoveRights)rights,speed,jump,scale,raw,permissions);
        }
        public static void WriteMovePacket(this NetworkWriter w,PNMovePacket p)
        {
            int start=w.Position;Validate(p);w.WriteByte(1);w.WriteByte(p.Kind);Stream(w,p.Stream);w.WriteUInt(p.Request);w.WriteLong(p.Revision);w.WriteLong(p.Life);
            if(p.Kind<=2)MirrorState(w,p.Mirror);
            if(p.Kind<=1){var c=p.Correction;w.WriteUInt(c.Id);w.WriteUInt(c.Epoch);w.WriteUInt(c.MirrorRevision);State(w,c.State);Vector(w,c.Recovery.ResumeVelocity);Vector(w,c.Recovery.ResumeAngularVelocity);w.WriteByte((byte)(c.Recovery.AwaitingGroundDeparture?1:0));w.WriteByte((byte)c.Reason);w.WriteUInt(c.ReportSequenceFloor);w.WriteUInt(c.BaselineRequestId);}
            else if(p.Kind<=3){w.WriteUInt(p.Pose.Sequence);w.WriteUInt(p.Pose.Epoch);w.WriteDouble(p.Pose.SampleTime);State(w,p.Pose.State);}
            else{var q=p.Report;w.WriteUInt(q.Sequence);w.WriteUInt(q.Epoch);w.WriteUInt(q.MirrorRevision);w.WriteUInt(q.CorrectionId);State(w,q.State);}
            if(w.Position-start>(p.Kind<=2?ControlBytes:PoseBytes))throw new FormatException("Move packet size.");
        }
        public static PNMovePacket ReadMovePacket(this NetworkReader r)
        {
            int start=r.Position;if(r.ReadByte()!=1)throw new FormatException("Move schema.");var p=new PNMovePacket{Kind=r.ReadByte(),Stream=Stream(r),Request=r.ReadUInt(),Revision=r.ReadLong(),Life=r.ReadLong()};var id=p.Stream.Identity;
            if(p.Kind>4)throw new FormatException("Move kind.");if(p.Kind<=2)p.Mirror=MirrorState(r,id);
            if(p.Kind<=1){uint cid=r.ReadUInt(),epoch=r.ReadUInt(),mirror=r.ReadUInt();var state=State(r);var v=Vector(r);var a=Vector(r);byte departure=r.ReadByte(),reason=r.ReadByte();if(departure>1)throw new FormatException("Move recovery.");p.Correction=new MoveCorrection(id,cid,epoch,mirror,state,new MoveRecovery(v,a,departure==1),(MoveCorrectionReason)reason,r.ReadUInt(),r.ReadUInt());}
            else if(p.Kind<=3){uint seq=r.ReadUInt(),epoch=r.ReadUInt();double time=r.ReadDouble();p.Pose=new MovePoseState(id,seq,epoch,time,State(r));}
            else{uint seq=r.ReadUInt(),epoch=r.ReadUInt(),mirror=r.ReadUInt(),correction=r.ReadUInt();p.Report=new MovePoseReport(id,seq,epoch,mirror,correction,State(r));}
            Validate(p);if(r.Position-start>(p.Kind<=2?ControlBytes:PoseBytes))throw new FormatException("Move packet bound.");return p;
        }
        public static void Validate(PNMovePacket p)
        {
            p.Stream.Validate();if(p.Kind>4||p.Request==0||p.Revision<1||p.Life<0)throw new FormatException("Move header.");var id=p.Stream.Identity;
            if(p.Kind<=2&&(p.Mirror==null||!p.Mirror.Identity.Equals(id)||p.Mirror.LifeRevision!=p.Life))throw new FormatException("Move mirror identity/life.");
            if(p.Kind<=1&&(p.Correction==null||!p.Correction.Identity.Equals(id)||!MoveOrder.AtLeast(p.Mirror.Revision,p.Correction.MirrorRevision)))throw new FormatException("Move correction pair.");
            if(p.Kind>=2&&p.Kind<=3&&(p.Pose==null||!p.Pose.Identity.Equals(id)))throw new FormatException("Move pose identity.");
            if(p.Kind==4&&(p.Report==null||!p.Report.Identity.Equals(id)))throw new FormatException("Move report identity.");
        }
    }
}
