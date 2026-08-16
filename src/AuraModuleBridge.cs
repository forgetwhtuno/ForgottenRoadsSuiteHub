using System;
using System.Collections.Generic;
using Lunaris;
using Lunaris.IPC;

namespace ErenshorSuiteHub
{
    // Optional transport adapter. A bridge exists for every known module even when the module is
    // absent; Aura subscriber objects do not own/register a channel. Availability therefore works
    // with Hub-first, mod-first, unload, and late reload without a load-order dependency.
    internal sealed class AuraModuleBridge
    {
        private readonly string _moduleId;
        private readonly SuiteModuleRegistry _registry;
        private readonly IAuraSubscriber<string> _describe;
        private readonly IAuraSubscriber<string> _basicSettings;
        private readonly IAuraSubscriber<string> _advancedSettings;
        private readonly IAuraSubscriber<string> _developerSettings;
        private readonly IAuraSubscriber<string> _uiState;
        private readonly IAuraSubscriber<string, string, string> _setBasicSetting;
        private readonly IAuraSubscriber<string, string, string> _invokeAction;
        private string _lastError = string.Empty;
        private List<SuiteSettingDescriptor> _cachedBasicSettings = new List<SuiteSettingDescriptor>();
        private List<SuiteSettingDescriptor> _cachedAdvancedSettings = new List<SuiteSettingDescriptor>();
        private List<SuiteSettingDescriptor> _cachedDeveloperSettings = new List<SuiteSettingDescriptor>();
        private SuiteUiStateDescriptor _cachedUiState;

        internal AuraModuleBridge(LunarisPlugin owner, SuiteModuleRegistry registry, string moduleId)
        {
            _moduleId = moduleId;
            _registry = registry;
            string prefix = "forgetwhtuno.erenshor.suite." + moduleId + ".v1.";
            _describe = owner.IPCAuraSubscriber<string>(prefix + "describe");
            _basicSettings = owner.IPCAuraSubscriber<string>(prefix + "settings.basic");
            _advancedSettings = owner.IPCAuraSubscriber<string>(prefix + "settings.advanced");
            _developerSettings = owner.IPCAuraSubscriber<string>(prefix + "settings.developer");
            _uiState = owner.IPCAuraSubscriber<string>(prefix + "ui.state");
            _setBasicSetting = owner.IPCAuraSubscriber<string, string, string>(prefix + "setting.set");
            _invokeAction = owner.IPCAuraSubscriber<string, string, string>(prefix + "action");
        }

        internal string ModuleId { get { return _moduleId; } }
        internal string LastError { get { return _lastError; } }
        internal bool Connected { get { return _registry.Get(_moduleId) != null; } }
        internal bool HasRuntimeSignal
        {
            get
            {
                try
                {
                    return (_describe != null && _describe.HasFunction) ||
                        (_invokeAction != null && _invokeAction.HasFunction) ||
                        (_uiState != null && _uiState.HasFunction);
                }
                catch { return false; }
            }
        }
        internal List<SuiteSettingDescriptor> CachedBasicSettings { get { return _cachedBasicSettings; } }
        internal List<SuiteSettingDescriptor> CachedAdvancedSettings { get { return _cachedAdvancedSettings; } }
        internal List<SuiteSettingDescriptor> CachedDeveloperSettings { get { return _cachedDeveloperSettings; } }
        internal SuiteUiStateDescriptor CachedUiState { get { return _cachedUiState; } }

        internal bool CanInvokeAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return false;
            SuiteModuleDescriptor descriptor = _registry.Get(_moduleId);
            if (descriptor == null || !descriptor.HasAction(actionId)) return false;
            try { return _invokeAction != null && _invokeAction.HasFunction; }
            catch { return false; }
        }

        internal void Poll()
        {
            try
            {
                if (!_describe.HasFunction)
                {
                    _registry.Unregister(_moduleId, this);
                    ClearCachedSettings();
                    _lastError = string.Empty;
                    return;
                }

                string payload = _describe.InvokeFunc();
                string error;
                SuiteModuleDescriptor descriptor = SuiteWireCodec.ParseModuleDescriptor(payload, _moduleId, out error);
                if (descriptor == null)
                {
                    _registry.Unregister(_moduleId, this);
                    ClearCachedSettings();
                    _lastError = error ?? "invalid descriptor";
                    return;
                }

                if (!_registry.Register(descriptor, this, out error))
                {
                    _lastError = error ?? "registration rejected";
                    return;
                }
                _lastError = string.Empty;

                _cachedBasicSettings = PollSettings(_basicSettings, SuiteSettingTier.Basic, "basic");
                _cachedAdvancedSettings = PollSettings(_advancedSettings, SuiteSettingTier.Advanced, "advanced");
                _cachedDeveloperSettings = PollSettings(_developerSettings, SuiteSettingTier.Developer, "developer");
                _cachedUiState = PollUiState(descriptor, true);
            }
            catch (Exception ex)
            {
                _registry.Unregister(_moduleId, this);
                ClearCachedSettings();
                _lastError = ex.GetType().Name;
            }
        }

