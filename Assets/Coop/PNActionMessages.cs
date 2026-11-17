using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Mirror;
using Newtonsoft.Json.Linq;
namespace ProjectN.Coop
{
    public struct PNActionStream : IEquatable<PNActionStream>
    {
        public Guid Session,Source; public uint NetId; public long Entity,Generation,Subscription; public ulong Nonce; public PNWorldBindingScope Scope;
        public void Validate(){Scope.Validate();if(Session==Guid.Empty||Source==Guid.Empty||NetId==0||Entity<=0||Generation<=0||Subscription<=0||Nonce==0)throw new FormatException("Action stream");}
        public bool Equals(PNActionStream o)=>Session==o.Session&&Source==o.Source&&NetId==o.NetId&&Entity==o.Entity&&Generation==o.Generation&&Subscription==o.Subscription&&Nonce==o.Nonce&&Scope.Equals(o.Scope);
    }
    // 0 baseline part; 1 live batch; 2 request; 3 actual-apply acknowledgement.
    public struct PNActionPacket : NetworkMessage
    {
        public PNActionStream Stream; public byte Kind; public long Request,Q,N; public double T;
        public int Total,Index,Count; public byte[] Digest,Body;
    }
    public static class PNActionWire
    {
        public const int MessageBytes=4096,ChunkBytes=3584,BaselineBytes=1048576,MaxParts=293;
        static readonly Encoding Utf8=new UTF8Encoding(false,true);
        public static void WriteActionStream(this NetworkWriter w,PNActionStream s)
        {s.Validate();w.WriteGuid(s.Session);w.WriteGuid(s.Source);w.WriteUInt(s.NetId);w.WriteLong(s.Entity);w.WriteLong(s.Generation);w.WriteLong(s.Subscription);w.WriteULong(s.Nonce);w.WriteGuid(s.Scope.World);w.WriteULong(s.Scope.Incarnation);w.WriteULong(s.Scope.Membership);w.WriteULong(s.Scope.Subscription);}
        public static PNActionStream ReadActionStream(this NetworkReader r)
        {var s=new PNActionStream{Session=r.ReadGuid(),Source=r.ReadGuid(),NetId=r.ReadUInt(),Entity=r.ReadLong(),Generation=r.ReadLong(),Subscription=r.ReadLong(),Nonce=r.ReadULong(),Scope=new PNWorldBindingScope{World=r.ReadGuid(),Incarnation=r.ReadULong(),Membership=r.ReadULong(),Subscription=r.ReadULong()}};s.Validate();return s;}
        public static void Header(PNActionPacket p)
        {
            p.Stream.Validate();if(p.Kind>3||p.Request<=0||p.Q<0||p.N<0||p.T<0||double.IsNaN(p.T)||double.IsInfinity(p.T)||p.Digest==null||p.Digest.Length!=32)throw new FormatException("Action header");
            if(p.Kind>=2){if(p.Total!=0||p.Count!=0||p.Index!=0)throw new FormatException("Action control");return;}
            if(p.Q<=0||p.Total<1||p.Total>(p.Kind==0?BaselineBytes:ChunkBytes)||p.Count!=(p.Total+ChunkBytes-1)/ChunkBytes||p.Count>MaxParts||p.Index<0||p.Index>=p.Count)throw new FormatException("Action parts");
        }
        public static void WriteActionPacket(this NetworkWriter w,PNActionPacket p)
        {
            Header(p);int n=p.Kind>=2?0:Math.Min(ChunkBytes,p.Total-p.Index*ChunkBytes);if(p.Body==null||p.Body.Length!=n)throw new FormatException("Action body");
            int start=w.Position;w.WriteByte(1);w.WriteActionStream(p.Stream);w.WriteByte(p.Kind);w.WriteLong(p.Request);w.WriteLong(p.Q);w.WriteLong(p.N);w.WriteDouble(p.T);w.WriteInt(p.Total);w.WriteInt(p.Index);w.WriteInt(p.Count);w.WriteBytes(p.Digest,0,32);w.WriteInt(n);w.WriteBytes(p.Body,0,n);if(w.Position-start>MessageBytes-2)throw new FormatException("Action message bound");
        }
        public static PNActionPacket ReadActionPacket(this NetworkReader r)
        {
            if(r.Remaining>MessageBytes-2||r.ReadByte()!=1)throw new FormatException("Action schema/size");
            var p=new PNActionPacket{Stream=r.ReadActionStream(),Kind=r.ReadByte(),Request=r.ReadLong(),Q=r.ReadLong(),N=r.ReadLong(),T=r.ReadDouble(),Total=r.ReadInt(),Index=r.ReadInt(),Count=r.ReadInt(),Digest=r.ReadBytes(32)};
            Header(p);int n=r.ReadInt();if(n!=(p.Kind>=2?0:Math.Min(ChunkBytes,p.Total-p.Index*ChunkBytes))||r.Remaining!=n)throw new FormatException("Action length");p.Body=r.ReadBytes(n);return p;
        }
        public static byte[] Hash(byte[] body){using(var sha=SHA256.Create())return sha.ComputeHash(body);}
        public static bool Same(byte[] a,byte[] b){if(a==null||b==null||a.Length!=b.Length)return false;int d=0;for(int i=0;i<a.Length;i++)d|=a[i]^b[i];return d==0;}
        public static PNActionPacket[] Pack(PNActionStream stream,long request,long q,double t,long cut,JArray body,bool baseline)
        {
            byte[] bytes=Encode(body);if(bytes.Length>(baseline?BaselineBytes:ChunkBytes))throw new FormatException("Action encoded bound");var digest=Hash(bytes);int count=(bytes.Length+ChunkBytes-1)/ChunkBytes;var result=new PNActionPacket[count];
            for(int i=0;i<count;i++){var part=new byte[Math.Min(ChunkBytes,bytes.Length-i*ChunkBytes)];Buffer.BlockCopy(bytes,i*ChunkBytes,part,0,part.Length);result[i]=new PNActionPacket{Stream=stream,Kind=(byte)(baseline?0:1),Request=request,Q=q,T=t,N=cut,Total=bytes.Length,Count=count,Index=i,Digest=(byte[])digest.Clone(),Body=part};Header(result[i]);}return result;
        }
        internal static int RequiredSize(PNActionPublication.Required row)
        {
            if(row==null)throw new FormatException("Action required row");
            switch(row.RecordKind){case 0:return 87+StringSize(row.EntryId)+StringSize(row.DataId)+StringSize(row.ActionId);case 2:return 96;case 3:return 105;case 4:return 91;default:throw new FormatException("Action required kind");}
        }
        static int StringSize(string text){if(text==null)throw new FormatException("Action string");int n=Utf8.GetByteCount(text);if(n>384)throw new FormatException("Action string");return 5+n;}
        internal static PNActionPacket[] PackRequired(PNActionStream stream,long request,long q,double t,long cut,System.Collections.Generic.IReadOnlyList<PNActionPublication.Required> rows,int count)
        {
            if(rows==null||count<1||count>256||count>rows.Count)throw new FormatException("Action required count");int size=5;for(int i=0;i<count;i++)size=checked(size+RequiredSize(rows[i]));if(size>ChunkBytes)throw new FormatException("Action required bytes");
            var body=new byte[size];
            using(var memory=new MemoryStream(body,true))using(var writer=new BinaryWriter(memory,Utf8))
            {
                void ArrayHead(int n){writer.Write((byte)0);writer.Write(n);}
                void Integer(long n){writer.Write((byte)1);writer.Write(n);}
                void Number(double n){PNTimeMath.Nonnegative(n);writer.Write((byte)2);writer.Write(n);}
                void Text(string text){var bytes=Utf8.GetBytes(text);writer.Write((byte)3);writer.Write(bytes.Length);writer.Write(bytes);}
                ArrayHead(count);
                for(int i=0;i<count;i++)
                {
                    var row=rows[i];int kind=row.RecordKind;
                    if(row.N<=0||row.RunKey.Cast==0||row.RunKey.Ordinal<=0||row.RunRevision<=0||row.RunHistoryGeneration<=0)throw new FormatException("Action required scalar");
                    ArrayHead(kind==0?4:kind==2?6:kind==3?7:8);Integer(kind);Integer(row.N);ArrayHead(2);Integer(row.RunKey.Cast);Integer(row.RunKey.Ordinal);
                    if(kind==0){if(double.IsNaN(row.DirectionX)||double.IsInfinity(row.DirectionX)||double.IsNaN(row.DirectionZ)||double.IsInfinity(row.DirectionZ))throw new FormatException("Action direction number");ArrayHead(7);Text(row.EntryId);Text(row.DataId);Text(row.ActionId);Integer(1);writer.Write((byte)2);writer.Write(row.DirectionX);writer.Write((byte)2);writer.Write(row.DirectionZ);Number(row.StartStamp);}
                    else if(kind==4){if(row.TerminalKind<1||row.TerminalKind>2)throw new FormatException("Action terminal kind");Integer(row.RunRevision);Integer(row.TerminalKind);Number(row.RecordTime);Number(row.RecordedProgress);Integer(row.RunHistoryGeneration);}
                    else{Integer(row.RunHistoryGeneration);Integer(row.RunRevision);if(kind==3){if(row.ResetReason<1||row.ResetReason>2)throw new FormatException("Action reset reason");Integer(row.ResetReason);}ArrayHead(3);Number(row.RecordTime);Number(row.RecordedProgress);Number(row.RecordedRate);}
                }
                if(memory.Position!=body.Length)throw new FormatException("Action required encoded size");
            }
            var packet=new PNActionPacket{Stream=stream,Kind=1,Request=request,Q=q,T=t,N=cut,Total=body.Length,Count=1,Index=0,Digest=Hash(body),Body=body};Header(packet);return new[]{packet};
        }
        internal const int LatestRowBytes=91;
        // Same bounded tree grammar, emitted directly from frozen scalar samples.
        // No JSON/JToken materialization and no change to the receiver schema.
        internal static PNActionPacket[] PackLatest(PNActionStream stream,long request,long q,double t,long cut,System.Collections.Generic.IReadOnlyList<PNActionPublication.Subscription.LatestSample> rows)
        {
            if(rows==null||rows.Count<1||rows.Count>32)throw new FormatException("Action latest count");
            var body=new byte[5+rows.Count*LatestRowBytes];
            using(var memory=new MemoryStream(body,true))using(var writer=new BinaryWriter(memory,Utf8))
            {
                void ArrayHead(int n){writer.Write((byte)0);writer.Write(n);}
                void Integer(long n){writer.Write((byte)1);writer.Write(n);}
                void Number(double n){PNTimeMath.Nonnegative(n);writer.Write((byte)2);writer.Write(n);}
                ArrayHead(rows.Count);
                foreach(var row in rows)
                {
                    if(row==null||row.Key.Cast==0||row.Key.Ordinal<=0||row.Revision<=0||row.HistoryGeneration<=0||row.NeedSequence<0)throw new FormatException("Action latest scalar");
                    ArrayHead(8);Integer(1);ArrayHead(2);Integer(row.Key.Cast);Integer(row.Key.Ordinal);Integer(row.Revision);Number(row.Stamp);Number(row.Progress);Number(row.Rate);Integer(row.HistoryGeneration);Integer(row.NeedSequence);
                }
                if(memory.Position!=body.Length)throw new FormatException("Action latest encoded size");
            }
            var packet=new PNActionPacket{Stream=stream,Kind=1,Request=request,Q=q,T=t,N=cut,Total=body.Length,Count=1,Index=0,Digest=Hash(body),Body=body};Header(packet);return new[]{packet};
        }
        // Bounded binary arrays preserve source integer/double distinctions. No objects,
        // arbitrary type metadata or JSON parsing in the receiver wire boundary.
        public static byte[] Encode(JArray value)
        {using(var m=new MemoryStream())using(var w=new BinaryWriter(m,Utf8,true)){int nodes=0;Write(w,value,0,ref nodes);if(m.Length>BaselineBytes)throw new FormatException("Action payload bytes");return m.ToArray();}}
        // Exact binary size without serializing every trial prefix in the sender.
        public static int EncodedSize(JToken value)
        {int nodes=0;int size=Size(value,0,ref nodes);if(size>BaselineBytes)throw new FormatException("Action payload bytes");return size;}
        static int Size(JToken value,int depth,ref int nodes)
        {
            if(depth>16||++nodes>100000)throw new FormatException("Action tree bound");
            if(value is JArray array){if(array.Count>8192)throw new FormatException("Action array");int bytes=5;foreach(var item in array)bytes=checked(bytes+Size(item,depth+1,ref nodes));return bytes;}
            if(value.Type==JTokenType.Integer){checked{long n=(long)value;}return 9;}
            if(value.Type==JTokenType.Float){double n=(double)value;if(double.IsNaN(n)||double.IsInfinity(n))throw new FormatException("Action number");return 9;}
            if(value.Type==JTokenType.String){int bytes=Utf8.GetByteCount((string)value);if(bytes>384)throw new FormatException("Action string");return 5+bytes;}
            throw new FormatException("Action scalar");
        }
        static void Write(BinaryWriter w,JToken v,int depth,ref int nodes)
        {
            if(depth>16||++nodes>100000||w.BaseStream.Length>BaselineBytes)throw new FormatException("Action tree bound");
            if(v is JArray a){if(a.Count>8192)throw new FormatException("Action array");w.Write((byte)0);w.Write(a.Count);foreach(var x in a)Write(w,x,depth+1,ref nodes);}
            else if(v.Type==JTokenType.Integer){w.Write((byte)1);w.Write(checked((long)v));}
            else if(v.Type==JTokenType.Float){double d=(double)v;if(double.IsNaN(d)||double.IsInfinity(d))throw new FormatException("Action number");w.Write((byte)2);w.Write(d);}
            else if(v.Type==JTokenType.String){byte[] b=Utf8.GetBytes((string)v);if(b.Length>384)throw new FormatException("Action string");w.Write((byte)3);w.Write(b.Length);w.Write(b);}
            else throw new FormatException("Action scalar");
        }
        internal struct LatestValue
        {public long Cast,Ordinal,Revision,HistoryGeneration,NeedSequence;public double Stamp,Progress,Rate;}
        // Fast path for homogeneous U packets. A mixed batch uses the original
        // bounded tree decoder. Parse the whole packet before model mutation.
        internal static LatestValue[] DecodeLatest(byte[] body)
        {
            if(body==null||body.Length<19||body.Length>ChunkBytes||body[0]!=0||body[5]!=0||body[10]!=1||body[11]!=1)return null;
            using(DecodeMarker.Auto())using(var memory=new MemoryStream(body,false))using(var reader=new BinaryReader(memory,Utf8))
            {
                bool ArrayHead(int count)=>reader.ReadByte()==0&&reader.ReadInt32()==count;
                long Integer(){if(reader.ReadByte()!=1)throw new FormatException("Action latest integer");return reader.ReadInt64();}
                double Number(){byte tag=reader.ReadByte();double value=tag==1?reader.ReadInt64():tag==2?reader.ReadDouble():throw new FormatException("Action latest number");PNTimeMath.Nonnegative(value);return value;}
                if(reader.ReadByte()!=0)return null;int count=reader.ReadInt32();
                if(count<1||count>256||count>(body.Length-5)/LatestRowBytes)return null;
                var rows=new LatestValue[count];
                for(int i=0;i<count;i++)
                {
                    if(!ArrayHead(8)||Integer()!=1)return null;
                    if(!ArrayHead(2))return null;
                    var value=new LatestValue{Cast=Integer(),Ordinal=Integer(),Revision=Integer(),Stamp=Number(),Progress=Number(),Rate=Number(),HistoryGeneration=Integer(),NeedSequence=Integer()};
                    if(value.Cast==0||value.Ordinal<=0||value.Revision<=0||value.HistoryGeneration<=0||value.NeedSequence<0)throw new FormatException("Action latest scalar range");rows[i]=value;
                }
                if(memory.Position!=body.Length)throw new FormatException("Action trailing latest payload");return rows;
            }
        }
        static readonly Unity.Profiling.ProfilerMarker DecodeMarker=new Unity.Profiling.ProfilerMarker("PN.Action.DecodeTree");
        public static JArray Decode(byte[] body)
        {using(DecodeMarker.Auto()){if(body==null||body.Length<1||body.Length>BaselineBytes)throw new FormatException("Action payload bound");using(var m=new MemoryStream(body,false))using(var r=new BinaryReader(m,Utf8)){int nodes=0;var a=Read(r,0,ref nodes) as JArray;if(a==null||m.Position!=m.Length)throw new FormatException("Action trailing payload");return a;}}}
        static JToken Read(BinaryReader r,int depth,ref int nodes)
        {
            if(depth>16||++nodes>100000)throw new FormatException("Action tree bound");switch(r.ReadByte()){
            case 0:int n=r.ReadInt32();if(n<0||n>8192||n>r.BaseStream.Length-r.BaseStream.Position)throw new FormatException("Action count");var a=new JArray();for(int i=0;i<n;i++)a.Add(Read(r,depth+1,ref nodes));return a;
            case 1:return new JValue(r.ReadInt64());case 2:double d=r.ReadDouble();if(double.IsNaN(d)||double.IsInfinity(d))throw new FormatException("Action finite");return new JValue(d);
            case 3:int len=r.ReadInt32();if(len<0||len>384||len>r.BaseStream.Length-r.BaseStream.Position)throw new FormatException("Action string");return new JValue(Utf8.GetString(r.ReadBytes(len)));default:throw new FormatException("Action type");}
        }
    }
}
