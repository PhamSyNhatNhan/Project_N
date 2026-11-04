using System;
using System.Collections.Generic;
using EveEngine;
namespace ProjectN.Coop.Tests
{
    public static class PNAdmissionChecks
    {
        public static string Run()
        {
            int count=0;void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
            void Reject(Action action,string name,string expected=null)
            {try{action();}catch(Exception error){if(expected!=null&&!error.Message.Contains(expected))throw new Exception(name+": "+error.Message);count++;return;}throw new Exception(name);}
            const string PersonA="0123456789abcdef0123456789abcdef",PersonB="fedcba9876543210fedcba9876543210";
            var local=new EveContentCompatibility(1,"rules",new Dictionary<string,string>{{"pack:fixture",new string('a',64)},{"dll:fixture",new string('b',64)}});
            PNAdmissionRequest Request()=>new PNAdmissionRequest {NetworkVersion=PNNetworkAuthenticator.NetworkVersion,Limits=new PNNetworkProfile().ProtocolLimits,Platform="PC",Mode="roguelite",SelectPlayer=true,Content=PNContentFingerprint.From(local),PlayerId=PersonA};
            EveContentCompatibility Read(PNAdmissionRequest value,int admitted=0,bool host=false,ICollection<string> online=null,ICollection<string> allowed=null)=>PNAdmissionRules.Validate(value,local,"PC","roguelite",admitted,3,host,online,allowed);
            local.Validate(Read(Request()));Check(true,"matching fingerprint admitted");
            var request=Request();request.NetworkVersion++;Reject(()=>Read(request),"network version mismatch","version");
            request=Request();request.Platform="Mobile";Reject(()=>Read(request),"no crossplay","Platform");
            request=Request();request.Mode="other";Reject(()=>Read(request),"mode mismatch","Mode");
            request=Request();Reject(()=>Read(request,3),"capacity before spawn","capacity");Read(request,3,true);Check(true,"host-local is not one of three remote slots");
            request=Request();request.Content.Rules="other";Reject(()=>Read(request),"rules mismatch","rules");
            request=Request();request.Content.Hashes[1].Value=new string('c',64);Reject(()=>Read(request),"pack maps to name","pack:fixture");
            request=Request();request.Content.Hashes[0].Value=new string('d',64);Reject(()=>Read(request),"DLL maps to name","dll:fixture");
            request=Request();request.Content.Hashes=new PNHash[0];Reject(()=>Read(request),"missing required content rejected","fixture");
            request=Request();request.Content.Hashes=new[]{request.Content.Hashes[0],request.Content.Hashes[0]};Reject(()=>Read(request),"duplicate fingerprint key rejected");
            request=Request();request.Content.Hashes=new PNHash[257];Reject(()=>Read(request),"bounded fingerprint count");
            request=Request();request.Content.Hashes[0].Value="short";Reject(()=>Read(request),"invalid hash rejected");
            request=Request();var accepted=Read(request);request.Content.Hashes[0].Value=new string('e',64);local.Validate(accepted);Check(true,"admitted fingerprint is isolated from mutable wire DTO");
            // ---- the person (Docs/RunSavePlan.md 5.10) ----
            request=Request();request.PlayerId=null;Reject(()=>Read(request),"a missing person id is rejected","Player id");
            request=Request();request.PlayerId=PersonA.ToUpperInvariant();Reject(()=>Read(request),"an upper-case person id is rejected","Player id");
            request=Request();request.PlayerId="short";Reject(()=>Read(request),"a short person id is rejected","Player id");
            request=Request();Reject(()=>Read(request,0,false,new List<string>{PersonA}),"a person already in the room is rejected","đã có trong phòng");
            request=Request();Read(request,0,false,new List<string>{PersonB});Check(true,"another person in the room does not block this one");
            request=Request();Reject(()=>Read(request,0,false,null,new List<string>{PersonB}),"a stranger to a continued run is rejected","tiếp tục");
            request=Request();Read(request,0,false,null,new List<string>{PersonA,PersonB});Check(true,"a person of the continued run may join");
            request=Request();Read(request,0,true,null,new List<string>{PersonB});Check(true,"the host machine itself is not held to the seat list here (its own seat is checked by the run)");
            return "PASS "+count+" admission validation cases (no transport/process claim)";
        }
    }
}
