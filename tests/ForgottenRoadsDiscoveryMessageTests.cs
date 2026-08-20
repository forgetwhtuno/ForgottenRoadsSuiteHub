using System;
using System.Collections.Generic;
using ErenshorSuiteHub;

internal static class ForgottenRoadsDiscoveryMessageTests
{
    internal static int RunAll()
    {
        int a = 0;

        // Catalog cross-check tripwire: every catalogued Suite module must have an explicit,
        // verified discovery hint (even if that hint is just "panel"). This forces a future module
        // added to SuiteModuleCatalog to also get a deliberate, source-verified entry here instead
        // of silently being omitted from the hint.
        a += TestAssert.Equal(SuiteModuleCatalog.All.Length, ForgottenRoadsDiscoveryCatalog.All.Length,
            "every catalog module has exactly one discovery hint entry");
        for (int i = 0; i < SuiteModuleCatalog.All.Length; i++)
        {
            string id = SuiteModuleCatalog.All[i].Id;
            a += TestAssert.False(string.IsNullOrEmpty(ForgottenRoadsDiscoveryCatalog.HintFor(id)),
                "catalog module has a non-empty hint: " + id);
        }

        // 7: unknown/unregistered modules do not appear - no entry is built for an id with no
        // verified catalog hint.
        a += TestAssert.Equal((string)null, ForgottenRoadsDiscoveryMessage.BuildEntry("totally-unregistered-module", "Ghost Mod"),
            "unregistered module id produces no entry");

        // 8: Party Tools hint uses /rollparty, never /partyroll.
        string partyToolsEntry = ForgottenRoadsDiscoveryMessage.BuildEntry("partytools", "Party Tools");
        a += TestAssert.True(partyToolsEntry != null && partyToolsEntry.Contains("/rollparty"), "Party Tools hint contains /rollparty");
        a += TestAssert.False(partyToolsEntry != null && partyToolsEntry.Contains("/partyroll"), "Party Tools hint never contains /partyroll");

        // Journal is panel-only, not a slash command.
        string journalEntry = ForgottenRoadsDiscoveryMessage.BuildEntry("journal", "Journal");
        a += TestAssert.True(journalEntry != null && journalEntry.Contains("JOURNAL"), "Journal hint references its launcher, not a command");
        a += TestAssert.False(journalEntry != null && journalEntry.Contains("/"), "Journal hint contains no invented slash command");

        // 10: no debug/private info - none of the shipped hints reference developer-only surfaces,
        // filesystem paths, or diagnostic-only commands.
        for (int i = 0; i < ForgottenRoadsDiscoveryCatalog.All.Length; i++)
        {
            string hint = ForgottenRoadsDiscoveryCatalog.All[i].Hint;
            a += TestAssert.False(hint.Contains("craftdiag"), "no /craftdiag debug command in a hint: " + ForgottenRoadsDiscoveryCatalog.All[i].ModuleId);
            a += TestAssert.False(hint.ToLowerInvariant().Contains("giveherb") || hint.ToLowerInvariant().Contains("givemushroom"),
                "no dev cheat command in a hint: " + ForgottenRoadsDiscoveryCatalog.All[i].ModuleId);
            a += TestAssert.False(hint.Contains(":\\") || hint.Contains("C:") || hint.Contains("Users"), "no filesystem path in a hint");
        }

        // 6 + 12: only positively installed modules appear, and the absence of one module leaves no
        // dangling/empty separator.
        List<ModPresence> partial = new List<ModPresence>();
        partial.Add(new ModPresence(SuiteModuleCatalog.Find("partytools"), true));
        partial.Add(new ModPresence(SuiteModuleCatalog.Find("follow"), false)); // not installed
        partial.Add(new ModPresence(SuiteModuleCatalog.Find("duel"), true));
        partial.Add(new ModPresence(SuiteModuleCatalog.Find("journal"), false)); // not installed
        List<string> partialLines = ForgottenRoadsDiscoveryMessage.Compose(partial);
        a += TestAssert.Equal(1, partialLines.Count, "small install set fits one line");
        string partialLine = partialLines[0];
        a += TestAssert.True(partialLine.Contains("Party Tools"), "installed module present: Party Tools");
        a += TestAssert.True(partialLine.Contains("Practice Duel"), "installed module present: Practice Duel");
        a += TestAssert.False(partialLine.Contains("Follow"), "uninstalled module absent: Follow");
        a += TestAssert.False(partialLine.Contains("Journal"), "uninstalled module absent: Journal");
        a += TestAssert.False(partialLine.Contains("•  •") || partialLine.Contains("•,") || partialLine.Contains(",  ,"),
            "no dangling/doubled separator when a module is skipped");
        a += TestAssert.False(partialLine.Contains("()"), "no empty parenthetical left behind");

        // Discovery text is a plain payload. Color belongs to the native chat/log entry, not to
        // either of the two visible message strings.
        for (int i = 0; i < partialLines.Count; i++)
        {
            a += TestAssert.False(partialLines[i].Contains("<color"), "discovery line has no opening rich-text markup");
            a += TestAssert.False(partialLines[i].Contains("</color>"), "discovery line has no closing rich-text markup");
        }
        a += TestAssert.True(partialLine.StartsWith("[Forgotten Roads] Installed: ", StringComparison.Ordinal),
            "discovery line retains the normal Installed prefix");
        a += TestAssert.True(partialLine.Contains("[Forgotten Roads]") && partialLine.Contains("Party Tools") &&
            partialLine.Contains("Practice Duel"), "discovery line retains normal Forgotten Roads content");

        // Empty/null installed list produces no lines at all (never an empty/near-empty message).
        a += TestAssert.Equal(0, ForgottenRoadsDiscoveryMessage.Compose(null).Count, "null installed list composes no lines");
        a += TestAssert.Equal(0, ForgottenRoadsDiscoveryMessage.Compose(new List<ModPresence>()).Count, "empty installed list composes no lines");

        // 9: message remains bounded - even with every catalogued module installed and hinted, the
        // result is never more than two lines, and no single line explodes unboundedly.
        List<ModPresence> everything = new List<ModPresence>();
        for (int i = 0; i < SuiteModuleCatalog.All.Length; i++)
            everything.Add(new ModPresence(SuiteModuleCatalog.All[i], true));
        List<string> allLines = ForgottenRoadsDiscoveryMessage.Compose(everything);
        a += TestAssert.True(allLines.Count >= 1 && allLines.Count <= 2, "full install set is at most two lines");
        for (int i = 0; i < allLines.Count; i++)
            a += TestAssert.True(allLines[i].Length <= ForgottenRoadsDiscoveryMessage.MaxLineLength + 64,
                "each line stays roughly within the concise-line budget");

        // ComposeLines never exceeds two lines regardless of how many synthetic entries are given,
        // proving the "not twelve lines" bound holds structurally, not just for the current catalog.
        List<string> manyEntries = new List<string>();
        for (int i = 0; i < 25; i++) manyEntries.Add("Module" + i + " (/cmd" + i + ")");
        List<string> manyLines = ForgottenRoadsDiscoveryMessage.ComposeLines(manyEntries);
        a += TestAssert.True(manyLines.Count <= 2, "even 25 synthetic entries stay within two lines");

        // A single entry is always exactly one line, however long its hint text is.
        List<string> oneLongEntry = new List<string>();
        oneLongEntry.Add("Solo Module (/one, /two, /three, /four, /five, /six, /seven, /eight, /nine, /ten)");
        a += TestAssert.Equal(1, ForgottenRoadsDiscoveryMessage.ComposeLines(oneLongEntry).Count, "a single entry never splits");

        return a;
    }
}
