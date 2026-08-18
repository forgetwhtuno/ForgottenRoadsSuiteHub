using System;

namespace ErenshorSuiteHub
{
    internal static class DiscoverabilityAuditTests
    {
        internal static int RunAll()
        {
            string[] required = { "deepsims", "partytools", "follow", "campmaster", "duel", "pvp", "nemesis", "crafting", "contracts", "guildlife", "journal" };
            for (int i = 0; i < required.Length; i++)
            {
                SuiteModuleDefinition definition = SuiteModuleCatalog.Find(required[i]);
                if (definition == null) throw new Exception("discoverability catalog missing " + required[i]);
                if (!definition.HasDedicatedPanel) throw new Exception("mouse fallback missing for " + required[i]);
                if (string.IsNullOrEmpty(definition.FallbackInterface)) throw new Exception("fallback description missing for " + required[i]);
            }
            return required.Length * 3;
        }
    }
}
