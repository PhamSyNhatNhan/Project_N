#if UNITY_EDITOR
using System;
using Mirror;
namespace ProjectN.Coop.Tests
{
    public static class PNWorldTransferWireChecks
    {
        public static string Run()
        {
            PNWeaverChecks.Run();var value=new PNWorldTransferNotice{Room=Guid.NewGuid(),Play=Guid.NewGuid(),Transfer=Guid.NewGuid(),RoomIncarnation=1,PlayRevision=2,Revision=3,Entity=4,Generation=5,Phase=0};
            var writer=new NetworkWriter();writer.Write(value);var copy=new NetworkReader(writer.ToArray()).Read<PNWorldTransferNotice>();
            if(copy.Room!=value.Room||copy.Play!=value.Play||copy.Transfer!=value.Transfer||copy.Entity!=4||copy.Generation!=5||copy.Revision!=3||copy.Phase!=0||writer.Position>PNWorldWire.ControlBytes)throw new Exception("Actual Weaver transfer notice roundtrip.");
            bool rejected=false;try{var bad=value;bad.Phase=4;writer.Reset();writer.Write(bad);}catch(FormatException){rejected=true;}if(!rejected)throw new Exception("Transfer phase must be bounded.");
            rejected=false;try{var reader=new NetworkReader(new byte[PNWorldWire.ControlBytes+1]);reader.Read<PNWorldTransferNotice>();}catch(FormatException){rejected=true;}if(!rejected)throw new Exception("Transfer decoder must reject oversized control payload.");
            rejected=false;try{writer.Reset();writer.Write(value);var bytes=writer.ToArray();Array.Resize(ref bytes,bytes.Length-1);new NetworkReader(bytes).Read<PNWorldTransferNotice>();}catch(System.IO.EndOfStreamException){rejected=true;}if(!rejected)throw new Exception("Truncated transfer notice must fail.");
            return "PASS 4 actual Weaver transfer control checks";
        }
    }
}
#endif
