namespace ErenshorSuiteHub
{
    // Unity-free shared dimensions for both retained-uGUI construction and deterministic page
    // measurement. Keeping structural row heights here prevents the compact-window estimator from
    // drifting away from the controls it is estimating.
    internal static class SuiteUiMetrics
    {
        internal const float OuterPadding = 10f;
        internal const float ContentPadding = 2f;
        internal const float SectionGap = 6f;
        internal const float RowGap = 3f;
        internal const float TextRowHeight = 16f;
        internal const float RowHeight = 24f;
        internal const float DisclosureRowHeight = 22f;
        internal const float HeaderHeight = 30f;
        internal const float LauncherHeight = 32f;
        internal const float BorderPixels = 1f;
    }
}
