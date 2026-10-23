using System;
using System.IO;
using System.Text;

public enum PNSettingsLoadStatus { Missing, Loaded, Backup, Legacy, Corrupt, Unsupported, Unavailable }
public sealed class PNSettingsLoad
{
    public PNUserSettings Value { get; }
    public PNSettingsLoadStatus Status { get; }
    public string Diagnostic { get; }
    internal PNSettingsLoad(PNUserSettings value, PNSettingsLoadStatus status, string diagnostic = null)
    { Value = value; Status = status; Diagnostic = diagnostic; }
}

// Load never deletes/renames user files. Only explicit saves replace the main file.
public sealed class PNSettingsStore
{
    public string FilePath { get; }
    public bool SaveRefused { get; private set; }
    private readonly string folder;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    public PNSettingsStore(string folder)
    {
        this.folder = Path.GetFullPath(folder ?? throw new ArgumentNullException(nameof(folder)));
        FilePath = Path.Combine(this.folder, "settings.json");
    }
    private PNUserSettings Read(string file, out bool unsupported)
    {
        unsupported = false; PNWorldFileStore.RejectReparse(folder); PNWorldFileStore.RejectReparse(file);
        if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > PNUserSettings.MaxBytes) throw new FormatException("Settings file too large.");
        return PNUserSettings.Parse(Utf8.GetString(File.ReadAllBytes(file)), out unsupported);
    }
    private static bool ReadFailure(Exception e) => e is IOException || e is UnauthorizedAccessException || e is FormatException || e is DecoderFallbackException || e is ArgumentException || e is OverflowException;
    public PNSettingsLoad Load()
    {
        SaveRefused = false; bool any = false; string diagnostic = null;
        foreach (var candidate in new[] { FilePath, FilePath + ".bak" })
        {
            try
            {
                any |= File.Exists(candidate);
                var value = Read(candidate, out bool unsupported);
                if (unsupported) { SaveRefused = true; return new PNSettingsLoad(new PNUserSettings(), PNSettingsLoadStatus.Unsupported, "Settings are from an unsupported schema."); }
                if (value != null) return new PNSettingsLoad(value, candidate == FilePath ? PNSettingsLoadStatus.Loaded : PNSettingsLoadStatus.Backup, diagnostic);
            }
            catch (Exception e) when (ReadFailure(e)) { diagnostic = e.Message; if (e is UnauthorizedAccessException) SaveRefused = true; }
        }
        if (!any && !SaveRefused)
        {
            try
            {
                PNWorldFileStore.RejectReparse(Path.Combine(folder,"localization.json")); PNWorldFileStore.RejectReparse(Path.Combine(folder,"localization.json.bak"));
                string legacy = PNLanguagePreferenceStore.Load(Path.Combine(folder, "localization.json"));
                if (legacy != null) return new PNSettingsLoad(new PNUserSettings(legacy), PNSettingsLoadStatus.Legacy);
            }
            catch (Exception e) when (ReadFailure(e)) { diagnostic = e.Message; }
        }
        return new PNSettingsLoad(new PNUserSettings(), SaveRefused ? PNSettingsLoadStatus.Unavailable : any ? PNSettingsLoadStatus.Corrupt : PNSettingsLoadStatus.Missing, diagnostic);
    }
    public void Save(PNUserSettings value)
    {
        if (SaveRefused) throw new InvalidOperationException("Settings are protected and cannot be overwritten.");
        byte[] bytes = Utf8.GetBytes((value ?? throw new ArgumentNullException(nameof(value))).ToJson());
        if (bytes.Length > PNUserSettings.MaxBytes) throw new InvalidOperationException("Settings too large.");
        Directory.CreateDirectory(folder); PNWorldFileStore.RejectReparse(folder);
        string temp = FilePath + ".tmp", backup = FilePath + ".bak";
        foreach (string file in new[] { FilePath, temp, backup }) PNWorldFileStore.RejectReparse(file);
        // Recheck before each write: a newer file may have appeared after Load.
        bool mainValid = false;
        foreach (string file in new[] { FilePath, backup })
        {
            try
            {
                var existing = Read(file, out bool unsupported);
                if (unsupported) { SaveRefused = true; throw new InvalidOperationException("Newer settings must not be overwritten."); }
                if (file == FilePath) mainValid = existing != null;
            }
            catch (Exception e) when (e is FormatException || e is DecoderFallbackException || e is ArgumentException || e is OverflowException)
            { if (File.Exists(file)) File.Copy(file, file + ".corrupt-" + Guid.NewGuid().ToString("N"), false); }
        }
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, mainValid ? backup : null, true);
        else File.Move(temp, FilePath);
    }
}
