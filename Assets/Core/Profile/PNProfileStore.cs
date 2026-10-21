using System;
using System.IO;
using System.Text;

public enum PNProfileLoadStatus { Missing, Loaded, RestoredFromBackup, CorruptReset, NewerVersion }

public sealed class PNProfileLoad
{
    public PNPlayerProfile Profile { get; }
    public PNProfileLoadStatus Status { get; }
    internal PNProfileLoad(PNPlayerProfile profile, PNProfileLoadStatus status) { Profile = profile; Status = status; }
}

// EN: Reads and writes the single profile file `<folder>/profile.json` (kept apart from world saves, which live in their own folder).
//     Write = temp file -> replace, the previous file kept as `profile.json.bak`. A corrupt file is renamed to `profile.json.corrupt`
//     and the backup (or defaults) used. A file from a newer schema is left untouched and Save refuses until the schema is understood.
//     Reparse points are rejected like the world store; file size is bounded.
// VI: Đọc/ghi tệp hồ sơ duy nhất; ghi qua tệp tạm, giữ `.bak`; tệp hỏng đổi tên `.corrupt`; tệp từ phiên bản mới hơn không bị ghi đè.
public sealed class PNProfileStore
{
    public const string FileName = "profile.json";
    private readonly string folder, path, backup, corrupt, temp;
    private bool locked;

    public PNProfileStore(string folder)
    {
        this.folder = Path.GetFullPath(folder ?? throw new ArgumentNullException(nameof(folder)));
        Directory.CreateDirectory(this.folder);
        PNWorldFileStore.RejectReparse(this.folder);
        path = Path.Combine(this.folder, FileName); backup = path + ".bak"; corrupt = path + ".corrupt"; temp = path + ".tmp";
        foreach (var p in new[] { path, backup, corrupt, temp }) PNWorldFileStore.RejectReparse(p);
    }

    public string FilePath => path;
    public bool SaveRefused => locked;

    private static PNPlayerProfile TryRead(string file, out bool newer)
    {
        newer = false;
        PNWorldFileStore.RejectReparse(file);
        var info = new FileInfo(file);
        if (!info.Exists) return null;
        if (info.Length > PNPlayerProfile.MaxBytes) throw new FormatException("Profile file is too large.");
        return PNPlayerProfile.Parse(new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file)), out newer);
    }

    public PNProfileLoad Load()
    {
        locked = false;
        if (!File.Exists(path))
        {
            // The main file can be gone after an interrupted replace; the backup is still good.
            if (File.Exists(backup)) { try { var b = TryRead(backup, out var bn); if (b != null && !bn) return new PNProfileLoad(b, PNProfileLoadStatus.RestoredFromBackup); } catch (Exception e) when (e is FormatException || e is IOException || e is DecoderFallbackException) { } }
            return new PNProfileLoad(new PNPlayerProfile(), PNProfileLoadStatus.Missing);
        }
        try
        {
            var profile = TryRead(path, out var newer);
            if (newer) { locked = true; return new PNProfileLoad(new PNPlayerProfile(), PNProfileLoadStatus.NewerVersion); }
            return new PNProfileLoad(profile, PNProfileLoadStatus.Loaded);
        }
        catch (Exception e) when (e is FormatException || e is DecoderFallbackException || e is IOException)
        {
            if (e is IOException && !File.Exists(path)) return new PNProfileLoad(new PNPlayerProfile(), PNProfileLoadStatus.Missing);
            try { File.Copy(path, corrupt, true); File.Delete(path); } catch (IOException) { }
            try
            {
                bool bn = false;
                var b = File.Exists(backup) ? TryRead(backup, out bn) : null;
                if (b != null && !bn) return new PNProfileLoad(b, PNProfileLoadStatus.RestoredFromBackup);
            }
            catch (Exception e2) when (e2 is FormatException || e2 is DecoderFallbackException || e2 is IOException) { }
            return new PNProfileLoad(new PNPlayerProfile(), PNProfileLoadStatus.CorruptReset);
        }
    }

    public void Save(PNPlayerProfile profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (locked) throw new InvalidOperationException("The profile file comes from a newer version and is not overwritten.");
        var bytes = new UTF8Encoding(false).GetBytes(profile.ToJson());
        if (bytes.Length > PNPlayerProfile.MaxBytes) throw new InvalidOperationException("Profile is too large.");
        PNWorldFileStore.RejectReparse(folder); PNWorldFileStore.RejectReparse(path); PNWorldFileStore.RejectReparse(temp); PNWorldFileStore.RejectReparse(backup);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, backup, true);
        else File.Move(temp, path);
    }
}
