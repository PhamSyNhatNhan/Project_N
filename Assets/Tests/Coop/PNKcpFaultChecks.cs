#if UNITY_EDITOR
using System;
using System.Reflection;
using Mirror;
using ProjectN.Composition;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    public static class PNKcpFaultChecks
    {
        public static string Run()
        {
            int checks=0;void C(bool ok,string why){if(!ok)throw new Exception("KCP fault "+why);checks++;}
            var active=Transport.active;var go=new GameObject("Disposable inactive datagram queue check");go.SetActive(false);
            try
            {
                var t=go.AddComponent<PNKcpFaultTransport>();t.DatagramLatencyMs=2000;
                var route=typeof(PNKcpFaultTransport).GetMethod("Route",BindingFlags.Instance|BindingFlags.NonPublic);
                var clear=typeof(PNKcpFaultTransport).GetMethod("Clear",BindingFlags.Instance|BindingFlags.NonPublic);
                void Send(byte[] bytes)=>route.Invoke(t,new object[]{false,0,new ArraySegment<byte>(bytes)});
                void Refuse(byte[] bytes){try{Send(bytes);}catch(TargetInvocationException x)when(x.InnerException is InvalidOperationException){C(true,"bounded queue refuses before copy");return;}throw new Exception("Expected queue refusal");}
                var large=new byte[1024*1024];for(int i=0;i<4;i++)Send(large);C(t.PendingBytes==PNKcpFaultTransport.QueueLimit,"byte limit reached");Refuse(large);clear.Invoke(t,null);C(t.PendingBytes==0,"byte queue cleanup");
                var tiny=new byte[1];for(int i=0;i<PNKcpFaultTransport.PacketLimit;i++)Send(tiny);C(t.PendingBytes==PNKcpFaultTransport.PacketLimit,"tiny entries count bytes");Refuse(tiny);clear.Invoke(t,null);C(t.PendingBytes==0,"entry queue cleanup");
                C(Transport.active==active,"inactive fixture leaves selected transport unchanged");
                return "PASS "+checks+" KCP datagram fixture byte/entry bounds";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
#endif
