using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Lunaris;
using Lunaris.Config;
using Lunaris.IPC;
using UnityEngine;

namespace ErenshorSuiteHub
{
    [LunarisPlugin(PluginGuid, PluginVersion, "forgetwhtuno",
        "One launcher and player-facing Hub for the optional Erenshor mod suite.")]
    [LunarisPermission(LunarisPermission.FileAccess | LunarisPermission.Harmony)]
    public sealed class ErenshorSuiteHubPlugin : LunarisPlugin
    {
        internal const string PluginGuid = "forgetwhtuno.erenshor.suitehub";
        internal const string PluginName = "Erenshor Suite Hub";
        internal const string PluginVersion = "0.4.0";

        internal static ErenshorSuiteHubPlugin Instance;

        private Harmony _harmony;
        private HubSettings _settings;
        private HubConfigEntry<float> _launcherX;
        private HubConfigEntry<float> _launcherY;
        private HubConfigEntry<float> _windowX;
        private HubConfigEntry<float> _windowY;
        private HubConfigEntry<float> _windowWidth;
        private HubConfigEntry<float> _windowHeight;

        private SuiteHubUi _ui;
        private bool _uiBuildAttempted;
        private bool _pendingToggle;
        private bool _pendingClose;
        private bool _pendingReset;
        private GameplayReadinessStage _lastLoggedStage = GameplayReadinessStage.CharacterSelect;

        private readonly GameplayReadinessPolicy _readiness = new GameplayReadinessPolicy();
        private readonly SuiteModuleRegistry _registry = new SuiteModuleRegistry();
        private readonly Dictionary<string, AuraModuleBridge> _bridges =
            new Dictionary<string, AuraModuleBridge>(StringComparer.Ordinal);
        private List<ModPresence> _detectedMods = new List<ModPresence>();
        private string _pluginsDirectory = string.Empty;
        private float _nextDiscoveryPoll;
        private float _nextBridgePoll;
        private IAuraProvider<string> _presenceProvider;

        private void Awake()
        {
            Instance = this;
            _settings = new HubSettings();
            Config.Register(ref _settings);
            InitializeConfigEntries();

            SuiteHubDiagnostics.Reset();
            SuiteHubDiagnostics.Enabled = _settings.UiDiagnostics;

            _pluginsDirectory = ResolvePluginsDirectory();
            _detectedMods = ModDiscovery.Scan(_pluginsDirectory);
            for (int i = 0; i < SuiteModuleCatalog.All.Length; i++)
            {
                SuiteModuleDefinition def = SuiteModuleCatalog.All[i];
                _bridges[def.Id] = new AuraModuleBridge(this, _registry, def.Id);
            }

            CreateUi();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            // Tiny, optional live-presence publisher so a mod's own launcher-suppression logic can
            // ask "is Hub actually alive" via Aura instead of scanning the scene for Hub components.
            // This never advertises actions/settings for a "module" -- it is a Hub self-describe,
            // separate from the per-mod v1 wire contract in AuraModuleBridge.
            try
            {
                _presenceProvider = this.IPCAuraProvider<string>("forgetwhtuno.erenshor.suitehub.v1.describe");
                _presenceProvider.RegisterFunc(DescribeHubPresence);
            }
            catch (Exception ex) { Logging.LogWarning("Suite Hub presence provider unavailable: " + ex.GetType().Name); }

            Logging.LogInfo("Erenshor Suite Hub " + PluginVersion +
                " loaded. UI is gated by native character/world readiness and appears only after gameplay control is established.");
        }

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

        // ---- UI wiring ---------------------------------------------------------------------------

        private void CreateUi()
        {
            _ui = new SuiteHubUi();
            _ui.GetMods = delegate { return _detectedMods; };
            _ui.GetHubVersion = delegate { return PluginVersion; };
            _ui.GetDeveloperEnabled = delegate { return _settings != null && _settings.DeveloperUi; };
            _ui.GetReadinessText = delegate { return ReadinessDiagnostic; };
            _ui.OnRequestToggle = delegate { _pendingToggle = true; };
            _ui.OnRequestClose = delegate { _pendingClose = true; };
            _ui.OnRequestResetPosition = delegate { _pendingReset = true; };
            _ui.PersistLauncherNormalized = PersistLauncherNormalized;
            _ui.PersistWindowNormalized = PersistWindowNormalized;
        }

        // The Canvas is built lazily on the first frame gameplay is actually ready, so the Hub never
        // exists during character select or zoning, and so EventSystem.current is guaranteed live.
        private void EnsureUiBuilt()
        {
            if (_ui == null || _ui.IsBuilt) return;
            if (!_readiness.IsReady) return;

            if (!_ui.Build(LoadLauncherNormalized(), LoadWindowNormalized(), LoadWindowSize()))
            {
                if (!_uiBuildAttempted)
                {
                    _uiBuildAttempted = true;
                    Logging.LogWarning("Suite Hub UI could not be created: no active EventSystem in the scene. " +
                        "Refusing to create a second EventSystem; the Hub will retry when one exists.");
                }
                return;
            }
            _uiBuildAttempted = false;
        }

