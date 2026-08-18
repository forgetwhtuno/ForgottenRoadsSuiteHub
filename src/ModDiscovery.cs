using System;
using System.Collections.Generic;
using System.IO;

namespace ErenshorSuiteHub
{
    // File presence answers only "installed on disk". Runtime availability and controls require
    // the optional module-owned Aura bridge and are tracked separately.
    //
    // Lunaris scans <game>/plugins recursively. Mirror that discovery boundary here so a module
    // installed in a nested manager/profile folder is not invisible to the Hub merely because its
    // canonical DLL is not at the plugins root. Config subtrees are intentionally ignored because
    // current Lunaris plugin discovery excludes them as well.
    internal static class ModDiscovery
    {
        internal static List<ModPresence> Scan(string pluginsDirectory)
        {
            List<ModPresence> result = new List<ModPresence>(SuiteModuleCatalog.All.Length);
            for (int i = 0; i < SuiteModuleCatalog.All.Length; i++)
            {
                SuiteModuleDefinition def = SuiteModuleCatalog.All[i];
                bool installed = false;
                try
                {
                    installed = HasDiscoverableCanonicalDll(pluginsDirectory, def.DllName);
                }
                catch { installed = false; }
                result.Add(new ModPresence(def, installed));
            }
            return result;
        }

        internal static List<ModPresence> InstalledOnly(List<ModPresence> source)
        {
            List<ModPresence> result = new List<ModPresence>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++) if (source[i].Installed) result.Add(source[i]);
            return result;
        }

        // Runtime Aura evidence is stronger than a canonical filename. Merge it only as
        // "installed/present" evidence; module identity and player controls still come from the
        // catalog + validated bridge descriptor. This keeps the normal Suite nav truthful when a
        // manager has renamed or nested the assembly while Lunaris has already loaded it.
        internal static List<ModPresence> MergeRuntimeSignals(List<ModPresence> source, ICollection<string> runtimeModuleIds)
        {
            List<ModPresence> result = new List<ModPresence>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                ModPresence presence = source[i];
                bool runtimePresent = runtimeModuleIds != null &&
                    runtimeModuleIds.Contains(presence.ModuleId);
                result.Add(new ModPresence(presence.Definition, presence.Installed || runtimePresent));
            }
            return result;
        }

        private static bool HasDiscoverableCanonicalDll(string pluginsDirectory, string dllName)
        {
            if (string.IsNullOrEmpty(pluginsDirectory) || string.IsNullOrEmpty(dllName) || !Directory.Exists(pluginsDirectory))
                return false;

            string[] matches = Directory.GetFiles(pluginsDirectory, dllName, SearchOption.AllDirectories);
            for (int i = 0; i < matches.Length; i++)
            {
                if (!IsInsideConfigDirectory(pluginsDirectory, matches[i])) return true;
            }
            return false;
        }

        internal static bool IsInsideConfigDirectory(string pluginsDirectory, string candidate)
        {
            if (string.IsNullOrEmpty(pluginsDirectory) || string.IsNullOrEmpty(candidate)) return false;
            try
            {
                string root = Path.GetFullPath(pluginsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string full = Path.GetFullPath(candidate);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
                string relative = full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < segments.Length - 1; i++)
                    if (string.Equals(segments[i], "config", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }
    }

    internal struct ModPresence
    {
        internal readonly SuiteModuleDefinition Definition;
        internal readonly bool Installed;

        internal string ModuleId { get { return Definition == null ? string.Empty : Definition.Id; } }
        internal string DllName { get { return Definition == null ? string.Empty : Definition.DllName; } }
        internal string DisplayName { get { return Definition == null ? string.Empty : Definition.DisplayName; } }

        internal ModPresence(SuiteModuleDefinition definition, bool installed)
        {
            Definition = definition;
            Installed = installed;
        }
    }
}
