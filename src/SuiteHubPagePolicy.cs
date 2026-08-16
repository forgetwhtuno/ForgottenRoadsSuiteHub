using System;
using System.Collections.Generic;

namespace ErenshorSuiteHub
{
    // Pure player-page structure policy shared by retained construction and deterministic
    // measurement. The Hub renders only sections that have real advertised content; a static
    // catalog "has panel" hint never reserves blank player UI.
    internal static class SuiteHubPagePolicy
    {
        internal static bool HasBasicSection(IList<SuiteSettingDescriptor> settings)
        {
            return settings != null && settings.Count > 0;
        }

        internal static bool HasPanelSection(IList<string> actions)
        {
            return HasAction(actions, "openPanel");
        }

        internal static bool HasAdvancedSection(IList<SuiteSettingDescriptor> advancedSettings,
            IList<string> actions)
        {
            return (advancedSettings != null && advancedSettings.Count > 0)
                || HasAction(actions, "resetPanel")
                || HasAction(actions, "resetLauncher");
        }

        internal static bool HasCompactActionSection(string moduleId, IList<string> actions)
        {
            return CountCompactActionRows(moduleId, actions) > 0;
        }

        internal static int CountCompactActionRows(string moduleId, IList<string> actions)
        {
            // These are existing, argument-free Nemesis controls already rendered by the Hub.
            // Do not expose arbitrary action IDs: Aura v1 does not describe argument requirements.
            if (!string.Equals(moduleId, "nemesis", StringComparison.Ordinal)) return 0;
            int count = 0;
            if (HasAction(actions, "clear")) count++;
            if (HasAction(actions, "confirm")) count++;
            if (HasAction(actions, "cancel")) count++;
            return count;
        }

        internal static bool HasAction(IList<string> actions, string actionId)
        {
            if (actions == null || string.IsNullOrEmpty(actionId)) return false;
            for (int i = 0; i < actions.Count; i++)
                if (string.Equals(actions[i], actionId, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
