using System;

namespace ErenshorSuiteHub
{
    // Verified primary entry points for the one-time Forgotten Roads discovery chat hint and the
    // on-demand /frhelp command. Every hint below was read directly from that sibling module's OWN
    // current chat-command parser (or confirmed to have none) - never guessed, never copied from
    // README/docs, never carried over from an older version. Re-verify against current source
    // whenever a sibling module's command surface changes; do not "helpfully" add a command here
    // that its parser does not currently accept.
    //
    // Evidence (module source file : what was verified):
    //   partytools  - src/ErenshorPartyToolsPlugin.cs TryMatchCommand calls: /tools, /rollparty
    //                 (before /roll so it can never be misread as the shorter command), /ptwho,
    //                 /ready, /roll. NOTE: the command is "/rollparty", never "/partyroll".
    //   follow      - src/ErenshorFollowPlugin.cs: /expedition and /efollow are both real
    //                 top-level commands (/elead exists too but is left out here to keep the hint
    //                 short; it is not required for the primary follow/expedition workflow).
    //   campmaster  - src/CampmasterPlugin.cs TryMatchCommand calls: /relax, /camp - both
    //                 documented as primary commands in Campmaster's own README.
    //   duel        - src/ErenshorDuelPlugin.cs Handle(): "/eduel" is the sole entry point; its
    //                 subcommands (status/nearby/stop/...) are reached through it, not separately.
    //   pvp         - src/ErenshorPvPPlugin.cs: "/epvp" is the sole entry point.
    //   journal     - no TypeText.CheckCommands patch anywhere in src/. Player access is the
    //                 retained JOURNAL launcher/panel only.
    //   deepsims, crafting, contracts, guildlife, nemesis - no verified PRIMARY player command.
    //     - crafting's only chat command is "/craftdiag" (and subcommands like "giveherb"/
    //       "givemushroom" explicitly commented as development/test-only) - a diagnostic surface,
    //       not a player feature, so it is deliberately excluded from the player-facing hint.
    //     - nemesis and deepsims each patch CheckCommands only to read ordinary chat for
    //       conversational triggers; neither exposes a dedicated slash command of its own.
    //     - contracts and guildlife expose no chat command at all.
    //     All five are dedicated-panel modules (see SuiteModuleCatalog), so their hint is the
    //     generic "panel" pointer rather than an invented command.
    internal static class ForgottenRoadsDiscoveryCatalog
    {
        private static readonly DiscoveryHintEntry[] Entries = new DiscoveryHintEntry[]
        {
            new DiscoveryHintEntry("deepsims", "panel"),
            new DiscoveryHintEntry("partytools", "/tools, /ready, /roll, /rollparty, /ptwho"),
            new DiscoveryHintEntry("follow", "/efollow, /expedition"),
            new DiscoveryHintEntry("campmaster", "/camp, /relax"),
            new DiscoveryHintEntry("duel", "/eduel"),
            new DiscoveryHintEntry("pvp", "/epvp"),
            new DiscoveryHintEntry("nemesis", "panel"),
            new DiscoveryHintEntry("crafting", "panel"),
            new DiscoveryHintEntry("contracts", "panel"),
            new DiscoveryHintEntry("guildlife", "panel"),
            new DiscoveryHintEntry("journal", "JOURNAL"),
        };

        internal static DiscoveryHintEntry[] All { get { return Entries; } }

        internal static string HintFor(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return null;
            for (int i = 0; i < Entries.Length; i++)
                if (string.Equals(Entries[i].ModuleId, moduleId, StringComparison.Ordinal)) return Entries[i].Hint;
            return null;
        }
    }

    internal struct DiscoveryHintEntry
    {
        internal readonly string ModuleId;
        internal readonly string Hint;

        internal DiscoveryHintEntry(string moduleId, string hint)
        {
            ModuleId = moduleId;
            Hint = hint;
        }
    }
}
