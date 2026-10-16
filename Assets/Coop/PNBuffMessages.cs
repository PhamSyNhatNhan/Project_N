using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EveEngine;
using Mirror;

namespace ProjectN.Coop
{
    public struct PNBuffStream : IEquatable<PNBuffStream>
    {
        public Guid Session;public uint NetId;public long Entity,Generation;public ulong Nonce;
        public bool Equals(PNBuffStream other)=>Session==other.Session&&NetId==other.NetId&&Entity==other.Entity&&Generation==other.Generation&&Nonce==other.Nonce;
        public void Validate(){if(Session==Guid.Empty||NetId==0||Entity<=0||Generation<=0||Nonce==0)throw new FormatException("Buff stream identity.");}
    }
    public struct PNBuffBaselineRequest:NetworkMessage {public PNBuffStream Stream;}
    // Every fragment repeats the bounded transfer declaration. Completion is count+exact bytes, not a partial state apply.
    public struct PNBuffPacket:NetworkMessage
    {
        public PNBuffStream Stream;public ulong Transfer;public byte Kind;public long Revision;
        public double Time,HostTime,Rate;public int Total,Index,Count;public byte[] Body;
    }
    public static class PNBuffWire
    {
        public const int BaselineBytes=1048576,RecordBytes=65536,ChunkBytes=16384;
        private static readonly Encoding Utf8=new UTF8Encoding(false,true);
        public static void WriteBuffPacket(this NetworkWriter w,PNBuffPacket p)
        {
            ValidatePacket(p);w.WriteByte(1);w.Write(p.Stream);w.WriteULong(p.Transfer);w.WriteByte(p.Kind);w.WriteLong(p.Revision);
            w.WriteDouble(p.Time);w.WriteDouble(p.HostTime);w.WriteDouble(p.Rate);w.WriteInt(p.Total);w.WriteInt(p.Index);w.WriteInt(p.Count);w.WriteInt(p.Body.Length);w.WriteBytes(p.Body,0,p.Body.Length);
        }
        public static PNBuffPacket ReadBuffPacket(this NetworkReader r)
        {
            if(r.ReadByte()!=1)throw new FormatException("Buff packet schema.");
            var p=new PNBuffPacket {Stream=r.Read<PNBuffStream>(),Transfer=r.ReadULong(),Kind=r.ReadByte(),Revision=r.ReadLong(),Time=r.ReadDouble(),HostTime=r.ReadDouble(),Rate=r.ReadDouble(),Total=r.ReadInt(),Index=r.ReadInt(),Count=r.ReadInt()};
            int size=r.ReadInt();ValidateHeader(p);if(size<1||size>ChunkBytes||size!=Math.Min(ChunkBytes,p.Total-p.Index*ChunkBytes))throw new FormatException("Buff fragment size.");
            p.Body=r.ReadBytes(size);return p;
        }
        private static void ValidateHeader(PNBuffPacket p)
        {
            p.Stream.Validate();if(p.Transfer==0||p.Kind>2||p.Revision<0||p.Total<1||p.Total>(p.Kind==0?BaselineBytes:RecordBytes)||p.Count!=(p.Total+ChunkBytes-1)/ChunkBytes||p.Index<0||p.Index>=p.Count)throw new FormatException("Buff transfer header.");
            PNBuffCollection.Seconds(p.Time,true);PNBuffCollection.Seconds(p.HostTime,true);PNBuffCollection.Seconds(p.Rate,true);
        }
        public static void ValidatePacket(PNBuffPacket p){ValidateHeader(p);if(p.Body==null||p.Body.Length!=Math.Min(ChunkBytes,p.Total-p.Index*ChunkBytes))throw new FormatException("Buff fragment size.");}
        private static void Id(NetworkWriter w,EveId id){var b=Utf8.GetBytes(id.ToString());if(b.Length<1||b.Length>128)throw new FormatException("Buff ID length.");w.WriteByte((byte)b.Length);w.WriteBytes(b,0,b.Length);}
        private static EveId Id(NetworkReader r){int n=r.ReadByte();if(n<1||n>128)throw new FormatException("Buff ID length.");return EveId.Parse(Utf8.GetString(r.ReadBytes(n)));}
        private static void Token(NetworkWriter w,Newtonsoft.Json.Linq.JToken value){var b=PNBuffPayloadValidation.Encode(value);w.WriteInt(b.Length);w.WriteBytes(b,0,b.Length);}
        private static Newtonsoft.Json.Linq.JToken Token(NetworkReader r,Action<int> reserveNodes=null){int n=r.ReadInt();if(n<1||n>PNBuffPayloadValidation.MaximumBytes)throw new FormatException("Buff payload length.");return PNBuffPayloadValidation.Decode(r.ReadBytes(n),reserveNodes);}
        private static void Nullable(NetworkWriter w,double? value){w.WriteBool(value.HasValue);if(value.HasValue)w.WriteDouble(value.Value);}
        private static double? Nullable(NetworkReader r)=>Bool(r)?r.ReadDouble():(double?)null;
        private static bool Bool(NetworkReader r){byte value=r.ReadByte();if(value>1)throw new FormatException("Buff boolean.");return value==1;}
        private static void Number(NetworkWriter w,long? value){w.WriteBool(value.HasValue);if(value.HasValue)w.WriteLong(value.Value);}
        private static long? Number(NetworkReader r)=>Bool(r)?r.ReadLong():(long?)null;
        private static void State(NetworkWriter w,PNBuffWireState s)
        {
            w.WriteLong(s.InstanceId);w.WriteLong(s.Revision);Id(w,s.BuffId);Id(w,s.ProfileId);w.WriteLong(s.SourceId.Value);Nullable(w,s.Stacks);w.WriteBool(s.Suspended);
            Number(w,s.Numbers.TopLeft);Number(w,s.Numbers.TopRight);Number(w,s.Numbers.BottomLeft);Number(w,s.Numbers.BottomRight);
            w.WriteByte((byte)s.Clock);w.WriteDouble(s.BuffTime);w.WriteDouble(s.AnchorTime);w.WriteDouble(s.Rate);Nullable(w,s.Expiration);w.WriteInt(s.PayloadVersion);Token(w,s.DynamicData);
        }
        private static PNBuffWireState State(NetworkReader r,Action<int> reserveNodes=null)
        {
            long instance=r.ReadLong(),revision=r.ReadLong();var id=Id(r);var profile=Id(r);var source=new EveEntityId(r.ReadLong());var stacks=Nullable(r);bool suspended=Bool(r);
            var numbers=new PNBuffNumbers(Number(r),Number(r),Number(r),Number(r));var clock=(PNBuffClock)r.ReadByte();double time=r.ReadDouble(),anchor=r.ReadDouble(),rate=r.ReadDouble();var expiry=Nullable(r);int version=r.ReadInt();var data=Token(r,reserveNodes);
            return new PNBuffWireState(instance,revision,id,profile,source,stacks,suspended,numbers,clock,time,anchor,rate,expiry,data,version);
        }
        private static void Cue(NetworkWriter w,PNBuffCue cue){w.WriteLong(cue.Id);w.WriteLong(cue.InstanceId);var name=Utf8.GetBytes(cue.Name);if(name.Length<1||name.Length>128)throw new FormatException("Buff cue name.");w.WriteByte((byte)name.Length);w.WriteBytes(name,0,name.Length);w.WriteDouble(cue.At);w.WriteDouble(cue.MaximumAge);Token(w,cue.Payload);}
        private static PNBuffCue Cue(NetworkReader r,Action<int> reserveNodes=null){long id=r.ReadLong(),instance=r.ReadLong();int n=r.ReadByte();if(n<1||n>128)throw new FormatException("Buff cue name.");var name=Utf8.GetString(r.ReadBytes(n));double at=r.ReadDouble(),age=r.ReadDouble();return new PNBuffCue(id,instance,name,at,age,Token(r,reserveNodes));}
        private static void Groups(NetworkWriter w,IReadOnlyDictionary<EveId,long> groups){if(groups.Count>4096)throw new FormatException("Buff groups.");w.WriteInt(groups.Count);foreach(var pair in groups.OrderBy(x=>x.Value)){Id(w,pair.Key);w.WriteLong(pair.Value);}}
        private static IReadOnlyDictionary<EveId,long> Groups(NetworkReader r){int n=r.ReadInt();if(n<0||n>4096)throw new FormatException("Buff groups.");var groups=new Dictionary<EveId,long>();for(int i=0;i<n;i++)groups.Add(Id(r),r.ReadLong());return groups;}
        public static byte[] Encode(PNBuffFrame frame)
        {using(var w=NetworkWriterPool.Get()){w.WriteByte(1);w.WriteInt(frame.State.States.Count);foreach(var s in frame.State.States){State(w,s);if(w.Position>BaselineBytes)throw new FormatException("Buff baseline bound.");}Groups(w,frame.Groups);return Finish(w,BaselineBytes);}}
        public static PNBuffFrame DecodeFrame(byte[] bytes,PNBuffPacket p)
        {
            var decoder=new FrameDecoder(bytes,p);while(!decoder.Advance()){}return decoder.Result;
        }
        public sealed class FrameDecoder
        {
            private readonly NetworkReader reader;private readonly PNBuffPacket packet;private readonly PNBuffWireState[] states;private int index;private readonly Action<int> reserveNodes;
            public PNBuffFrame Result {get;private set;}
            public FrameDecoder(byte[] bytes,PNBuffPacket packet,Action<int> reserveNodes=null)
            {reader=new NetworkReader(bytes);this.packet=packet;this.reserveNodes=reserveNodes;if(reader.ReadByte()!=1)throw new FormatException("Buff frame schema.");int n=reader.ReadInt();if(n<0||n>4096)throw new FormatException("Buff instances.");states=new PNBuffWireState[n];}
            public bool Advance()
            {
                if(Result!=null)return true;
                for(int quantum=0;quantum<8&&index<states.Length;quantum++)states[index++]=State(reader,reserveNodes);
                if(index<states.Length)return false;
                var groups=Groups(reader);End(reader);
                Result=new PNBuffFrame(new PNBuffWireMessage(packet.Stream.Session,new EveEntityId(packet.Stream.Entity),packet.Stream.Generation,packet.Revision,PNBuffWireKind.Snapshot,states),packet.Time,groups);return true;
            }
        }
        public static byte[] Encode(PNBuffChange change)
        {
            using(var w=NetworkWriterPool.Get())
            {
                w.WriteByte(1);w.WriteByte((byte)change.State.Kind);if(change.State.Kind==PNBuffWireKind.Removed){w.WriteLong(change.State.RemovedId);w.WriteByte((byte)change.State.EndReason.Value);w.WriteBool(change.FinalState!=null);if(change.FinalState!=null)State(w,change.FinalState);}
                else {if(change.State.States.Count!=1)throw new FormatException("Buff change state count.");State(w,change.State.States[0]);}
                if(change.State.Cues.Count>256)throw new FormatException("Buff cues.");w.WriteInt(change.State.Cues.Count);foreach(var cue in change.State.Cues){Cue(w,cue);if(w.Position>RecordBytes)throw new FormatException("Buff record bound.");}Groups(w,change.Groups);return Finish(w,RecordBytes);
            }
        }
        public static PNBuffChange DecodeChange(byte[] bytes,PNBuffPacket p,Action<int> reserveNodes=null)
        {
            var r=new NetworkReader(bytes);if(r.ReadByte()!=1)throw new FormatException("Buff change schema.");var kind=(PNBuffWireKind)r.ReadByte();long removed=0;PNBuffEndReason? reason=null;PNBuffWireState final=null;var states=new List<PNBuffWireState>();
            if(kind==PNBuffWireKind.Removed){removed=r.ReadLong();reason=(PNBuffEndReason)r.ReadByte();if(!Enum.IsDefined(typeof(PNBuffEndReason),reason.Value))throw new FormatException("Buff removal reason.");if(Bool(r))final=State(r,reserveNodes);if(final==null||final.InstanceId!=removed)throw new FormatException("Buff final removal state.");}
            else if(kind==PNBuffWireKind.Added||kind==PNBuffWireKind.Updated)states.Add(State(r,reserveNodes));else throw new FormatException("Buff change kind.");
            int n=r.ReadInt();if(n<0||n>256)throw new FormatException("Buff cues.");var cues=new PNBuffCue[n];for(int i=0;i<n;i++)cues[i]=Cue(r,reserveNodes);var groups=Groups(r);End(r);
            return new PNBuffChange(new PNBuffWireMessage(p.Stream.Session,new EveEntityId(p.Stream.Entity),p.Stream.Generation,p.Revision,kind,states,removed,reason,cues),final,p.Time,groups);
        }
        public static byte[] Encode(PNBuffOptionalCueMessage cue){using(var w=NetworkWriterPool.Get()){w.WriteByte(1);Cue(w,cue.Cue);return Finish(w,RecordBytes);}}
        public static PNBuffOptionalCueMessage DecodeCue(byte[] bytes,PNBuffPacket p,Action<int> reserveNodes=null){var r=new NetworkReader(bytes);if(r.ReadByte()!=1)throw new FormatException("Buff cue schema.");var cue=Cue(r,reserveNodes);End(r);return new PNBuffOptionalCueMessage(p.Stream.Session,new EveEntityId(p.Stream.Entity),p.Stream.Generation,cue);}
        private static byte[] Finish(NetworkWriter w,int bound){if(w.Position>bound)throw new FormatException("Buff encoded byte bound.");return w.ToArray();}
        private static void End(NetworkReader r){if(r.Remaining!=0)throw new FormatException("Trailing Buff record data.");}
    }
}
