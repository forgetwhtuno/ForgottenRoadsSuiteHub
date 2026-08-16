using System.Collections.Generic;
using ErenshorSuiteHub;

internal static class SuiteHubPagePolicyTests
{
    internal static int RunAll()
    {
        int a = 0;

        List<string> noActions = new List<string>();
        List<string> panelActions = new List<string> { "openPanel", "closePanel" };
        a += TestAssert.False(SuiteHubPagePolicy.HasPanelSection(noActions),
            "module without openPanel does not render PANEL section");
        a += TestAssert.True(SuiteHubPagePolicy.HasPanelSection(panelActions),
            "advertised openPanel generically renders PANEL section");

        List<SuiteSettingDescriptor> empty = new List<SuiteSettingDescriptor>();
        List<SuiteSettingDescriptor> basic = new List<SuiteSettingDescriptor>
        {
            new SuiteSettingDescriptor { Id = "x", Label = "X", Kind = SuiteSettingKind.Bool }
        };
        a += TestAssert.False(SuiteHubPagePolicy.HasBasicSection(empty),
            "empty Basic schema omits BASIC section");
        a += TestAssert.True(SuiteHubPagePolicy.HasBasicSection(basic),
            "Basic schema renders BASIC section");

        a += TestAssert.False(SuiteHubPagePolicy.HasAdvancedSection(empty, noActions),
            "empty Advanced schema without reset actions omits disclosure");
        a += TestAssert.True(SuiteHubPagePolicy.HasAdvancedSection(empty,
            new List<string> { "resetPanel" }), "resetPanel places Advanced disclosure");
        a += TestAssert.True(SuiteHubPagePolicy.HasAdvancedSection(empty,
            new List<string> { "resetLauncher" }), "resetLauncher places Advanced disclosure");

        List<string> nemesisActions = new List<string> { "clear", "confirm", "cancel", "select" };
        a += TestAssert.Equal(3, SuiteHubPagePolicy.CountCompactActionRows("nemesis", nemesisActions),
            "only existing argument-free Nemesis actions render compactly");
        a += TestAssert.Equal(0, SuiteHubPagePolicy.CountCompactActionRows("journal", nemesisActions),
            "module-specific action semantics are not invented for other modules");

        return a;
    }
}
