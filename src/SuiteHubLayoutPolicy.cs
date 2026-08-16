using System.Collections.Generic;

namespace ErenshorSuiteHub
{
    // Explicit structural content-height model for the Hub. It mirrors the exact sequential rows
    // BuildOverview/BuildModulePage construct. Dynamic status/warning/action-result text and
    // current setting VALUES do not resize the outer window; the ScrollRect handles any wrapped
    // overflow without making the panel breathe.
    internal static class SuiteHubLayoutPolicy
    {
        private sealed class Measure
        {
            private int _children;
            private float _height;

            internal void Add(float height)
            {
                if (height <= 0f) return;
                if (_children > 0) _height += SuiteUiMetrics.RowGap;
                _height += height;
                _children++;
            }

            internal float ContentHeight
            {
                get { return _height + SuiteUiMetrics.ContentPadding * 2f; }
            }
        }

        internal static float EstimateOverviewContentHeight(int installedModuleCount, bool developerEnabled)
        {
            if (installedModuleCount < 0) installedModuleCount = 0;
            Measure m = new Measure();
            m.Add(SuiteUiMetrics.TextRowHeight); // OVERVIEW
            m.Add(SuiteUiMetrics.TextRowHeight); // version
            m.Add(SuiteUiMetrics.TextRowHeight); // installed/connected count
            AddSectionBreak(m);
            m.Add(SuiteUiMetrics.TextRowHeight); // INSTALLED
            if (installedModuleCount == 0) m.Add(SuiteUiMetrics.TextRowHeight);
            for (int i = 0; i < installedModuleCount; i++) m.Add(SuiteUiMetrics.TextRowHeight);
            AddSectionBreak(m);
            m.Add(SuiteUiMetrics.TextRowHeight); // HOW THIS WORKS
            m.Add(SuiteUiMetrics.TextRowHeight * 2f); // explanatory copy
            if (developerEnabled)
            {
                AddSectionBreak(m);
                m.Add(SuiteUiMetrics.TextRowHeight); // DEVELOPER
                m.Add(SuiteUiMetrics.TextRowHeight); // readiness
                m.Add(SuiteUiMetrics.TextRowHeight); // discovery explanation
            }
            return m.ContentHeight;
        }

        internal static float EstimateModuleContentHeight(
            string moduleId,
            bool runtimeBridgeExists,
            IList<string> actions,
            IList<SuiteSettingDescriptor> basicSettings,
            IList<SuiteSettingDescriptor> advancedSettings,
            IList<SuiteSettingDescriptor> developerSettings,
            bool showAdvanced,
            bool showDeveloper,
            bool developerEnabled)
        {
            Measure m = new Measure();
            m.Add(SuiteUiMetrics.TextRowHeight); // module title
            AddSectionBreak(m);
            m.Add(SuiteUiMetrics.TextRowHeight); // STATUS

            if (!runtimeBridgeExists)
            {
                // Static recovery explanation can wrap to two lines. Optional bridge-error text is
                // dynamic and deliberately not reserved here.
                m.Add(SuiteUiMetrics.TextRowHeight * 2f);
                return m.ContentHeight;
            }

            // One retained status row always exists. Warning text is optional/dynamic.
            m.Add(SuiteUiMetrics.TextRowHeight);

            if (SuiteHubPagePolicy.HasBasicSection(basicSettings))
            {
                AddSectionBreak(m);
                m.Add(SuiteUiMetrics.TextRowHeight); // BASIC
                AddSettings(m, basicSettings);
            }

            if (SuiteHubPagePolicy.HasPanelSection(actions))
            {
                AddSectionBreak(m);
                m.Add(SuiteUiMetrics.TextRowHeight); // PANEL
                m.Add(SuiteUiMetrics.RowHeight); // Open <module>
            }

            int compactActions = SuiteHubPagePolicy.CountCompactActionRows(moduleId, actions);
            if (compactActions > 0)
            {
                AddSectionBreak(m);
                m.Add(SuiteUiMetrics.TextRowHeight); // ACTIONS
                for (int i = 0; i < compactActions; i++) m.Add(SuiteUiMetrics.RowHeight);
            }

            if (SuiteHubPagePolicy.HasAdvancedSection(advancedSettings, actions))
            {
                AddSectionBreak(m);
                m.Add(SuiteUiMetrics.DisclosureRowHeight);
                if (showAdvanced)
                {
                    AddSettings(m, advancedSettings);
                    if (SuiteHubPagePolicy.HasAction(actions, "resetPanel")) m.Add(SuiteUiMetrics.RowHeight);
                    if (SuiteHubPagePolicy.HasAction(actions, "resetLauncher")) m.Add(SuiteUiMetrics.RowHeight);
                }
            }

            if (developerEnabled)
            {
                AddSectionBreak(m);
                m.Add(SuiteUiMetrics.DisclosureRowHeight);
                if (showDeveloper)
                {
                    m.Add(SuiteUiMetrics.TextRowHeight); // version
                    AddSettings(m, developerSettings);
                    // developer bridge-error text is optional/dynamic.
                }
            }

            return m.ContentHeight;
        }

        internal static float ResolveWindowHeight(float structuralContentHeight,
            float maximumEnvelopeHeight, float screenHeight)
        {
            float preferredTotal = structuralContentHeight
                + SuiteUiMetrics.HeaderHeight
                + SuiteUiMetrics.OuterPadding * 2f;
            return SuiteUiGeometry.ResolveCompactWindowHeight(
                preferredTotal, maximumEnvelopeHeight, screenHeight);
        }

        private static void AddSectionBreak(Measure m)
        {
            // In retained layout this is an actual small Spacer child. RowGap is also applied on
            // each side, yielding a crisp ~12px logical section separation (3 + 6 + 3), not a
            // large reserved block.
            m.Add(SuiteUiMetrics.SectionGap);
        }

        private static void AddSettings(Measure m, IList<SuiteSettingDescriptor> settings)
        {
            if (settings == null) return;
            for (int i = 0; i < settings.Count; i++)
            {
                SuiteSettingDescriptor s = settings[i];
                if (s == null) continue;
                if (s.Kind == SuiteSettingKind.Bool)
                    m.Add(SuiteUiMetrics.RowHeight);
                else if (s.Kind == SuiteSettingKind.Choice && s.Mutable && s.Options.Count > 0)
                    m.Add(SuiteUiMetrics.RowHeight);
                else
                    m.Add(SuiteUiMetrics.TextRowHeight);
            }
        }
    }
}
