using System;
using System.Collections.Generic;
namespace EveEngine
{
    // Adapter-owned pre-publication storage. Required data is never silently dropped.
    public sealed class EveBindingInbox
    {
        private sealed class Slot
        {public long Token;public int MetadataBytes;public byte[] State;public readonly Queue<byte[]> Events=new Queue<byte[]>();public int EventBytes;}
        private readonly Dictionary<uint,Slot> slots=new Dictionary<uint,Slot>();
        public int MaximumBindings=64,MaximumStateBytes=32768,MaximumEvents=64,MaximumEventBytes=65536,MaximumTotalEvents=4096,MaximumTotalBytes=4*1024*1024;
        public int TotalBytes {get;private set;}
        public int TotalEvents {get;private set;}
        public int Count=>slots.Count;
        public bool Contains(uint binding,long token)=>slots.TryGetValue(binding,out var value)&&value.Token==token;
        public void Reserve(uint binding,long token,int metadataBytes)
        {
            if(binding==0||token<=0||metadataBytes<0||slots.ContainsKey(binding)||slots.Count>=MaximumBindings||metadataBytes>MaximumTotalBytes-TotalBytes)throw new InvalidOperationException("Pending binding limit/identity violated.");
            slots.Add(binding,new Slot {Token=token,MetadataBytes=metadataBytes});TotalBytes+=metadataBytes;
        }
        private Slot Get(uint binding,long token)
        {if(!slots.TryGetValue(binding,out var value)||value.Token!=token)throw new InvalidOperationException("Stale binding token.");return value;}
        public void Latest(uint binding,long token,byte[] payload)
        {
            var slot=Get(binding,token);if(payload==null||payload.Length>MaximumStateBytes)throw new InvalidOperationException("Latest state limit exceeded.");
            int delta=payload.Length-(slot.State?.Length??0);if(delta>MaximumTotalBytes-TotalBytes)throw new InvalidOperationException("Pending byte limit exceeded.");
            slot.State=(byte[])payload.Clone();TotalBytes+=delta;
        }
        public void Required(uint binding,long token,byte[] payload)
        {
            var slot=Get(binding,token);
            if(payload==null||slot.Events.Count>=MaximumEvents||payload.Length>MaximumEventBytes-slot.EventBytes||TotalEvents>=MaximumTotalEvents||payload.Length>MaximumTotalBytes-TotalBytes)throw new InvalidOperationException("Required event limit exceeded.");
            slot.Events.Enqueue((byte[])payload.Clone());slot.EventBytes+=payload.Length;TotalEvents++;TotalBytes+=payload.Length;
        }
        public void Apply(uint binding,long token,Action<byte[]> state,Action<byte[]> required)
        {
            var slot=Get(binding,token);
            if(slot.State!=null){state((byte[])slot.State.Clone());TotalBytes-=slot.State.Length;slot.State=null;}
            while(slot.Events.Count>0){var value=slot.Events.Peek();required((byte[])value.Clone());slot.Events.Dequeue();slot.EventBytes-=value.Length;TotalEvents--;TotalBytes-=value.Length;}
        }
        public void Release(uint binding,long token)
        {
            var slot=Get(binding,token);TotalBytes-=slot.MetadataBytes+(slot.State?.Length??0)+slot.EventBytes;TotalEvents-=slot.Events.Count;slots.Remove(binding);
        }
    }
}
