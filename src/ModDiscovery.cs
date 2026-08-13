using System.Collections.Generic;
using System.IO;

namespace ErenshorSuiteHub
{
    // File presence answers only "installed on disk". Runtime availability and controls require
    // the optional module-owned Aura bridge and are tracked separately.
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
                    installed = !string.IsNullOrEmpty(pluginsDirectory) && Directory.Exists(pluginsDirectory) &&
                        File.Exists(Path.Combine(pluginsDirectory, def.DllName));
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
