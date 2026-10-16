using System;
using System.IO;
using System.Collections.Generic;

namespace ProjectN.Coop
{
    public sealed class PNBuffBaselineAssembly:IDisposable
    {
        public PNBuffPacket Header {get;}
        private readonly byte[] bytes;
        private IDisposable reservation;
        private readonly Func<int,IDisposable> reserveStructure;
        private readonly List<IDisposable> structures=new List<IDisposable>();
        private int next,position;
        public double Started {get;}
        public bool Complete=>next==Header.Count;
        public PNBuffBaselineAssembly(PNBuffPacket first,double now,Func<int,IDisposable> reserve,Func<int,IDisposable> reserveStructure=null)
        {
            PNBuffWire.ValidatePacket(first);if(first.Index!=0)throw new FormatException("Buff transfer must begin at fragment zero.");
            Header=first;Started=now;this.reserveStructure=reserveStructure;
            reservation=reserve(checked(first.Total*3+(first.Kind==0?4096*64:256*64)));
            try{bytes=new byte[first.Total];Add(first);}catch{reservation.Dispose();reservation=null;throw;}
        }
        public void Add(PNBuffPacket p)
        {
            PNBuffWire.ValidatePacket(p);var h=Header;
            if(!p.Stream.Equals(h.Stream)||p.Transfer!=h.Transfer||p.Kind!=h.Kind||p.Revision!=h.Revision||p.Time!=h.Time||p.HostTime!=h.HostTime||p.Rate!=h.Rate||p.Total!=h.Total||p.Count!=h.Count||p.Index!=next)throw new FormatException("Conflicting/out-of-order Buff transfer.");
            Buffer.BlockCopy(p.Body,0,bytes,position,p.Body.Length);position+=p.Body.Length;next++;
        }
        public byte[] Body(){if(!Complete||position!=bytes.Length)throw new InvalidDataException("Incomplete Buff transfer.");return bytes;}
        public void ReserveNodes(int count){if(reserveStructure!=null)structures.Add(reserveStructure(checked(count*128)));}
        public void Dispose(){foreach(var lease in structures)lease.Dispose();structures.Clear();reservation?.Dispose();reservation=null;}
    }
}
