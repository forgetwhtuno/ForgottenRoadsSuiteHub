using System;
using System.Reflection;
using HarmonyLib;

namespace ErenshorSuiteHub
{
    // Native Escape consumption is intentionally a compatibility binding, not a guessed patch.
    // The verified target constants remain empty in source until the exact current
    // Assembly-CSharp method responsible for vanilla Escape/menu opening is proven against the
    // installed game assemblies. While unbound the Hub advertises quickClose=0 and never swallows
    // Escape.
    internal static class SuiteNativeEscapeCompatibility
    {
        // DO NOT fill these from memory. Supply only after local assembly verification.
        private const string VerifiedDeclaringTypeName = "";
        private const string VerifiedMethodName = "";

        private static bool _bound;
        private static string _bindingStatus = "native Escape target not supplied/verified";

        internal static bool IsNativeConsumeBound { get { return _bound; } }
        internal static string BindingStatus { get { return _bindingStatus; } }

        internal static bool TryBind(Harmony harmony)
        {
            _bound = false;
            _bindingStatus = "native Escape target not supplied/verified";
            if (harmony == null) return false;
            if (string.IsNullOrEmpty(VerifiedDeclaringTypeName) || string.IsNullOrEmpty(VerifiedMethodName))
                return false;

            try
            {
                Type type = AccessTools.TypeByName(VerifiedDeclaringTypeName);
                if (type == null)
                {
                    _bindingStatus = "verified Escape declaring type could not be resolved in current Assembly-CSharp";
                    return false;
                }

                // Do not let a name-only binding silently pick an arbitrary overload. The supplied
                // declaring type + method name must resolve to exactly one method declared on that
                // type. If the real target is overloaded, this compatibility shim must be updated
                // with its proven parameter signature before quick-close can ever become active.
                MethodInfo target = null;
                MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                int matches = 0;
                for (int i = 0; i < methods.Length; i++)
                {
                    if (!string.Equals(methods[i].Name, VerifiedMethodName, StringComparison.Ordinal)) continue;
                    target = methods[i];
                    matches++;
                }
                if (matches != 1 || target == null)
                {
                    _bindingStatus = matches == 0
                        ? "verified Escape target could not be resolved in current Assembly-CSharp"
                        : "verified Escape target name is overloaded; exact parameter signature is required";
                    return false;
                }

                HarmonyMethod prefix = new HarmonyMethod(typeof(SuiteNativeEscapeCompatibility), "Prefix");
                harmony.Patch(target, prefix: prefix);
                _bound = true;
                _bindingStatus = "bound to verified native Escape/menu handler";
                return true;
            }
            catch (Exception ex)
            {
                _bound = false;
                _bindingStatus = "Escape compatibility bind failed: " + ex.GetType().Name;
                return false;
            }
        }

        internal static void ResetBindingState()
        {
            _bound = false;
        }

        // Harmony prefixes return false to suppress the original. This can only execute when
        // TryBind successfully resolved the explicitly verified target above.
        private static bool Prefix()
        {
            if (!_bound) return true;
            return !ErenshorSuiteHubPlugin.TryQuickCloseFromBoundNativeEscape();
        }
    }
}
