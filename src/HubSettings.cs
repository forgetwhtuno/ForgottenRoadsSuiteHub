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

        [Config("LauncherX", "UI", "Saved Suite Hub launcher X position. -1 places it near the right side of the screen on first use.")]
        public float LauncherX = -1f;

        [Config("LauncherY", "UI", "Saved Suite Hub launcher Y position. -1 places it near the top-right on first use.")]
        public float LauncherY = -1f;

        [Config("WindowX", "UI", "Saved Suite Hub window X position. -1 centers the window on first use.")]
        public float WindowX = -1f;

        [Config("WindowY", "UI", "Saved Suite Hub window Y position. -1 centers the window on first use.")]
        public float WindowY = -1f;

        [Config("WindowWidth", "UI", "Suite Hub window width in pixels.")]
        public float WindowWidth = 480f;

        [Config("WindowHeight", "UI", "Suite Hub window height in pixels.")]
        public float WindowHeight = 360f;
    }
}
