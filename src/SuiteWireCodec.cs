using System;
using System.Collections.Generic;

namespace ErenshorSuiteHub
{
    // Aura transports only BCL primitives/strings. Query-style key/value payloads avoid a shared
    // contract DLL and therefore preserve Hub optionality and independent mod load/unload.
    internal static class SuiteWireCodec
    {
        internal const int MaxPayloadLength = 8192;

        internal static SuiteModuleDescriptor ParseModuleDescriptor(string payload, string expectedModuleId, out string error)
        {
            error = null;
            Dictionary<string, string> f;
            if (!TryParseFields(payload, out f, out error)) return null;

            int protocol;
            if (!int.TryParse(Get(f, "protocol"), out protocol)) { error = "protocol missing"; return null; }
            SuiteModuleDescriptor d = new SuiteModuleDescriptor();
            d.ProtocolVersion = protocol;
            d.ModuleId = Get(f, "module");
            d.DisplayName = Get(f, "display");
            d.Version = Get(f, "version");
            d.Summary = Get(f, "summary");
            d.Status = Get(f, "status");
            d.Warning = Get(f, "warning");

            string actions = Get(f, "actions");
            if (!string.IsNullOrEmpty(actions))
            {
                string[] parts = actions.Split(',');
                for (int i = 0; i < parts.Length; i++) if (parts[i].Length > 0) d.Actions.Add(parts[i]);
            }

            if (!string.Equals(d.ModuleId, expectedModuleId, StringComparison.Ordinal))
            {
                error = "module id mismatch";
                return null;
            }
            if (!SuiteDescriptorValidation.ValidateModule(d, out error)) return null;
            return d;
        }

        internal static List<SuiteSettingDescriptor> ParseSettings(string payload, SuiteSettingTier expectedTier, out string error)
        {
            error = null;
            List<SuiteSettingDescriptor> result = new List<SuiteSettingDescriptor>();
            if (string.IsNullOrEmpty(payload)) return result;
            if (payload.Length > MaxPayloadLength) { error = "settings payload too long"; return null; }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            string[] lines = payload.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                Dictionary<string, string> f;
                if (!TryParseFields(lines[i], out f, out error)) return null;

                SuiteSettingDescriptor s = new SuiteSettingDescriptor();
                s.Id = Get(f, "id");
                s.Label = Get(f, "label");
                s.Value = Get(f, "value");
                s.Mutable = string.Equals(Get(f, "mutable"), "true", StringComparison.OrdinalIgnoreCase);
                if (!TryTier(Get(f, "tier"), out s.Tier) || s.Tier != expectedTier)
                {
                    error = "setting tier mismatch";
                    return null;
                }
                if (!TryKind(Get(f, "type"), out s.Kind)) { error = "invalid setting type"; return null; }
                string options = Get(f, "options");
                if (!string.IsNullOrEmpty(options))
                {
                    string[] optParts = options.Split(',');
                    for (int oi = 0; oi < optParts.Length; oi++) if (optParts[oi].Length > 0) s.Options.Add(optParts[oi]);
                }
                if (!SuiteDescriptorValidation.ValidateSetting(s, out error)) return null;
                if (!ids.Add(s.Id)) { error = "duplicate setting id"; return null; }
                result.Add(s);
            }
            return result;
        }

        internal static bool TryParseFields(string payload, out Dictionary<string, string> fields, out string error)
        {
            fields = new Dictionary<string, string>(StringComparer.Ordinal);
            error = null;
            if (payload == null) { error = "payload is null"; return false; }
            if (payload.Length > MaxPayloadLength) { error = "payload too long"; return false; }
            if (payload.Length == 0) { error = "payload is empty"; return false; }

            string[] pairs = payload.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                if (pairs[i].Length == 0) continue;
                int eq = pairs[i].IndexOf('=');
                if (eq <= 0) { error = "invalid field"; return false; }
                string key;
                string value;
                try
                {
                    key = Uri.UnescapeDataString(pairs[i].Substring(0, eq));
                    value = Uri.UnescapeDataString(pairs[i].Substring(eq + 1));
                }
                catch { error = "invalid escaping"; return false; }
                if (key.Length == 0 || key.Length > 64 || fields.ContainsKey(key))
                {
                    error = "invalid or duplicate field";
                    return false;
                }
                fields.Add(key, value);
            }
            return true;
        }

        private static string Get(Dictionary<string, string> f, string key)
        {
            string value;
            return f.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static bool TryTier(string value, out SuiteSettingTier tier)
        {
            if (string.Equals(value, "basic", StringComparison.OrdinalIgnoreCase)) { tier = SuiteSettingTier.Basic; return true; }
            if (string.Equals(value, "advanced", StringComparison.OrdinalIgnoreCase)) { tier = SuiteSettingTier.Advanced; return true; }
            if (string.Equals(value, "developer", StringComparison.OrdinalIgnoreCase)) { tier = SuiteSettingTier.Developer; return true; }
            tier = SuiteSettingTier.Basic;
            return false;
        }

        private static bool TryKind(string value, out SuiteSettingKind kind)
        {
            if (string.Equals(value, "bool", StringComparison.OrdinalIgnoreCase)) { kind = SuiteSettingKind.Bool; return true; }
            if (string.Equals(value, "text", StringComparison.OrdinalIgnoreCase)) { kind = SuiteSettingKind.Text; return true; }
            if (string.Equals(value, "number", StringComparison.OrdinalIgnoreCase)) { kind = SuiteSettingKind.Number; return true; }
            if (string.Equals(value, "choice", StringComparison.OrdinalIgnoreCase)) { kind = SuiteSettingKind.Choice; return true; }
            kind = SuiteSettingKind.Text;
            return false;
        }
    }
}
