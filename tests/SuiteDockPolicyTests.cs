using System.Collections.Generic;
using ErenshorSuiteHub;

internal static class SuiteDockPolicyTests
{
    internal static int RunAll()
    {
        int a = 0;
        SuiteModuleDescriptor journal = Descriptor("journal", "Journal", "openPanel", "closePanel");
        SuiteModuleDescriptor duel = Descriptor("duel", "Practice Duel", "challenge", "stop");

        a += TestAssert.True(SuiteDockPolicy.IsAllowedDockAction("openPanel"), "dock allows literal openPanel");
        a += TestAssert.False(SuiteDockPolicy.IsAllowedDockAction("closePanel"), "dock rejects closePanel");
        a += TestAssert.False(SuiteDockPolicy.IsAllowedDockAction("stop"), "dock rejects gameplay action");
        a += TestAssert.True(SuiteDockPolicy.CanLaunchPanel(true, journal, true), "installed registered openPanel is launchable");
        a += TestAssert.False(SuiteDockPolicy.CanLaunchPanel(false, journal, true), "unavailable module is not launchable");
        a += TestAssert.False(SuiteDockPolicy.CanLaunchPanel(true, null, true), "missing provider is not launchable");
        a += TestAssert.False(SuiteDockPolicy.CanLaunchPanel(true, duel, true), "module missing openPanel is not launchable");
        a += TestAssert.False(SuiteDockPolicy.CanLaunchPanel(true, journal, false), "missing action endpoint is not launchable");

        a += TestAssert.True(SuiteDockPolicy.DockCanvasSortingOrder > 540,
            "dock canvas stays above the highest audited sibling retained panel");

        List<SuiteDockModuleState> ownershipStates = new List<SuiteDockModuleState>
        {
            State("journal", "Journal", journal, false, true),
            State("duel", "Practice Duel", duel, false, true)
        };
        a += TestAssert.True(SuiteDockPolicy.CanOwnInstalledDedicatedPanels(ownershipStates),
            "contextual module without a dedicated panel does not block Hub launcher ownership");
        ownershipStates[0].Descriptor = null;
        a += TestAssert.False(SuiteDockPolicy.CanOwnInstalledDedicatedPanels(ownershipStates),
            "provider fault/missing registration prevents Hub launcher ownership");
        ownershipStates[0].Descriptor = journal;
        a += TestAssert.True(SuiteDockPolicy.CanOwnInstalledDedicatedPanels(ownershipStates),
            "late module registration restores truthful Hub launcher ownership");

        HashSet<string> parsed = SuiteDockPolicy.ParseHiddenShortcuts("pvp,journal,pvp,unknown");
        a += TestAssert.True(parsed.Contains("journal") && parsed.Contains("pvp") && parsed.Count == 2,
            "hidden shortcut persistence parses known ids and deduplicates");
        a += TestAssert.Equal("pvp,journal", SuiteDockPolicy.SerializeHiddenShortcuts(parsed),
            "hidden shortcut persistence serializes in stable catalog order");
        a += TestAssert.Equal(string.Empty, SuiteDockPolicy.SerializeHiddenShortcuts(new string[0]),
            "new modules default visible when not explicitly hidden");

        List<SuiteDockModuleState> states = new List<SuiteDockModuleState>
        {
            State("journal", "Journal", journal, false, false),
            State("pvp", "PvP", Descriptor("pvp", "PvP", "openPanel"), false, true),
            State("crafting", "Crafting", Descriptor("crafting", "Crafting", "openPanel"), true, true),
            State("partytools", "Party Tools", Descriptor("partytools", "Party Tools", "openPanel"), false, true)
        };
        List<SuiteDockModuleState> visible = SuiteDockPolicy.OrderedLaunchable(states, false);
        a += TestAssert.Equal(2, visible.Count, "hidden and unavailable shortcuts are not rendered normally");
        a += TestAssert.Equal("partytools", visible[0].ModuleId, "dock uses catalog order instead of registration order");
        a += TestAssert.Equal("pvp", visible[1].ModuleId, "dock ordering remains deterministic");

        List<SuiteDockModuleState> customize = SuiteDockPolicy.OrderedLaunchable(states, true);
        a += TestAssert.Equal(3, customize.Count, "customize includes hidden but still-safe shortcuts");
        a += TestAssert.Equal("crafting", customize[2].ModuleId, "customize keeps hidden module in stable order");

        int normalSignature = SuiteDockPolicy.ComputeStructureSignature(states, false);
        states[1].Descriptor.Status = "dynamic status changed";
        a += TestAssert.Equal(normalSignature, SuiteDockPolicy.ComputeStructureSignature(states, false),
            "dynamic descriptor status does not rebuild dock structure");
        states[1].Descriptor = null;
        a += TestAssert.True(normalSignature != SuiteDockPolicy.ComputeStructureSignature(states, false),
            "provider loss changes dock structure");

        a += TestAssert.False(SuiteDockPolicy.ShouldOpenUpward(700f, 30f, 220f, 1080f),
            "dock opens downward when enough space exists below");
        a += TestAssert.True(SuiteDockPolicy.ShouldOpenUpward(30f, 30f, 220f, 1080f),
            "dock opens upward near bottom edge");
        return a;
    }

    private static SuiteDockModuleState State(string id, string name, SuiteModuleDescriptor descriptor,
        bool hidden, bool installed)
    {
        return new SuiteDockModuleState
        {
            ModuleId = id,
            DisplayName = name,
            Descriptor = descriptor,
            Hidden = hidden,
            Installed = installed,
            ActionEndpointAvailable = true
        };
    }

    private static SuiteModuleDescriptor Descriptor(string id, string name, params string[] actions)
    {
        SuiteModuleDescriptor d = new SuiteModuleDescriptor();
        d.ProtocolVersion = 1;
        d.ModuleId = id;
        d.DisplayName = name;
        d.Version = "1";
        d.Summary = string.Empty;
        for (int i = 0; i < actions.Length; i++) d.Actions.Add(actions[i]);
        return d;
    }
}
