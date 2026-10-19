using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class PNWorldFileRead
{
    public PNWorldSaveEnvelope Envelope {get;}
    public long Revision {get;}
    public bool Recovered {get;}
    internal PNWorldFileRead(PNWorldSaveEnvelope envelope,long revision,bool recovered){Envelope=envelope;Revision=revision;Recovered=recovered;}
}
// Immutable revisions + commit markers. Readers never accept uncommitted data.
// Atomic same-directory rename is required; flush cannot promise every storage device's power-loss semantics.
public sealed class PNWorldFileStore
{
    private readonly string root;
    private readonly Action<string> boundary;
    private readonly long diskBudget;
    public PNWorldFileStore(string root,Action<string> boundary=null,long diskBudget=512L*1024*1024)
    {if(diskBudget<4096)throw new ArgumentOutOfRangeException(nameof(diskBudget));this.diskBudget=diskBudget;this.root=Path.GetFullPath(root??throw new ArgumentNullException(nameof(root)));this.boundary=boundary;Directory.CreateDirectory(this.root);RejectReparse(this.root);}
    public static void ValidateKey(string key)
    {if(string.IsNullOrEmpty(key)||key.Length>64||key=="."||key==".."||key.Any(c=>!((c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='_'||c=='-')))throw new ArgumentException("Save key must be 1-64 lowercase ASCII letters/digits/_/-.");}
    public static void RejectReparse(string path)
    {
        for(var p=new DirectoryInfo(Path.GetFullPath(path));p!=null;p=p.Parent)
            if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse paths are not accepted for content/save storage.");
        if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse file is not accepted.");
    }
    private string Folder(string key)
    {ValidateKey(key);var path=Path.Combine(root,key);RejectReparse(path);return path;}
    public long Write(string key,PNWorldSaveEnvelope envelope,CancellationToken cancel=default)
    {
        if(envelope==null)throw new ArgumentNullException(nameof(envelope));var folder=Folder(key);Directory.CreateDirectory(folder);
        var lockPath=Path.Combine(folder,"writer.lock");RejectReparse(lockPath);
        var budgetPath=Path.Combine(root,"budget.lock");RejectReparse(budgetPath);
        using(var budgetLock=new FileStream(budgetPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        using(var writerLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            long used=0;var folders=Directory.GetDirectories(root);if(folders.Length>1024)throw new IOException("Save root key count exceeds bound.");
            foreach(var directory in folders)
            {
                RejectReparse(directory);RecoverUncommitted(directory);
                var records=Directory.GetFiles(directory,"*.record");if(records.Length>1024)throw new IOException("Save record count exceeds bound.");foreach(var file in records){RejectReparse(file);used=checked(used+new FileInfo(file).Length);}
            }
            if(used+envelope.Bytes+4096>diskBudget)throw new IOException("Save disk quota exceeded; prior committed revisions retained.");
            var files=Commits(folder);if(files.Length>1024)throw new IOException("Save revision directory exceeds bound.");
            long revision=checked(Math.Max(DateTime.UtcNow.Ticks,files.Select(Revision).DefaultIfEmpty(0).Max()+1));
            var name=revision.ToString("D20",CultureInfo.InvariantCulture);var dataPath=Path.Combine(folder,name+".record");var commitPath=Path.Combine(folder,name+".commit");
            var staged=dataPath+".pending";var markerPending=commitPath+".pending";bool committed=false;
            try
            {
                cancel.ThrowIfCancellationRequested();var bytes=Encoding.UTF8.GetBytes(envelope.Json);boundary?.Invoke("before-write");
                using(var stream=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);boundary?.Invoke("after-write");stream.Flush(true);}
                boundary?.Invoke("after-flush");cancel.ThrowIfCancellationRequested();
                if(PNWorldSaveEnvelope.Hash(ReadBounded(staged,PNWorldSaveEnvelope.MaximumBytes))!=PNWorldSaveEnvelope.Hash(bytes))throw new IOException("Written save verification failed.");
                File.Move(staged,dataPath);boundary?.Invoke("after-data-publish");cancel.ThrowIfCancellationRequested();
                var marker=Encoding.UTF8.GetBytes(new JObject{["revision"]=revision,["length"]=bytes.Length,["hash"]=PNWorldSaveEnvelope.Hash(bytes)}.ToString(Formatting.None));
                using(var stream=new FileStream(markerPending,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(marker,0,marker.Length);stream.Flush(true);}
                boundary?.Invoke("before-commit");cancel.ThrowIfCancellationRequested();File.Move(markerPending,commitPath);committed=true;
                // The commit is authoritative even if a diagnostic/cancellation fires afterwards.
                try{boundary?.Invoke("after-commit");Prune(folder);}catch{/* housekeeping cannot undo a committed save */}
                return revision;
            }
            finally
            {
                if(!committed){DeleteOwned(staged);DeleteOwned(markerPending);DeleteOwned(dataPath);}
            }
        }
    }
    public PNWorldFileRead Read(string key,int maximumBytes=PNWorldSaveEnvelope.MaximumBytes)
    {
        var folder=Folder(key);if(!Directory.Exists(folder))throw new FileNotFoundException("Save key not found.");
        var files=Commits(folder);if(files.Length==0)throw new FileNotFoundException("No committed save revision.");if(files.Length>1024)throw new IOException("Save revision directory exceeds bound.");
        Exception last=null;bool recovered=false;
        foreach(var file in files)
            try{return new PNWorldFileRead(ReadRevision(file,maximumBytes),Revision(file),recovered);}
            catch(Exception error) when(error is IOException||error is InvalidDataException||error is JsonException||error is FormatException||error is ArgumentException||error is OverflowException)
            {last=error;recovered=true;}
        throw new InvalidDataException("No intact committed save revision remains.",last);
    }
    private static PNWorldSaveEnvelope ReadRevision(string markerPath,int maximumBytes=PNWorldSaveEnvelope.MaximumBytes)
    {
        RejectReparse(markerPath);var marker=JObject.Parse(Encoding.UTF8.GetString(ReadBounded(markerPath,4096)));
        long revision=Revision(markerPath);if((long?)marker["revision"]!=revision||(int?)marker["length"]==null)throw new FormatException("Invalid save commit marker.");
        if((int)marker["length"]<1||(int)marker["length"]>PNWorldSaveEnvelope.MaximumBytes)throw new InvalidDataException("Invalid committed save length.");
        if((int)marker["length"]>maximumBytes)throw new InvalidOperationException("Save read exceeds available IO byte budget.");
        var path=Path.ChangeExtension(markerPath,"record");RejectReparse(path);var bytes=ReadBounded(path,PNWorldSaveEnvelope.MaximumBytes);
        if(bytes.Length!=(int)marker["length"]||PNWorldSaveEnvelope.Hash(bytes)!=(string)marker["hash"])throw new InvalidDataException("Save checksum/length mismatch.");
        return new PNWorldSaveEnvelope(new UTF8Encoding(false,true).GetString(bytes));
    }
    private static byte[] ReadBounded(string path,int max)
    {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {if(stream.Length<1||stream.Length>max)throw new InvalidDataException("Save file size exceeds bound.");var data=new byte[(int)stream.Length];int n=0;while(n<data.Length){int read=stream.Read(data,n,data.Length-n);if(read==0)throw new EndOfStreamException();n+=read;}return data;}
    }
    private static string[] Commits(string folder)=>Directory.GetFiles(folder,"*.commit").Where(p=>Path.GetFileNameWithoutExtension(p).Length==20&&long.TryParse(Path.GetFileNameWithoutExtension(p),out _)).OrderByDescending(Revision).ToArray();
    private static long Revision(string file)=>long.Parse(Path.GetFileNameWithoutExtension(file),CultureInfo.InvariantCulture);
    private static void DeleteOwned(string path){if(File.Exists(path)){RejectReparse(path);File.Delete(path);}}
    private static void RecoverUncommitted(string folder)
    {
        // Called only under the root writer lock: no other writer can still own these staging generations.
        foreach(var file in Directory.GetFiles(folder,"*.pending"))
        {string name=Path.GetFileName(file);if(name.Length==35&&long.TryParse(name.Substring(0,20),out _)&&(name.EndsWith(".record.pending",StringComparison.Ordinal)||name.EndsWith(".commit.pending",StringComparison.Ordinal)))DeleteOwned(file);}
        foreach(var file in Directory.GetFiles(folder,"*.record"))if(Path.GetFileNameWithoutExtension(file).Length==20&&long.TryParse(Path.GetFileNameWithoutExtension(file),out _)&&!File.Exists(Path.ChangeExtension(file,"commit")))DeleteOwned(file);
    }
    private static void Prune(string folder)
    {
        int valid=0;
        foreach(var file in Commits(folder))
        {try{ReadRevision(file);if(++valid<=2)continue;}catch{if(valid==0)continue;}DeleteOwned(file);DeleteOwned(Path.ChangeExtension(file,"record"));}
        // Only our exact revision-shaped staging files are eligible for recovery cleanup.
        RecoverUncommitted(folder);
    }
}
