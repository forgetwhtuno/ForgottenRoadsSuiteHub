using ErenshorSuiteHub;

internal static class SuiteSettingMutationPolicyTests
{
    internal static int RunAll()
    {
        int a = 0;

        SuiteSettingMutationRefreshPlan success = SuiteSettingMutationPolicy.Resolve(true);
        a += TestAssert.True(success.PollAuthoritativeState,
            "successful setting mutation immediately re-reads module state");
        a += TestAssert.True(success.RefreshRetainedValues,
            "successful setting mutation immediately refreshes retained values");

        SuiteSettingMutationRefreshPlan rejected = SuiteSettingMutationPolicy.Resolve(false);
        a += TestAssert.False(rejected.PollAuthoritativeState,
            "rejected mutation does not perform unnecessary authoritative poll");
        a += TestAssert.True(rejected.RefreshRetainedValues,
            "rejected mutation still refreshes visible result feedback");

        a += TestAssert.Equal("ok changed",
            SuiteSettingMutationPolicy.VisibleResult(true, "ok changed"),
            "provider success text is preserved for visible feedback");
        a += TestAssert.Equal("Updated",
            SuiteSettingMutationPolicy.VisibleResult(true, string.Empty),
            "empty successful result still produces visible confirmation");
        a += TestAssert.Equal("Setting rejected",
            SuiteSettingMutationPolicy.VisibleResult(false, string.Empty),
            "empty rejected result never leaves a silent click");

        return a;
    }
}
