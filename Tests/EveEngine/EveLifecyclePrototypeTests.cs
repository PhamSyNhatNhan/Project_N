using System;
using static EveSessionSmokeTests;

public static class EveLifecyclePrototypeTests
{
    public static void Run()
    {
        string result = EveLifecycleModelChecks.Run();
        Check(result.StartsWith("PASS ", StringComparison.Ordinal), "Lifecycle pure prototype suite");
        Console.WriteLine(result);
        string cleanup = EveLifecycleCleanupChecks.Run();
        Check(cleanup.StartsWith("PASS ", StringComparison.Ordinal), "Lifecycle cleanup scheduler suite");
        Console.WriteLine(cleanup);
    }
}
