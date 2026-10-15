#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using EveEngine;
using EveEngine.Movement;
using UnityEngine;

public sealed class PNMoveIpcRuntime : EveRuntime
{
    public override EveSessionRole Role => PNMoveIpcFixture.Arg("--move-role")=="host"?EveSessionRole.Host:EveSessionRole.Client;
    public override string RulesVersion => EveGameRuntime.CurrentRulesVersion;
    protected override IEnumerable<EveIContentModule> CreateContentModules() => new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()};
    protected override IEnumerable<EveContentDocument> ReadContentDocuments() => new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments();
}
// Disposable Player correctness fixture. Loopback transport and pre-created known actors, not production admission/actor transport.
public sealed class PNMoveIpcFixture : MonoBehaviour
{
    private readonly List<string> evidence=new List<string>();
    private readonly List<double> adapterMs=new List<double>(),frameMs=new List<double>();
    private PNMoveIpcRuntime runtime;
    private PNMoveHostWorld world;
    private PNMoveHostConnection hostConnection;
    private PNMoveClientConnection client;
    private PNMove owner,observer;
    private UdpClient socket;
    private IPEndPoint remote;
    private bool host,done,admitted,froze,thawed,relocated,locked;
    private PNControlLease walkLock;
    private double started,lastHello=-1,hostOffset;
    private long freeze;
    private int accepted,fenced,stale,packets,dropped,duplicates,errors;
    private int fixedReports;
    private bool sawFrozen,sawThaw,sawCorrection;
    private bool died,revived,sawDeadHold,sawRevive;
    private double firstDead=-1;
    private byte[] delayed;
    private double delayedUntil;
    private string output;
    public static string Arg(string key)
    { var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0 && i+1<args.Length?args[i+1]:null; }
    private void Start()
    {
        try
        {
            host=Arg("--move-role")=="host";output=Arg("--move-out")??".utmp/MoveIPC";Directory.CreateDirectory(output);
            Application.runInBackground=true;Application.targetFrameRate=120;Time.fixedDeltaTime=.02f;
            started=Time.realtimeSinceStartupAsDouble;
            int localPort=int.Parse(Arg("--move-port"));int remotePort=int.Parse(Arg("--move-peer"));
            socket=new UdpClient(new IPEndPoint(IPAddress.Loopback,localPort));socket.Client.Blocking=false;remote=new IPEndPoint(IPAddress.Loopback,remotePort);
            runtime=gameObject.AddComponent<PNMoveIpcRuntime>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(100,1,100);
            if(host)
            {
                var session=EveRegistrySession.Context;session.Coop.AdmitPeer(7,session.Compatibility);
                owner=Actor(7101,7,new Vector3(0,1.02f,0));observer=Actor(7102,0,new Vector3(-4,1.02f,4));
                world=new PNMoveHostWorld(session);world.Track(owner,true);world.Track(observer,false);hostConnection=world.Connect(7);admitted=true;
            }
        }
        catch(Exception error) { Finish(false,error.ToString()); }
    }
    private PNMove Actor(long id,long ownerId,Vector3 position)
    {
        var root=new GameObject("Move IPC "+id);root.SetActive(false);root.transform.position=position;
        root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:eve"),ownerId);
        root.AddComponent<CapsuleCollider>().height=2;root.AddComponent<Rigidbody>().constraints=RigidbodyConstraints.FreezeRotation;
        root.AddComponent<PNPlayerStat>();root.AddComponent<EveTimeScale>();root.AddComponent<PNBuffManager>().RenderPresentation=false;
        var move=root.AddComponent<PNMove>();root.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current);root.SetActive(true);return move;
    }
    private void Update()
    {
        if(done || socket==null)return;
        try
        {
            double time=Time.realtimeSinceStartupAsDouble-started;
            if(host && time-lastHello>=.1)
            {
                lastHello=time;var hello=new byte[25];hello[0]=0xf1;Array.Copy(EveRegistrySession.Context.SessionId.ToByteArray(),0,hello,1,16);Array.Copy(BitConverter.GetBytes(time),0,hello,17,8);Send(hello,false);
            }
            for(int i=0;i<64 && socket.Available>0;i++)
            {
                IPEndPoint source=null;var bytes=socket.Receive(ref source);if(!source.Equals(remote))continue;
                if(!host && bytes.Length==25 && bytes[0]==0xf1)
                {
                    if(!admitted)
                    {
                        var session=EveRegistrySession.Context;var sessionBytes=new byte[16];Array.Copy(bytes,1,sessionBytes,0,16);var hostId=new Guid(sessionBytes);
                        session.Coop.AdmitHost(hostId,session.Compatibility,7);owner=Actor(7101,7,new Vector3(0,1.02f,0));observer=Actor(7102,0,new Vector3(-4,1.02f,4));
                        client=new PNMoveClientConnection(session,hostId);client.Bind(owner);client.Bind(observer);owner.OwnerPoseReport+=b=>fixedReports++;admitted=true;
                    }
                    hostOffset=BitConverter.ToDouble(bytes,17)-time;continue;
                }
                if(!admitted)continue;
                if(host)
                {
                    var result=world.Receive(bytes,7,time);
                    if(result==MoveReportResult.Accepted)accepted++;else if(result==MoveReportResult.Fenced)fenced++;else if(result==MoveReportResult.Stale)stale++;
                }
                else client.Receive(bytes);
            }
            if(admitted)
            {
                if(host)
                {
                    observer.SetMoveDirection(Vector2.right);
                    if(time>2 && !froze) { froze=true;owner.GetComponent<EveTimeScale>().MinScale=0;freeze=owner.GetComponent<EveTimeScale>().AddTimedModifier(EveId.Parse("test:ipc_freeze"),EveTimeModifierKind.Scale,-1,10);evidence.Add("freeze at "+time); }
                    if(time>3 && !thawed) { thawed=true;owner.GetComponent<EveTimeScale>().RemoveModifier(freeze);evidence.Add("thaw at "+time); }
                    if(time>4 && !relocated) { relocated=true;world.Relocate(owner.NetworkIdentity(EveRegistrySession.Context.SessionId),new MovementState(new MoveVector(-2,2,0),new MoveVector(0,4,0)),new MoveRecovery(new MoveVector(0,4,0),default,true));evidence.Add("relocate at "+time); }
                    if(time>5 && !locked) { locked=true;walkLock=owner.GetComponent<PNControlManager>().State.Acquire("IPC walk lock",new[]{PNControlIds.Move}); }
                    if(time>6 && !died)
                    {
                        died=true;world.Relocate(owner.NetworkIdentity(EveRegistrySession.Context.SessionId),new MovementState(new MoveVector(0,4,0),default),default);
                        owner.GetComponent<PNStat>().SetCurrent(PNContentIds.Health,0);evidence.Add("dead airborne at "+time);
                    }
                    if(time>7 && !revived) { revived=true;var stat=owner.GetComponent<PNStat>();stat.SetCurrent(PNContentIds.Health,stat.GetMax(PNContentIds.Health));evidence.Add("revive at "+time); }
                }
                else
                {
                    owner.SetMoveDirection(Vector2.right);
                    if(owner.OwnerMovement?.Mirror?.Scale==0)sawFrozen=true;
                    if(sawFrozen && owner.OwnerMovement?.Mirror?.Scale>0)sawThaw=true;
                    if(owner.OwnerMovement?.AppliedCorrection>=3)sawCorrection=true;
                    if(owner.OwnerMovement?.Mirror?.Dead==true)
                    { if(firstDead<0)firstDead=time;if(time-firstDead>.04 && owner.Body.isKinematic)sawDeadHold=true; }
                    else if(sawDeadHold && owner.IsReady)sawRevive=true;
                }
            }
            if(delayed!=null && time>=delayedUntil) { socket.Send(delayed,delayed.Length,remote);delayed=null; }
            if(time>8)
            {
                bool pass=host?accepted>100 && relocated && locked && revived && owner.Body.isKinematic:admitted && fixedReports>100 && sawFrozen && sawThaw && sawCorrection && sawDeadHold && sawRevive && !owner.GetComponent<EveEntityLoader>().LoadContext.CanMutate && observer.ObserverMovement.Count>0 && owner.OwnerMovement.AppliedCorrection>1;
                Finish(pass,"admitted="+admitted+" accepted="+accepted+" fenced="+fenced+" stale="+stale+" fixedReports="+fixedReports+" frozen="+sawFrozen+" thaw="+sawThaw+" corrected="+sawCorrection+" deadHold="+sawDeadHold+" revived="+sawRevive+" pose="+(owner!=null?owner.Body.position.ToString():"none"));
            }
        }
        catch(Exception error) { Finish(false,error.ToString()); }
    }
    private void LateUpdate()
    {
        if(done || !admitted)return;
        try
        {
            double time=Time.realtimeSinceStartupAsDouble-started;
            long stamp=System.Diagnostics.Stopwatch.GetTimestamp();
            if(host) { world.Advance(time);hostConnection.Advance(time);hostConnection.Send(batch=>{foreach(var bytes in batch)Send(bytes,true);return true;}); }
            else { client.AdvanceObservers(Math.Max(0,time+hostOffset));client.SendReports(batch=>{foreach(var bytes in batch)Send(bytes,true);return true;}); }
            if(time>1 && adapterMs.Count<10000) { adapterMs.Add((System.Diagnostics.Stopwatch.GetTimestamp()-stamp)*1000d/System.Diagnostics.Stopwatch.Frequency);frameMs.Add(Time.unscaledDeltaTime*1000d); }
        }
        catch(Exception error) { Finish(false,error.ToString()); }
    }
    private void Send(byte[] bytes,bool fault)
    {
        packets++;
        if(fault && packets%7==0) { dropped++;return; }
        if(fault && packets%11==0 && delayed==null) { delayed=(byte[])bytes.Clone();delayedUntil=Time.realtimeSinceStartupAsDouble-started+.07;return; }
        socket.Send(bytes,bytes.Length,remote);if(fault && packets%5==0) { socket.Send(bytes,bytes.Length,remote);duplicates++; }
    }
    private void OnLog(string condition,string stack,LogType type) { if(type==LogType.Exception || type==LogType.Error)errors++; }
    private void OnEnable()=>Application.logMessageReceived+=OnLog;
    private void OnDisable()=>Application.logMessageReceived-=OnLog;
    private void Finish(bool pass,string reason)
    {
        if(done)return;done=true;
        try
        {
            walkLock?.Dispose();client?.Dispose();hostConnection?.Dispose();world?.Dispose();runtime?.EndSession();socket?.Close();
            evidence.Add((pass && errors==0?"PASS":"FAIL")+" "+reason+" packets="+packets+" dropped="+dropped+" duplicates="+duplicates+" errors="+errors);
            double Quantile(List<double> source,double percentile) { var sorted=source.OrderBy(x=>x).ToArray();return sorted.Length==0?0:sorted[(int)Math.Ceiling((sorted.Length-1)*percentile)]; }
            evidence.Add("OBSERVED 2-actor loopback only frames="+frameMs.Count+" frame-p99-ms="+Quantile(frameMs,.99)+" adapter-Late-mean-ms="+(adapterMs.Count>0?adapterMs.Average():0)+" adapter-Late-p99-ms="+Quantile(adapterMs,.99));
            File.WriteAllLines(Path.Combine(output??".",host?"Host.txt":"Owner.txt"),evidence);
        }
        catch(Exception error) { pass=false;File.WriteAllText(Path.Combine(output??".","CleanupFailure.txt"),error.ToString()); }
        Application.Quit(pass && errors==0?0:1);
    }
}
#endif
