using System;

namespace ErenshorSuiteHub
{
    // Pure display semantics for player-facing settings. Boolean state is always textual as well as
    // colored so a player never has to infer state from a subtle tint.
    internal static class SuiteSettingDisplayPolicy
    {
        internal static bool IsOn(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        internal static string BooleanText(bool value)
        {
            return value ? "ON" : "OFF";
        }

        internal static string BooleanText(string value)
        {
            return BooleanText(IsOn(value));
        }

        internal static string BooleanButtonText(string label, bool value)
        {
            return (label ?? string.Empty) + " [" + BooleanText(value) + "]";
        }

        internal static string BooleanButtonText(string label, string value)
        {
            return BooleanButtonText(label, IsOn(value));
        }
    }
}
