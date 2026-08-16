using System;

namespace ErenshorSuiteHub
{
    internal struct SuiteSettingMutationRefreshPlan
    {
        internal readonly bool PollAuthoritativeState;
        internal readonly bool RefreshRetainedValues;

        internal SuiteSettingMutationRefreshPlan(bool pollAuthoritativeState, bool refreshRetainedValues)
        {
            PollAuthoritativeState = pollAuthoritativeState;
            RefreshRetainedValues = refreshRetainedValues;
        }
    }

    // A successful mutation is immediately followed by an authoritative module re-read, then the
    // retained page reconciles its dynamic bindings. Rejections do not need an extra provider poll
    // but still refresh the retained action-result text so the click never feels silent.
    internal static class SuiteSettingMutationPolicy
    {
        internal static SuiteSettingMutationRefreshPlan Resolve(bool mutationSucceeded)
        {
            return new SuiteSettingMutationRefreshPlan(mutationSucceeded, true);
        }

        internal static string VisibleResult(bool mutationSucceeded, string providerResult)
        {
            if (!string.IsNullOrEmpty(providerResult)) return providerResult;
            return mutationSucceeded ? "Updated" : "Setting rejected";
        }
    }
}
