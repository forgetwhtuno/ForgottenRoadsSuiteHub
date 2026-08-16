namespace ErenshorSuiteHub
{
    internal enum SuiteHubPageChangeReason
    {
        ModuleSelection,
        Disclosure,
        Schema,
        Dynamic
    }

    // Pure ScrollRect policy. Module identity changes start a new page at the top. Disclosure and
    // schema rebuilds preserve the current position, and dynamic refreshes do not rebuild/touch the
    // ScrollRect at all.
    internal static class SuiteHubScrollPolicy
    {
        internal static bool ShouldResetFor(SuiteHubPageChangeReason reason)
        {
            return reason == SuiteHubPageChangeReason.ModuleSelection;
        }

        internal static float ResolveAfterStructuralRebuild(float previousNormalizedPosition,
            bool resetToTop)
        {
            if (resetToTop) return 1f;
            if (float.IsNaN(previousNormalizedPosition) || float.IsInfinity(previousNormalizedPosition))
                return 1f;
            if (previousNormalizedPosition < 0f) return 0f;
            if (previousNormalizedPosition > 1f) return 1f;
            return previousNormalizedPosition;
        }
    }
}
