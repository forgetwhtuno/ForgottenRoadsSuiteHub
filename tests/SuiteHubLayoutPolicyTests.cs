using System.Collections.Generic;
using ErenshorSuiteHub;

internal static class SuiteHubLayoutPolicyTests
{
    internal static int RunAll()
    {
        int a = 0;

        List<SuiteSettingDescriptor> journalBasic = new List<SuiteSettingDescriptor>
        {
            BoolSetting("showLauncher", "Show Journal launcher", "true")
        };
        List<string> journalActions = new List<string>
        {
            "openPanel", "closePanel", "resetPanel", "resetLauncher"
        };
        float journalContent = SuiteHubLayoutPolicy.EstimateModuleContentHeight(
            "journal", true, journalActions,
            journalBasic, Empty(), Empty(),
            false, false, false);
        float journalHeight = SuiteHubLayoutPolicy.ResolveWindowHeight(journalContent, 430f, 1080f);
        a += TestAssert.Equal(261f, journalHeight,
            "Journal title/status/basic/panel/advanced flow is explicit and compact");
        a += TestAssert.True(journalHeight < 300f,
            "small Journal page does not reserve the 430px envelope");
        a += TestAssert.Equal(journalContent + SuiteUiMetrics.HeaderHeight + SuiteUiMetrics.OuterPadding * 2f,
            journalHeight, "unclamped small page gives its sequential content exactly one viewport");

        // Deep Sims currently advertises five Basic controls and sixteen Advanced controls.
        // Collapsed Advanced must keep the page at the live compact shape rather than dumping all
        // twenty-one rows into one giant initial scroll.
        List<SuiteSettingDescriptor> deepBasic = new List<SuiteSettingDescriptor>
        {
            ChoiceSetting("perspective"), ChoiceSetting("socialMode"), ChoiceSetting("activity"),
            BoolSetting("autonomousSocial", "Autonomous social chatter", "true"),
            BoolSetting("partyChatResponses", "Reply to party chat", "true")
        };
        List<SuiteSettingDescriptor> deepAdvanced = new List<SuiteSettingDescriptor>();
        for (int i = 0; i < 16; i++)
            deepAdvanced.Add(BoolSetting("advanced" + i.ToString(), "Advanced", "true"));
        float deepCollapsed = SuiteHubLayoutPolicy.EstimateModuleContentHeight(
            "deepsims", true, new List<string> { "refreshStatus" },
            deepBasic, deepAdvanced, Empty(), false, false, false);
        a += TestAssert.Equal(314f,
            SuiteHubLayoutPolicy.ResolveWindowHeight(deepCollapsed, 430f, 1080f),
            "Deep Sims collapsed Advanced page stays compact at known structural flow height");

        float deepExpanded = SuiteHubLayoutPolicy.EstimateModuleContentHeight(
            "deepsims", true, new List<string> { "refreshStatus" },
            deepBasic, deepAdvanced, Empty(), true, false, false);
        a += TestAssert.True(deepExpanded > deepCollapsed,
            "opening Advanced predictably increases structural content height");
        a += TestAssert.Equal(430f,
            SuiteHubLayoutPolicy.ResolveWindowHeight(deepExpanded, 430f, 1080f),
            "expanded large page clamps and scrolls at maximum envelope");

        List<SuiteSettingDescriptor> oneSetting = new List<SuiteSettingDescriptor>
        {
            BoolSetting("enabled", "Enabled", "true")
        };
        float oneSettingContent = SuiteHubLayoutPolicy.EstimateModuleContentHeight(
            "campmaster", true, new List<string>(),
            oneSetting, Empty(), Empty(), false, false, false);
        a += TestAssert.Equal(230f,
            SuiteHubLayoutPolicy.ResolveWindowHeight(oneSettingContent, 430f, 1080f),
            "single-setting page uses minimum usable height");

        // A module with no settings and no panel/action section stays at the compact floor; the
        // layout model does not reserve a phantom PANEL block.
        float statusOnly = SuiteHubLayoutPolicy.EstimateModuleContentHeight(
            "follow", true, new List<string>(), Empty(), Empty(), Empty(),
            false, false, false);
        a += TestAssert.Equal(230f,
            SuiteHubLayoutPolicy.ResolveWindowHeight(statusOnly, 430f, 1080f),
            "status-only module has no empty panel/control region");

        // Developer disclosure is structural only when globally available; opening it adds its
        // version/settings rows predictably.
        List<SuiteSettingDescriptor> dev = new List<SuiteSettingDescriptor>
        {
            BoolSetting("verbose", "Verbose", "false")
        };
        float devCollapsed = SuiteHubLayoutPolicy.EstimateModuleContentHeight(
            "campmaster", true, new List<string>(), oneSetting, Empty(), dev,
            false, false, true);
        float devExpanded = SuiteHubLayoutPolicy.EstimateModuleContentHeight(
            "campmaster", true, new List<string>(), oneSetting, Empty(), dev,
            false, true, true);
        a += TestAssert.True(devExpanded > devCollapsed,
            "Developer expansion predictably changes structural layout");

        return a;
    }

    private static List<SuiteSettingDescriptor> Empty()
    {
        return new List<SuiteSettingDescriptor>();
    }

    private static SuiteSettingDescriptor BoolSetting(string id, string label, string value)
    {
        return new SuiteSettingDescriptor
        {
            Id = id,
            Label = label,
            Tier = SuiteSettingTier.Basic,
            Kind = SuiteSettingKind.Bool,
            Value = value,
            Mutable = true
        };
    }

    private static SuiteSettingDescriptor ChoiceSetting(string id)
    {
        SuiteSettingDescriptor s = new SuiteSettingDescriptor
        {
            Id = id,
            Label = id,
            Tier = SuiteSettingTier.Basic,
            Kind = SuiteSettingKind.Choice,
            Value = "A",
            Mutable = true
        };
        s.Options.Add("A");
        s.Options.Add("B");
        return s;
    }
}
