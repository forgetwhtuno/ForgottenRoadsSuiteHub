using ErenshorSuiteHub;

internal static class SuiteDockInteractionStateTests
{
    public static int RunAll()
    {
        int a = 0;
        SuiteDockInteractionState state = new SuiteDockInteractionState();
        a += TestAssert.False(state.IsExpanded, "dock starts collapsed");
        state.Toggle();
        a += TestAssert.True(state.IsExpanded, "collapsed -> expanded");
        state.Toggle();
        a += TestAssert.False(state.IsExpanded, "expanded -> collapsed");

        for (int i = 0; i < 8; i++) { state.Toggle(); state.Toggle(); }
        a += TestAssert.False(state.IsExpanded, "repeated expand/collapse cycles remain collapsed");

        state.ShowCustomize();
        a += TestAssert.True(state.IsExpanded && state.IsCustomizing, "customize opens inside expanded dock");
        state.DoneCustomize();
        a += TestAssert.True(state.IsExpanded && !state.IsCustomizing, "done returns to launcher rows");

        state.Expand(false);
        a += TestAssert.False(state.CompleteLaunch(false), "failed panel launch does not auto-collapse");
        a += TestAssert.True(state.IsExpanded, "failed panel launch remains expanded for feedback");
        a += TestAssert.True(state.CompleteLaunch(true), "successful panel launch completes");
        a += TestAssert.False(state.IsExpanded, "successful panel launch auto-collapses");
        return a;
    }
}
