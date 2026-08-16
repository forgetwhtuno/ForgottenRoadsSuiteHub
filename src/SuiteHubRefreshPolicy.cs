using System;
using System.Collections.Generic;

namespace ErenshorSuiteHub
{
    // Unity-free structural change detection. Dynamic values are deliberately excluded so a bridge
    // poll can update retained controls in place without destroying/rebuilding page GameObjects.
    internal static class SuiteHubRefreshPolicy
    {
        internal struct OverviewModuleShape
        {
            internal string ModuleId;
            internal bool Connected;
            internal string Version;

            internal OverviewModuleShape(string moduleId, bool connected, string version)
            {
                ModuleId = moduleId ?? string.Empty;
                Connected = connected;
                Version = version ?? string.Empty;
            }
        }

        // Navigation structure is exactly the ordered set of rendered rows. Selection/highlight is
        // dynamic visual state and MUST NOT participate in this signature.
        internal static int ComputeNavStructureSignature(IList<ModPresence> mods)
        {
            unchecked
            {
                int h = 13;
                if (mods == null) return h;
                for (int i = 0; i < mods.Count; i++)
                {
                    if (!mods[i].Installed) continue;
                    h = Mix(h, mods[i].ModuleId);
                }
                return h;
            }
        }

        // Overview rows are simple enough that bridge presence/version changes may rebuild the
        // Overview page. These are infrequent structural/runtime-identity changes, not ordinary
        // status/toggle churn.
        internal static int ComputeOverviewStructureSignature(IList<OverviewModuleShape> modules,
            bool developerEnabled)
        {
            unchecked
            {
                int h = 19;
                if (modules != null)
                {
                    for (int i = 0; i < modules.Count; i++)
                    {
                        h = Mix(h, modules[i].ModuleId);
                        h = h * 31 + (modules[i].Connected ? 1 : 0);
                        h = Mix(h, modules[i].Version);
                    }
                }
                h = h * 31 + (developerEnabled ? 1 : 0);
                return h;
            }
        }

        // Selected-page STRUCTURE only. Intentionally excludes descriptor Status/Warning, all
        // setting Value fields, and action-result text. Those update retained bindings in place.
        internal static int ComputePageStructureSignature(
            string selectedModuleId,
            bool bridgeExists,
            IList<string> actions,
            IList<SuiteSettingDescriptor> basicSettings,
            IList<SuiteSettingDescriptor> advancedSettings,
            IList<SuiteSettingDescriptor> developerSettings,
            bool showAdvanced,
            bool showDeveloper,
            bool developerEnabled)
        {
            unchecked
            {
                int h = 17;
                h = Mix(h, selectedModuleId ?? string.Empty);
                h = h * 31 + (bridgeExists ? 1 : 0);

                if (bridgeExists)
                {
                    h = MixActionSchema(h, actions);
                    h = MixSettingSchema(h, basicSettings);
                    h = MixSettingSchema(h, advancedSettings);
                    h = MixSettingSchema(h, developerSettings);
                }

                h = h * 31 + (showAdvanced ? 1 : 0);
                h = h * 31 + (showDeveloper ? 1 : 0);
                h = h * 31 + (developerEnabled ? 1 : 0);
                return h;
            }
        }

        private static int MixActionSchema(int h, IList<string> actions)
        {
            if (actions == null || actions.Count == 0) return h * 31;

            // Action order has no visual/contract meaning, so normalize it to avoid a spurious
            // rebuild if a provider serializes the same action set in a different order.
            List<string> ordered = new List<string>(actions.Count);
            for (int i = 0; i < actions.Count; i++) ordered.Add(actions[i] ?? string.Empty);
            ordered.Sort(StringComparer.Ordinal);

            h = h * 31 + ordered.Count;
            for (int i = 0; i < ordered.Count; i++) h = Mix(h, ordered[i]);
            return h;
        }

        private static int MixSettingSchema(int h, IList<SuiteSettingDescriptor> settings)
        {
            if (settings == null) return h * 31;
            h = h * 31 + settings.Count;
            for (int i = 0; i < settings.Count; i++)
            {
                SuiteSettingDescriptor s = settings[i];
                if (s == null)
                {
                    h = h * 31;
                    continue;
                }
                h = Mix(h, s.Id);
                h = Mix(h, s.Label);
                h = h * 31 + (int)s.Tier;
                h = h * 31 + (int)s.Kind;
                h = h * 31 + (s.Mutable ? 1 : 0);
                h = h * 31 + s.Options.Count;
                for (int oi = 0; oi < s.Options.Count; oi++) h = Mix(h, s.Options[oi]);
                // s.Value is intentionally excluded.
            }
            return h;
        }

        private static int Mix(int h, string value)
        {
            unchecked
            {
                if (value == null) return h * 31;
                int sh = 23;
                for (int i = 0; i < value.Length; i++) sh = sh * 31 + value[i];
                return h * 31 + sh;
            }
        }
    }
}
