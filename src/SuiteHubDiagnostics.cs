using System;

namespace ErenshorSuiteHub
{
    // Bounded UI lifecycle diagnostics for the 0.3.0 production regression hunt.
    //
    // Rules: never log per-frame. Counters are logged only when a value actually changes, and the
    // idle summary is emitted at most once every IdleReportSeconds and only if something moved.
    // Everything routes through ErenshorSuiteHubPlugin so the inherited Lunaris logger is reachable.
    internal static class SuiteHubDiagnostics
    {
        private const float IdleReportSeconds = 10f;

        internal static bool Enabled;

        internal static int RootCreates;
        internal static int RootDestroys;
        internal static int LauncherCreates;
        internal static int WindowCreates;
        internal static int WindowDestroys;
        internal static int NavRebuilds;
        internal static int PageRebuilds;
        internal static int ModuleRefreshes;
        internal static int SetActiveChanges;
        internal static int SelectionChanges;

        private static int _lastReportedSignature;
        private static float _nextReport;

        internal static void Log(string message)
        {
            if (!Enabled) return;
            ErenshorSuiteHubPlugin plugin = ErenshorSuiteHubPlugin.Instance;
            if (plugin == null) return;
            try { plugin.LogUi("[HubUI] " + message); }
            catch (Exception) { }
        }

        internal static void Reset()
        {
            RootCreates = 0; RootDestroys = 0; LauncherCreates = 0;
            WindowCreates = 0; WindowDestroys = 0;
            NavRebuilds = 0; PageRebuilds = 0; ModuleRefreshes = 0;
            SetActiveChanges = 0; SelectionChanges = 0;
            _lastReportedSignature = 0;
            _nextReport = 0f;
        }

        // Called from the plugin's Update. Emits a single counter line only when a counter has
        // actually moved since the last report, at most once per IdleReportSeconds. Sitting idle
        // with the Hub open should produce NO further lines once the UI has settled.
        internal static void TickReport(float unscaledTime)
        {
            if (!Enabled) return;
            if (unscaledTime < _nextReport) return;
            _nextReport = unscaledTime + IdleReportSeconds;

            int signature = RootCreates * 31 + RootDestroys * 37 + LauncherCreates * 41 +
                            WindowCreates * 43 + WindowDestroys * 47 + NavRebuilds * 53 +
                            PageRebuilds * 59 + ModuleRefreshes * 61 + SetActiveChanges * 67 +
                            SelectionChanges * 71;
            if (signature == _lastReportedSignature) return;
            _lastReportedSignature = signature;

            Log("counters rootCreate=" + RootCreates +
                " rootDestroy=" + RootDestroys +
                " launcherCreate=" + LauncherCreates +
                " windowCreate=" + WindowCreates +
                " windowDestroy=" + WindowDestroys +
                " navRebuild=" + NavRebuilds +
                " pageRebuild=" + PageRebuilds +
                " moduleRefresh=" + ModuleRefreshes +
                " setActive=" + SetActiveChanges +
                " selectionChange=" + SelectionChanges);
        }
    }
}
