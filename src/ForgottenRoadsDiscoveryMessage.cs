using System.Collections.Generic;

namespace ErenshorSuiteHub
{
    // Pure, Unity-free composer for the Forgotten Roads discovery hint / "/frhelp" output. Turns a
    // list of currently (positively) installed modules into at most two short, readable chat lines.
    // Never invents a command: any module without a verified ForgottenRoadsDiscoveryCatalog entry is
    // silently skipped rather than shown with a guessed hint.
    internal static class ForgottenRoadsDiscoveryMessage
    {
        // Soft budget for a single chat line. Not a hard MMO chat limit - just the point past which
        // one line stops being "concise" and the message should split into two lines instead.
        internal const int MaxLineLength = 170;

        // Shared identity marker for every Hub-authored social-log line. ForgottenRoadsChatStyle
        // uses it to avoid learning a native style from our own output.
        internal const string Tag = "[Forgotten Roads] ";

        private const string Prefix = Tag + "Installed: ";
        private const string ContinuationPrefix = Tag;
        private const string Separator = " • ";

        // "DisplayName (hint)", or null if this module has no verified discovery hint yet. Kept
        // separate from Compose() so tests can exercise the per-module decision directly.
        internal static string BuildEntry(string moduleId, string displayName)
        {
            string hint = ForgottenRoadsDiscoveryCatalog.HintFor(moduleId);
            if (string.IsNullOrEmpty(hint)) return null;
            string name = string.IsNullOrEmpty(displayName) ? moduleId : displayName;
            return name + " (" + hint + ")";
        }

        // installedMods should already reflect BOTH disk discovery and live Aura/runtime evidence
        // (see ErenshorSuiteHubPlugin.GetEffectiveModPresence) - i.e. only modules Hub can actually
        // prove are present, in the fixed catalog order so output is deterministic.
        internal static List<string> Compose(List<ModPresence> installedMods)
        {
            List<string> entries = new List<string>();
            if (installedMods != null)
            {
                for (int i = 0; i < installedMods.Count; i++)
                {
                    ModPresence mod = installedMods[i];
                    if (!mod.Installed) continue;
                    string entry = BuildEntry(mod.ModuleId, mod.DisplayName);
                    if (entry != null) entries.Add(entry);
                }
            }
            return ComposeLines(entries);
        }

        // Joins already-built "Name (hint)" entries into at most two chat lines. A single entry, or
        // any set short enough to fit MaxLineLength, becomes exactly one line; otherwise the entries
        // are split roughly in half across exactly two lines - never more, regardless of how many
        // modules are installed.
        internal static List<string> ComposeLines(List<string> entries)
        {
            List<string> lines = new List<string>();
            if (entries == null || entries.Count == 0) return lines;

            string single = Prefix + string.Join(Separator, entries.ToArray());
            if (entries.Count == 1 || single.Length <= MaxLineLength)
            {
                lines.Add(single);
                return lines;
            }

            int firstCount = (entries.Count + 1) / 2;
            List<string> first = entries.GetRange(0, firstCount);
            List<string> second = entries.GetRange(firstCount, entries.Count - firstCount);
            lines.Add(Prefix + string.Join(Separator, first.ToArray()));
            lines.Add(ContinuationPrefix + string.Join(Separator, second.ToArray()));
            return lines;
        }
    }
}
