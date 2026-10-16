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
            var local=new EveContentCompatibility(1,"rules",new Dictionary<string,string>{{"pack:fixture",new string('a',64)},{"dll:fixture",new string('b',64)}});
            PNAdmissionRequest Request()=>new PNAdmissionRequest {NetworkVersion=PNNetworkAuthenticator.NetworkVersion,Platform="PC",Mode="roguelite",SelectPlayer=true,Content=PNContentFingerprint.From(local)};
            EveContentCompatibility Read(PNAdmissionRequest value,int admitted=0,bool host=false)=>PNAdmissionRules.Validate(value,local,"PC","roguelite",admitted,3,host);
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
            return "PASS "+count+" admission validation cases (no transport/process claim)";
        }
    }
}
