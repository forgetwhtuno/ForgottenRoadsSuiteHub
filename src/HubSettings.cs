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
        // placement". Values greater than 1 are absolute pixels written by the pre-0.3.0 OnGUI Hub
        // and are migrated automatically on first read (SuiteUiGeometry.InterpretStoredAxis).

        [Config("LauncherX", "UI", "Saved MODS launcher X position, normalized 0-1 across the screen width. -1 places it near the top-right on first use.")]
        public float LauncherX = -1f;

        [Config("LauncherY", "UI", "Saved MODS launcher Y position, normalized 0-1 up the screen height. -1 places it near the top-right on first use.")]
        public float LauncherY = -1f;

        [Config("WindowX", "UI", "Saved Suite Hub window X position, normalized 0-1 across the screen width. -1 centers the window on first use.")]
        public float WindowX = -1f;

        [Config("WindowY", "UI", "Saved Suite Hub window Y position, normalized 0-1 up the screen height. -1 centers the window on first use.")]
        public float WindowY = -1f;

        [Config("WindowWidth", "UI", "Suite Hub window width in pixels.")]
        public float WindowWidth = 620f;

        [Config("WindowHeight", "UI", "Suite Hub window height in pixels.")]
        public float WindowHeight = 430f;

        [Config("DeveloperUi", "Developer", "Show Suite Hub developer diagnostics and developer-level settings exposed by modules.")]
        public bool DeveloperUi = false;

        [Config("UiDiagnostics", "Developer", "Log bounded Suite Hub UI lifecycle diagnostics as [HubUI] lines: canvas/launcher/window creation with the actual drag components read back off each handle, pointer-down on drag grips, module descriptor counts on selection, readiness transitions, and rebuild counters. Never logs per frame. Temporary aid for the 0.3.0 production regression; safe to leave on.")]
        public bool UiDiagnostics = true;

        [Config("HubInteractionValidated", "Developer", "Set true only after a live run confirms Suite Hub click/drag/camera-containment all work end to end. Reserved for future consumption by sibling mods' launcher-suppression policy (see AGENTS.md); currently informational only, since existing sibling SuiteUiPolicy.IsHubAvailable() checks only for Hub's presence, not this flag.")]
        public bool HubInteractionValidated = false;
    }
}
