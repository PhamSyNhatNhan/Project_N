#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Mirror;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    public static class PNRetiredStreamChecks
    {
        public static string Run()
        {
            int count=0;void Check(bool value,string label){if(!value)throw new Exception(label);count++;}
            var docs=new PNGameContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray();
            var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
            typeof(EveRegistrySession).GetMethod("Begin",BindingFlags.Static|BindingFlags.NonPublic,null,Type.EmptyTypes,null).Invoke(null,null);typeof(EveRegistrySession).GetMethod("Start",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"fixture:retired")});
            var root=new GameObject("Disposable retired adapter scope check");root.SetActive(false);var manager=root.AddComponent<PNNetworkManager>();
            var type=typeof(PNNetworkManager);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(manager,args);
            try
            {
                type.GetProperty("IsPlaying").GetSetMethod(true).Invoke(manager,new object[]{true});type.GetField("subscribedSession",flags).SetValue(manager,EveRegistrySession.Context);
                var connection=new NetworkConnectionToClient(991){isAuthenticated=true,authenticationData=new PNPeer{Owner=1}};
                var stream=new PNMoveStream{Session=EveRegistrySession.Context.SessionId,NetId=10,Entity=20,Generation=2,Nonce=17,Scope=new PNWorldBindingScope{World=Guid.NewGuid(),Incarnation=1,Membership=3,Subscription=4}};
                var peerType=type.GetNestedType("MovePeer",BindingFlags.NonPublic);var peer=Activator.CreateInstance(peerType,true);peerType.GetField("Connection").SetValue(peer,connection);peerType.GetField("Stream").SetValue(peer,stream);Call("RememberRetiredMove",peer);
                Check((bool)Call("IsRetiredMove",connection,stream),"previously authenticated retired Move stream is ignored");
                var wrong=stream;wrong.Scope.World=Guid.NewGuid();Check(!(bool)Call("IsRetiredMove",connection,wrong),"wrong-world scope is not a retired-stream match");wrong=stream;wrong.Scope.Membership++;Check(!(bool)Call("IsRetiredMove",connection,wrong),"new membership cannot inherit old retirement permission");wrong=stream;wrong.Nonce++;Check(!(bool)Call("IsRetiredMove",connection,wrong),"different nonce is not silently accepted");
                var reused=new NetworkConnectionToClient(991){isAuthenticated=true,authenticationData=new PNPeer{Owner=2}};Check(!(bool)Call("IsRetiredMove",reused,stream),"reused numeric connection ID cannot inherit an earlier connection scope");
                var buff=new PNBuffStream{Session=stream.Session,NetId=stream.NetId,Entity=stream.Entity,Generation=stream.Generation,Nonce=stream.Nonce,Scope=stream.Scope};var buffType=type.GetNestedType("BuffPeer",BindingFlags.NonPublic);var buffPeer=Activator.CreateInstance(buffType,true);buffType.GetField("Connection").SetValue(buffPeer,connection);buffType.GetField("Stream").SetValue(buffPeer,buff);Call("RememberRetiredBuff",buffPeer);
                Check((bool)Call("IsRetiredBuff",connection,buff)&&manager.RetiredStreamCount==2,"Buff retirement has its own exact stream match");
                Check(manager.PendingWorldBytes==512&&(int)type.GetField("pendingBuffBytes",flags).GetValue(manager)==512&&manager.PendingBuffBytes==0,"retired scope history charges the existing shared required ledger");Call("ClearRetiredStreams");Check(manager.RetiredStreamCount==0&&manager.PendingWorldBytes==0&&manager.PendingBuffBytes==0,"history teardown releases all shared reservations");
            }
            finally{Call("ClearRetiredStreams");type.GetProperty("IsPlaying").GetSetMethod(true).Invoke(manager,new object[]{false});type.GetField("subscribedSession",flags).SetValue(manager,null);UnityEngine.Object.DestroyImmediate(root);typeof(EveRegistrySession).GetMethod("End",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);}
            return "PASS "+count+" retired adapter scope/nonce/connection/ledger checks";
        }
    }
}
#endif
