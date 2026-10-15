using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

// Disposable fixture storage. No subscription or string capture in Control.
internal sealed class PNPopupMeasurementProbe : IDisposable
{
    private readonly Dictionary<PNDamagePopupPool, (int active, int idle)> pools = new();
    private readonly List<object> frames = new(), strings = new();
    private readonly string output;
    private readonly bool captureStrings;
    private int active, idle, peak, shows, creates, returns, retires;
    private long retainedChars, droppedStrings;
    internal string Phase = "startup";
    internal int Repeat, Sample;
    internal PNPopupMeasurementProbe(string output, bool captureStrings)
    { this.output = output; this.captureStrings = captureStrings; PNDamagePopupDiagnostics.Observe = Observe; }
    private void Observe(string operation, PNDamagePopupPool pool, PNDamagePopup popup)
    {
        pools.TryGetValue(pool, out var previous);
        if (operation == "destroy-pool")
        {
            active -= previous.active; idle -= previous.idle; pools.Remove(pool); return;
        }
        int a = pool.CountActive, i = pool.CountInactive;
        active += a - previous.active; idle += i - previous.idle; pools[pool] = (a, i);
        peak = Math.Max(peak, active);
        if (operation == "create") creates++;
        if (operation == "after-advance") returns += Math.Max(0, previous.active - a);
        if (operation == "retire") retires++;
        if (operation != "after-show") return;
        shows++;
        if (!captureStrings) return;
        string text = popup.DisplayText;
        if (strings.Count >= 100000 || retainedChars + text.Length > 8 * 1024 * 1024) { droppedStrings++; return; }
        retainedChars += text.Length;
        // Authored styles/assets stay fixed here; this estimates string reuse, not mesh reuse.
        strings.Add(new { frame = Time.frameCount, phase = Phase, repeat = Repeat, sample = Sample,
            text, critical = popup.IsCritical, spriteAsset = popup.Icons == null ? null : popup.Icons.name,
            iconScale = popup.ElementIconScale });
    }
    internal void Frame()
    {
        var dead = new List<PNDamagePopupPool>();
        foreach (var pair in pools) if (pair.Key == null) { active -= pair.Value.active; idle -= pair.Value.idle; dead.Add(pair.Key); }
        foreach (var pool in dead) pools.Remove(pool);
        peak = Math.Max(peak, active);
        frames.Add(new { frame = Time.frameCount, phase = Phase, repeat = Repeat, sample = Sample,
            shows, creates, returns, retires, active, idle, activePeak = peak, poolCount = pools.Count, droppedStrings });
        shows = creates = returns = retires = 0; peak = active;
        if (frames.Count >= 1000 || strings.Count >= 10000) Flush();
    }
    internal void Flush()
    {
        using (var w = new StreamWriter(Path.Combine(output, "PopupFrames.jsonl"), true)) foreach (var row in frames) w.WriteLine(JsonConvert.SerializeObject(row));
        using (var w = new StreamWriter(Path.Combine(output, "PopupStrings.jsonl"), true)) foreach (var row in strings) w.WriteLine(JsonConvert.SerializeObject(row));
        frames.Clear(); strings.Clear(); retainedChars = 0;
    }
    public void Dispose() { PNDamagePopupDiagnostics.Observe = null; Flush(); pools.Clear(); }
}
