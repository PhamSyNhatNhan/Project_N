using System;
using System.Linq;
using System.Text;
using EveEngine;
using Mirror;

namespace ProjectN.Coop
{
    public struct PNWorldTicket : IEquatable<PNWorldTicket>
    {
        public Guid Room,Play,World;
        public ulong RoomIncarnation,PlayRevision,Incarnation,Subscription;
        public void Validate()
        {if(Room==Guid.Empty||Play==Guid.Empty||World==Guid.Empty||RoomIncarnation==0||PlayRevision==0||Incarnation==0||Subscription==0)throw new FormatException("World ticket.");}
        public bool Equals(PNWorldTicket x)=>Room==x.Room&&Play==x.Play&&World==x.World&&RoomIncarnation==x.RoomIncarnation&&PlayRevision==x.PlayRevision&&Incarnation==x.Incarnation&&Subscription==x.Subscription;
    }
    public struct PNWorldBindingScope : IEquatable<PNWorldBindingScope>
    {
        public Guid World;
        public ulong Incarnation,Membership,Subscription;
        public void Validate(){if(World==Guid.Empty||Incarnation==0||Membership==0||Subscription==0)throw new FormatException("World binding scope.");}
        public bool Equals(PNWorldBindingScope x)=>World==x.World&&Incarnation==x.Incarnation&&Membership==x.Membership&&Subscription==x.Subscription;
    }
    public struct PNWorldOffer : NetworkMessage
    {public PNWorldTicket Ticket;public string Definition,ContentHash;public bool Active,DefaultWorld,Paused;}
    public struct PNWorldLoaded : NetworkMessage
    {public PNWorldTicket Ticket;public bool Success;}
    public struct PNWorldFence : NetworkMessage
    {public PNWorldTicket Ticket;public ulong Revision;public PNStateFence Target;}
    public struct PNWorldApplied : NetworkMessage
    {public PNWorldTicket Ticket;public ulong Revision;public string Digest;public bool Confirmed,InitialReady;}
    public static class PNWorldWire
    {
        public const int ControlBytes=16384,DescriptorBytes=262144;
        private static readonly Encoding Utf8=new UTF8Encoding(false,true);
        private static void Control(NetworkReader reader){if(reader.Remaining>ControlBytes)throw new FormatException("World control message bound.");}
        private static void Text(NetworkWriter w,string value,int maximum)
        {if(value==null)throw new FormatException("Missing world string.");var bytes=Utf8.GetBytes(value);if(bytes.Length<1||bytes.Length>maximum)throw new FormatException("World string bound.");w.WriteUShort((ushort)bytes.Length);w.WriteBytes(bytes,0,bytes.Length);}
        private static string Text(NetworkReader r,int maximum)
        {int size=r.ReadUShort();if(size<1||size>maximum)throw new FormatException("World string bound.");var b=r.ReadBytesSegment(size);return Utf8.GetString(b.Array,b.Offset,b.Count);}
        private static void Hash(string value){if(value==null||value.Length!=64||value.Any(c=>!Uri.IsHexDigit(c)))throw new FormatException("World content/digest hash.");}
        public static void WriteWorldTicket(this NetworkWriter w,PNWorldTicket value)
        {value.Validate();w.WriteGuid(value.Room);w.WriteGuid(value.Play);w.WriteGuid(value.World);w.WriteULong(value.RoomIncarnation);w.WriteULong(value.PlayRevision);w.WriteULong(value.Incarnation);w.WriteULong(value.Subscription);}
        public static PNWorldTicket ReadWorldTicket(this NetworkReader r)
        {var v=new PNWorldTicket{Room=r.ReadGuid(),Play=r.ReadGuid(),World=r.ReadGuid(),RoomIncarnation=r.ReadULong(),PlayRevision=r.ReadULong(),Incarnation=r.ReadULong(),Subscription=r.ReadULong()};v.Validate();return v;}
        public static void WriteWorldBindingScope(this NetworkWriter w,PNWorldBindingScope value)
        {if(value.World!=Guid.Empty)value.Validate();else if(value.Incarnation!=0||value.Membership!=0||value.Subscription!=0)throw new FormatException("Partial world scope.");w.WriteGuid(value.World);w.WriteULong(value.Incarnation);w.WriteULong(value.Membership);w.WriteULong(value.Subscription);}
        public static PNWorldBindingScope ReadWorldBindingScope(this NetworkReader r)
        {var v=new PNWorldBindingScope{World=r.ReadGuid(),Incarnation=r.ReadULong(),Membership=r.ReadULong(),Subscription=r.ReadULong()};if(v.World!=Guid.Empty)v.Validate();else if(v.Incarnation!=0||v.Membership!=0||v.Subscription!=0)throw new FormatException("Partial world scope.");return v;}
        public static void WriteWorldOffer(this NetworkWriter w,PNWorldOffer v)
        {v.Ticket.Validate();_ = EveId.Parse(v.Definition);Hash(v.ContentHash);w.WriteWorldTicket(v.Ticket);Text(w,v.Definition,128);Text(w,v.ContentHash,64);w.WriteByte((byte)((v.Active?1:0)|(v.DefaultWorld?2:0)|(v.Paused?4:0)));}
        public static PNWorldOffer ReadWorldOffer(this NetworkReader r)
        {Control(r);var v=new PNWorldOffer{Ticket=r.ReadWorldTicket(),Definition=Text(r,128),ContentHash=Text(r,64)};int flags=r.ReadByte();if(flags>7)throw new FormatException("World offer flags.");_ = EveId.Parse(v.Definition);Hash(v.ContentHash);v.Active=(flags&1)!=0;v.DefaultWorld=(flags&2)!=0;v.Paused=(flags&4)!=0;return v;}
        public static void WriteWorldLoaded(this NetworkWriter w,PNWorldLoaded v){w.WriteWorldTicket(v.Ticket);w.WriteByte((byte)(v.Success?1:0));}
        public static PNWorldLoaded ReadWorldLoaded(this NetworkReader r){Control(r);var t=r.ReadWorldTicket();byte ok=r.ReadByte();if(ok>1)throw new FormatException("World load flags.");return new PNWorldLoaded{Ticket=t,Success=ok==1};}
        public static void WriteWorldFence(this NetworkWriter w,PNWorldFence v)
        {if(v.Revision==0)throw new FormatException("World fence revision.");Hash(v.Target.Digest);int start=w.Position;w.WriteWorldTicket(v.Ticket);w.WriteULong(v.Revision);w.Write(v.Target);if(w.Position-start>ControlBytes)throw new FormatException("World fence control size.");}
        public static PNWorldFence ReadWorldFence(this NetworkReader r)
        {Control(r);var v=new PNWorldFence{Ticket=r.ReadWorldTicket(),Revision=r.ReadULong(),Target=r.Read<PNStateFence>()};if(v.Revision==0)throw new FormatException("World fence revision.");Hash(v.Target.Digest);return v;}
        public static void WriteWorldApplied(this NetworkWriter w,PNWorldApplied v)
        {if(v.Revision==0)throw new FormatException("World applied revision.");Hash(v.Digest);w.WriteWorldTicket(v.Ticket);w.WriteULong(v.Revision);Text(w,v.Digest,64);w.WriteByte((byte)((v.Confirmed?1:0)|(v.InitialReady?2:0)));}
        public static PNWorldApplied ReadWorldApplied(this NetworkReader r)
        {Control(r);var v=new PNWorldApplied{Ticket=r.ReadWorldTicket(),Revision=r.ReadULong(),Digest=Text(r,64)};byte flags=r.ReadByte();if(v.Revision==0||flags>3)throw new FormatException("World applied revision/flags.");v.Confirmed=(flags&1)!=0;v.InitialReady=(flags&2)!=0;Hash(v.Digest);return v;}
    }
}
