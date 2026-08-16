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

            string nested = Path.Combine(dir, "manager", "profile");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "ErenshorContracts.dll"), "nested stub");
            List<ModPresence> recursive = ModDiscovery.Scan(dir);
            ModPresence nestedContracts = recursive.Find(delegate(ModPresence p) { return p.ModuleId == "contracts"; });
            a += TestAssert.True(nestedContracts.Installed, "recursive Lunaris plugin root discovery finds nested canonical module");

            string config = Path.Combine(dir, "config", "backup");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "ErenshorGuildLife.dll"), "config stub");
            List<ModPresence> configIgnored = ModDiscovery.Scan(dir);
            ModPresence guild = configIgnored.Find(delegate(ModPresence p) { return p.ModuleId == "guildlife"; });
            a += TestAssert.False(guild.Installed, "config subtree is excluded from Lunaris-compatible disk discovery");
            a += TestAssert.True(ModDiscovery.IsInsideConfigDirectory(dir, Path.Combine(config, "ErenshorGuildLife.dll")), "config path classifier is deterministic");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        List<ModPresence> nullScan = ModDiscovery.Scan(null);
        a += TestAssert.Equal(SuiteModuleCatalog.All.Length, nullScan.Count, "null path safe");

        List<ModPresence> diskAbsent = ModDiscovery.Scan(missing);
        HashSet<string> runtimeOnly = new HashSet<string>(StringComparer.Ordinal);
        runtimeOnly.Add("pvp");
        List<ModPresence> effective = ModDiscovery.MergeRuntimeSignals(diskAbsent, runtimeOnly);
        ModPresence runtimePvp = effective.Find(delegate(ModPresence p) { return p.ModuleId == "pvp"; });
        ModPresence runtimeJournal = effective.Find(delegate(ModPresence p) { return p.ModuleId == "journal"; });
        a += TestAssert.True(runtimePvp.Installed, "runtime Aura evidence makes a renamed/nested loaded module visible in normal nav");
        a += TestAssert.False(runtimeJournal.Installed, "runtime merge does not fabricate unrelated module presence");
        return a;
    }
}
