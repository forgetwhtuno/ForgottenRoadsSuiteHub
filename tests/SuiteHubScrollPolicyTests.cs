using ErenshorSuiteHub;

internal static class SuiteHubScrollPolicyTests
{
    internal static int RunAll()
    {
        int a = 0;

        a += TestAssert.True(SuiteHubScrollPolicy.ShouldResetFor(
            SuiteHubPageChangeReason.ModuleSelection), "module selection resets page scroll");
        a += TestAssert.False(SuiteHubScrollPolicy.ShouldResetFor(
            SuiteHubPageChangeReason.Disclosure), "disclosure rebuild preserves page scroll");
        a += TestAssert.False(SuiteHubScrollPolicy.ShouldResetFor(
            SuiteHubPageChangeReason.Schema), "schema rebuild preserves page scroll");
        a += TestAssert.False(SuiteHubScrollPolicy.ShouldResetFor(
            SuiteHubPageChangeReason.Dynamic), "dynamic refresh never resets page scroll");

        a += TestAssert.Equal(1f,
            SuiteHubScrollPolicy.ResolveAfterStructuralRebuild(0.12f, true),
            "selected module starts at top");
        a += TestAssert.Equal(0.42f,
            SuiteHubScrollPolicy.ResolveAfterStructuralRebuild(0.42f, false),
            "non-selection structural rebuild preserves scroll");
        a += TestAssert.Equal(1f,
            SuiteHubScrollPolicy.ResolveAfterStructuralRebuild(float.NaN, false),
            "invalid retained scroll recovers to top");

        return a;
    }
}
