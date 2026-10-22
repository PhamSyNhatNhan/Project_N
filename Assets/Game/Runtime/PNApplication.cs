#if UNITY_5_3_OR_NEWER
using UnityEngine;

// Closing the application, one line for every platform: the player quits, the Editor leaves Play Mode (Application.Quit does nothing there).
public static class PNApplication
{
    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
#endif
