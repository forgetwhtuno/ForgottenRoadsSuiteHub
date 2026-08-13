using System;
using System.Collections.Generic;
using System.IO;
using ErenshorSuiteHub;

internal static class ModDiscoveryTests
{
    internal static int RunAll()
    {
        int a = 0;
        string missing = Path.Combine(Path.GetTempPath(), "ErenshorSuiteHubTests_" + Guid.NewGuid().ToString("N"));
        List<ModPresence> absent = ModDiscovery.Scan(missing);
        a += TestAssert.Equal(SuiteModuleCatalog.All.Length, absent.Count, "one presence row per catalog module");
        for (int i = 0; i < absent.Count; i++) a += TestAssert.False(absent[i].Installed, "missing dir reports absent");

        string dir = Path.Combine(Path.GetTempPath(), "ErenshorSuiteHubTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "ErenshorJournal.dll"), "stub");
            File.WriteAllText(Path.Combine(dir, "ErenshorPvP.dll"), "stub");
            List<ModPresence> result = ModDiscovery.Scan(dir);
            List<ModPresence> installed = ModDiscovery.InstalledOnly(result);
            a += TestAssert.Equal(2, installed.Count, "installed-only tab filter");
            a += TestAssert.Equal("pvp", installed[0].ModuleId, "catalog order keeps PvP before Journal in installed nav");
            a += TestAssert.Equal("journal", installed[1].ModuleId, "catalog order keeps Journal last");
            for (int i = 0; i < result.Count; i++)
            {
                a += TestAssert.Equal(SuiteModuleCatalog.All[i].Id, result[i].ModuleId, "catalog/discovery id order");
                a += TestAssert.Equal(SuiteModuleCatalog.All[i].DllName, result[i].DllName, "catalog/discovery dll order");
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        List<ModPresence> nullScan = ModDiscovery.Scan(null);
        a += TestAssert.Equal(SuiteModuleCatalog.All.Length, nullScan.Count, "null path safe");
        return a;
    }
}
