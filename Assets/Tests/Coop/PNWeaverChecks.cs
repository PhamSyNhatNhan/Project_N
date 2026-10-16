#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using Mirror;
using UnityEditor;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    public static class PNWeaverChecks
    {
        public static string Run()
        {
            // RuntimeInitializeOnLoad is not invoked by an Edit Mode command. Invoke the actual
            // Weaver-emitted initializer for this assembly; no hand-written substitute serializers.
            typeof(PNNetworkManager).Assembly.GetType("Mirror.GeneratedNetworkCode",true)
                .GetMethod("InitReadWriters",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,null);
            int count=0;void Check(bool x,string s){if(!x)throw new Exception(s);count++;}
            var content=new PNContentFingerprint {Protocol=1,Rules="project_n:74",Hashes=new[]{new PNHash {Key="pack:fixture",Value=new string('a',64)}}};
            var request=new PNAdmissionRequest {NetworkVersion=PNNetworkAuthenticator.NetworkVersion,Mode="roguelite",Platform="PC",Content=content};
            var writer=new NetworkWriter();writer.Write(request);var decoded=new NetworkReader(writer.ToArray()).Read<PNAdmissionRequest>();
            Check(decoded.NetworkVersion==request.NetworkVersion&&decoded.Mode==request.Mode&&decoded.Platform==request.Platform,"request primitive fields");
            content.Read().Validate(decoded.Content.Read());Check(true,"content hash and rules roundtrip");
            var response=new PNAdmissionResponse {Accepted=true,HostSession=Guid.NewGuid(),Owner=42,Content=content};
            writer.Reset();writer.Write(response);var read=new NetworkReader(writer.ToArray()).Read<PNAdmissionResponse>();
            Check(read.Accepted&&read.HostSession==response.HostSession&&read.Owner==42,"admission Guid and owner");
            var actor=new PNActorMetadata {HostSession=response.HostSession,Entity=8,Generation=3,Owner=42,Character="project_n:eve",SceneKey="fixture:scene",EntityKey="fixture:entity"};
            writer.Reset();writer.Write(actor);var copy=new NetworkReader(writer.ToArray()).Read<PNActorMetadata>();
            Check(copy.HostSession==actor.HostSession&&copy.Entity==8&&copy.Generation==3&&copy.Owner==42&&copy.Character==actor.Character&&copy.SceneKey==actor.SceneKey&&copy.EntityKey==actor.EntityKey,"actor metadata all fields");
            writer.Reset();writer.Write(new PNHeartbeat {Sequence=7,Paused=true});var heart=new NetworkReader(writer.ToArray()).Read<PNHeartbeat>();Check(heart.Sequence==7&&heart.Paused,"heartbeat");
            writer.Reset();writer.Write(new PNResume {Sequence=9,Acknowledged=true});var resume=new NetworkReader(writer.ToArray()).Read<PNResume>();Check(resume.Sequence==9&&resume.Acknowledged,"resume barrier");
            return "PASS "+count+" actual Mirror Weaver roundtrips; Windows target supported="+BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,BuildTarget.StandaloneWindows64)+" Android target supported="+BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android);
        }
    }
}
#endif
