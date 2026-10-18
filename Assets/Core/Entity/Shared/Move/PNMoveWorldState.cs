#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class PNMove
{
    public JObject CaptureWorldState()
    {
        if(Mode!=EveMoveMode.None||motionExecutions.Count!=0)throw new InvalidOperationException("Running movement requires an explicit execution serializer or normal cancellation.");
        var state=ReadMovementState();var recovery=ReadMovementRecovery();
        return new JObject{["facing"]=state.Facing,["grounded"]=state.Grounded,["vertical"]=state.VerticalView,["awaiting"]=recovery.AwaitingGroundDeparture,["velocity"]=new JArray(recovery.ResumeVelocity.X,recovery.ResumeVelocity.Y,recovery.ResumeVelocity.Z),["angular"]=new JArray(recovery.ResumeAngularVelocity.X,recovery.ResumeAngularVelocity.Y,recovery.ResumeAngularVelocity.Z),["move"]=BaseCanMove,["flip"]=BaseCanFlip,["jump"]=BaseCanJump,["last"]=new JArray(LastMoveDirection.x,LastMoveDirection.y),["input"]=!(this is PNPlayerMove player)||player.BaseCanInput};
    }
    private static Vector3 StateVector(JToken data,int count)
    {
        if(!(data is JArray values)||values.Count!=count)throw new FormatException("Movement vector required.");
        float Read(int index){if(values[index].Type!=JTokenType.Float&&values[index].Type!=JTokenType.Integer)throw new FormatException("Movement number required.");float value=(float)values[index];if(float.IsNaN(value)||float.IsInfinity(value)||Math.Abs(value)>1000000)throw new FormatException("Finite bounded movement value required.");return value;}
        return new Vector3(Read(0),Read(1),count==3?Read(2):0);
    }
    public static void ValidateWorldState(JObject state)
    {
        int face=PNAbilityCollection.Integer(state,"facing"),vertical=PNAbilityCollection.Integer(state,"vertical");if(face!=1&&face!=-1||vertical<0||vertical>2)throw new FormatException("Movement facing/vertical state.");
        foreach(var key in new[]{"grounded","awaiting","move","flip","jump","input"})PNAbilityCollection.Bool(state,key);
        StateVector(state["velocity"],3);StateVector(state["angular"],3);StateVector(state["last"],2);
    }
    public void RestoreWorldState(JObject state)
    {
        if(loadContext?.IsValid!=true||!loadContext.Session.HasAuthority||!loadContext.AllowsModuleAccess||body==null)throw new InvalidOperationException("Movement restoration requires the loader module scope.");
        ValidateWorldState(state);base.CanMove=(bool)state["move"];base.CanFlip=(bool)state["flip"];base.CanJump=(bool)state["jump"];
        RestoreFacing((int)state["facing"]);RefreshFacingVisual();RestoreLocomotionView((bool)state["grounded"],(byte)state["vertical"],(bool)state["awaiting"]);
        var last=StateVector(state["last"],2);LastMoveDirection=new Vector2(last.x,last.y);SetMoveDirection(Vector2.zero);
        if(this is PNPlayerMove player)player.CanInput=(bool)state["input"];
        var velocity=StateVector(state["velocity"],3);var angular=StateVector(state["angular"],3);savedVelocity=velocity;savedAngularVelocity=angular;
        if(!body.isKinematic){float scale=clock!=null?clock.Scale:1;body.linearVelocity=velocity*scale;body.angularVelocity=angular*scale;previousScale=scale>0?scale:1;}
    }
}
#endif
