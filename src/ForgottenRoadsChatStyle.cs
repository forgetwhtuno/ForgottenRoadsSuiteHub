using System;
using System.Collections.Generic;

namespace ErenshorSuiteHub
{
    // SINGLE PRESENTATION AUTHORITY for every Forgotten Roads line the Hub writes into the native
    // social log (the one-time discovery hint AND /frhelp). Unity-free on purpose so the whole
    // policy is exercised by the deterministic test suite.
    //
    // WHY THIS EXISTS
    // ---------------
    // Current Erenshor renders a chat entry in IDLog.BuildVisibleText: when ChatLogLine.ColorString
    // is non-empty it wraps the text in a TextMeshPro color tag; when it is empty it appends the
    // raw text. TMP prints an unrecognized color *name* as literal characters instead of applying
    // it, so a color token the current build's TMP does not know turns the whole line into visible
    // markup. That is exactly what the previously shipped named token did live.
    //
    // POLICY
    // ------
    // 1. The payload text is always markup-free (SanitizePayload is applied at the emit site).
    // 2. The color is metadata only, and only ever a value this runtime actually produced:
    //    a native SystemMessages ChatLogLine observed on real UpdateSocialLog.LogAdd traffic.
    // 3. A style is accepted only in literal hex form (#RGB / #RGBA / #RRGGBB / #RRGGBBAA), which
    //    TMP parses numerically and therefore cannot render as literal text. Named tokens are
    //    never accepted or fabricated here, no matter what the legacy string LogAdd overload used
    //    to tolerate.
    // 4. If nothing safe has been observed yet, the style is the empty string: the native renderer
    //    then emits no tag at all and the line is plain/default readable text. Plain is always the
    //    fallback - a guessed color is never one.
    internal static class ForgottenRoadsChatStyle
    {
        // Empty ColorString == native renderer adds no tag at all == plain readable line.
        internal const string PlainStyle = "";

        private const string OpeningTag = "<color";
        private const string ClosingTag = "</color>";
        private const int MaxObservedStyles = 8;

        private static string _capturedStyle = PlainStyle;
        private static readonly List<string> _observedStyles = new List<string>();

        // Style used for the next emitted Forgotten Roads line. Empty until real native traffic
        // has supplied something safe.
        internal static string CapturedStyle { get { return _capturedStyle; } }

        internal static bool HasCapturedStyle { get { return _capturedStyle.Length > 0; } }

        // Every distinct safe native SystemMessages style seen this session, in observation order.
        // Diagnostics only - the emitted style stays stable at the first accepted observation so
        // Hub output does not change color mid-session.
        internal static List<string> ObservedStyles { get { return new List<string>(_observedStyles); } }

        internal static string Diagnostic
        {
            get
            {
                return HasCapturedStyle
                    ? "native SystemMessages style captured: " + _capturedStyle +
                      " (observed=" + _observedStyles.Count + ")"
                    : "no native SystemMessages style observed yet; emitting plain lines";
            }
        }

        internal static void Reset()
        {
            _capturedStyle = PlainStyle;
            _observedStyles.Clear();
        }

        // Called from the Harmony observer on real UpdateSocialLog.LogAdd(ChatLogLine) traffic.
        // isSystemMessages is the caller's already-masked ChatLogLine.MyLogType test, so this stays
        // free of Assembly-CSharp types. Returns true when this observation changed captured state
        // (i.e. worth logging once).
        internal static bool ObserveNativeLine(bool isSystemMessages, string colorString, string chatString)
        {
            if (!isSystemMessages) return false;
            // Never learn from our own output - that would let a bad style become self-confirming.
            if (IsOwnPayload(chatString)) return false;
            if (!IsSafeColorString(colorString)) return false;

            bool changed = false;
            if (!_observedStyles.Contains(colorString))
            {
                if (_observedStyles.Count < MaxObservedStyles) _observedStyles.Add(colorString);
                changed = true;
            }
            // First safe native style wins and then stays put: deterministic presentation.
            if (!HasCapturedStyle)
            {
                _capturedStyle = colorString;
                changed = true;
            }
            return changed;
        }

        internal static bool IsOwnPayload(string chatString)
        {
            if (string.IsNullOrEmpty(chatString)) return false;
            return chatString.IndexOf(ForgottenRoadsDiscoveryMessage.Tag, StringComparison.Ordinal) >= 0;
        }

        // Literal hex only. An arbitrary non-empty string is NOT a valid style: names are exactly
        // the class of value that rendered as visible markup on the current build.
        internal static bool IsSafeColorString(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (value[0] != '#') return false;
            int digits = value.Length - 1;
            if (digits != 3 && digits != 4 && digits != 6 && digits != 8) return false;
            for (int i = 1; i < value.Length; i++)
            {
                if (!IsHexDigit(value[i])) return false;
            }
            return true;
        }

        internal static bool ContainsMarkup(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf(OpeningTag, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf(ClosingTag, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Defense in depth at the single emit site: whatever the composer produced, the visible
        // payload leaves here without any color markup in it.
        internal static string SanitizePayload(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            string text = line;

            int closing;
            while ((closing = text.IndexOf(ClosingTag, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                text = text.Substring(0, closing) + text.Substring(closing + ClosingTag.Length);
            }

            while (true)
            {
                int start = text.IndexOf(OpeningTag, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                int end = text.IndexOf('>', start);
                if (end < 0) { text = text.Substring(0, start); break; }
                text = text.Substring(0, start) + text.Substring(end + 1);
            }

            return text.Trim();
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
