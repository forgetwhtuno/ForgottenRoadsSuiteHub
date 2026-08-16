using ErenshorSuiteHub;

internal static class SuiteSettingDisplayPolicyTests
{
    internal static int RunAll()
    {
        int a = 0;
        a += TestAssert.Equal("ON", SuiteSettingDisplayPolicy.BooleanText(true), "true bool displays ON");
        a += TestAssert.Equal("OFF", SuiteSettingDisplayPolicy.BooleanText(false), "false bool displays OFF");
        a += TestAssert.Equal("ON", SuiteSettingDisplayPolicy.BooleanText("TRUE"), "wire true displays ON");
        a += TestAssert.Equal("OFF", SuiteSettingDisplayPolicy.BooleanText("false"), "wire false displays OFF");
        a += TestAssert.Equal("Deep Sims [ON]",
            SuiteSettingDisplayPolicy.BooleanButtonText("Deep Sims", true),
            "true bool state is contained in clickable control text");
        a += TestAssert.Equal("Show Journal Launcher [OFF]",
            SuiteSettingDisplayPolicy.BooleanButtonText("Show Journal Launcher", false),
            "false bool state is contained in clickable control text");
        a += TestAssert.Equal("Nemesis Enabled [ON]",
            SuiteSettingDisplayPolicy.BooleanButtonText("Nemesis Enabled", "true"),
            "wire bool mutation maps immediately to PvP-style button text");
        return a;
    }
}
