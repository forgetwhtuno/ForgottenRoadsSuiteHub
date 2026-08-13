using System;
using System.Collections.Generic;
using System.IO;
using Lunaris;
using Lunaris.Config;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSuiteHub
{
    // Phase 1 skeleton: a compact launcher, one movable window with exactly one working tab
    // (Overview), and simple file-presence discovery of the other suite mods' plugin DLLs. No
    // per-mod tabs, no live mod registration, no Aura API usage, no dependency in either
    // direction between the Hub and any other suite mod -- see AGENTS.md and README.md.
    [LunarisPlugin(PluginGuid, PluginVersion, "forgetwhtuno",
        "Compact launcher and overview window for the Erenshor mod suite. Phase 1: Overview tab and installed-mod discovery only.")]
    [LunarisPermission(LunarisPermission.Harmony)]
    public sealed class ErenshorSuiteHubPlugin : LunarisPlugin
    {
        internal const string PluginGuid = "forgetwhtuno.erenshor.suitehub";
        internal const string PluginName = "Erenshor Suite Hub";
        internal const string PluginVersion = "0.1.0";

        internal static ErenshorSuiteHubPlugin Instance;
        private Harmony _harmony;

        private HubSettings _settings;
        private HubConfigEntry<float> _launcherX;
        private HubConfigEntry<float> _launcherY;
        private HubConfigEntry<float> _windowX;
        private HubConfigEntry<float> _windowY;
        private HubConfigEntry<float> _windowWidth;
        private HubConfigEntry<float> _windowHeight;

        private HubWindow _window;
        private HubLauncher _launcher;
        private Rect _windowRect;
        private Rect _launcherRect;
        private bool _open;
        private bool _launcherDirty;
        private float _launcherSaveAfter;
        private bool _cursorVisibleBeforeOpen;
        private CursorLockMode _cursorLockBeforeOpen;

        // Toggle/close requests observed in OnGUI are applied in Update, never mid-OnGUI. IMGUI
        // dispatches several event passes (Layout, input, Repaint) per rendered frame; flipping
        // _open in the middle of that sequence desyncs GUI.Window's Layout/Repaint bookkeeping.
        // This exact bug class was live-confirmed and fixed this session in ErenshorContracts,
        // then Guild Life and PvP. Deferring the mutation to Update keeps _open constant for the
        // whole of every OnGUI pass in a given frame.
        private bool _pendingToggle;
        private bool _pendingClose;

        private List<ModPresence> _detectedMods = new List<ModPresence>();
        private string _pluginsDirectory = string.Empty;

        private void Awake()
        {
            Instance = this;
            _settings = new HubSettings();
            Config.Register(ref _settings);
            InitializeConfigEntries();

            _pluginsDirectory = ResolvePluginsDirectory();
            _detectedMods = ModDiscovery.Scan(_pluginsDirectory);

            _window = new HubWindow();
            _launcher = new HubLauncher();
            _windowRect = ResolveInitialWindowRect();
            _launcherRect = ResolveInitialLauncherRect();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Logging.LogInfo("Erenshor Suite Hub " + PluginVersion +
                " loaded. Compact MODS launcher appears once a character is loaded into the world. " +
                "Phase 1 skeleton: Overview tab and file-presence mod discovery only.");
        }

        // The Hub's own DLL is loaded from <GameDir>\plugins\ErenshorSuiteHub.dll. Sibling suite
        // mods' DLLs (ErenshorJournal.dll etc.) sit directly alongside it in that same folder --
        // see ErenshorContracts's Awake(), which builds its data path as
        // AppContext.BaseDirectory + "plugins" + "config" + ... This only makes sense if
        // AppContext.BaseDirectory is the game root, one level *above* plugins, so the plugins
        // folder itself is AppContext.BaseDirectory + "plugins". This has not been independently
        // re-verified against a live process for this repo; it follows the same assumption every
        // other native-Lunaris mod in the suite already relies on.
        private static string ResolvePluginsDirectory()
        {
            try { return Path.Combine(AppContext.BaseDirectory, "plugins"); }
            catch { return string.Empty; }
        }

        private void InitializeConfigEntries()
        {
            _launcherX = new HubConfigEntry<float>(delegate { return _settings.LauncherX; }, delegate(float v) { _settings.LauncherX = v; });
            _launcherY = new HubConfigEntry<float>(delegate { return _settings.LauncherY; }, delegate(float v) { _settings.LauncherY = v; });
            _windowX = new HubConfigEntry<float>(delegate { return _settings.WindowX; }, delegate(float v) { _settings.WindowX = v; });
            _windowY = new HubConfigEntry<float>(delegate { return _settings.WindowY; }, delegate(float v) { _settings.WindowY = v; });
            _windowWidth = new HubConfigEntry<float>(delegate { return _settings.WindowWidth; }, delegate(float v) { _settings.WindowWidth = v; });
            _windowHeight = new HubConfigEntry<float>(delegate { return _settings.WindowHeight; }, delegate(float v) { _settings.WindowHeight = v; });
        }

        private void Update()
        {
            try
            {
                // Apply any toggle/close requested during last frame's OnGUI passes now, before
                // this frame's OnGUI runs, so _open is stable for every event pass in the frame.
                if (_pendingClose)
                {
                    _pendingClose = false;
                    if (_open) CloseWindow();
                }
                if (_pendingToggle)
                {
                    _pendingToggle = false;
                    ToggleWindow();
                }

                if (!IsLocalCharacterReady() && _open) CloseWindow();

                if (_launcherDirty && Time.unscaledTime >= _launcherSaveAfter) PersistLauncherRect();
            }
            catch (Exception ex)
            {
                Logging.LogError("Erenshor Suite Hub update failed: " + ex);
            }
        }

        private void OnGUI()
        {
            try
            {
                // Recomputed fresh here too (not just read from a field Update() last wrote) so
                // the launcher/window can never be drawn for a stray frame if OnGUI happens to
                // run ahead of Update() in Unity's event ordering. NOT READY -> draw nothing.
                if (!IsLocalCharacterReady())
                {
                    if (_open) CloseWindow();
                    return;
                }

                if (_open && _window != null)
                {
                    _windowRect = ClampWindowRect(_window.Draw(_windowRect, PluginVersion, _detectedMods));
                    if (_window.RequestClose) _pendingClose = true;
                }

                if (_launcher != null)
                {
                    Rect previous = _launcherRect;
                    _launcherRect = ClampLauncherRect(_launcher.Draw(_launcherRect, _open));
                    if (!RectsNearlyEqual(previous, _launcherRect)) MarkLauncherDirty();
                    if (_launcher.RequestToggle) _pendingToggle = true;
                }
            }
            catch (Exception ex)
            {
                Logging.LogError("Erenshor Suite Hub UI failed: " + ex);
                if (_open) CloseWindow();
            }
        }

        // True while the pointer (already converted to GUI screen-space by the caller) is over
        // the Hub window or its launcher button. The click-passthrough Harmony patches below use
        // this so a click on the Hub cannot also drop the player's world target or spin the
        // camera.
        internal bool PointerIsOverUi(Vector2 guiPoint)
        {
            if (_open && _windowRect.Contains(guiPoint)) return true;
            if (_launcherRect.Contains(guiPoint)) return true;
            return false;
        }

        // Verified player-ready signal (not scene-name matching). Same exact pattern already
        // reused this session by Journal, Contracts, and Guild Life. Recomputed every frame
        // cheaply; never cached across scene loads.
        private static bool IsLocalCharacterReady()
        {
            try
            {
                return !GameData.InCharSelect && GameData.PlayerControl != null && GameData.PlayerControl.Myself != null &&
                    GameData.PlayerControl.Myself.MyStats != null && GameData.PlayerControl.Myself.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        private void OnDestroy()
        {
            try { SuiteHubCameraLookPatch.Restore(); } catch { }
            try { PersistLauncherRect(); } catch { }
            try { if (_window != null) _window.Dispose(); } catch { }
            try { if (_launcher != null) _launcher.Dispose(); } catch { }
            try { if (_open) RestoreCursor(); } catch { }
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { }
            _window = null;
            _launcher = null;
            if (Instance == this) Instance = null;
        }

        private void ToggleWindow()
        {
            if (_open) CloseWindow();
            else OpenWindow();
        }

        private void OpenWindow()
        {
            if (_open) return;
            // Re-scan on every open so the Overview tab reflects the plugins folder as it
            // currently is, not only as it was at Awake time (a mod could have been added or
            // removed from the folder between game launches, though not usually mid-session).
            _detectedMods = ModDiscovery.Scan(_pluginsDirectory);
            _open = true;
            _cursorVisibleBeforeOpen = Cursor.visible;
            _cursorLockBeforeOpen = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void CloseWindow()
        {
            if (!_open) return;
            _open = false;
            PersistWindowRect();
            RestoreCursor();
        }

        private void RestoreCursor()
        {
            Cursor.visible = _cursorVisibleBeforeOpen;
            Cursor.lockState = _cursorLockBeforeOpen;
        }

        private void MarkLauncherDirty()
        {
            _launcherDirty = true;
            _launcherSaveAfter = Time.unscaledTime + 0.8f;
        }

        private Rect ResolveInitialWindowRect()
        {
            float width = Mathf.Clamp(_windowWidth.Value, 360f, Mathf.Max(360f, Screen.width - 20f));
            float height = Mathf.Clamp(_windowHeight.Value, 260f, Mathf.Max(260f, Screen.height - 20f));
            float x = _windowX.Value < 0f ? (Screen.width - width) * 0.5f : _windowX.Value;
            float y = _windowY.Value < 0f ? (Screen.height - height) * 0.5f : _windowY.Value;
            return ClampWindowRect(new Rect(x, y, width, height));
        }

        private Rect ResolveInitialLauncherRect()
        {
            float x = _launcherX.Value < 0f ? Mathf.Max(0f, Screen.width - HubLauncher.Width - 18f) : _launcherX.Value;
            float y = _launcherY.Value < 0f ? 8f : _launcherY.Value;
            return ClampLauncherRect(new Rect(x, y, HubLauncher.Width, HubLauncher.Height));
        }

        private static Rect ClampWindowRect(Rect rect)
        {
            float maxWidth = Mathf.Max(360f, Screen.width - 20f);
            float maxHeight = Mathf.Max(260f, Screen.height - 20f);
            rect.width = Mathf.Clamp(rect.width, 360f, maxWidth);
            rect.height = Mathf.Clamp(rect.height, 260f, maxHeight);
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - rect.height));
            return rect;
        }

        private static Rect ClampLauncherRect(Rect rect)
        {
            rect.width = HubLauncher.Width;
            rect.height = HubLauncher.Height;
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - rect.height));
            return rect;
        }

        private void PersistWindowRect()
        {
            if (_windowX == null || _windowY == null || _windowWidth == null || _windowHeight == null) return;
            Rect rect = ClampWindowRect(_windowRect);
            _windowX.Value = rect.x;
            _windowY.Value = rect.y;
            _windowWidth.Value = rect.width;
            _windowHeight.Value = rect.height;
            Config.Save();
        }

        private void PersistLauncherRect()
        {
            if (_launcherX == null || _launcherY == null) return;
            Rect rect = ClampLauncherRect(_launcherRect);
            _launcherX.Value = rect.x;
            _launcherY.Value = rect.y;
            Config.Save();
            _launcherDirty = false;
        }

        private static bool RectsNearlyEqual(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) < 0.25f &&
                   Mathf.Abs(a.y - b.y) < 0.25f &&
                   Mathf.Abs(a.width - b.width) < 0.25f &&
                   Mathf.Abs(a.height - b.height) < 0.25f;
        }
    }

    // IMGUI doesn't own the raw click Erenshor reads here, so a click on the Hub window or its
    // launcher would otherwise also affect the world (deselect target, move camera). Same
    // pattern as Journal/Contracts/Guild Life/PvP's own click-through guards.
    [HarmonyPatch(typeof(PlayerControl), "LeftClick")]
    internal static class SuiteHubPanelLeftClickPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            try
            {
                if (ErenshorSuiteHubPlugin.Instance == null) return true;
                Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                return !ErenshorSuiteHubPlugin.Instance.PointerIsOverUi(mouse);
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(csMouseOrbit), "LateUpdate")]
    internal static class SuiteHubCameraLookPatch
    {
        private static csMouseOrbit _muted;
        private static float _mutedX;
        private static float _mutedY;

        internal static void Restore()
        {
            csMouseOrbit orbit = _muted;
            _muted = null;
            if (orbit == null) return;
            try { orbit.xSpeed = _mutedX; orbit.ySpeed = _mutedY; } catch { }
        }

        [HarmonyPrefix]
        private static void Prefix(csMouseOrbit __instance)
        {
            Restore();
            try
            {
                if (__instance == null || ErenshorSuiteHubPlugin.Instance == null) return;
                Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (!ErenshorSuiteHubPlugin.Instance.PointerIsOverUi(mouse)) return;
                _mutedX = __instance.xSpeed;
                _mutedY = __instance.ySpeed;
                __instance.xSpeed = 0f;
                __instance.ySpeed = 0f;
                _muted = __instance;
            }
            catch { }
        }

        [HarmonyPostfix]
        private static void Postfix() { Restore(); }
    }
}
