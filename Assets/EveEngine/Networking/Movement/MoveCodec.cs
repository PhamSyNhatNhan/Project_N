using System;
using System.IO;
using System.Text;

namespace EveEngine.Movement
{
    // Standalone binary sidechannel. Never nested inside actor JSON/base64.
    public static class MoveCodec
    {
        public const int MaximumBytes=8704;
        private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        public static byte[] Encode(object message)
        {
            using(var stream=new MemoryStream())
            using(var writer=new BinaryWriter(stream,Utf8,true))
            {
                writer.Write((byte)1);
                byte kind=message is MovePoseReport?(byte)1:message is MoveMirror?(byte)2:message is MoveCorrection?(byte)3:message is MovePoseState?(byte)4:message is MoveBaselineRequest?(byte)5:throw new ArgumentException("Move message.");
                writer.Write(kind);
                var id=message is MoveMirror mirror?mirror.Identity:((MoveMessage)message).Identity;
                writer.Write(id.Session.ToByteArray()); writer.Write(id.Entity); writer.Write(id.Generation);
                if(message is MovePoseReport report)
                { writer.Write(report.Sequence); writer.Write(report.Epoch); writer.Write(report.MirrorRevision); writer.Write(report.CorrectionId); Write(writer,report.State); }
                else if(message is MoveMirror m)
                {
                    writer.Write(m.Revision); writer.Write(m.LifeRevision); writer.Write(m.Dead); writer.Write((byte)m.Rights);
                    writer.Write(m.MoveSpeed); writer.Write(m.JumpSpeed); writer.Write(m.Scale); writer.Write(m.RawScale);
                    writer.Write((byte)m.MotionPermissions.Count);
                    foreach(var permission in m.MotionPermissions) { var bytes=Utf8.GetBytes(permission); writer.Write((byte)bytes.Length); writer.Write(bytes); }
                }
                else if(message is MoveCorrection c)
                {
                    writer.Write(c.Id); writer.Write(c.Epoch); writer.Write(c.MirrorRevision); writer.Write(c.ReportSequenceFloor); writer.Write(c.BaselineRequestId); writer.Write((byte)c.Reason); Write(writer,c.State);
                    Write(writer,c.Recovery.ResumeVelocity); Write(writer,c.Recovery.ResumeAngularVelocity); writer.Write(c.Recovery.AwaitingGroundDeparture);
                }
                else if(message is MovePoseState s)
                { writer.Write(s.Sequence); writer.Write(s.Epoch); writer.Write(s.SampleTime); Write(writer,s.State); }
                else if(message is MoveBaselineRequest request)writer.Write(request.RequestId);
                writer.Flush(); if(stream.Length>MaximumBytes)throw new FormatException("Move capacity."); return stream.ToArray();
            }
        }
        public static object Decode(byte[] bytes)
        {
            if(bytes==null || bytes.Length<34 || bytes.Length>MaximumBytes)throw new FormatException("Move size.");
            try
            {
                using(var stream=new MemoryStream(bytes,false))
                using(var reader=new BinaryReader(stream,Utf8))
                {
                    if(reader.ReadByte()!=1)throw new FormatException("Move version.");
                    byte kind=reader.ReadByte(); var id=new MoveIdentity(new Guid(Read(reader,16)),reader.ReadInt64(),reader.ReadInt64()); object result;
                    switch(kind)
                    {
                        case 1: { uint seq=reader.ReadUInt32(),epoch=reader.ReadUInt32(),mirror=reader.ReadUInt32(),correction=reader.ReadUInt32(); result=new MovePoseReport(id,seq,epoch,mirror,correction,ReadState(reader)); break; }
                        case 2:
                        {
                            uint revision=reader.ReadUInt32(); long life=reader.ReadInt64(); bool dead=Bool(reader); var rights=(MoveRights)reader.ReadByte();
                            float speed=reader.ReadSingle(),jump=reader.ReadSingle(),scale=reader.ReadSingle(),raw=reader.ReadSingle(); int count=reader.ReadByte();
                            if(count>64)throw new FormatException("Permission capacity."); var permissions=new string[count];
                            for(int i=0;i<count;i++) { int length=reader.ReadByte(); if(length>128)throw new FormatException("Permission length."); permissions[i]=Utf8.GetString(Read(reader,length)); }
                            result=new MoveMirror(id,revision,life,dead,rights,speed,jump,scale,raw,permissions); break;
                        }
                        case 3:
                        {
                            uint correction=reader.ReadUInt32(),epoch=reader.ReadUInt32(),mirror=reader.ReadUInt32(),floor=reader.ReadUInt32(),request=reader.ReadUInt32(); var reason=(MoveCorrectionReason)reader.ReadByte(); var state=ReadState(reader);
                            result=new MoveCorrection(id,correction,epoch,mirror,state,new MoveRecovery(ReadVector(reader),ReadVector(reader),Bool(reader)),reason,floor,request); break;
                        }
                        case 4: { uint seq=reader.ReadUInt32(),epoch=reader.ReadUInt32(); double time=reader.ReadDouble(); result=new MovePoseState(id,seq,epoch,time,ReadState(reader)); break; }
                        case 5: { result=new MoveBaselineRequest(id,reader.ReadUInt32());break; }
                        default: throw new FormatException("Move kind.");
                    }
                    if(stream.Position!=stream.Length)throw new FormatException("Move trailing bytes."); return result;
                }
            }
            catch(Exception error) when(error is IOException || error is ArgumentException || error is DecoderFallbackException)
            { throw new FormatException("Malformed Move message.",error); }
        }
        private static byte[] Read(BinaryReader r,int count) { var b=r.ReadBytes(count); if(b.Length!=count)throw new EndOfStreamException(); return b; }
        private static bool Bool(BinaryReader r) { byte b=r.ReadByte(); if(b>1)throw new FormatException("Move boolean."); return b==1; }
        private static void Write(BinaryWriter w,MoveVector v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        private static MoveVector ReadVector(BinaryReader r) => new MoveVector(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        private static void Write(BinaryWriter w,MovementState s) { Write(w,s.Position); Write(w,s.Velocity); w.Write((sbyte)s.Facing); w.Write(s.Grounded); w.Write(s.VerticalView); }
        private static MovementState ReadState(BinaryReader r) => new MovementState(ReadVector(r),ReadVector(r),r.ReadSByte(),Bool(r),r.ReadByte());
    }
}
