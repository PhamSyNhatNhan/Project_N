using System;
using System.IO;
using System.Text;

public enum PNRunSaveStatus { Missing, Loaded, RestoredFromBackup, Corrupt, NewerVersion }

public sealed class PNRunSaveLoad
{
    public PNRunSave Save { get; }
    public PNRunSaveStatus Status { get; }
    internal PNRunSaveLoad(PNRunSave save, PNRunSaveStatus status) { Save = save; Status = status; }
}

// EN: Reads and writes the one saved run, `<folder>/run.json` (Docs/RunSavePlan.md 5.4), the way PNProfileStore keeps the profile: write to a temp file then replace, the previous file kept
//     as `.bak`; a corrupt file is renamed `.corrupt` and the backup tried; a file from a newer schema is left untouched and Save refuses; reparse points are rejected; the size is bounded.
//     Delete removes the file and its backup, so a deleted run cannot come back from the backup.
// VI: Đọc/ghi run đã lưu (một ô) theo cách PNProfileStore giữ profile: ghi tệp tạm rồi thay thế, giữ `.bak`; tệp hỏng đổi tên `.corrupt`; schema mới hơn không bị ghi đè. Xoá cả `.bak`.
public sealed class PNRunSaveStore
{
    public const string FileName = "run.json";
    private readonly string folder, path, backup, corrupt, temp;
    private bool locked;

    public PNRunSaveStore(string folder)
    {
        this.folder = Path.GetFullPath(folder ?? throw new ArgumentNullException(nameof(folder)));
        Directory.CreateDirectory(this.folder);
        PNWorldFileStore.RejectReparse(this.folder);
        path = Path.Combine(this.folder, FileName); backup = path + ".bak"; corrupt = path + ".corrupt"; temp = path + ".tmp";
        foreach (var p in new[] { path, backup, corrupt, temp }) PNWorldFileStore.RejectReparse(p);
    }

    public string FilePath => path;
    public bool SaveRefused => locked;

    private static PNRunSave TryRead(string file, out bool newer)
    {
        newer = false;
        PNWorldFileStore.RejectReparse(file);
        var info = new FileInfo(file);
        if (!info.Exists) return null;
        if (info.Length > PNRunSave.MaxBytes) throw new FormatException("Run save is too large.");
        return PNRunSave.Parse(new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file)), out newer);
    }

    public PNRunSaveLoad Load()
    {
        locked = false;
        if (!File.Exists(path))
        {
            // The main file can be gone after an interrupted replace; the backup is still good.
            if (File.Exists(backup)) { try { var b = TryRead(backup, out var bn); if (b != null && !bn) return new PNRunSaveLoad(b, PNRunSaveStatus.RestoredFromBackup); } catch (Exception e) when (e is FormatException || e is IOException || e is DecoderFallbackException) { } }
            return new PNRunSaveLoad(null, PNRunSaveStatus.Missing);
        }
        try
        {
            var save = TryRead(path, out var newer);
            if (newer) { locked = true; return new PNRunSaveLoad(null, PNRunSaveStatus.NewerVersion); }
            return new PNRunSaveLoad(save, PNRunSaveStatus.Loaded);
        }
        catch (Exception e) when (e is FormatException || e is DecoderFallbackException || e is IOException)
        {
            if (e is IOException && !File.Exists(path)) return new PNRunSaveLoad(null, PNRunSaveStatus.Missing);
            try { File.Copy(path, corrupt, true); File.Delete(path); } catch (IOException) { }
            try
            {
                bool bn = false;
                var b = File.Exists(backup) ? TryRead(backup, out bn) : null;
                if (b != null && !bn) return new PNRunSaveLoad(b, PNRunSaveStatus.RestoredFromBackup);
            }
            catch (Exception e2) when (e2 is FormatException || e2 is DecoderFallbackException || e2 is IOException) { }
            return new PNRunSaveLoad(null, PNRunSaveStatus.Corrupt);
        }
    }

    public void Save(PNRunSave save)
    {
        if (save == null) throw new ArgumentNullException(nameof(save));
        if (locked) throw new InvalidOperationException("The run save comes from a newer version and is not overwritten.");
        var bytes = new UTF8Encoding(false).GetBytes(save.ToJson());
        if (bytes.Length > PNRunSave.MaxBytes) throw new InvalidOperationException("Run save is too large.");
        PNWorldFileStore.RejectReparse(folder); PNWorldFileStore.RejectReparse(path); PNWorldFileStore.RejectReparse(temp); PNWorldFileStore.RejectReparse(backup);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, backup, true);
        else File.Move(temp, path);
    }

    // The run is over (or summed up): its file and its backup go. A save from a newer version is not ours to delete.
    public void Delete()
    {
        if (locked) throw new InvalidOperationException("The run save comes from a newer version and is not deleted.");
        foreach (var file in new[] { path, backup, temp })
        {
            PNWorldFileStore.RejectReparse(file);
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
