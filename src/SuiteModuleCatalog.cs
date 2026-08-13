namespace ErenshorSuiteHub
{
    internal sealed class SuiteModuleDefinition
    {
        internal readonly string Id;
        internal readonly string DisplayName;
        internal readonly string DllName;
        internal readonly string Summary;
        internal readonly bool HasDedicatedPanel;
        internal readonly string FallbackInterface;

        internal SuiteModuleDefinition(string id, string displayName, string dllName, string summary,
            bool hasDedicatedPanel, string fallbackInterface)
        {
            Id = id;
            DisplayName = displayName;
            DllName = dllName;
            Summary = summary;
            HasDedicatedPanel = hasDedicatedPanel;
            FallbackInterface = fallbackInterface;
        }
    }

    internal static class SuiteModuleCatalog
    {
        // Player-facing order. This is metadata only; the Hub never assumes that a DLL's presence
        // grants a callable control API.
        internal static readonly SuiteModuleDefinition[] All = new SuiteModuleDefinition[]
        {
            new SuiteModuleDefinition("deepsims", "Deep Sims", "ErenshorDeepSims.dll", "Grounded Sim social dialogue and memory.", false, "/dsims, /aistatus"),
            new SuiteModuleDefinition("partytools", "Party Tools", "ErenshorPartyTools.dll", "Ready checks and party rolls.", true, "/tools, /ready, /roll"),
            new SuiteModuleDefinition("follow", "Follow", "ErenshorFollow.dll", "Follow, lead, and expedition assistance.", false, "/efollow, /elead, /expedition"),
            new SuiteModuleDefinition("campmaster", "Campmaster", "ErenshorCampmaster.dll", "Camp and relaxation context tools.", false, "/camp, /relax"),
            new SuiteModuleDefinition("duel", "Practice Duel", "ErenshorDuel.dll", "Friendly non-lethal practice duels.", false, "/eduel"),
            new SuiteModuleDefinition("pvp", "PvP", "ErenshorPvP.dll", "Arranged and ambient PvP encounters.", true, "/epvp"),
            new SuiteModuleDefinition("nemesis", "Nemesis", "ErenshorNemesis.dll", "Persistent rivalry encounters.", false, "/enemesis"),
            new SuiteModuleDefinition("crafting", "Crafting", "ErenshorCraftingExpanded.dll", "Expanded crafting and foraging systems.", true, "/craftdiag"),
            new SuiteModuleDefinition("contracts", "Contracts", "ErenshorContracts.dll", "Contract board and objective tracking.", true, "Dedicated contract board"),
            new SuiteModuleDefinition("guildlife", "Guild Life", "ErenshorGuildLife.dll", "Guild roster and bulletin-life features.", true, "Dedicated Guild Life panel"),
            new SuiteModuleDefinition("journal", "Journal", "ErenshorJournal.dll", "Player notes, tabs, and chronicle entries.", true, "Dedicated Journal panel")
        };

        internal static SuiteModuleDefinition Find(string id)
        {
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Id, id, System.StringComparison.Ordinal)) return All[i];
            return null;
        }
    }
}
