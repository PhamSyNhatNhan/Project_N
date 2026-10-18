using System;
using System.Linq;
using EveEngine;
namespace ProjectN.Coop
{
    public static class PNAdmissionRules
    {
        public static EveContentCompatibility Validate(PNAdmissionRequest request,EveContentCompatibility local,string platform,string mode,int admitted,int capacity,bool hostLocal=false)
        {
            if(request.NetworkVersion!=PNNetworkAuthenticator.NetworkVersion)throw new InvalidOperationException("Network version mismatch.");
            if(request.Limits==null||request.Limits.Length>256)throw new InvalidOperationException("Protocol limits missing or oversized.");
            if(request.Platform!=platform)throw new InvalidOperationException("Platform mismatch (PC/Mobile crossplay disabled).");
            if(mode!=null&&request.Mode!=mode)throw new InvalidOperationException("Mode mismatch.");
            var remote=request.Content.Read();
            try{local.Validate(remote);}
            catch(InvalidOperationException error)
            {
                var packs=local.ContentHashes.Keys.Union(remote.ContentHashes.Keys).Where(key=>(key.StartsWith("pack:",StringComparison.Ordinal)||key.StartsWith("dll:",StringComparison.Ordinal))&&(!local.ContentHashes.TryGetValue(key,out var a)||!remote.ContentHashes.TryGetValue(key,out var b)||a!=b)).OrderBy(x=>x,StringComparer.Ordinal).Take(8).ToArray();
                if(packs.Length>0)throw new InvalidOperationException("Content pack/mod mismatch: "+string.Join(", ",packs),error);
                throw;
            }
            if(!hostLocal&&admitted>=capacity)throw new InvalidOperationException("Mode capacity reached.");
            return remote;
        }
    }
}
