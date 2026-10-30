#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEditor;
using UnityEngine;

// Opt-in observer: does not pump, retry, stop Play, or change scenes/settings.
[InitializeOnLoad]
public static class EveEditorStopCleanupChecks
{
    private static EveSessionContext session;
    private static EveWorldContext[] worlds;
    private static string output;
    private static double stoppedAt;
    private static StringBuilder trace;
    static EveEditorStopCleanupChecks() { EditorApplication.update += Observe; }
    public static string Watch(string path)
    {
        if (!Application.isPlaying || !EveRegistrySession.IsReady || session != null)
            throw new InvalidOperationException("An unwatched Ready Play session is required.");
        var current = EveRegistrySession.Context;
        if (current.Entities.Count < 1 || current.Cleanup == null)
            throw new InvalidOperationException("Load real actors before watching Stop.");
        session = current; worlds = current.Worlds.Snapshot().ToArray(); output = path;
        stoppedAt = -1; trace = new StringBuilder();
        return "Watching " + session.Entities.Count + " loaded actors and " + worlds.Length + " worlds";
    }
    private static void Observe()
    {
        if (session == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (stoppedAt < 0) stoppedAt = EditorApplication.timeSinceStartup;
        double elapsed = EditorApplication.timeSinceStartup - stoppedAt;
        trace.AppendLine("t=" + elapsed.ToString("F3") + " state=" + EveRegistrySession.State +
            " pending=" + EveRegistrySession.PendingCleanupCount + " entities=" + session.Entities.Count +
            " services=" + session.PendingServiceCleanupCount + " residents=" + session.Cleanup.ResidentCount +
            " leases=" + worlds.Sum(x => x.LeaseCount) + " error=" + EveRegistrySession.LastError);
        if (EveRegistrySession.State != EveSessionState.Stopped && elapsed < 10) return;
        bool pass = EveRegistrySession.State == EveSessionState.Stopped && EveRegistrySession.PendingCleanupCount == 0 &&
            EveRegistrySession.LastError == null && session.Entities.Count == 0 && session.PendingServiceCleanupCount == 0 &&
            session.Cleanup.ResidentCount == 0 && session.Worlds.Snapshot().Count == 0 &&
            worlds.All(x => x.State == EveWorldState.Disposed && x.LeaseCount == 0 && x.PendingCleanup == 0);
        trace.AppendLine((pass ? "PASS" : "FAIL") + " automatic Editor Stop cleanup; no observer pump or retry");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, trace.ToString());
            if (pass) Debug.Log("PASS Editor Stop cleanup: entities/services/residents/worlds/leases all zero");
            else Debug.LogError(trace.ToString());
        }
        finally { session = null; worlds = null; trace = null; output = null; }
    }
}
#endif
