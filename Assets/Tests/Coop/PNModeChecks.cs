using System;
using EveEngine;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    public static class PNModeChecks
    {
        private sealed class Policy:IPNSessionPolicy
        {public string ModeId=>"fixture";public int MaximumClients=>2;public double GraceSeconds=>15;public void OnOwnerLeft(Action retire)=>retire();}
        public static string Run()
        {
            int count=0;void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
            var hub=new EveRegistryHub();var modes=hub.GetOrCreate<PNModeDefinition>();int creates=0;
            modes.Register(new PNModeDefinition(EveId.Parse("fixture:original"),typeof(Policy),p=>{creates++;return new Policy();}));
            modes.Register(new PNModeDefinition(EveId.Parse("replacement:selected"),typeof(Policy),p=>{creates+=10;return new Policy();}));
            modes.RegisterBase(EveId.Parse("base:fixture"),EveId.Parse("fixture:original"),"fixture",compatible:(a,b)=>true);
            modes.RequestOverride(EveId.Parse("base:fixture"),EveId.Parse("replacement:selected"),"replacement","1.0.0",new[]{"fixture"});
            hub.Seal();var first=PNModeDefinition.Select(hub,"fixture",new PNNetworkProfile());var second=PNModeDefinition.Select(hub,"fixture",new PNNetworkProfile());
            Check(creates==20&&!ReferenceEquals(first,second),"selected registered factory and fresh policy per play");
            try{PNModeDefinition.Select(hub,"missing",new PNNetworkProfile());throw new Exception("unknown accepted");}catch(System.Collections.Generic.KeyNotFoundException){count++;}
            try{PNModeDefinition.Select(hub,"fixture",new PNNetworkProfile{MaximumClients=1});throw new Exception("capacity accepted");}catch(InvalidOperationException){count++;}
            var invalid=new PNModeDefinition(EveId.Parse("fixture:invalid"),typeof(Policy),p=>null);
            try{invalid.Create(new PNNetworkProfile());throw new Exception("null accepted");}catch(InvalidOperationException){count++;}
            Check(typeof(IPNNetworkSessionDriver).GetMembers().Length==4,"original driver interface unchanged");
            var root=new GameObject("Disposable room Ready race check");root.SetActive(false);
            try
            {
                var manager=root.AddComponent<PNNetworkManager>();
                typeof(PNNetworkManager).GetProperty("IsPlaying").GetSetMethod(true).Invoke(manager,new object[]{true});
                typeof(PNNetworkManager).GetProperty("PlayRevision").GetSetMethod(true).Invoke(manager,new object[]{1UL});
                var peer=new PNPeer{Owner=1,Grace=new PNGraceWindow(15)};
                var connection=new Mirror.NetworkConnectionToClient(900){authenticationData=peer,isAuthenticated=true};
                manager.OnServerReady(connection);manager.OnServerAddPlayer(connection);
                Check(!connection.isReady&&connection.identity==null&&peer.WantsRoomShell,"in-flight lobby Ready/AddPlayer waits for matching play attach");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            return "PASS "+count+" mode selection/capacity/ABI cases";
        }
    }
}
