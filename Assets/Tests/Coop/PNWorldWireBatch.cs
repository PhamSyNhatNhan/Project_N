#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace ProjectN.Coop.Tests
{
    // Batch entry for the clone project: the world wire checks, their result written to .utmp/WireChecks/Batch.txt, then the editor exits (a plain -executeMethod without Exit or -quit leaves a batch editor idle).
    public static class PNWorldWireBatch
    {
        public static void Run()
        {
            bool failed = false; string text;
            try { text = PNWorldWireChecks.Run(); }
            catch (Exception error) { failed = true; text = "FAIL " + error.Message; Debug.LogException(error); }
            Directory.CreateDirectory(".utmp/WireChecks");
            File.WriteAllText(".utmp/WireChecks/Batch.txt", text);
            EditorApplication.Exit(failed ? 1 : 0);
        }
    }
}
#endif
