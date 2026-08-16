using System;
using Lunaris.Config;

namespace ErenshorSuiteHub
{
    // Loader-neutral ConfigEntry-style shim, matching the pattern already used by every other
    // suite launcher-owning mod (Journal/Contracts/Guild Life/PvP) so this migration stays
    // mechanical and predictable.
    internal sealed class HubConfigEntry<T>
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;

        internal HubConfigEntry(Func<T> get, Action<T> set)
        {
            _get = get;
            _set = set;
        }

        internal T Value
        {
            get { return _get(); }
            set { _set(value); }
        }
    }

    internal sealed class HubSettings
    {
        public HubSettings() { }

        // Position values are NORMALIZED (0..1 of screen extent), measured from the bottom-left
        // corner, so a saved layout survives a resolution change. -1 means "unset, use the default
        // placement". Values greater than 1 are legacy pixels written by the pre-0.3.0 OnGUI Hub;
        // their coordinate system is incompatible, so they are rejected and fall back to defaults.

        [Config("LauncherX", "UI", "Saved MODS launcher X position, normalized 0-1 across the screen width. -1 places it near the top-right on first use.")]
        public float LauncherX = -1f;

        [Config("LauncherY", "UI", "Saved MODS launcher Y position, normalized 0-1 up the screen height. -1 places it near the top-right on first use.")]
        public float LauncherY = -1f;

        [Config("WindowX", "UI", "Saved Suite Hub window X position, normalized 0-1 across the screen width. -1 centers the window on first use.")]
        public float WindowX = -1f;

        [Config("WindowY", "UI", "Saved Suite Hub window Y position, normalized 0-1 up the screen height. -1 centers the window on first use.")]
        public float WindowY = -1f;

        [Config("WindowWidth", "UI", "Maximum Suite Hub window width in pixels.")]
        public float WindowWidth = 620f;

        [Config("WindowHeight", "UI", "Maximum Suite Hub window height in pixels. Smaller pages fit to a compact height when opened.")]
        public float WindowHeight = 430f;

        [Config("HiddenShortcuts", "Dock", "Comma-separated Suite module ids hidden from the expanded MODS dock. Newly available safe panel shortcuts are shown by default until explicitly hidden here.")]
        public string DockHiddenShortcuts = string.Empty;

        [Config("ConsolidatedLauncherModules", "Dock", "Internal one-time migration ledger. A module is listed after its existing standalone-with-Hub launcher preference has been safely turned off through that module's own Suite setting contract, so the unified dock is the default launcher surface without rewriting sibling config files directly.")]
        public string DockConsolidatedLauncherModules = string.Empty;

        [Config("DeveloperUi", "Developer", "Show Suite Hub developer diagnostics and developer-level settings exposed by modules.")]
        public bool DeveloperUi = false;

        [Config("UiDiagnostics", "Developer", "Opt in to bounded Suite Hub UI lifecycle diagnostics as [HubUI] lines. Never logs per frame; release default is OFF.")]
        public bool UiDiagnostics = false;

        [Config("HubInteractionValidated", "Developer", "Manual live-validation marker for Suite Hub click/drag/camera containment. Informational only; launcher fallback uses the live Ready + uiAvailable presence capability instead of this flag.")]
        public bool HubInteractionValidated = false;
    }
}
