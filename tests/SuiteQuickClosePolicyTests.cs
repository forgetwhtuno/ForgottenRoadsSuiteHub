using System;
using System.Collections.Generic;
using ErenshorSuiteHub;

internal static class SuiteQuickClosePolicyTests
{
    private sealed class FakeRuntime : ISuiteQuickCloseRuntime
    {
        internal readonly Dictionary<string, SuiteUiStateDescriptor> States =
            new Dictionary<string, SuiteUiStateDescriptor>(StringComparer.Ordinal);
        internal readonly HashSet<string> CloseActions = new HashSet<string>(StringComparer.Ordinal);
        internal readonly HashSet<string> ThrowRead = new HashSet<string>(StringComparer.Ordinal);
        internal readonly HashSet<string> ThrowClose = new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<string> CloseCalls = new List<string>();
        internal readonly HashSet<string> MissingReported = new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<string> Faults = new List<string>();
        internal int HasCloseChecks;
        internal int GameplayCancellationCalls;
        internal int HubCloseCalls;
        internal bool HubCloseSucceeds = true;
        internal SuiteUiStateDescriptor HubState;

        public SuiteUiStateDescriptor ReadHubUiState() { return HubState; }

        public SuiteUiStateDescriptor ReadUiState(string moduleId)
        {
            if (ThrowRead.Contains(moduleId)) throw new InvalidOperationException("provider fault");
            SuiteUiStateDescriptor state;
            return States.TryGetValue(moduleId, out state) ? state : null;
        }

        public bool HasClosePanelAction(string moduleId)
        {
            HasCloseChecks++;
            return CloseActions.Contains(moduleId);
        }

        public bool TryClosePanel(string moduleId, out string result)
        {
            if (ThrowClose.Contains(moduleId)) throw new InvalidOperationException("close fault");
            CloseCalls.Add(moduleId);
            SuiteUiStateDescriptor state;
            if (States.TryGetValue(moduleId, out state)) state.Open = false;
            result = "ok";
            return true;
        }

        public bool TryCloseHub()
        {
            HubCloseCalls++;
            if (!HubCloseSucceeds) return false;
            if (HubState != null) HubState.Open = false;
            return true;
        }

        public void ReportMissingClosePanel(string moduleId) { MissingReported.Add(moduleId); }
        public void ReportFault(string moduleId, string stage, Exception error) { Faults.Add(moduleId + "|" + stage); }
    }

