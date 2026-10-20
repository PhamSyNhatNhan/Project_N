#if UNITY_5_3_OR_NEWER
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// EN: Records the graphics states (pipeline state objects) a run actually uses, so they can be compiled ahead of time on DX12/Vulkan (docs: Docs/ShaderWarmupPlan.md phase B,
//     Assets/Game/Runtime/PNShaderWarmupREADME.md). Off unless the Player is started with `-pn-gsc-record=<file>`; the file is written when the application quits, or
//     after `-pn-gsc-record-seconds=<n>` seconds (the Player then quits itself). Play normally (Hall, a run, skills, enemies, lorica break), then quit. Several recordings are
//     merged and installed by `PNGraphicsStateTools` (Editor).
// VI: Ghi lại các graphics state (PSO) mà một lượt chạy thực sự dùng để biên dịch trước trên DX12/Vulkan. Chỉ bật khi có -pn-gsc-record=<file>.
public sealed class PNGraphicsStateRecorder : MonoBehaviour
{
    private GraphicsStateCollection collection;
    private string file; private float seconds; private bool saved;
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        var path = Arg("-pn-gsc-record="); if (string.IsNullOrEmpty(path)) return;
        var host = new GameObject("PNGraphicsStateRecorder"); DontDestroyOnLoad(host);
        host.AddComponent<PNGraphicsStateRecorder>().Begin(path, float.TryParse(Arg("-pn-gsc-record-seconds="), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0f);
    }
    private void Begin(string path, float duration)
    {
        file = path; seconds = duration; collection = new GraphicsStateCollection();
        if (!collection.BeginTrace()) { Debug.LogWarning("Graphics state tracing could not start (unsupported graphics API?): " + SystemInfo.graphicsDeviceType); collection = null; return; }
        Debug.Log("Graphics state tracing started on " + SystemInfo.graphicsDeviceType + " -> " + file);
        Application.quitting += Save;
    }
    private void Update() { if (collection != null && seconds > 0f && Time.realtimeSinceStartup >= seconds) { Save(); Application.Quit(); } }
    private void Save()
    {
        if (collection == null || saved) return;
        saved = true;
        try
        {
            collection.EndTrace(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            bool ok = collection.SaveToFile(file);
            Debug.Log("Graphics states saved=" + ok + " variants=" + collection.variantCount + " states=" + collection.totalGraphicsStateCount + " api=" + collection.graphicsDeviceType + " platform=" + collection.runtimePlatform + " file=" + file);
        }
        catch (Exception error) { Debug.LogException(error); }
    }
}
#endif