        internal bool TrySetSetting(string id, string value, out string result)
        {
            result = "setting endpoint unavailable";
            if (!_setBasicSetting.HasFunction) return false;
            try
            {
                result = _setBasicSetting.InvokeFunc(id, value) ?? string.Empty;
                bool ok = result.StartsWith("ok", StringComparison.OrdinalIgnoreCase);
                if (ok)
                {
                    SuiteSettingDescriptor cached = FindCachedSetting(id);
                    if (cached != null) cached.Value = value ?? string.Empty;
                }
                return ok;
            }
            catch (Exception ex) { result = ex.GetType().Name; return false; }
        }

        internal SuiteSettingDescriptor FindCachedSetting(string id)
        {
            SuiteSettingDescriptor found = FindSetting(_cachedBasicSettings, id);
            if (found != null) return found;
            found = FindSetting(_cachedAdvancedSettings, id);
            if (found != null) return found;
            return FindSetting(_cachedDeveloperSettings, id);
        }

        // Quick-close is event-driven, so do not rely on the normal one-second bridge poll for
        // whether a panel opened a moment ago. Refresh only ui.state at the decision point;
        // descriptor/settings remain on their ordinary cadence. This raw refresh intentionally
        // does NOT discard a valid "open" state just because closePanel is missing: the centralized
        // manager needs to see that structural contract gap so it can report it once and fail closed.
        internal SuiteUiStateDescriptor RefreshUiStateForQuickClose()
        {
            SuiteModuleDescriptor descriptor = _registry.Get(_moduleId);
            if (descriptor == null)
            {
                _cachedUiState = null;
                return null;
            }
            if (!_uiState.HasFunction)
            {
                _cachedUiState = null;
                return null;
            }

            // Unlike the ordinary one-second Poll(), let provider/parse failures escape to the
            // centralized coordinator. It catches each module independently and reports the fault
            // once, so one broken provider cannot prevent other open panels or Hub from closing.
            string error;
            SuiteUiStateDescriptor state = SuiteWireCodec.ParseUiState(_uiState.InvokeFunc(), _moduleId, out error);
            if (state == null) throw new InvalidOperationException(error ?? "invalid ui.state");
            _cachedUiState = state;
            return _cachedUiState;
        }

        internal bool TryInvokeAction(string actionId, string argument, out string result)
        {
            result = "action endpoint unavailable";
            SuiteModuleDescriptor d = _registry.Get(_moduleId);
            if (d == null || !d.HasAction(actionId) || !_invokeAction.HasFunction) return false;
            try
            {
                result = _invokeAction.InvokeFunc(actionId, argument ?? string.Empty) ?? string.Empty;
                return result.StartsWith("ok", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { result = ex.GetType().Name; return false; }
        }

        internal void Disconnect()
        {
            _registry.Unregister(_moduleId, this);
            ClearCachedSettings();
        }

        private SuiteUiStateDescriptor PollUiState(SuiteModuleDescriptor descriptor, bool requireCloseContract)
        {
            try
            {
                if (!_uiState.HasFunction) return null;
                string error;
                SuiteUiStateDescriptor state = SuiteWireCodec.ParseUiState(_uiState.InvokeFunc(), _moduleId, out error);
                if (state == null)
                {
                    if (string.IsNullOrEmpty(_lastError)) _lastError = error ?? "invalid ui.state";
                    return null;
                }
                if (requireCloseContract && !SuiteQuickClosePolicy.ModuleStateSatisfiesCloseContract(state, descriptor))
                {
                    if (string.IsNullOrEmpty(_lastError)) _lastError = "closeable ui.state requires advertised closePanel action";
                    return null;
                }
                return state;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(_lastError)) _lastError = "ui.state " + ex.GetType().Name;
                return null;
            }
        }

        private static SuiteSettingDescriptor FindSetting(List<SuiteSettingDescriptor> settings, string id)
        {
            if (settings == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < settings.Count; i++)
                if (string.Equals(settings[i].Id, id, StringComparison.Ordinal)) return settings[i];
            return null;
        }

        private List<SuiteSettingDescriptor> PollSettings(IAuraSubscriber<string> endpoint, SuiteSettingTier tier, string label)
        {
            if (!endpoint.HasFunction) return new List<SuiteSettingDescriptor>();
            string error;
            List<SuiteSettingDescriptor> parsed = SuiteWireCodec.ParseSettings(endpoint.InvokeFunc(), tier, out error);
            if (parsed != null) return parsed;
            if (string.IsNullOrEmpty(_lastError)) _lastError = error ?? ("invalid " + label + " settings");
            return new List<SuiteSettingDescriptor>();
        }

        private void ClearCachedSettings()
        {
            _cachedBasicSettings = new List<SuiteSettingDescriptor>();
            _cachedAdvancedSettings = new List<SuiteSettingDescriptor>();
            _cachedDeveloperSettings = new List<SuiteSettingDescriptor>();
            _cachedUiState = null;
        }
    }
}
