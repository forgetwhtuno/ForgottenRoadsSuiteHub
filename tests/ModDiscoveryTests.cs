using System;
using System.Collections.Generic;
using System.IO;
using ErenshorSuiteHub;

// Covers the pure file-presence discovery logic (ModDiscovery.Scan) used by the Overview tab.
// No UnityEngine or game assembly dependency, so this runs outside the live game. Exercises a
// real (temporary) directory rather than mocking File/Directory, since ModDiscovery intentionally
// has no injected filesystem abstraction -- it is a thin, directly testable wrapper over
// File.Exists.
internal static class ModDiscoveryTests
{
    internal static int RunAll()
    {
        int assertions = 0;
        assertions += TestAllAbsentWhenDirectoryDoesNotExist();
        assertions += TestAllAbsentWhenDirectoryEmpty();
        assertions += TestDetectsPresentFiles();
        assertions += TestOrderMatchesKnownMods();
        assertions += TestHandlesNullOrEmptyPathGracefully();
        return assertions;
    }

    private static int TestAllAbsentWhenDirectoryDoesNotExist()
    {
        string missing = Path.Combine(Path.GetTempPath(), "ErenshorSuiteHubTests_" + Guid.NewGuid().ToString("N"));
        List<ModPresence> result = ModDiscovery.Scan(missing);
        Equal(ModDiscovery.KnownMods.Length, result.Count, "scan returns one entry per known mod even when the directory is missing");
        for (int i = 0; i < result.Count; i++)
            False(result[i].Installed, "mod '" + result[i].DllName + "' must be reported absent when the plugins directory does not exist");
        return result.Count + 1;
    }

    private static int TestAllAbsentWhenDirectoryEmpty()
    {
        string dir = CreateTempDirectory();
        try
        {
            List<ModPresence> result = ModDiscovery.Scan(dir);
            for (int i = 0; i < result.Count; i++)
                False(result[i].Installed, "mod '" + result[i].DllName + "' must be reported absent in an empty plugins directory");
            return result.Count;
        }
        finally { DeleteTempDirectory(dir); }
    }

    private static int TestDetectsPresentFiles()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ErenshorJournal.dll"), "stub");
            File.WriteAllText(Path.Combine(dir, "ErenshorPvP.dll"), "stub");

            List<ModPresence> result = ModDiscovery.Scan(dir);
            int installedCount = 0;
            for (int i = 0; i < result.Count; i++)
            {
                bool expected = string.Equals(result[i].DllName, "ErenshorJournal.dll", StringComparison.Ordinal) ||
                                 string.Equals(result[i].DllName, "ErenshorPvP.dll", StringComparison.Ordinal);
                Equal(expected, result[i].Installed, "presence for '" + result[i].DllName + "' must match whether the stub file was created");
                if (result[i].Installed) installedCount++;
            }
            Equal(2, installedCount, "exactly the two stub DLLs must be reported installed");
            return result.Count + 1;
        }
        finally { DeleteTempDirectory(dir); }
    }

    private static int TestOrderMatchesKnownMods()
    {
        string dir = CreateTempDirectory();
        try
        {
            List<ModPresence> result = ModDiscovery.Scan(dir);
            Equal(ModDiscovery.KnownMods.Length, result.Count, "scan result count matches KnownMods count");
            for (int i = 0; i < result.Count; i++)
            {
                Equal(ModDiscovery.KnownMods[i].Key, result[i].DllName, "scan result order must match KnownMods order at index " + i.ToString());
                Equal(ModDiscovery.KnownMods[i].Value, result[i].DisplayName, "display name must match KnownMods at index " + i.ToString());
            }
            return result.Count * 2 + 1;
        }
        finally { DeleteTempDirectory(dir); }
    }

    private static int TestHandlesNullOrEmptyPathGracefully()
    {
        List<ModPresence> nullResult = ModDiscovery.Scan(null);
        Equal(ModDiscovery.KnownMods.Length, nullResult.Count, "null path still returns one entry per known mod");
        for (int i = 0; i < nullResult.Count; i++)
            False(nullResult[i].Installed, "null path must report every mod absent, not throw");

        List<ModPresence> emptyResult = ModDiscovery.Scan(string.Empty);
        for (int i = 0; i < emptyResult.Count; i++)
            False(emptyResult[i].Installed, "empty path must report every mod absent, not throw");

        return nullResult.Count + emptyResult.Count + 2;
    }

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ErenshorSuiteHubTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteTempDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch { }
    }

    private static void Equal(int expected, int actual, string label)
    {
        if (expected != actual)
            throw new Exception(label + " expected=" + expected.ToString() + " actual=" + actual.ToString());
    }

    private static void Equal(bool expected, bool actual, string label)
    {
        if (expected != actual)
            throw new Exception(label + " expected=" + expected.ToString() + " actual=" + actual.ToString());
    }

    private static void Equal(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new Exception(label + " expected=" + expected + " actual=" + actual);
    }

    private static void False(bool value, string label)
    {
        if (value) throw new Exception(label);
    }
}
