using System;
using System.Collections.Generic;
using System.IO;
using ErenshorSuiteHub;

internal static class ForgottenRoadsChatStyleTests
{
    private const string OpeningMarkup = "<color";
    private const string ClosingMarkup = "</color>";
    private static readonly string[] ForbiddenVisibleMarkup =
        { "<color=cyan>", "<color=", "</color>", "<size=", "<b>", "<i>" };

    // A real native SystemMessages ColorString from the installed Erenshor build's own chat code
    // (TypeText: "You are not currently in a group."). Used ONLY as observed sample input for the
    // observer, never as a shipped default.
    private const string NativeSystemStyle = "#00B2B7";
    private const string NativeSystemStyleAlt = "#FF9000";

    internal static int RunAll()
    {
        int a = 0;
        a += ValidationTests();
        a += ObservationTests();
        a += PayloadTests();
        a += SourceContractTests();
        ForgottenRoadsChatStyle.Reset();
        return a;
    }

    private static int ValidationTests()
    {
        int a = 0;
        a += TestAssert.True(ForgottenRoadsChatStyle.IsSafeColorString(NativeSystemStyle),
            "native hex SystemMessages style is a valid style");
        a += TestAssert.True(ForgottenRoadsChatStyle.IsSafeColorString("#0AF"),
            "short hex style is valid");
        a += TestAssert.True(ForgottenRoadsChatStyle.IsSafeColorString("#00B2B7FF"),
            "hex style with alpha is valid");

        // Named tokens are exactly the class of value that rendered as literal markup live.
        string[] namedTokens = { "cyan", "lightblue", "white", "yellow", "grey", "red" };
        for (int i = 0; i < namedTokens.Length; i++)
        {
            a += TestAssert.False(ForgottenRoadsChatStyle.IsSafeColorString(namedTokens[i]),
                "named color token is rejected: " + namedTokens[i]);
        }
        a += TestAssert.False(ForgottenRoadsChatStyle.IsSafeColorString(""), "empty style is not valid");
        a += TestAssert.False(ForgottenRoadsChatStyle.IsSafeColorString(null), "null style is not valid");
        a += TestAssert.False(ForgottenRoadsChatStyle.IsSafeColorString("#00B2B"), "malformed hex is rejected");
        a += TestAssert.False(ForgottenRoadsChatStyle.IsSafeColorString("#00B2BZ"), "non-hex digits are rejected");
        a += TestAssert.False(ForgottenRoadsChatStyle.IsSafeColorString("00B2B7"), "hex without # is rejected");
        return a;
    }

    private static int ObservationTests()
    {
        int a = 0;

        // Absent any observed native style, output falls back to plain.
        ForgottenRoadsChatStyle.Reset();
        a += TestAssert.False(ForgottenRoadsChatStyle.HasCapturedStyle, "no style is captured initially");
        a += TestAssert.Equal(ForgottenRoadsChatStyle.PlainStyle, ForgottenRoadsChatStyle.CapturedStyle,
            "fallback style is plain (no tag emitted by the native renderer)");
        a += TestAssert.Equal("", ForgottenRoadsChatStyle.CapturedStyle, "plain style is the empty ColorString");

        // Non-system traffic teaches nothing.
        ForgottenRoadsChatStyle.ObserveNativeLine(false, "#123456", "someone shouts");
        a += TestAssert.False(ForgottenRoadsChatStyle.HasCapturedStyle,
            "non-SystemMessages traffic does not supply a style");

        // A named token on real system traffic is still not a usable style.
        ForgottenRoadsChatStyle.ObserveNativeLine(true, "yellow", "You gain experience.");
        a += TestAssert.False(ForgottenRoadsChatStyle.HasCapturedStyle,
            "native named token is observed but not adopted");

        // A real native SystemMessages hex style is captured and reused.
        bool changed = ForgottenRoadsChatStyle.ObserveNativeLine(true, NativeSystemStyle,
            "You are not currently in a group.");
        a += TestAssert.True(changed, "first safe native style observation changes state");
        a += TestAssert.True(ForgottenRoadsChatStyle.HasCapturedStyle, "native hex style is captured");
        a += TestAssert.Equal(NativeSystemStyle, ForgottenRoadsChatStyle.CapturedStyle,
            "captured native SystemMessages style is reused for Hub output");

        // Captured style stays stable so Hub output does not change color mid-session.
        ForgottenRoadsChatStyle.ObserveNativeLine(true, NativeSystemStyleAlt, "This target is invulnerable.");
        a += TestAssert.Equal(NativeSystemStyle, ForgottenRoadsChatStyle.CapturedStyle,
            "first captured style wins for the session");
        List<string> observed = ForgottenRoadsChatStyle.ObservedStyles;
        a += TestAssert.Equal(2, observed.Count, "every distinct safe native style is recorded for diagnostics");

        // Hub never learns from its own output.
        ForgottenRoadsChatStyle.Reset();
        ForgottenRoadsChatStyle.ObserveNativeLine(true, "#ABCDEF",
            ForgottenRoadsDiscoveryMessage.Tag + "Installed: Nemesis (/nemesis)");
        a += TestAssert.False(ForgottenRoadsChatStyle.HasCapturedStyle,
            "Hub's own line is never treated as native evidence");

        ForgottenRoadsChatStyle.Reset();
        return a;
    }

