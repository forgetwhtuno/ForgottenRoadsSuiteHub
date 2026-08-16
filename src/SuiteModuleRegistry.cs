using System;
using System.Collections.Generic;

namespace ErenshorSuiteHub
{
    internal enum SuiteSettingTier { Basic, Advanced, Developer }
    internal enum SuiteSettingKind { Bool, Text, Number, Choice }

    internal sealed class SuiteSettingDescriptor
    {
        internal string Id;
        internal string Label;
        internal SuiteSettingTier Tier;
        internal SuiteSettingKind Kind;
        internal string Value;
        internal bool Mutable;
        internal readonly List<string> Options = new List<string>();
    }

    internal sealed class SuiteModuleDescriptor
    {
        internal int ProtocolVersion;
        internal string ModuleId;
        internal string DisplayName;
        internal string Version;
        internal string Summary;
        internal string Status;
        internal string Warning;
        internal readonly List<string> Actions = new List<string>();

        internal bool HasAction(string id)
        {
            for (int i = 0; i < Actions.Count; i++)
                if (string.Equals(Actions[i], id, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    internal sealed class RegisteredSuiteModule
    {
        internal SuiteModuleDescriptor Descriptor;
        internal object OwnerToken;
    }

    // One logical provider per module ID. Registering the same owner again updates in place;
    // a different owner cannot silently replace a live provider. This is intentionally separate
    // from Lunaris so late-registration/unload behavior is deterministic and unit-testable.
    internal sealed class SuiteModuleRegistry
    {
        private readonly Dictionary<string, RegisteredSuiteModule> _modules =
            new Dictionary<string, RegisteredSuiteModule>(StringComparer.Ordinal);

        internal int Count { get { return _modules.Count; } }

        internal bool Register(SuiteModuleDescriptor descriptor, object ownerToken, out string error)
        {
            error = null;
            if (!SuiteDescriptorValidation.ValidateModule(descriptor, out error)) return false;
            if (ownerToken == null) { error = "owner token is required"; return false; }

            RegisteredSuiteModule existing;
            if (_modules.TryGetValue(descriptor.ModuleId, out existing))
            {
                if (!object.ReferenceEquals(existing.OwnerToken, ownerToken))
                {
                    error = "module id is already registered by another provider";
                    return false;
                }
                existing.Descriptor = descriptor;
                return true;
            }

            _modules.Add(descriptor.ModuleId, new RegisteredSuiteModule { Descriptor = descriptor, OwnerToken = ownerToken });
            return true;
        }

        internal bool Unregister(string moduleId, object ownerToken)
        {
            if (string.IsNullOrEmpty(moduleId) || ownerToken == null) return false;
            RegisteredSuiteModule existing;
            if (!_modules.TryGetValue(moduleId, out existing)) return false;
            if (!object.ReferenceEquals(existing.OwnerToken, ownerToken)) return false;
            return _modules.Remove(moduleId);
        }

        internal SuiteModuleDescriptor Get(string moduleId)
        {
            RegisteredSuiteModule value;
            return _modules.TryGetValue(moduleId, out value) ? value.Descriptor : null;
        }

        internal void Clear() { _modules.Clear(); }
    }

    internal static class SuiteDescriptorValidation
    {
        internal static bool ValidateModule(SuiteModuleDescriptor descriptor, out string error)
        {
            error = null;
            if (descriptor == null) { error = "descriptor is null"; return false; }
            if (descriptor.ProtocolVersion != 1) { error = "unsupported protocol"; return false; }
            if (!ValidId(descriptor.ModuleId, 48)) { error = "invalid module id"; return false; }
            if (SuiteModuleCatalog.Find(descriptor.ModuleId) == null) { error = "unknown module id"; return false; }
            if (!BoundedText(descriptor.DisplayName, 1, 64)) { error = "invalid display name"; return false; }
            if (!BoundedText(descriptor.Version, 1, 32)) { error = "invalid version"; return false; }
            if (!BoundedText(descriptor.Summary, 0, 240)) { error = "summary too long"; return false; }
            if (!BoundedText(descriptor.Status, 0, 240)) { error = "status too long"; return false; }
            if (!BoundedText(descriptor.Warning, 0, 240)) { error = "warning too long"; return false; }

            HashSet<string> actionIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < descriptor.Actions.Count; i++)
            {
                string action = descriptor.Actions[i];
                if (!ValidId(action, 48)) { error = "invalid action id"; return false; }
                if (!actionIds.Add(action)) { error = "duplicate action id"; return false; }
            }
            return true;
        }

        internal static bool ValidateUiState(SuiteUiStateDescriptor state, string expectedModuleId, out string error)
        {
            error = null;
            if (state == null) { error = "ui state is null"; return false; }
            if (state.ProtocolVersion != 1) { error = "unsupported ui state protocol"; return false; }
            if (!string.Equals(state.ModuleId, expectedModuleId, StringComparison.Ordinal))
            {
                error = "ui state module id mismatch";
                return false;
            }
            if (SuiteModuleCatalog.Find(state.ModuleId) == null) { error = "unknown ui state module id"; return false; }
            if (state.SortOrder < -10000 || state.SortOrder > 10000) { error = "ui state sort order out of range"; return false; }
            if (double.IsNaN(state.Activated) || double.IsInfinity(state.Activated) || state.Activated < 0d)
            {
                error = "invalid ui activation time";
                return false;
            }
            return true;
        }

        internal static bool ValidateSetting(SuiteSettingDescriptor setting, out string error)
        {
            error = null;
            if (setting == null) { error = "setting is null"; return false; }
            if (!ValidId(setting.Id, 64)) { error = "invalid setting id"; return false; }
            if (!BoundedText(setting.Label, 1, 80)) { error = "invalid setting label"; return false; }
            if (!BoundedText(setting.Value, 0, 256)) { error = "setting value too long"; return false; }
            if (setting.Kind == SuiteSettingKind.Bool &&
                !string.Equals(setting.Value, "true", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(setting.Value, "false", StringComparison.OrdinalIgnoreCase))
            {
                error = "invalid bool value";
                return false;
            }
            if (setting.Kind == SuiteSettingKind.Choice)
            {
                HashSet<string> optionSet = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < setting.Options.Count; i++)
                {
                    string option = setting.Options[i];
                    if (string.IsNullOrEmpty(option) || option.Length > 64) { error = "invalid choice option"; return false; }
                    if (!optionSet.Add(option)) { error = "duplicate choice option"; return false; }
                }
                if (setting.Mutable)
                {
                    if (setting.Options.Count == 0) { error = "mutable choice requires options"; return false; }
                    if (!optionSet.Contains(setting.Value)) { error = "choice value not in options"; return false; }
                }
            }
            return true;
        }

        private static bool ValidId(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length > max) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')) return false;
            }
            return true;
        }

        private static bool BoundedText(string value, int min, int max)
        {
            if (value == null) return min == 0;
            return value.Length >= min && value.Length <= max;
        }
    }
}
