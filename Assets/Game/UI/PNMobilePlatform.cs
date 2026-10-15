#if UNITY_5_3_OR_NEWER
using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
#endif

// Device's mobile shim is global. Select it only for Unity's main Simulator view.
public static class PNMobilePlatform
{
#if UNITY_EDITOR
    // Unity 6000.6: this view persists when focus moves to Inspector/Scene/Console.
    // Isolate the internal Editor API; unsupported versions fail closed to desktop.
    private static readonly MethodInfo mainView=typeof(EditorWindow).Assembly
        .GetType("UnityEditor.PlayModeView")?.GetMethod("GetMainPlayModeView",BindingFlags.Static|BindingFlags.NonPublic);
#endif
    public static bool UsesSimulatedScreen
    {
        get
        {
#if UNITY_EDITOR
            var view=mainView?.Invoke(null,null) as EditorWindow;
            return view!=null && view.GetType().FullName=="UnityEditor.DeviceSimulation.SimulatorWindow";
#else
            return false;
#endif
        }
    }
    public static bool IsMobile(bool simulatedScreen)
    {
#if UNITY_EDITOR
        return simulatedScreen && UnityEngine.Device.Application.isMobilePlatform;
#else
        return Application.isMobilePlatform;
#endif
    }
}
#endif
