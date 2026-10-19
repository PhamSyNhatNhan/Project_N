using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
namespace ProjectN.Coop
{
    public struct PNLanRequest : NetworkMessage { public int NetworkVersion; }
    public struct PNLanResponse : NetworkMessage
    {
        public int NetworkVersion, Port, Players, Capacity;
        public string Name, Platform, Mode, Digest;
        public bool Locked;
    }
    // LAN room discovery over UDP broadcast (Mirror's base). It lives outside any room so a client can search before it has one.
    [DisallowMultipleComponent]
    public sealed class PNLanDiscovery : NetworkDiscoveryBase<PNLanRequest,PNLanResponse>
    {
        public const long Handshake=0x504E4C414E0001L; // "PNLAN" v1: other Mirror games on the LAN are ignored
        public const double RoomLifetimeSeconds=8;
        private sealed class Seen { public PNLanResponse Response; public string Address; public double At; }
        private readonly Dictionary<string,Seen> seen=new Dictionary<string,Seen>();
        // Set by the host while it advertises; evaluated per request so counts stay current.
        public Func<PNLanResponse> HostInfo;
        public Func<PNLanResponse> LocalInfo;

        private void Awake(){ secretHandshake=Handshake; }
        public override void Start()
        {
            // The base would auto-advertise on a headless build; this component only advertises while a room is hosted.
            bool headless=Utils.IsHeadless();
            base.Start();
            if(headless)StopDiscovery();
        }
        public bool Advertising=>serverUdpClient!=null;
        public bool Searching=>clientUdpClient!=null;
        public string LastError{get;private set;}
        public void BeginAdvertising(Func<PNLanResponse> info)
        {
            HostInfo=info;
            try{AdvertiseServer();LastError=null;}
            catch(Exception error){LastError="Không mở được cổng quảng bá LAN: "+error.Message;Debug.LogWarning(LastError);}
        }
        public void EndAdvertising()
        {
            HostInfo=null;
            if(serverUdpClient!=null&&clientUdpClient==null)StopDiscovery();
            else if(serverUdpClient!=null){try{serverUdpClient.Close();}catch(Exception){}serverUdpClient=null;}
        }
        public void BeginSearching()
        {
            seen.Clear();
            try{StartDiscovery();LastError=null;}
            catch(Exception error){LastError="Không tìm được phòng LAN: "+error.Message;Debug.LogWarning(LastError);}
        }
        public void EndSearching(){if(clientUdpClient!=null)StopDiscovery();seen.Clear();}
        protected override PNLanRequest GetRequest()=>new PNLanRequest{NetworkVersion=PNNetworkAuthenticator.NetworkVersion};
        protected override PNLanResponse ProcessRequest(PNLanRequest request,IPEndPoint endpoint)=>HostInfo!=null?HostInfo():default;
        protected override void ProcessResponse(PNLanResponse response,IPEndPoint endpoint)
        {
            if(response.Port<=0||response.Port>65535||response.Capacity<0||response.Players<0||response.Capacity>64)return;
            string address=endpoint.Address.ToString();
            seen[address+":"+response.Port]=new Seen{Response=response,Address=address,At=Time.realtimeSinceStartupAsDouble};
        }
        public IReadOnlyList<PNLanRoomInfo> Rooms()
        {
            double now=Time.realtimeSinceStartupAsDouble;
            foreach(var key in seen.Where(x=>now-x.Value.At>RoomLifetimeSeconds).Select(x=>x.Key).ToArray())seen.Remove(key);
            var local=LocalInfo!=null?LocalInfo():default;
            return seen.Values.OrderBy(x=>x.Address).Select(x=>new PNLanRoomInfo
            {
                Name=string.IsNullOrWhiteSpace(x.Response.Name)?x.Address:x.Response.Name,Address=x.Address,Port=x.Response.Port,Mode=x.Response.Mode,Platform=x.Response.Platform,
                Players=x.Response.Players,Capacity=x.Response.Capacity,Locked=x.Response.Locked,Unavailable=Unavailable(x.Response,local)
            }).ToList();
        }
        private static string Unavailable(PNLanResponse room,PNLanResponse local)
        {
            if(local.NetworkVersion!=0&&room.NetworkVersion!=local.NetworkVersion)return "Khác phiên bản mạng";
            if(!string.IsNullOrEmpty(local.Platform)&&room.Platform!=local.Platform)return "Khác nền tảng ("+room.Platform+")";
            if(!string.IsNullOrEmpty(local.Mode)&&room.Mode!=local.Mode)return "Khác chế độ";
            if(!string.IsNullOrEmpty(local.Digest)&&room.Digest!=local.Digest)return "Khác nội dung/mod";
            if(room.Locked)return "Run đã bắt đầu";
            if(room.Capacity>0&&room.Players>=room.Capacity)return "Phòng đầy";
            return null;
        }
        // Short digest of everything admission compares, so a browser can show "different content/mods" before connecting.
        public static string Digest(EveEngine.EveContentCompatibility value)
        {
            var fingerprint=PNContentFingerprint.From(value);
            var text=fingerprint.Protocol+"|"+fingerprint.Rules+"|"+string.Join(";",fingerprint.Hashes.Select(h=>h.Key+"="+h.Value));
            using(var sha=System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-","").Substring(0,16).ToLowerInvariant();
        }
        // IPv4 addresses a LAN peer could reach this machine on, best first (has a gateway, not virtual-looking).
        public static IReadOnlyList<string> LocalAddresses()
        {
            var result=new List<(string Address,int Score)>();
            try
            {
                foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if(nic.OperationalStatus!=OperationalStatus.Up||nic.NetworkInterfaceType==NetworkInterfaceType.Loopback||nic.NetworkInterfaceType==NetworkInterfaceType.Tunnel)continue;
                    var properties=nic.GetIPProperties();
                    bool gateway=properties.GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork&&!g.Address.Equals(IPAddress.Any));
                    string name=(nic.Name+" "+nic.Description).ToLowerInvariant();
                    bool virtualLooking=name.Contains("virtual")||name.Contains("vmware")||name.Contains("vbox")||name.Contains("hyper-v")||name.Contains("vethernet")||name.Contains("tap")||name.Contains("vpn");
                    foreach(var unicast in properties.UnicastAddresses)
                        if(unicast.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(unicast.Address)&&!unicast.Address.ToString().StartsWith("169.254."))
                            result.Add((unicast.Address.ToString(),(gateway?2:0)+(virtualLooking?0:1)));
                }
            }
            catch(Exception error){Debug.LogWarning("Không đọc được địa chỉ LAN: "+error.Message);}
            return result.OrderByDescending(x=>x.Score).Select(x=>x.Address).Distinct().ToList();
        }
    }
}
