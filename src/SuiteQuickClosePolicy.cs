using System;
using System.Collections.Generic;

namespace ErenshorSuiteHub
{
    internal sealed class SuiteUiStateDescriptor
    {
        internal int ProtocolVersion;
        internal string ModuleId;
        internal bool Open;
        internal bool Closeable;
        internal int SortOrder;
        internal double Activated;
    }

    // Runtime seam for the single Suite Escape owner. Implementations expose only visual UI state
    // and the literal closePanel action; gameplay cancel/stop actions are intentionally unreachable.
    internal interface ISuiteQuickCloseRuntime
    {
        SuiteUiStateDescriptor ReadHubUiState();
        SuiteUiStateDescriptor ReadUiState(string moduleId);
        bool HasClosePanelAction(string moduleId);
        bool TryClosePanel(string moduleId, out string result);
        bool TryCloseHub();
        void ReportMissingClosePanel(string moduleId);
        void ReportFault(string moduleId, string stage, Exception error);
    }

    internal sealed class SuiteQuickCloseResult
    {
        internal bool HadDismissibleUi;
        internal bool ClosedHub;
        internal int ModuleCloseAttempts;
        internal int ModuleCloseSuccesses;
        internal int UnsupportedOpenModules;
        internal int ProviderFaults;
        internal string TopmostModuleId = string.Empty;

        // The native Escape action may be suppressed only when this invocation actually closed a
        // Suite-owned visual surface. A visible-but-unsupported/faulting panel never traps vanilla.
        internal bool ShouldConsumeNativeEscape
        {
            get { return ClosedHub || ModuleCloseSuccesses > 0; }
        }
    }

    internal static class SuiteQuickClosePolicy
    {
        internal const string ClosePanelActionId = "closePanel";
        internal const string HubModuleId = "suitehub";

        private sealed class Candidate
        {
            internal string ModuleId;
            internal SuiteUiStateDescriptor State;
            internal bool IsHub;
        }

        internal static bool ModuleStateSatisfiesCloseContract(SuiteUiStateDescriptor state, SuiteModuleDescriptor descriptor)
        {
            if (state == null) return false;
            if (!state.Closeable) return true;
            return descriptor != null && descriptor.HasAction(ClosePanelActionId);
        }

        // Exactly one visual surface is dismissed per Escape: the topmost open closeable Suite
        // surface by Canvas sort order, then most recent activation. This matches normal menu
        // stacking and prevents one keypress from unexpectedly collapsing every open module.
        // Dynamic ui.state is read only at the decision point; duplicate catalog IDs are ignored.
        internal static SuiteQuickCloseResult CloseTopmost(IList<string> moduleIds, ISuiteQuickCloseRuntime runtime)
        {
            SuiteQuickCloseResult result = new SuiteQuickCloseResult();
            if (runtime == null) return result;

            Candidate best = null;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (moduleIds != null)
            {
                for (int i = 0; i < moduleIds.Count; i++)
                {
                    string moduleId = moduleIds[i] ?? string.Empty;
                    if (moduleId.Length == 0 || !seen.Add(moduleId)) continue;

                    SuiteUiStateDescriptor state;
                    try { state = runtime.ReadUiState(moduleId); }
                    catch (Exception ex)
                    {
                        result.ProviderFaults++;
                        SafeReportFault(runtime, moduleId, "ui.state", ex);
                        continue;
                    }

                    if (!IsOpenCloseableModuleState(state, moduleId, runtime, result)) continue;
                    Candidate candidate = new Candidate { ModuleId = moduleId, State = state, IsHub = false };
                    if (IsAbove(candidate, best)) best = candidate;
                }
            }

            try
            {
                SuiteUiStateDescriptor hub = runtime.ReadHubUiState();
                if (hub != null && hub.Open && hub.Closeable &&
                    string.Equals(hub.ModuleId, HubModuleId, StringComparison.Ordinal))
                {
                    Candidate candidate = new Candidate { ModuleId = HubModuleId, State = hub, IsHub = true };
                    if (IsAbove(candidate, best)) best = candidate;
                }
            }
            catch (Exception ex)
            {
                result.ProviderFaults++;
                SafeReportFault(runtime, HubModuleId, "ui.state", ex);
            }

            if (best == null) return result;
            result.HadDismissibleUi = true;
            result.TopmostModuleId = best.ModuleId;

            if (best.IsHub)
            {
                try
                {
                    result.ClosedHub = runtime.TryCloseHub();
                }
                catch (Exception ex)
                {
                    result.ProviderFaults++;
                    SafeReportFault(runtime, HubModuleId, "closePanel", ex);
                }
                return result;
            }

            bool hasClose;
            try { hasClose = runtime.HasClosePanelAction(best.ModuleId); }
            catch (Exception ex)
            {
                result.ProviderFaults++;
                SafeReportFault(runtime, best.ModuleId, "descriptor", ex);
                return result;
            }

            if (!hasClose)
            {
                result.UnsupportedOpenModules++;
                try { runtime.ReportMissingClosePanel(best.ModuleId); } catch (Exception) { }
                return result;
            }

            result.ModuleCloseAttempts++;
            try
            {
                string closeResult;
                if (runtime.TryClosePanel(best.ModuleId, out closeResult)) result.ModuleCloseSuccesses++;
            }
            catch (Exception ex)
            {
                result.ProviderFaults++;
                SafeReportFault(runtime, best.ModuleId, "closePanel", ex);
            }
            return result;
        }

        private static bool IsOpenCloseableModuleState(SuiteUiStateDescriptor state, string moduleId,
            ISuiteQuickCloseRuntime runtime, SuiteQuickCloseResult result)
        {
            if (state == null || !state.Open || !state.Closeable) return false;
            if (string.Equals(state.ModuleId, moduleId, StringComparison.Ordinal)) return true;
            result.ProviderFaults++;
            SafeReportFault(runtime, moduleId, "ui.state", new InvalidOperationException("module id mismatch"));
            return false;
        }

        private static bool IsAbove(Candidate candidate, Candidate current)
        {
            if (candidate == null || candidate.State == null) return false;
            if (current == null || current.State == null) return true;
            if (candidate.State.SortOrder != current.State.SortOrder)
                return candidate.State.SortOrder > current.State.SortOrder;
            if (candidate.State.Activated != current.State.Activated)
                return candidate.State.Activated > current.State.Activated;
            return string.CompareOrdinal(candidate.ModuleId, current.ModuleId) > 0;
        }

        private static void SafeReportFault(ISuiteQuickCloseRuntime runtime, string moduleId, string stage, Exception ex)
        {
            try { runtime.ReportFault(moduleId, stage, ex); } catch (Exception) { }
        }
    }
}
