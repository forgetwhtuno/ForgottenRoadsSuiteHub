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
        // Guards against junction/symlink cycles; real plugin layouts are a few levels deep.
        private const int MaxDiscoveryDepth = 32;

        internal static List<ModPresence> Scan(string pluginsDirectory)
        {
            HashSet<string> discoverable = CollectDiscoverableDllNames(pluginsDirectory);
            List<ModPresence> result = new List<ModPresence>(SuiteModuleCatalog.All.Length);
            for (int i = 0; i < SuiteModuleCatalog.All.Length; i++)
            {
                SuiteModuleDefinition def = SuiteModuleCatalog.All[i];
                bool installed = !string.IsNullOrEmpty(def.DllName) && discoverable.Contains(def.DllName);
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

        // Scan runs on the main thread every couple of seconds during gameplay, so walk the tree
        // once per scan rather than once per catalogued module, and prune config subtrees instead
        // of walking and then filtering them: they hold per-mod runtime data (Deep Sims memory alone
        // can be thousands of files) and Lunaris does not load plugins from them anyway. An
        // unreadable directory is skipped rather than making every module look uninstalled.
        private static HashSet<string> CollectDiscoverableDllNames(string pluginsDirectory)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(pluginsDirectory)) return names;
            try { if (!Directory.Exists(pluginsDirectory)) return names; }
            catch { return names; }

            Stack<KeyValuePair<string, int>> pending = new Stack<KeyValuePair<string, int>>();
            pending.Push(new KeyValuePair<string, int>(pluginsDirectory, 0));
            while (pending.Count > 0)
            {
                KeyValuePair<string, int> current = pending.Pop();
                try
                {
                    string[] files = Directory.GetFiles(current.Key, "*.dll");
                    for (int i = 0; i < files.Length; i++) names.Add(Path.GetFileName(files[i]));
                }
                catch { }

                if (current.Value >= MaxDiscoveryDepth) continue;
                string[] children;
                try { children = Directory.GetDirectories(current.Key); }
                catch { continue; }
                for (int i = 0; i < children.Length; i++)
                {
                    if (string.Equals(Path.GetFileName(children[i]), "config", StringComparison.OrdinalIgnoreCase)) continue;
                    pending.Push(new KeyValuePair<string, int>(children[i], current.Value + 1));
                }
            }
            return names;
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
