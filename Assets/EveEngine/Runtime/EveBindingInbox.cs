using System;
using System.Collections.Generic;
namespace EveEngine
{
    // Adapter-owned pre-publication storage. Required data is never silently dropped.
    public sealed class EveBindingInbox
    {
        private sealed class Slot
        {public long Token;public int MetadataBytes;public byte[] State;public readonly Queue<byte[]> Events=new Queue<byte[]>();public int EventBytes;
         public readonly Dictionary<string,(object Value,int Bytes)> Values=new Dictionary<string,(object,int)>();
         public readonly List<(string Kind,object Value,int Bytes)> Notices=new List<(string,object,int)>();}
        private readonly Dictionary<uint,Slot> slots=new Dictionary<uint,Slot>();
        public int MaximumBindings=64,MaximumStateBytes=32768,MaximumEvents=64,MaximumEventBytes=65536,MaximumTotalEvents=4096,MaximumTotalBytes=4*1024*1024;
        public int TotalBytes {get;private set;}
        public int TotalEvents {get;private set;}
        public int Count=>slots.Count;
        // Large adapter assemblies retain a charged lease through decode/staging.
        public IDisposable ReserveBytes(uint binding,long token,int bytes)
        {
            var slot=Get(binding,token);if(bytes<0||bytes>MaximumTotalBytes-TotalBytes)throw new InvalidOperationException("Pending assembly byte limit.");
            slot.MetadataBytes=checked(slot.MetadataBytes+bytes);TotalBytes+=bytes;
            return new ByteLease(this,binding,token,bytes);
        }
        private sealed class ByteLease:IDisposable
        {
            private EveBindingInbox owner;private readonly uint binding;private readonly long token;private readonly int bytes;
            public ByteLease(EveBindingInbox owner,uint binding,long token,int bytes){this.owner=owner;this.binding=binding;this.token=token;this.bytes=bytes;}
            public void Dispose(){if(owner==null)return;if(owner.Contains(binding,token)){var slot=owner.Get(binding,token);slot.MetadataBytes-=bytes;owner.TotalBytes-=bytes;}owner=null;}
        }
        public bool Contains(uint binding,long token)=>slots.TryGetValue(binding,out var value)&&value.Token==token;
        public void Reserve(uint binding,long token,int metadataBytes)
        {
            if(binding==0||token<=0||metadataBytes<0||slots.ContainsKey(binding)||slots.Count>=MaximumBindings||metadataBytes>MaximumTotalBytes-TotalBytes)throw new InvalidOperationException("Pending binding limit/identity violated.");
            slots.Add(binding,new Slot {Token=token,MetadataBytes=metadataBytes});TotalBytes+=metadataBytes;
        }
        private Slot Get(uint binding,long token)
        {if(!slots.TryGetValue(binding,out var value)||value.Token!=token)throw new InvalidOperationException("Stale binding token.");return value;}
        // Typed adapters supply immutable values and their validated wire size. All kinds share quotas.
        public void LatestValue<T>(uint binding,long token,string kind,T immutable,int bytes) where T:class
        {
            var slot=Get(binding,token);if(string.IsNullOrEmpty(kind)||immutable==null||bytes<0||bytes>MaximumStateBytes)throw new InvalidOperationException("Typed state bounds.");
            int delta=bytes-(slot.Values.TryGetValue(kind,out var old)?old.Bytes:0);
            if(delta>MaximumTotalBytes-TotalBytes)throw new InvalidOperationException("Pending byte limit exceeded.");
            slot.Values[kind]=(immutable,bytes);TotalBytes+=delta;
        }
        public T ReadLatest<T>(uint binding,long token,string kind) where T:class
        {var slot=Get(binding,token);return slot.Values.TryGetValue(kind,out var item)?item.Value as T:null;}
        public void RequiredValue<T>(uint binding,long token,string kind,T immutable,int bytes) where T:class
        {
            var slot=Get(binding,token);
            if(string.IsNullOrEmpty(kind)||immutable==null||bytes<0||slot.Events.Count+slot.Notices.Count>=MaximumEvents||bytes>MaximumEventBytes-slot.EventBytes||TotalEvents>=MaximumTotalEvents||bytes>MaximumTotalBytes-TotalBytes)throw new InvalidOperationException("Required typed event bounds.");
            slot.Notices.Add((kind,immutable,bytes));slot.EventBytes+=bytes;TotalEvents++;TotalBytes+=bytes;
        }
        public IEnumerable<T> ReadRequired<T>(uint binding,long token,string kind) where T:class
        {foreach(var item in Get(binding,token).Notices)if(item.Kind==kind)yield return (T)item.Value;}
        public void Latest(uint binding,long token,byte[] payload)
        {
            var slot=Get(binding,token);if(payload==null||payload.Length>MaximumStateBytes)throw new InvalidOperationException("Latest state limit exceeded.");
            int delta=payload.Length-(slot.State?.Length??0);if(delta>MaximumTotalBytes-TotalBytes)throw new InvalidOperationException("Pending byte limit exceeded.");
            slot.State=(byte[])payload.Clone();TotalBytes+=delta;
        }
        public void Required(uint binding,long token,byte[] payload)
        {
            var slot=Get(binding,token);
            if(payload==null||slot.Events.Count+slot.Notices.Count>=MaximumEvents||payload.Length>MaximumEventBytes-slot.EventBytes||TotalEvents>=MaximumTotalEvents||payload.Length>MaximumTotalBytes-TotalBytes)throw new InvalidOperationException("Required event limit exceeded.");
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
            var slot=Get(binding,token);TotalBytes-=slot.MetadataBytes+(slot.State?.Length??0)+slot.EventBytes;foreach(var value in slot.Values.Values)TotalBytes-=value.Bytes;TotalEvents-=slot.Events.Count+slot.Notices.Count;slots.Remove(binding);
        }
    }
}
