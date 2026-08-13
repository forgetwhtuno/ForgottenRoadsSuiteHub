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
        private readonly IAuraSubscriber<string, string, string> _setBasicSetting;
        private readonly IAuraSubscriber<string, string, string> _invokeAction;
        private string _lastError = string.Empty;
        private List<SuiteSettingDescriptor> _cachedBasicSettings = new List<SuiteSettingDescriptor>();
        private List<SuiteSettingDescriptor> _cachedAdvancedSettings = new List<SuiteSettingDescriptor>();
        private List<SuiteSettingDescriptor> _cachedDeveloperSettings = new List<SuiteSettingDescriptor>();

        internal AuraModuleBridge(LunarisPlugin owner, SuiteModuleRegistry registry, string moduleId)
        {
            _moduleId = moduleId;
            _registry = registry;
            string prefix = "forgetwhtuno.erenshor.suite." + moduleId + ".v1.";
            _describe = owner.IPCAuraSubscriber<string>(prefix + "describe");
            _basicSettings = owner.IPCAuraSubscriber<string>(prefix + "settings.basic");
            _advancedSettings = owner.IPCAuraSubscriber<string>(prefix + "settings.advanced");
            _developerSettings = owner.IPCAuraSubscriber<string>(prefix + "settings.developer");
            _setBasicSetting = owner.IPCAuraSubscriber<string, string, string>(prefix + "setting.set");
            _invokeAction = owner.IPCAuraSubscriber<string, string, string>(prefix + "action");
        }

        internal string ModuleId { get { return _moduleId; } }
        internal string LastError { get { return _lastError; } }
        internal bool Connected { get { return _registry.Get(_moduleId) != null; } }
        internal List<SuiteSettingDescriptor> CachedBasicSettings { get { return _cachedBasicSettings; } }
        internal List<SuiteSettingDescriptor> CachedAdvancedSettings { get { return _cachedAdvancedSettings; } }
        internal List<SuiteSettingDescriptor> CachedDeveloperSettings { get { return _cachedDeveloperSettings; } }

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
                return result.StartsWith("ok", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) { result = ex.GetType().Name; return false; }
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
        }
    }
}
