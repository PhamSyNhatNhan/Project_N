using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EveEngine;
using Mirror;

namespace ProjectN.Coop
{
    public struct PNStatNotice:NetworkMessage
    {public uint NetId;public Guid Session;public long Entity,Generation;public PNStatFrame State;}
    public struct PNStateFence:NetworkMessage
    {public Guid Session;public uint Attempt,NetId;public byte Adapter;public ulong Nonce;public long Entity,Generation,Revision;public string Digest;}
    public static class PNStatWire
    {
        private static readonly Encoding Utf8=new UTF8Encoding(false,true);
        public static int Size(PNStatFrame state)
        {
            if(state==null)return 1;
            int size=22;
            foreach(var pair in state.Resources){int count=Utf8.GetByteCount(pair.Key.ToString());if(count<1||count>128)throw new FormatException("Stat ID wire bound.");size=checked(size+18+count);}
            if(size>32768)throw new FormatException("Stat payload bound.");return size;
        }
        public static void WriteStatFrame(this NetworkWriter writer,PNStatFrame frame)
        {
            Size(frame);writer.WriteByte((byte)(frame==null?0:1));if(frame==null)return;
            writer.WriteByte(1);writer.WriteByte((byte)(frame.LifeOnly?1:0));writer.WriteLong(frame.Revision);writer.WriteLong(frame.LifeRevision);writer.WriteByte((byte)(frame.Dead?1:0));writer.WriteUShort((ushort)frame.Resources.Count);
            foreach(var pair in frame.Resources.OrderBy(x=>x.Key.ToString(),StringComparer.Ordinal))
            {var bytes=Utf8.GetBytes(pair.Key.ToString());writer.WriteByte((byte)bytes.Length);writer.WriteBytes(bytes,0,bytes.Length);writer.WriteByte((byte)EveStatKind.Resource);writer.WriteDouble(pair.Value.Current);writer.WriteDouble(pair.Value.Max);}
        }
        public static PNStatFrame ReadStatFrame(this NetworkReader reader)
        {
            byte present=reader.ReadByte();if(present==0)return null;if(present!=1||reader.ReadByte()!=1)throw new FormatException("Stat schema.");
            byte flags=reader.ReadByte();long revision=reader.ReadLong(),life=reader.ReadLong();byte dead=reader.ReadByte();int count=reader.ReadUShort();
            if(flags>1||dead>1||count>128||flags==1&&count!=0)throw new FormatException("Stat header.");
            var values=new Dictionary<EveId,PNResourceValue>();
            for(int i=0;i<count;i++)
            {
                int length=reader.ReadByte();if(length<1||length>128)throw new FormatException("Stat ID size.");
                var bytes=reader.ReadBytesSegment(length);var id=EveId.Parse(Utf8.GetString(bytes.Array,bytes.Offset,bytes.Count));
                if(reader.ReadByte()!=(byte)EveStatKind.Resource||values.ContainsKey(id))throw new FormatException("Stat kind/duplicate.");
                values.Add(id,new PNResourceValue(reader.ReadDouble(),reader.ReadDouble()));
            }
            var frame=new PNStatFrame(revision,life,dead!=0,values,flags==1);Size(frame);return frame;
        }
    }
}
