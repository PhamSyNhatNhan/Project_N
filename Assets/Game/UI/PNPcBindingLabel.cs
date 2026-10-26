#if UNITY_5_3_OR_NEWER
using UnityEngine.InputSystem;

// A read-only projection of the player's resolved action, never another input owner.
internal sealed class PNPcBindingLabel
{
    private InputAction action;
    private InputControl selected;
    private bool dirty=true;
    internal string Text {get;private set;}="—";
    internal string FullText {get;private set;}="";
    internal void Bind(InputAction value){if(ReferenceEquals(action,value))return;action=value;selected=null;dirty=true;}
    internal void Invalidate()=>dirty=true;
    internal static bool IsDesktop(InputControl control)=>control?.device is Keyboard||control?.device is Mouse;
    internal bool Refresh()
    {
        // Let Input System evaluate interactions/composites and binding masks. A gamepad
        // driving the action does not illuminate a keyboard label.
        bool pressed=action!=null&&action.enabled&&action.IsPressed()&&IsDesktop(action.activeControl);
        if(pressed&&selected!=action.activeControl){selected=action.activeControl;dirty=true;}
        if(!dirty)return pressed;
        dirty=false;
        if(action==null){Text="—";FullText="";return false;}
        bool found=false;
        foreach(var control in action.controls)if(IsDesktop(control)&&control==selected){found=true;break;}
        if(!found){selected=null;foreach(var control in action.controls)if(IsDesktop(control)){selected=control;break;}}
        Text="—";FullText="";
        if(selected!=null)
        {
            int index=action.GetBindingIndexForControl(selected);
            if(index>=0)
            {
                // Composite labels include their modifiers, rather than suggesting that
                // pressing just the modifier triggers the action.
                while(index>0&&action.bindings[index].isPartOfComposite)index--;
                FullText=action.GetBindingDisplayString(index,InputBinding.DisplayStringOptions.DontUseShortDisplayNames|InputBinding.DisplayStringOptions.DontIncludeInteractions);
                Text=Compact(FullText);
            }
        }
        return pressed;
    }
    private static string Compact(string text)
    {
        switch(text){case "Left Button":case "LMB":return "M1";case "Right Button":case "RMB":return "M2";case "Middle Button":case "MMB":return "M3";
            case "Space":return "Spc";case "Escape":return "Esc";}
        return string.IsNullOrEmpty(text)?"—":text.Replace("Left Shift","LS").Replace("Right Shift","RS").Replace("Left Ctrl","LC").Replace("Right Ctrl","RC").Replace("Left Alt","LA").Replace("Right Alt","RA");
    }
}
#endif
