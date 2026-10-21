using System;

// EN: `--pn-allow-forbidden`: lets Forbidden characters be played for testing. Honoured only in the Editor and Development builds; a release
//     build ignores the flag. The host evaluates it too, so a room with Forbidden characters needs the host to have it on.
// VI: Cờ --pn-allow-forbidden cho phép chơi nhân vật Forbidden để thử; chỉ có tác dụng ở Editor / Development build, host cũng phải bật.
public static class PNForbiddenSwitch
{
    public const string Flag = "--pn-allow-forbidden";
    private static bool? value;
    public static bool Allowed
    {
        get
        {
            if (value.HasValue) return value.Value;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            value = Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0;
#else
            value = false;
#endif
            return value.Value;
        }
    }
    // Tests and tools set the value directly; null re-reads the command line.
    public static void Override(bool? forced) { value = forced; }
}
