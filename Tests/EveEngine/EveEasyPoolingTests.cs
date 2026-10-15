using System;
using EveEngine;
using UnityEngine;

public static class EveEasyPoolingTests
{
    public static void Run()
    {
        var prefab = new GameObject("bullet");
        Reject(() => new EveEasyPooling(null), "pool rejects null prefab");
        Reject(() => new EveEasyPooling(prefab, -1), "pool validates capacity");
        Reject(() => new EveEasyPooling(prefab, maxSize: 0), "pool validates idle limit");
        var pool = new EveEasyPooling(prefab);
        int created = 0, destroyed = 0, released = 0;
        pool.Created += _ => created++;
        pool.Destroyed += _ => destroyed++;
        pool.Released += _ => released++;
        var first = pool.Get(); var second = pool.Get();
        Check(first != second && pool.CountActive == 2 && pool.CountInactive == 0, "inactive objects reserved immediately");
        Check(!first.activeInHierarchy, "Get returns inactive for caller setup");
        first.SetActive(true); first.SetActive(false);
        Check(pool.CountActive == 2, "disable is independent of Release");
        Check(pool.Release(first) && !pool.Release(first) && !pool.Release(null), "release once, duplicate/null safe");
        Check(pool.CountInactive == 1 && released == 1, "release adds to idle list");
        Check(pool.Get() == first && created == 2, "Get reuses instance without creation");
        using (var other = new EveEasyPooling(prefab))
            Check(!other.Release(first) && pool.CountActive == 2, "foreign pool release rejected");
        pool.ReleaseAll();
        Check(pool.CountInactive == 2 && pool.CountActive == 0, "ReleaseAll retains objects");
        pool.Get(); pool.Clear();
        Check(pool.CountAll == 0 && destroyed == 2 && released == 3, "Clear destroys idle and active without Release callbacks");
        var next = pool.Get(); pool.Dispose(); pool.Dispose();
        Check(pool.IsDisposed && pool.CountAll == 0 && destroyed == 3 && !pool.Release(next), "Dispose destroys once");
        Reject(() => pool.Get(), "disposed pool rejects Get");
        using (var limited = new EveEasyPooling(prefab, maxSize: 2))
        {
            for (int i = 0; i < 10; i++) limited.Get();
            Check(limited.CountActive == 10, "maxSize never caps active bullets");
            limited.ReleaseAll();
            Check(limited.CountAll == 2 && limited.CountInactive == 2, "maxSize limits idle retention");
        }
        using (var burst = new EveEasyPooling(prefab))
        {
            int totalCreates = 0;
            burst.Created += _ => totalCreates++;
            for (int cycle = 0; cycle < 4; cycle++)
            {
                for (int i = 0; i < 800; i++) burst.Get();
                burst.ReleaseAll();
            }
            Check(totalCreates == 800 && burst.CountInactive == 800, "bullet hell stabilizes at observed peak");
        }
        using (var events = new EveEasyPooling(prefab))
        {
            int seen = 0;
            events.Got += _ => throw new Exception("observer failure");
            events.Got += _ => { seen++; events.Get(); };
            events.Got += _ => seen++;
            Check(events.Get() != null && seen == 2 && events.CountActive == 1,
                "observer isolation and recursive mutation guard");
        }
    }
    private static void Check(bool value, string message) => EveSessionSmokeTests.Check(value, message);
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception) { Check(true, message); return; }
        Check(false, message);
    }
}
