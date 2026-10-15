#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;

// UI Toolkit transport using the original OnScreenControl virtual-device lifecycle.
public sealed class PNMobileVirtualControl : OnScreenControl
{
    private string path;
    protected override string controlPathInternal { get=>path; set=>path=value; }
    public void Configure(string value)
    {
        if(controlPath==value)return;
        // OnScreenControl's setter sets up a new control without retiring the old one.
        // Disable first so a live character rebind cannot corrupt its shared device list.
        bool wasEnabled=enabled;
        if(isActiveAndEnabled)enabled=false;
        controlPath=value;
        enabled=wasEnabled;
    }
    public void PushButton(bool pressed)=>SendValueToControl(pressed?1f:0f);
    public void PushStick(Vector2 value)=>SendValueToControl(value);
}
#endif
