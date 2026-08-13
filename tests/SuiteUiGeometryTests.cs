using ErenshorSuiteHub;

internal static class SuiteUiGeometryTests
{
    internal static int RunAll()
    {
        int a = 0;
        a += TestAssert.True(SuiteUiGeometry.LauncherRegionsDoNotOverlap(112f), "launcher grip and button do not overlap");

        SuiteRect launcher = SuiteUiGeometry.ClampLauncher(new SuiteRect(-99f, 9999f, 1f, 1f), 1920f, 1080f, 112f);
        a += TestAssert.Equal(0f, launcher.X, "launcher left recovery");
        a += TestAssert.Equal(1050f, launcher.Y, "launcher bottom recovery");
        a += TestAssert.Equal(112f, launcher.Width, "launcher fixed width");
        a += TestAssert.Equal(30f, launcher.Height, "launcher fixed height");

        SuiteRect window = SuiteUiGeometry.ClampWindow(new SuiteRect(float.NaN, -100f, 99999f, float.NaN), 1280f, 720f);
        a += TestAssert.Equal(0f, window.X, "NaN x recovery");
        a += TestAssert.Equal(0f, window.Y, "negative y recovery");
        a += TestAssert.Equal(1260f, window.Width, "oversize width clamped");
        a += TestAssert.Equal(430f, window.Height, "NaN height recovers to default");

        a += RunNormalizedPositionTests();
        return a;
    }

    // --- retained-uGUI normalized position persistence -----------------------------------------
    private static int RunNormalizedPositionTests()
    {
        int a = 0;

        // Unset / invalid stored values are reported as Unset so callers apply default placement.
        a += TestAssert.Equal(SuiteUiGeometry.Unset, SuiteUiGeometry.InterpretStoredAxis(-1f, 1920f), "unset axis stays unset");
        a += TestAssert.Equal(SuiteUiGeometry.Unset, SuiteUiGeometry.InterpretStoredAxis(float.NaN, 1920f), "NaN stored axis rejected");
        a += TestAssert.Equal(SuiteUiGeometry.Unset, SuiteUiGeometry.InterpretStoredAxis(float.PositiveInfinity, 1920f), "infinite stored axis rejected");

        // Already-normalized values pass through untouched.
        a += TestAssert.Equal(0.25f, SuiteUiGeometry.InterpretStoredAxis(0.25f, 1920f), "normalized axis passes through");
        a += TestAssert.Equal(1f, SuiteUiGeometry.InterpretStoredAxis(1f, 1920f), "normalized upper bound passes through");

        // Legacy absolute pixels from the OnGUI Hub are NOT migrated: that layout used a top-left
        // origin with Y increasing downward, so rescaling would mirror the panel vertically.
        // Reverting to default placement once is the honest behaviour.
        a += TestAssert.Equal(SuiteUiGeometry.Unset, SuiteUiGeometry.InterpretStoredAxis(960f, 1920f), "legacy pixel axis is not migrated");
        a += TestAssert.Equal(SuiteUiGeometry.Unset, SuiteUiGeometry.InterpretStoredAxis(5000f, 1920f), "legacy oversize pixel axis is not migrated");

        // Normalizing is inverse of resolving for an in-bounds value.
        a += TestAssert.Equal(0.5f, SuiteUiGeometry.NormalizeAxis(960f, 1920f), "normalize midpoint");
        a += TestAssert.Equal(0f, SuiteUiGeometry.NormalizeAxis(float.NaN, 1920f), "normalize rejects NaN");
        a += TestAssert.Equal(0f, SuiteUiGeometry.NormalizeAxis(100f, 0f), "normalize rejects zero screen extent");

        // Resolving clamps so the panel stays FULLY on screen (this is the off-screen recovery).
        a += TestAssert.Equal(960f, SuiteUiGeometry.ResolveAxis(0.5f, 1920f, 100f), "resolve midpoint");
        a += TestAssert.Equal(1820f, SuiteUiGeometry.ResolveAxis(1f, 1920f, 100f), "resolve clamps right edge on screen");
        a += TestAssert.Equal(0f, SuiteUiGeometry.ResolveAxis(-5f, 1920f, 100f), "resolve clamps negative to left edge");
        a += TestAssert.Equal(0f, SuiteUiGeometry.ResolveAxis(float.NaN, 1920f, 100f), "resolve rejects NaN");
        a += TestAssert.Equal(0f, SuiteUiGeometry.ResolveAxis(0.5f, 1920f, 99999f), "oversized panel pinned to origin");

        // A saved layout survives a resolution change: same normalized value, new screen, still on screen.
        SuiteRect small = SuiteUiGeometry.ResolvePanel(0.98f, 0.98f, 620f, 430f, 1280f, 720f);
        a += TestAssert.Equal(660f, small.X, "resolution change keeps window on screen horizontally");
        a += TestAssert.Equal(290f, small.Y, "resolution change keeps window on screen vertically");

        SuiteRect big = SuiteUiGeometry.ResolvePanel(0.5f, 0.5f, 620f, 430f, 1920f, 1080f);
        a += TestAssert.Equal(960f, big.X, "panel resolves at midpoint on large screen");
        a += TestAssert.Equal(540f, big.Y, "panel resolves at midpoint vertically");

        // Recovery detection.
        a += TestAssert.True(SuiteUiGeometry.NeedsRecovery(float.NaN, 0f, 100f, 100f, 1920f, 1080f), "NaN needs recovery");
        a += TestAssert.True(SuiteUiGeometry.NeedsRecovery(-1f, 0f, 100f, 100f, 1920f, 1080f), "negative x needs recovery");
        a += TestAssert.True(SuiteUiGeometry.NeedsRecovery(1900f, 0f, 100f, 100f, 1920f, 1080f), "overflow right needs recovery");
        a += TestAssert.True(SuiteUiGeometry.NeedsRecovery(0f, 1000f, 100f, 100f, 1920f, 1080f), "overflow top needs recovery");
        a += TestAssert.True(!SuiteUiGeometry.NeedsRecovery(10f, 10f, 100f, 100f, 1920f, 1080f), "in-bounds needs no recovery");

        return a;
    }
}
