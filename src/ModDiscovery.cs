using System.Collections.Generic;
using System.IO;

namespace ErenshorSuiteHub
{
    // Pure, Unity-free discovery logic so it is testable without a live game instance. Phase 1
    // discovery is deliberately dumb: it only checks whether a known sibling mod's plugin DLL
    // file exists on disk in the same plugins folder this Hub's own DLL loaded from. No
    // reflection into the DLL, no type loading, no interaction with the other mod, no Aura API,
    // no cross-repo registration. This keeps the Hub 100% safe to load with any subset (including
    // zero) of the other suite mods present, and never requires the Hub for any other mod.
    internal static class ModDiscovery
    {
        // (plugin DLL file name, human-readable display name) for every suite mod this Hub knows
        // how to look for. Source: Erenshor-Mod-Suite/suite.json "dll" field for each mod entry.
        internal static readonly KeyValuePair<string, string>[] KnownMods = new KeyValuePair<string, string>[]
        {
            new KeyValuePair<string, string>("ErenshorDeepSims.dll", "Deep Sims"),
            new KeyValuePair<string, string>("ErenshorPartyTools.dll", "Party Tools"),
            new KeyValuePair<string, string>("ErenshorContracts.dll", "Contracts"),
            new KeyValuePair<string, string>("ErenshorJournal.dll", "Journal"),
            new KeyValuePair<string, string>("ErenshorGuildLife.dll", "Guild Life"),
            new KeyValuePair<string, string>("ErenshorCampmaster.dll", "Campmaster"),
            new KeyValuePair<string, string>("ErenshorNemesis.dll", "Nemesis"),
            new KeyValuePair<string, string>("ErenshorCraftingExpanded.dll", "Crafting Expanded"),
            new KeyValuePair<string, string>("ErenshorDuel.dll", "Practice Duel"),
            new KeyValuePair<string, string>("ErenshorPvP.dll", "PvP"),
            new KeyValuePair<string, string>("ErenshorFollow.dll", "Follow"),
        };

        // Returns one ModPresence per known suite mod, in KnownMods order. Never throws: any I/O
        // failure (missing/unreadable directory, permission error) is treated as "not installed"
        // for every entry rather than surfacing an exception to the caller.
        internal static List<ModPresence> Scan(string pluginsDirectory)
        {
            List<ModPresence> result = new List<ModPresence>(KnownMods.Length);
            for (int i = 0; i < KnownMods.Length; i++)
            {
                string dll = KnownMods[i].Key;
                string displayName = KnownMods[i].Value;
                bool installed = false;
                try
                {
                    installed = !string.IsNullOrEmpty(pluginsDirectory) &&
                        Directory.Exists(pluginsDirectory) &&
                        File.Exists(Path.Combine(pluginsDirectory, dll));
                }
                catch
                {
                    installed = false;
                }
                result.Add(new ModPresence(dll, displayName, installed));
            }
            return result;
        }
    }

    internal struct ModPresence
    {
        internal readonly string DllName;
        internal readonly string DisplayName;
        internal readonly bool Installed;

        internal ModPresence(string dllName, string displayName, bool installed)
        {
            DllName = dllName;
            DisplayName = displayName;
            Installed = installed;
        }
    }
}