        private void Update()
        {
            try
            {
                RefreshReadiness();

                // Readiness drives SetActive and window close. If it oscillates, the whole canvas
                // is toggled and the window is repeatedly closed - which would look exactly like
                // heavy flicker and "nothing is draggable". Log transitions only, never per frame.
                if (_readiness.Stage != _lastLoggedStage)
                {
                    GameplayReadinessStage previous = _lastLoggedStage;
                    _lastLoggedStage = _readiness.Stage;
                    SuiteHubDiagnostics.Log("readiness " + previous + " -> " + _readiness.Stage);
                }

                EnsureUiBuilt();

                if (_ui != null && _ui.IsBuilt)
                {
                    // Readiness gate: the launcher is only visible/interactive once the player has
                    // real gameplay control. Hiding the root also releases any drag we owned.
                    _ui.SetVisible(_readiness.IsReady);
                    _ui.Tick();
                }

                if (!_readiness.IsReady)
                {
                    // Zoning / character select: make sure the window is closed and no drag flag is
                    // left latched from an interrupted gesture.
                    if (_ui != null && _ui.IsWindowOpen) CloseWindow();
                    SuiteDragGuard.ForceReleaseIfHubOwned();
                }

                if (_pendingClose) { _pendingClose = false; CloseWindow(); }
                if (_pendingToggle)
                {
                    _pendingToggle = false;
                    if (_readiness.IsReady) ToggleWindow();
                }
                if (_pendingReset) { _pendingReset = false; ResetPositions(); }

                if (_readiness.IsReady)
                {
                    if (Time.unscaledTime >= _nextDiscoveryPoll)
                    {
                        _nextDiscoveryPoll = Time.unscaledTime + 2f;
                        List<ModPresence> refreshed = ModDiscovery.Scan(_pluginsDirectory);
                        if (DiscoveryChanged(_detectedMods, refreshed))
                        {
                            _detectedMods = refreshed;
                            // A mod being installed/uninstalled changes the nav list itself.
                            if (_ui != null && _ui.IsWindowOpen) _ui.QueueNavStructureRebuild();
                        }
                    }
                    if (Time.unscaledTime >= _nextBridgePoll)
                    {
                        _nextBridgePoll = Time.unscaledTime + 1f;
                        PollModuleBridges();
                        // Rebuild ONLY if the polled data actually changed. Rebuilding
                        // unconditionally on this 1 Hz cadence tore the whole window down and
                        // recreated it every second - the primary cause of the 0.3.0 flicker.
                        if (_ui != null && _ui.IsWindowOpen) _ui.QueueRebuildIfContentChanged();
                    }
                }

                SuiteHubDiagnostics.TickReport(Time.unscaledTime);
            }
            catch (Exception ex)
            {
                Logging.LogError("Erenshor Suite Hub update failed: " + ex);
                // Exception recovery: never strand the native drag flag.
                try { SuiteDragGuard.ForceReleaseIfHubOwned(); } catch { }
            }
        }

