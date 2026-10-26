#if UNITY_EDITOR
using System;
using UnityEngine.InputSystem;

public static class PNPcControlsEditorChecks
{
    public static int Run()
    {
        int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception("PC controls: "+label);checks++;}
        var keyboard=InputSystem.AddDevice<Keyboard>("PcHudChecksKeyboard");var pad=InputSystem.AddDevice<Gamepad>("PcHudChecksPad");
        var label=new PNPcBindingLabel();
        using(var action=new InputAction("PcHudChecks",InputActionType.Button,interactions:"tap"))
        try
        {
            action.AddBinding("<Keyboard>/j",groups:"Keyboard");action.AddBinding("<Gamepad>/buttonSouth",groups:"Gamepad");
            label.Bind(action);label.Refresh();Check(!action.enabled,"observing does not enable action");
            action.Enable();label.Invalidate();label.Refresh();Check(label.Text=="J","resolved keyboard label");
            action.ApplyBindingOverride(0,"<Keyboard>/space");label.Invalidate();label.Refresh();Check(label.Text=="Spc","runtime override label");
            action.ApplyBindingOverride(0,"");label.Invalidate();label.Refresh();Check(label.Text=="—","disabled binding does not show default");
            action.ApplyBindingOverride(0,"<Mouse>/leftButton");var mouse=InputSystem.AddDevice<Mouse>("PcHudChecksMouse");
            try{label.Invalidate();label.Refresh();Check(label.Text=="M1","mouse compact alias");}finally{InputSystem.RemoveDevice(mouse);}
            action.bindingMask=InputBinding.MaskByGroup("Gamepad");label.Invalidate();label.Refresh();Check(label.Text=="—","mask excludes desktop label");
            label.Bind(null);Check(!label.Refresh()&&label.Text=="—"&&action.enabled,"unbind only releases observation");
        }
        finally{label.Bind(null);InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(pad);}
        using(var composite=new InputAction("PcHudComposite",InputActionType.Button))
        {
            var keyboard2=InputSystem.AddDevice<Keyboard>("PcHudCompositeKeyboard");
            try
            {
                composite.AddCompositeBinding("ButtonWithOneModifier").With("Modifier","<Keyboard>/leftShift").With("Button","<Keyboard>/j");composite.Enable();label.Bind(composite);
                label.Refresh();Check(label.FullText.Contains("J")&&label.FullText.Contains("Shift"),"composite retains complete binding label");
            }
            finally{label.Bind(null);InputSystem.RemoveDevice(keyboard2);}
        }
        return checks;
    }
}
#endif
