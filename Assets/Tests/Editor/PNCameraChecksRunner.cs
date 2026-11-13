#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Runs the native camera checks (edit mode) and writes the result to .utmp/CameraChecks.txt.
public static class PNCameraChecksRunner
{
    [MenuItem("Tools/PN/Run Native Camera Checks")]
    public static void RunAll()
    {
        var lines = new System.Collections.Generic.List<string>();
        void One(string name, Func<string> run)
        {
            try { var r = run(); lines.Add("== " + name + "\n" + r); }
            catch (Exception e) { lines.Add("== " + name + "\nEXCEPTION " + e.Message + "\n" + e.StackTrace); }
        }
        One("Director", PNCameraDirectorChecks.Run);
        One("Zoom", PNCameraZoomChecks.Run);
        One("Extras", PNCameraExtrasChecks.Run);
        One("Cinematic", PNCameraCinematicChecks.Run);
        var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".utmp", "CameraChecks.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, string.Join("\n", lines));
    }
}
#endif