    internal static int RunAll()
    {
        int a = 0;

        SuiteUiStateDescriptor closeableState = State("journal", true, true, 520, 1d);
        SuiteModuleDescriptor noClose = new SuiteModuleDescriptor();
        noClose.Actions.Add("openPanel");
        a += TestAssert.False(SuiteQuickClosePolicy.ModuleStateSatisfiesCloseContract(closeableState, noClose),
            "closeable ui.state requires closePanel action");
        noClose.Actions.Add("closePanel");
        a += TestAssert.True(SuiteQuickClosePolicy.ModuleStateSatisfiesCloseContract(closeableState, noClose),
            "advertised closePanel satisfies close contract");

        // Nothing open: the verified native Prefix must pass vanilla Escape unchanged.
        FakeRuntime runtime = new FakeRuntime();
        runtime.States["journal"] = State("journal", false, true, 520, 1d);
        runtime.CloseActions.Add("journal");
        SuiteQuickCloseResult result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "journal" }, runtime);
        a += TestAssert.False(result.HadDismissibleUi, "closed module is not a quick-close candidate");
        a += TestAssert.False(result.ShouldConsumeNativeEscape, "no Suite UI open leaves vanilla Escape untouched");
        a += TestAssert.Equal(0, runtime.CloseCalls.Count, "closed module is not redundantly closed");

        // One open module closes exactly once and therefore owns this verified native keypress.
        runtime = new FakeRuntime(); AddOpen(runtime, "journal", 520, 2d);
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "journal" }, runtime);
        a += TestAssert.Equal("journal", result.TopmostModuleId, "single open module is selected");
        a += TestAssert.Equal(1, result.ModuleCloseSuccesses, "single module closes");
        a += TestAssert.True(result.ShouldConsumeNativeEscape, "actual visual close consumes verified native Escape");
        a += TestAssert.Equal(0, runtime.GameplayCancellationCalls, "quick close cannot reach gameplay cancellation");

        // Higher Canvas sort order wins; lower panels are left open for a later Escape.
        runtime = new FakeRuntime();
        AddOpen(runtime, "pvp", 520, 50d);
        AddOpen(runtime, "partytools", 521, 1d);
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "pvp", "partytools" }, runtime);
        a += TestAssert.Equal("partytools", result.TopmostModuleId, "higher sort order is topmost");
        a += TestAssert.Equal(1, runtime.CloseCalls.Count, "one Escape closes one module only");
        a += TestAssert.Equal("partytools", runtime.CloseCalls[0], "topmost module closes first");
        a += TestAssert.True(runtime.States["pvp"].Open, "lower module stays open");

        // Equal sort order uses activation time as the deterministic z-order tie break.
        runtime = new FakeRuntime();
        AddOpen(runtime, "journal", 520, 3d);
        AddOpen(runtime, "pvp", 520, 9d);
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "journal", "pvp" }, runtime);
        a += TestAssert.Equal("pvp", result.TopmostModuleId, "newer activation wins equal sort order");

        // Hub advertises its real Canvas order/activation and participates in the same topmost rule.
        runtime = new FakeRuntime();
        AddOpen(runtime, "partytools", 521, 100d);
        runtime.HubState = State(SuiteQuickClosePolicy.HubModuleId, true, true, 600, 2d);
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "partytools" }, runtime);
        a += TestAssert.Equal(SuiteQuickClosePolicy.HubModuleId, result.TopmostModuleId, "Hub Canvas can be topmost");
        a += TestAssert.True(result.ClosedHub, "topmost Hub closes");
        a += TestAssert.Equal(0, runtime.CloseCalls.Count, "module behind Hub is untouched");

        // Repeated verified Escapes peel one visual layer at a time, then vanilla receives Escape.
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "partytools" }, runtime);
        a += TestAssert.Equal("partytools", result.TopmostModuleId, "second Escape reaches next visual layer");
        a += TestAssert.True(result.ShouldConsumeNativeEscape, "second actual close is consumed");
        SuiteQuickCloseResult third = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "partytools" }, runtime);
        a += TestAssert.False(third.ShouldConsumeNativeEscape, "after Suite UI closes, Escape falls through to vanilla");

        // Hub close must also succeed before the verified native Escape is consumed.
        runtime = new FakeRuntime();
        runtime.HubState = State(SuiteQuickClosePolicy.HubModuleId, true, true, 600, 2d);
        runtime.HubCloseSucceeds = false;
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string>(), runtime);
        a += TestAssert.False(result.ClosedHub, "failed Hub close is not reported as closed");
        a += TestAssert.False(result.ShouldConsumeNativeEscape, "failed Hub close leaves vanilla Escape untouched");

        // Dynamic ui.state is read at decision time, not from stale bridge polling state.
        runtime = new FakeRuntime();
        runtime.States["contracts"] = State("contracts", false, true, 521, 1d);
        runtime.CloseActions.Add("contracts");
        SuiteQuickClosePolicy.CloseTopmost(new List<string> { "contracts" }, runtime);
        runtime.States["contracts"].Open = true;
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "contracts" }, runtime);
        a += TestAssert.Equal(1, runtime.CloseCalls.Count, "later-opened module is discovered dynamically");

        // An open topmost module that lacks closePanel blocks closing anything behind it and does
        // not trap native Escape. This is safer than silently invoking a gameplay action or lower UI.
        runtime = new FakeRuntime();
        runtime.States["pvp"] = State("pvp", true, true, 530, 1d); // deliberately no close action
        AddOpen(runtime, "journal", 520, 2d);
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "journal", "pvp" }, runtime);
        a += TestAssert.Equal("pvp", result.TopmostModuleId, "unsupported topmost is still the visible topmost surface");
        a += TestAssert.Equal(1, result.UnsupportedOpenModules, "missing closePanel is reported");
        a += TestAssert.True(runtime.MissingReported.Contains("pvp"), "missing closePanel report identifies module");
        a += TestAssert.Equal(0, runtime.CloseCalls.Count, "lower module is not closed behind unsupported topmost");
        a += TestAssert.False(result.ShouldConsumeNativeEscape, "failed visual close cannot consume native Escape");
        a += TestAssert.Equal(0, runtime.GameplayCancellationCalls, "missing closePanel never falls back to gameplay cancel");

        // A close fault likewise leaves vanilla untouched because nothing actually closed.
        runtime = new FakeRuntime(); AddOpen(runtime, "journal", 520, 1d); runtime.ThrowClose.Add("journal");
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "journal" }, runtime);
        a += TestAssert.Equal(1, result.ProviderFaults, "closePanel fault is contained");
        a += TestAssert.False(result.ShouldConsumeNativeEscape, "close fault does not swallow native Escape");

        // Non-closeable status surfaces (for example persistent travel HUDs) never own quick-close.
        runtime = new FakeRuntime();
        runtime.States["follow"] = State("follow", true, false, 900, 1d);
        runtime.CloseActions.Add("follow");
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "follow" }, runtime);
        a += TestAssert.False(result.HadDismissibleUi, "non-closeable status surface is not a menu candidate");
        a += TestAssert.Equal(0, runtime.HasCloseChecks, "non-closeable surface never reaches action lookup");

        // Duplicated catalog IDs cannot trigger duplicate actions.
        runtime = new FakeRuntime(); AddOpen(runtime, "journal", 520, 1d);
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "journal", "journal" }, runtime);
        a += TestAssert.Equal(1, runtime.CloseCalls.Count, "duplicate catalog ID closes once");

        // Provider faults are contained and a known healthy candidate can still be selected.
        runtime = new FakeRuntime(); runtime.ThrowRead.Add("journal"); AddOpen(runtime, "contracts", 521, 1d);
        result = SuiteQuickClosePolicy.CloseTopmost(new List<string> { "journal", "contracts" }, runtime);
        a += TestAssert.Equal(1, result.ProviderFaults, "ui.state provider fault is contained");
        a += TestAssert.Equal("contracts", result.TopmostModuleId, "healthy known candidate still closes");

        return a;
    }

    private static SuiteUiStateDescriptor State(string moduleId, bool open, bool closeable, int sortOrder, double activated)
    {
        return new SuiteUiStateDescriptor
        {
            ProtocolVersion = 1,
            ModuleId = moduleId,
            Open = open,
            Closeable = closeable,
            SortOrder = sortOrder,
            Activated = activated
        };
    }

    private static void AddOpen(FakeRuntime runtime, string moduleId, int sortOrder, double activated)
    {
        runtime.States[moduleId] = State(moduleId, true, true, sortOrder, activated);
        runtime.CloseActions.Add(moduleId);
    }
}
