using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Mirror;

namespace ProjectN.Coop
{
    public struct PNHash { public string Key, Value; }
    public struct PNContentFingerprint
    {
        public int Protocol;
        public string Rules;
        public PNHash[] Hashes;
        public static PNContentFingerprint From(EveContentCompatibility value) => new PNContentFingerprint
        { Protocol=value.ProtocolVersion, Rules=value.RulesVersion, Hashes=value.ContentHashes.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new PNHash {Key=x.Key,Value=x.Value}).ToArray() };
        public EveContentCompatibility Read()
        {
            if(string.IsNullOrWhiteSpace(Rules) || Rules.Length>128 || Hashes==null || Hashes.Length>256) throw new InvalidOperationException("Invalid content fingerprint.");
            var values=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var hash in Hashes)
            {
                if(string.IsNullOrWhiteSpace(hash.Key)||hash.Key.Length>512||hash.Value==null||hash.Value.Length!=64)throw new InvalidOperationException("Invalid content hash.");
                values.Add(hash.Key,hash.Value);
            }
            return new EveContentCompatibility(Protocol,Rules,values);
        }
    }
    public struct PNAdmissionRequest : NetworkMessage
    { public int NetworkVersion; public string Platform,Mode,Limits; public bool SelectPlayer; public PNContentFingerprint Content; }
    public struct PNAdmissionResponse : NetworkMessage
    { public bool Accepted; public string Reason,Limits; public Guid HostSession; public long Owner; public PNContentFingerprint Content; }
    public struct PNActorMetadata
    {
        public Guid HostSession;
        public long Entity,Generation,Owner;
        public string Character,SceneKey,EntityKey;
    }
    public struct PNHeartbeat : NetworkMessage { public uint Sequence,ResumeFloor; public bool Paused,ResumeRequired; }
    public struct PNResume : NetworkMessage { public uint Sequence; public bool Acknowledged; public string BindingDigest; }
}
