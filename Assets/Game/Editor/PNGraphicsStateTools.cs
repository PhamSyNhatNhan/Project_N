#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// EN: Merges graphics-state recordings made by PNGraphicsStateRecorder (Player started with -pn-gsc-record=<file>) and installs the result where PNShaderWarmup loads it
//     (Assets/StreamingAssets/GraphicsStates/<RuntimePlatform>_<GraphicsDeviceType>.graphicsstate). Batch usage:
//       Unity.exe -batchmode -quit -projectPath <project> -executeMethod PNGraphicsStateTools.Install -pn-gsc-inputs=a.graphicsstate;b.graphicsstate [-pn-gsc-output=<file>]
//     All inputs must be recorded for the same platform and graphics API. Docs: Assets/Game/Runtime/PNShaderWarmupREADME.md.
// VI: Gộp các bản ghi graphics state và cài vào StreamingAssets/GraphicsStates để PNShaderWarmup nạp.
public static class PNGraphicsStateTools
{
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);

    public static void Install()
    {
        int code = 0;
        try { Debug.Log(Merge((Arg("-pn-gsc-inputs=") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), Arg("-pn-gsc-output="))); }
        catch (Exception error) { Debug.LogException(error); code = 1; }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    public static string Merge(string[] inputs, string output = null)
    {
        if (inputs == null || inputs.Length == 0) throw new ArgumentException("At least one recording is required (-pn-gsc-inputs=a;b).");
        GraphicsStateCollection merged = null; string report = "";
        foreach (var input in inputs)
        {
            var part = new GraphicsStateCollection();
            if (!File.Exists(input) || !part.LoadFromFile(input)) throw new InvalidOperationException("Cannot read graphics states: " + input);
            report += Path.GetFileName(input) + ": " + part.variantCount + " variants, " + part.totalGraphicsStateCount + " states; ";
            if (merged == null) { merged = part; continue; }
            if (part.graphicsDeviceType != merged.graphicsDeviceType || part.runtimePlatform != merged.runtimePlatform)
                throw new InvalidOperationException("Recordings differ in platform or graphics API: " + input);
            if (!merged.Append(part)) throw new InvalidOperationException("Could not merge " + input);
        }
        string file = output ?? Path.Combine(Application.dataPath, "StreamingAssets", "GraphicsStates", merged.runtimePlatform + "_" + merged.graphicsDeviceType + ".graphicsstate");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
        if (!merged.SaveToFile(file)) throw new InvalidOperationException("Could not write " + file);
        return "Graphics states installed: " + merged.variantCount + " variants, " + merged.totalGraphicsStateCount + " states (" + merged.runtimePlatform + "/" + merged.graphicsDeviceType + ") -> " + file + " [" + report + "]";
    }
}
#endif