    private static int PayloadTests()
    {
        int a = 0;

        List<ModPresence> installed = new List<ModPresence>();
        installed.Add(new ModPresence(SuiteModuleCatalog.Find("nemesis"), true));
        installed.Add(new ModPresence(SuiteModuleCatalog.Find("duel"), true));
        installed.Add(new ModPresence(SuiteModuleCatalog.Find("pvp"), true));
        List<string> lines = ForgottenRoadsDiscoveryMessage.Compose(installed);
        a += TestAssert.True(lines.Count > 0, "composer produced discovery lines for this fixture");
        for (int i = 0; i < lines.Count; i++)
        {
            string payload = ForgottenRoadsChatStyle.SanitizePayload(lines[i]);
            a += TestAssert.True(payload.IndexOf(OpeningMarkup, StringComparison.OrdinalIgnoreCase) < 0,
                "visible payload contains no opening color markup");
            a += TestAssert.True(payload.IndexOf(ClosingMarkup, StringComparison.OrdinalIgnoreCase) < 0,
                "visible payload contains no closing color markup");
            a += TestAssert.False(ForgottenRoadsChatStyle.ContainsMarkup(payload),
                "visible payload is markup-free");
            for (int m = 0; m < ForbiddenVisibleMarkup.Length; m++)
                a += TestAssert.False(payload.IndexOf(ForbiddenVisibleMarkup[m], StringComparison.OrdinalIgnoreCase) >= 0,
                    "discovery payload has no visible rich-text token: " + ForbiddenVisibleMarkup[m]);
            a += TestAssert.Equal(lines[i], payload, "markup-free composer output passes through unchanged");
        }

        // Defense in depth: even a payload that somehow arrived with markup leaves clean.
        string dirty = OpeningMarkup + "=cyan>" + ForgottenRoadsDiscoveryMessage.Tag +
            "Installed: Duel (/duel)" + ClosingMarkup;
        string cleaned = ForgottenRoadsChatStyle.SanitizePayload(dirty);
        a += TestAssert.Equal(ForgottenRoadsDiscoveryMessage.Tag + "Installed: Duel (/duel)", cleaned,
            "markup is stripped from the visible payload");
        a += TestAssert.False(ForgottenRoadsChatStyle.ContainsMarkup(cleaned), "sanitized payload has no markup left");
        a += TestAssert.True(ForgottenRoadsChatStyle.ContainsMarkup(dirty), "markup detector recognizes markup");
        a += TestAssert.Equal("", ForgottenRoadsChatStyle.SanitizePayload(null), "null payload is dropped");
        return a;
    }

    // Source-level proof of the presentation contract inside the Unity-bound plugin, which the
    // deterministic suite cannot execute.
    private static int SourceContractTests()
    {
        int a = 0;
        string root = Environment.GetEnvironmentVariable("ERENSHOR_SUITEHUB_SOURCE_ROOT") ?? string.Empty;
        a += TestAssert.True(Directory.Exists(root), "presentation source contract root is available");
        if (!Directory.Exists(root)) return a;

        string plugin = File.ReadAllText(Path.Combine(root, "src", "ErenshorSuiteHubPlugin.cs"));
        string style = File.ReadAllText(Path.Combine(root, "src", "ForgottenRoadsChatStyle.cs"));

        a += TestAssert.Equal(1, Count(plugin, "new ChatLogLine("),
            "exactly one typed ChatLogLine construction site (one presentation authority)");
        a += TestAssert.True(plugin.IndexOf("ForgottenRoadsChatStyle.CapturedStyle", StringComparison.Ordinal) >= 0,
            "emitted style comes from observed native traffic");
        a += TestAssert.True(plugin.IndexOf("ForgottenRoadsChatStyle.SanitizePayload", StringComparison.Ordinal) >= 0,
            "emitted payload is sanitized at the single emit site");
        a += TestAssert.True(plugin.IndexOf("[HarmonyPatch(typeof(UpdateSocialLog), \"LogAdd\"", StringComparison.Ordinal) >= 0,
            "native SystemMessages traffic is actually observed");

        // No named color token may be hardcoded into typed ChatLogLine construction.
        string[] namedTokens = { "\"cyan\"", "\"lightblue\"", "\"white\"", "\"yellow\"", "\"grey\"", "\"red\"", "\"green\"" };
        for (int i = 0; i < namedTokens.Length; i++)
        {
            a += TestAssert.Equal(0, Count(plugin, namedTokens[i]),
                "no hardcoded named color token in plugin source: " + namedTokens[i]);
            a += TestAssert.Equal(0, Count(style, namedTokens[i]),
                "no hardcoded named color token in style source: " + namedTokens[i]);
        }

        // Both the startup discovery hint and /frhelp must route through the same helper.
        a += TestAssert.True(Count(plugin, "LogDiscoveryHintLines(") >= 4,
            "shared helper is declared and used by both discovery paths");
        a += TestAssert.True(InBody(plugin, "private void EmitForgottenRoadsDiscoveryHint()",
            "private static void LogDiscoveryHintLines", "LogDiscoveryHintLines("),
            "startup discovery hint uses the shared presentation helper");
        a += TestAssert.True(InBody(plugin, "internal void HandleForgottenRoadsHelpCommand()",
            "private List<SuiteDockModuleState> BuildDockModuleStates()", "LogDiscoveryHintLines("),
            "/frhelp uses the same shared presentation helper");
        a += TestAssert.False(InBody(plugin, "internal void HandleForgottenRoadsHelpCommand()",
            "private List<SuiteDockModuleState> BuildDockModuleStates()", "new ChatLogLine("),
            "/frhelp does not build its own chat line");
        return a;
    }

    private static bool InBody(string text, string start, string end, string needle)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        int to = text.IndexOf(end, StringComparison.Ordinal);
        if (from < 0 || to <= from) return false;
        return text.Substring(from, to - from).IndexOf(needle, StringComparison.Ordinal) >= 0;
    }

    private static int Count(string text, string needle)
    {
        int count = 0;
        int at = 0;
        while (true)
        {
            at = text.IndexOf(needle, at, StringComparison.Ordinal);
            if (at < 0) break;
            count++;
            at += needle.Length;
        }
        return count;
    }
}
