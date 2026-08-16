using UnityEngine;

namespace ErenshorSuiteHub
{
    // Shared retained-uGUI visual tokens translated directly from Follow's existing SIM ACTIONS
    // menu. Keep this palette compact and Erenshor-like: dark translucent surfaces, thin cyan
    // framing, crisp square geometry, and no glossy/mobile-style rounded cards.
    internal static class SuiteUiTheme
    {
        internal static readonly Color PanelBackground = new Color(0.015f, 0.09f, 0.125f, 0.72f);
        internal static readonly Color PanelBorder = new Color(0.03f, 0.67f, 0.86f, 0.95f);
        internal static readonly Color HeaderBackground = new Color(0.025f, 0.13f, 0.17f, 0.88f);
        internal static readonly Color ControlBackground = new Color(0.035f, 0.17f, 0.22f, 0.78f);
        internal static readonly Color ControlBorder = new Color(0.13f, 0.55f, 0.68f, 0.90f);
        internal static readonly Color ControlHover = new Color(0.12f, 0.38f, 0.48f, 0.90f);
        internal static readonly Color ControlPressed = new Color(0.08f, 0.28f, 0.36f, 0.94f);
        internal static readonly Color SelectedBackground = new Color(0.07f, 0.28f, 0.34f, 0.94f);
        internal static readonly Color TextPrimary = new Color(0.88f, 0.92f, 0.91f, 1.00f);
        internal static readonly Color TextAccent = new Color(0.56f, 0.88f, 1.00f, 1.00f);
        internal static readonly Color TextSecondary = new Color(0.56f, 0.78f, 0.88f, 1.00f);
        internal static readonly Color TextWarning = new Color(1.00f, 0.94f, 0.74f, 1.00f);

        internal const float OuterPadding = SuiteUiMetrics.OuterPadding;
        internal const float SectionGap = SuiteUiMetrics.SectionGap;
        internal const float RowGap = SuiteUiMetrics.RowGap;
        internal const float RowHeight = SuiteUiMetrics.RowHeight;
        internal const float DisclosureRowHeight = SuiteUiMetrics.DisclosureRowHeight;
        internal const float HeaderHeight = SuiteUiMetrics.HeaderHeight;
        internal const float LauncherHeight = SuiteUiMetrics.LauncherHeight;
        internal const float BorderPixels = SuiteUiMetrics.BorderPixels;
    }
}