        private static bool DiscoveryChanged(List<ModPresence> a, List<ModPresence> b)
        {
            if (a == null || b == null) return true;
            if (a.Count != b.Count) return true;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Installed != b[i].Installed) return true;
                if (!string.Equals(a[i].ModuleId, b[i].ModuleId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private void ToggleWindow()
        {
            if (_ui == null) return;
            if (_ui.IsWindowOpen) CloseWindow(); else OpenWindow();
        }

        private void OpenWindow()
        {
            if (_ui == null || !_ui.IsBuilt || _ui.IsWindowOpen || !_readiness.IsReady) return;
            _detectedMods = ModDiscovery.Scan(_pluginsDirectory);
            PollModuleBridges();
            _ui.View.SetOpen(true);
            _ui.OpenWindow(LoadWindowNormalized(), LoadWindowSize());
        }

        private void CloseWindow()
        {
            if (_ui == null || !_ui.IsWindowOpen) return;
            _ui.View.SetOpen(false);
            _ui.CloseWindow();
        }

        // ---- position persistence ------------------------------------------------------------
        // Stored NORMALIZED (0..1 of screen extent) so a saved layout survives resolution changes.
        // Values written by the previous OnGUI Hub were absolute pixels; SuiteUiGeometry migrates
        // those transparently on first read. Config is written once per completed drag (via
        // SuiteDragGuard.OnDragCompleted), never per drag frame.

        private Vector2 LoadLauncherNormalized()
        {
            float x = SuiteUiGeometry.InterpretStoredAxis(_launcherX.Value, Screen.width);
            float y = SuiteUiGeometry.InterpretStoredAxis(_launcherY.Value, Screen.height);
            // Default placement: top-right, matching where the old launcher lived.
            if (x <= SuiteUiGeometry.Unset) x = Screen.width > 0 ? Mathf.Clamp01((Screen.width - 150f) / Screen.width) : 0.85f;
            if (y <= SuiteUiGeometry.Unset) y = Screen.height > 0 ? Mathf.Clamp01((Screen.height - 40f) / Screen.height) : 0.95f;
            return new Vector2(x, y);
        }

        private Vector2 LoadWindowNormalized()
        {
            float x = SuiteUiGeometry.InterpretStoredAxis(_windowX.Value, Screen.width);
            float y = SuiteUiGeometry.InterpretStoredAxis(_windowY.Value, Screen.height);
            Vector2 size = LoadWindowSize();
            if (x <= SuiteUiGeometry.Unset)
                x = Screen.width > 0 ? Mathf.Clamp01((Screen.width - size.x) * 0.5f / Screen.width) : 0.25f;
            if (y <= SuiteUiGeometry.Unset)
                y = Screen.height > 0 ? Mathf.Clamp01((Screen.height - size.y) * 0.5f / Screen.height) : 0.25f;
            return new Vector2(x, y);
        }

        private Vector2 LoadWindowSize()
        {
            float w = _windowWidth.Value;
            float h = _windowHeight.Value;
            if (float.IsNaN(w) || float.IsInfinity(w) || w <= 0f) w = 620f;
            if (float.IsNaN(h) || float.IsInfinity(h) || h <= 0f) h = 430f;
            w = Mathf.Clamp(w, 420f, Mathf.Max(420f, Screen.width - 20f));
            h = Mathf.Clamp(h, 300f, Mathf.Max(300f, Screen.height - 20f));
            return new Vector2(w, h);
        }

        private void PersistLauncherNormalized(Vector2 normalized)
        {
            if (_launcherX == null || _launcherY == null) return;
            _launcherX.Value = normalized.x;
            _launcherY.Value = normalized.y;
            SafeSaveConfig();
        }

        private void PersistWindowNormalized(Vector2 normalized)
        {
            if (_windowX == null || _windowY == null) return;
            _windowX.Value = normalized.x;
            _windowY.Value = normalized.y;
            SafeSaveConfig();
        }

        private void SafeSaveConfig()
        {
            try { Config.Save(); }
            catch (Exception ex) { Logging.LogWarning("Suite Hub could not save layout: " + ex.GetType().Name); }
        }

        private void ResetPositions()
        {
            _launcherX.Value = SuiteUiGeometry.Unset;
            _launcherY.Value = SuiteUiGeometry.Unset;
            _windowX.Value = SuiteUiGeometry.Unset;
            _windowY.Value = SuiteUiGeometry.Unset;
            _windowWidth.Value = 620f;
            _windowHeight.Value = 430f;
            SafeSaveConfig();

            if (_ui == null || !_ui.IsBuilt) return;
            _ui.ApplyLauncherPosition(LoadLauncherNormalized());
            if (_ui.IsWindowOpen) _ui.ApplyWindowPosition(LoadWindowNormalized(), LoadWindowSize());
        }

        // ---- readiness / bridges ----------------------------------------------------------------

        private void RefreshReadiness()
        {
            GameplayReadinessSignals s = new GameplayReadinessSignals();
            try
            {
                s.InCharacterSelect = GameData.InCharSelect;
                s.IsZoning = GameData.Zoning;
                s.HasPlayerControl = GameData.PlayerControl != null;
                if (s.HasPlayerControl)
                {
                    s.HasPlayer = GameData.PlayerControl.Myself != null;
                    if (s.HasPlayer)
                    {
                        s.HasStats = GameData.PlayerControl.Myself.MyStats != null;
                        s.PlayerActive = GameData.PlayerControl.Myself.gameObject.activeInHierarchy;
                    }
                    s.PlayerCanMove = GameData.PlayerControl.CanMove;
                }
                s.HasSimManager = GameData.SimMngr != null;
                s.HasSimGrouping = GameData.SimPlayerGrouping != null;
            }
            catch
            {
                // A partially initialized static graph is normal during login/zoning; missing
                // evidence always fails closed.
            }
            _readiness.Evaluate(s, Time.unscaledTime);
        }

        private string DescribeHubPresence()
        {
            // interactionValidated is reserved for future consumption by sibling mods' own
            // SuiteUiPolicy.IsHubAvailable() launcher-suppression checks. Today every sibling mod's
            // policy only checks for Hub's mere presence (FindObjectsOfType<LunarisPlugin>() for
            // this exact type), so this field currently has no observed effect anywhere -- see
            // AGENTS.md "Launcher-suppression posture" for the interim mitigation.
            return "protocol=1&module=suitehub&display=Suite%20Hub&version=" + Uri.EscapeDataString(PluginVersion) +
                "&status=" + (_readiness.IsReady ? "Ready" : "NotReady") +
                "&interactionValidated=" + (_settings != null && _settings.HubInteractionValidated ? "true" : "false");
        }

        internal string ReadinessDiagnostic
        {
            get { return _readiness.Stage.ToString(); }
        }

        // SuiteHubDiagnostics and the Harmony patch classes are outside this type and cannot reach
        // the inherited Lunaris Logging member directly; route through the live instance.
        internal void LogUi(string message)
        {
            try { Logging.LogInfo(message); } catch (Exception) { }
        }

        private void PollModuleBridges()
        {
            foreach (KeyValuePair<string, AuraModuleBridge> pair in _bridges) pair.Value.Poll();
        }

        internal static SuiteModuleDescriptor GetRegisteredModule(string moduleId)
        {
            return Instance == null ? null : Instance._registry.Get(moduleId);
        }

        internal static AuraModuleBridge GetModuleBridge(string moduleId)
        {
            if (Instance == null) return null;
            AuraModuleBridge bridge;
            return Instance._bridges.TryGetValue(moduleId, out bridge) ? bridge : null;
        }

        internal static bool TrySetModuleSetting(string moduleId, string settingId, string value, out string result)
        {
            AuraModuleBridge bridge = GetModuleBridge(moduleId);
            if (bridge == null) { result = "Suite bridge unavailable"; return false; }
            return bridge.TrySetSetting(settingId, value, out result);
        }

        internal static bool TryInvokeModuleAction(string moduleId, string actionId, string argument, out string result)
        {
            AuraModuleBridge bridge = GetModuleBridge(moduleId);
            if (bridge == null) { result = "Suite bridge unavailable"; return false; }
            return bridge.TryInvokeAction(actionId, argument, out result);
        }

        private void OnDestroy()
        {
            // Order matters: release any drag we own and tear the Canvas down before anything else,
            // so a hot unload can never leave GameData.DraggingUIElement latched true or leave an
            // orphaned Hub canvas behind for a second instance to duplicate.
            try { SuiteDragGuard.ForceReleaseIfHubOwned(); } catch { }
            try { if (_ui != null) _ui.Destroy(); } catch { }
            _ui = null;

            try { foreach (KeyValuePair<string, AuraModuleBridge> pair in _bridges) pair.Value.Disconnect(); } catch { }
            _bridges.Clear();
            try { if (_presenceProvider != null) _presenceProvider.UnregisterFunc(); } catch { }
            _presenceProvider = null;
            _registry.Clear();
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { }
            _readiness.Reset();
            if (Instance == this) Instance = null;
        }

        // Developer/debug recovery only. NOT the supported player access route: the visible MODS
        // control is. If MODS cannot be clicked, the feature is broken even though this works.
        internal void HandleModsChatCommand()
        {
            if (!_readiness.IsReady)
            {
                Logging.LogInfo("[SuiteHub] /mods ignored: gameplay not ready yet (stage=" + _readiness.Stage + ")");
                return;
            }
            _pendingToggle = true;
        }
    }

    // Developer/debug recovery command. Registration mechanism copied exactly from Deep Sims'
    // already-working native chat interception (mods\DeepSim-erenshor\src\DeepSimsPlugin.cs,
    // TypeTextCheckCommandsPatch on TypeText.CheckCommands) rather than inventing a new one.
    // Returning true (unhandled) preserves vanilla unknown-command handling for every other input.
    //
    // This is the ONLY Harmony patch the Suite Hub UI still needs. The previous input/camera
    // compatibility patches were deleted when the Hub moved to retained uGUI - see
    // docs/SUITE_UI_ARCHITECTURE.md for what each one did and why it is now redundant.
    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    internal static class SuiteHubChatCommandPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(TypeText __instance)
        {
            try
            {
                if (ErenshorSuiteHubPlugin.Instance == null || __instance == null || __instance.typed == null) return true;
                string rawText = __instance.typed.text;
                if (string.IsNullOrEmpty(rawText)) return true;
                string trimmed = rawText.Trim();
                if (!string.Equals(trimmed, "/mods", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(trimmed, "/suitehub", StringComparison.OrdinalIgnoreCase))
                    return true;

                try { __instance.typed.text = string.Empty; } catch { }
                ErenshorSuiteHubPlugin.Instance.HandleModsChatCommand();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
