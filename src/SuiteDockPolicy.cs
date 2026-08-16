using System;
using System.Collections.Generic;
using System.Text;

namespace ErenshorSuiteHub
{
    internal sealed class SuiteDockModuleState
    {
        internal string ModuleId;
        internal string DisplayName;
        internal bool Installed;
        internal SuiteModuleDescriptor Descriptor;
        internal bool ActionEndpointAvailable;
        internal bool Hidden;

        internal bool CanLaunch
        {
            get { return SuiteDockPolicy.CanLaunchPanel(Installed, Descriptor, ActionEndpointAvailable); }
        }
    }

    // Unity-free policy for the compact MODS dock. The dock has exactly one authority: open a
    // verified player-facing panel through the literal openPanel contract. No other module action
    // is reachable from this surface.
    internal static class SuiteDockPolicy
    {
        internal const string OpenPanelActionId = "openPanel";
        internal const int DockCanvasSortingOrder = 600;

        internal static bool IsAllowedDockAction(string actionId)
        {
            return string.Equals(actionId, OpenPanelActionId, StringComparison.Ordinal);
        }

        internal static bool CanLaunchPanel(bool installed, SuiteModuleDescriptor descriptor,
            bool actionEndpointAvailable)
        {
            return installed && descriptor != null && descriptor.HasAction(OpenPanelActionId) && actionEndpointAvailable;
        }

        // The Hub may claim launcher ownership only when every installed catalogued module that is
        // known to require a dedicated player-facing panel has a currently safe dock launch path.
        // Non-panel/contextual modules never block ownership because there is no launcher to replace.
        internal static bool CanOwnInstalledDedicatedPanels(IList<SuiteDockModuleState> states)
        {
            if (states == null) return true;
            for (int si = 0; si < states.Count; si++)
            {
                SuiteDockModuleState state = states[si];
                if (state == null || !state.Installed) continue;
                SuiteModuleDefinition definition = SuiteModuleCatalog.Find(state.ModuleId);
                if (definition == null || !definition.HasDedicatedPanel) continue;
                if (!state.CanLaunch) return false;
            }
            return true;
        }

        internal static HashSet<string> ParseHiddenShortcuts(string raw)
        {
            HashSet<string> hidden = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw)) return hidden;
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string id = (parts[i] ?? string.Empty).Trim();
                if (id.Length == 0 || SuiteModuleCatalog.Find(id) == null) continue;
                hidden.Add(id);
            }
            return hidden;
        }

        // Stable catalog-order serialization avoids config churn and makes persistence deterministic.
        internal static string SerializeHiddenShortcuts(IEnumerable<string> hiddenIds)
        {
            HashSet<string> hidden = new HashSet<string>(StringComparer.Ordinal);
            if (hiddenIds != null)
            {
                foreach (string id in hiddenIds)
                    if (!string.IsNullOrEmpty(id) && SuiteModuleCatalog.Find(id) != null) hidden.Add(id);
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < SuiteModuleCatalog.All.Length; i++)
            {
                string id = SuiteModuleCatalog.All[i].Id;
                if (!hidden.Contains(id)) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(id);
            }
            return sb.ToString();
        }

        internal static List<SuiteDockModuleState> OrderedLaunchable(IList<SuiteDockModuleState> states,
            bool includeHidden)
        {
            List<SuiteDockModuleState> result = new List<SuiteDockModuleState>();
            if (states == null) return result;

            for (int ci = 0; ci < SuiteModuleCatalog.All.Length; ci++)
            {
                string wanted = SuiteModuleCatalog.All[ci].Id;
                for (int si = 0; si < states.Count; si++)
                {
                    SuiteDockModuleState state = states[si];
                    if (state == null || !string.Equals(state.ModuleId, wanted, StringComparison.Ordinal)) continue;
                    if (state.CanLaunch && (includeHidden || !state.Hidden)) result.Add(state);
                    break;
                }
            }
            return result;
        }

        // Only shortcut structure participates: availability, safe openPanel capability, display name,
        // and visibility preference. Module status/ui.state values are deliberately excluded.
        internal static int ComputeStructureSignature(IList<SuiteDockModuleState> states, bool customize)
        {
            unchecked
            {
                int h = customize ? 43 : 41;
                List<SuiteDockModuleState> ordered = OrderedLaunchable(states, customize);
                for (int i = 0; i < ordered.Count; i++)
                {
                    SuiteDockModuleState state = ordered[i];
                    h = Mix(h, state.ModuleId);
                    h = Mix(h, state.DisplayName);
                    h = h * 31 + (state.Hidden ? 1 : 0);
                }
                return h;
            }
        }

        internal static bool ShouldOpenUpward(float launcherY, float launcherHeight, float menuHeight,
            float screenHeight)
        {
            if (!Finite(launcherY)) launcherY = 0f;
            if (!Finite(launcherHeight) || launcherHeight < 0f) launcherHeight = 0f;
            if (!Finite(menuHeight) || menuHeight < 0f) menuHeight = 0f;
            if (!Finite(screenHeight) || screenHeight < 0f) screenHeight = 0f;
            float below = Math.Max(0f, launcherY);
            float above = Math.Max(0f, screenHeight - launcherY - launcherHeight);
            if (below >= menuHeight) return false;
            if (above >= menuHeight) return true;
            return above > below;
        }

        private static int Mix(int h, string value)
        {
            unchecked
            {
                if (value == null) return h * 31;
                int sh = 29;
                for (int i = 0; i < value.Length; i++) sh = sh * 31 + value[i];
                return h * 31 + sh;
            }
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
