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
        "One launcher and player-facing Hub for the optional Forgotten Roads for Erenshor collection.")]
    [LunarisPermission(LunarisPermission.FileAccess | LunarisPermission.Harmony)]
    public sealed class ErenshorSuiteHubPlugin : LunarisPlugin, ISuiteQuickCloseRuntime
    {
        internal const string PluginGuid = "forgetwhtuno.erenshor.suitehub";
        internal const string PluginName = "Forgotten Roads Hub";
        internal const string PluginVersion = "0.5.2";

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
        private bool _pendingOpenSuite;
        private string _pendingDockModuleOpen = string.Empty;
        private bool _pendingClose;
        private bool _pendingReset;
        private GameplayReadinessStage _lastLoggedStage = GameplayReadinessStage.CharacterSelect;
        private HashSet<string> _hiddenDockShortcuts = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _consolidatedLauncherModules = new HashSet<string>(StringComparer.Ordinal);

        private readonly GameplayReadinessPolicy _readiness = new GameplayReadinessPolicy();
        private readonly SuiteModuleRegistry _registry = new SuiteModuleRegistry();
        private readonly Dictionary<string, AuraModuleBridge> _bridges =
            new Dictionary<string, AuraModuleBridge>(StringComparer.Ordinal);
        // Structural capabilities are discovered on the normal bridge cadence and retained in the
        // registry. Escape never scans assemblies/reflection providers; it only refreshes dynamic
        // ui.state for this fixed catalog and consults the cached descriptor action list.
        private readonly List<string> _quickCloseModuleIds = new List<string>();
        private readonly HashSet<string> _quickCloseContractWarnings =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _quickCloseFaultWarnings =
            new HashSet<string>(StringComparer.Ordinal);
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
            _hiddenDockShortcuts = SuiteDockPolicy.ParseHiddenShortcuts(_settings.DockHiddenShortcuts);
            _consolidatedLauncherModules = SuiteDockPolicy.ParseHiddenShortcuts(_settings.DockConsolidatedLauncherModules);

            SuiteHubDiagnostics.Reset();
            SuiteHubDiagnostics.Enabled = _settings.UiDiagnostics;

            _pluginsDirectory = ResolvePluginsDirectory();
            _detectedMods = ModDiscovery.Scan(_pluginsDirectory);
            for (int i = 0; i < SuiteModuleCatalog.All.Length; i++)
            {
                SuiteModuleDefinition def = SuiteModuleCatalog.All[i];
                _bridges[def.Id] = new AuraModuleBridge(this, _registry, def.Id);
                _quickCloseModuleIds.Add(def.Id);
            }

            CreateUi();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            SuiteNativeEscapeCompatibility.TryBind(_harmony);

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

            Logging.LogInfo("Forgotten Roads Hub " + PluginVersion +
                " loaded. UI is gated by native character/world readiness and appears only after gameplay control is established.");
            if (!SuiteNativeEscapeCompatibility.IsNativeConsumeBound)
                Logging.LogInfo("Suite quick-close native consumption is disabled: " +
                    SuiteNativeEscapeCompatibility.BindingStatus +
                    ". Suite Escape polling is disabled; use explicit close controls until a native consume boundary is verified.");
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
            _ui.OnRequestOpenSuite = delegate { _pendingOpenSuite = true; };
            _ui.OnRequestDockModuleOpen = delegate(string moduleId) { _pendingDockModuleOpen = moduleId ?? string.Empty; };
            _ui.GetDockModules = BuildDockModuleStates;
            _ui.SetDockShortcutVisible = SetDockShortcutVisible;
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
                if (_pendingOpenSuite)
                {
                    _pendingOpenSuite = false;
                    if (_readiness.IsReady)
                    {
                        OpenWindow();
                        if (_ui != null) _ui.CompleteDockLaunch(true);
                    }
                }
                if (!string.IsNullOrEmpty(_pendingDockModuleOpen))
                {
                    string moduleId = _pendingDockModuleOpen;
                    _pendingDockModuleOpen = string.Empty;
                    string result = "Gameplay not ready";
                    bool opened = _readiness.IsReady && TryOpenDockModulePanel(moduleId, out result);
                    if (opened)
                    {
                        if (_ui != null) _ui.CompleteDockLaunch(true);
                    }
                    else if (_ui != null)
                    {
                        _ui.SetDockFeedback(string.IsNullOrEmpty(result) ? "Panel unavailable" : result);
                    }
                }
                if (_pendingReset) { _pendingReset = false; ResetPositions(); }

                // Escape is intentionally not polled here. Until an exact native Escape/menu
                // boundary is proven, Suite UI uses explicit close controls and vanilla Escape is
                // left completely untouched. When the verified Harmony prefix binds, it becomes the
                // single Suite-owned quick-close authority.

                if (_readiness.IsReady)
                {
                    if (Time.unscaledTime >= _nextDiscoveryPoll)
                    {
                        _nextDiscoveryPoll = Time.unscaledTime + 2f;
                        List<ModPresence> refreshed = ModDiscovery.Scan(_pluginsDirectory);
                        if (DiscoveryChanged(_detectedMods, refreshed))
                        {
                            _detectedMods = refreshed;
                            // A mod being installed/uninstalled changes both the full Suite nav
                            // and the compact dock shortcut candidates.
                            if (_ui != null)
                            {
                                if (_ui.IsWindowOpen) _ui.QueueNavStructureRebuild();
                                _ui.QueueDockRebuildIfContentChanged();
                            }
                        }
                    }
                    if (Time.unscaledTime >= _nextBridgePoll)
                    {
                        _nextBridgePoll = Time.unscaledTime + 1f;
                        PollModuleBridges();
                        ConsolidateStandaloneLaunchersOnce();
                        // Rebuild ONLY if structural data actually changed. Dynamic status/ui.state
                        // churn is excluded by both the Suite window and dock signatures.
                        if (_ui != null)
                        {
                            if (_ui.IsWindowOpen) _ui.QueueRebuildIfContentChanged();
                            _ui.QueueDockRebuildIfContentChanged();
                        }
                    }
                }

                SuiteHubDiagnostics.TickReport(Time.unscaledTime);
            }
            catch (Exception ex)
            {
                Logging.LogError("Forgotten Roads Hub update failed: " + ex);
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
        // Values written by the previous OnGUI Hub were incompatible top-left-origin pixels;
        // SuiteUiGeometry rejects them and falls back to known-good defaults. Config is written once per completed drag (via
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
            float maxW = Mathf.Max(1f, Screen.width - SuiteUiGeometry.WindowScreenMargin);
            float minW = Mathf.Min(420f, maxW);
            float maxH = Mathf.Max(1f, Screen.height - SuiteUiGeometry.WindowScreenMargin);
            float minH = Mathf.Min(SuiteUiGeometry.CompactWindowMinHeight, maxH);
            w = Mathf.Clamp(w, minW, maxW);
            h = Mathf.Clamp(h, minH, maxH);
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
            catch (Exception ex) { Logging.LogWarning("Suite Hub could not save config: " + ex.GetType().Name); }
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
            // uiAvailable is the practical launcher-fallback capability: it reports whether the
            // retained Hub UI actually exists right now. The older interactionValidated field is
            // retained as diagnostic/manual validation metadata only; sibling launchers must not
            // stay permanently forced on just because that optional validation bit is false.
            // uiAvailable is also the sibling launcher-ownership claim. It is true only when the
            // retained UI exists AND every installed catalogued dedicated-panel module can be safely
            // opened through a live literal openPanel endpoint. A missing/malformed provider therefore
            // makes siblings fail back to their own launchers instead of being stranded.
            bool uiAvailable = _ui != null && _ui.IsBuilt && CanOwnInstalledLauncherAccess();
            return "protocol=1&module=suitehub&display=Suite%20Hub&version=" + Uri.EscapeDataString(PluginVersion) +
                "&status=" + (_readiness.IsReady ? "Ready" : "NotReady") +
                "&uiAvailable=" + (uiAvailable ? "true" : "false") +
                "&interactionValidated=" + (_settings != null && _settings.HubInteractionValidated ? "true" : "false") +
                "&quickCloseContract=1" +
                "&quickCloseCentral=1" +
                "&quickClose=" + (SuiteNativeEscapeCompatibility.IsNativeConsumeBound ? "1" : "0");
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

        private List<SuiteDockModuleState> BuildDockModuleStates()
        {
            List<SuiteDockModuleState> states = new List<SuiteDockModuleState>();
            for (int i = 0; i < _detectedMods.Count; i++)
            {
                ModPresence presence = _detectedMods[i];
                AuraModuleBridge bridge;
                _bridges.TryGetValue(presence.ModuleId, out bridge);
                SuiteModuleDescriptor descriptor = _registry.Get(presence.ModuleId);
                states.Add(new SuiteDockModuleState
                {
                    ModuleId = presence.ModuleId,
                    DisplayName = descriptor != null && !string.IsNullOrEmpty(descriptor.DisplayName)
                        ? descriptor.DisplayName : presence.DisplayName,
                    // Runtime provider evidence is stronger than an exact DLL filename. This
                    // keeps the dock truthful if a manager/profile changes the physical filename.
                    Installed = presence.Installed || (bridge != null && bridge.HasRuntimeSignal),
                    Descriptor = descriptor,
                    ActionEndpointAvailable = bridge != null && bridge.CanInvokeAction(SuiteDockPolicy.OpenPanelActionId),
                    Hidden = _hiddenDockShortcuts.Contains(presence.ModuleId)
                });
            }
            return states;
        }

        private bool SetDockShortcutVisible(string moduleId, bool visible)
        {
            if (SuiteModuleCatalog.Find(moduleId) == null) return false;
            if (visible) _hiddenDockShortcuts.Remove(moduleId); else _hiddenDockShortcuts.Add(moduleId);
            _settings.DockHiddenShortcuts = SuiteDockPolicy.SerializeHiddenShortcuts(_hiddenDockShortcuts);
            SafeSaveConfig();
            return true;
        }

        // Fail-closed ownership claim used by sibling standalone launcher policies. File presence alone
        // never grants ownership; for every installed module that is catalogued as having a dedicated
        // launcher/panel, Hub must currently hold a valid descriptor AND live action endpoint for
        // literal openPanel. Registration failure therefore keeps uiAvailable=false.
        private bool CanOwnInstalledLauncherAccess()
        {
            return SuiteDockPolicy.CanOwnInstalledDedicatedPanels(BuildDockModuleStates());
        }

        // One-time migration through each module's OWN validated setting.set endpoint. This never edits
        // sibling config files directly. It converts legacy "show my floating launcher even with Hub"
        // defaults into the unified-dock default once safe openPanel access has been proven. The ledger
        // prevents repeated enforcement: after migration, a player may explicitly turn a standalone
        // launcher back on and Hub will respect that choice.
        private void ConsolidateStandaloneLaunchersOnce()
        {
            List<SuiteDockModuleState> states = BuildDockModuleStates();
            bool ledgerChanged = false;
            for (int i = 0; i < states.Count; i++)
            {
                SuiteDockModuleState state = states[i];
                if (state == null || !state.CanLaunch || _consolidatedLauncherModules.Contains(state.ModuleId)) continue;
                AuraModuleBridge bridge;
                if (!_bridges.TryGetValue(state.ModuleId, out bridge) || bridge == null) continue;
                SuiteSettingDescriptor setting = bridge.FindCachedSetting("showLauncher");
                if (setting == null || setting.Kind != SuiteSettingKind.Bool || !setting.Mutable) continue;

                bool migrated = true;
                if (SuiteSettingDisplayPolicy.IsOn(setting.Value))
                {
                    string result;
                    migrated = bridge.TrySetSetting("showLauncher", "false", out result);
                    if (migrated) bridge.Poll();
                    else Logging.LogWarning("Suite dock could not consolidate " + state.ModuleId +
                        " standalone launcher through its setting contract: " + result);
                }

                if (migrated)
                {
                    _consolidatedLauncherModules.Add(state.ModuleId);
                    ledgerChanged = true;
                }
            }
            if (!ledgerChanged) return;
            _settings.DockConsolidatedLauncherModules =
                SuiteDockPolicy.SerializeHiddenShortcuts(_consolidatedLauncherModules);
            SafeSaveConfig();
        }

        private bool TryOpenDockModulePanel(string moduleId, out string result)
        {
            result = "Panel unavailable";
            if (string.IsNullOrEmpty(moduleId) || SuiteModuleCatalog.Find(moduleId) == null) return false;
            AuraModuleBridge bridge;
            if (!_bridges.TryGetValue(moduleId, out bridge) || bridge == null ||
                !bridge.CanInvokeAction(SuiteDockPolicy.OpenPanelActionId)) return false;
            bool ok = bridge.TryInvokeAction(SuiteDockPolicy.OpenPanelActionId, string.Empty, out result);
            if (ok) bridge.Poll();
            return ok;
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
            bool ok = bridge.TrySetSetting(settingId, value, out result);
            // A successful mutation must be visible immediately. Re-read describe + all setting
            // tiers synchronously instead of leaving status/value text stale until the next 1 Hz
            // bridge poll. The UI then reconciles dynamic values in place unless schema changed.
            SuiteSettingMutationRefreshPlan plan = SuiteSettingMutationPolicy.Resolve(ok);
            if (plan.PollAuthoritativeState) bridge.Poll();
            return ok;
        }

        internal static bool TryInvokeModuleAction(string moduleId, string actionId, string argument, out string result)
        {
            AuraModuleBridge bridge = GetModuleBridge(moduleId);
            if (bridge == null) { result = "Suite bridge unavailable"; return false; }
            bool ok = bridge.TryInvokeAction(actionId, argument, out result);
            if (ok) bridge.Poll();
            return ok;
        }

        // Called only by SuiteNativeEscapeCompatibility after a verified native Escape/menu target
        // has been bound. One native keypress dismisses exactly one topmost Suite visual surface.
        // The prefix suppresses vanilla only when that surface was actually closed; the next Escape
        // therefore peels the next Suite layer or reaches the native handler normally.
        internal static bool TryQuickCloseFromBoundNativeEscape()
        {
            ErenshorSuiteHubPlugin instance = Instance;
            if (instance == null || !SuiteNativeEscapeCompatibility.IsNativeConsumeBound) return false;
            SuiteQuickCloseResult result = instance.TryCloseTopmostSuiteUi();
            return result != null && result.ShouldConsumeNativeEscape;
        }

        private SuiteQuickCloseResult TryCloseTopmostSuiteUi()
        {
            return SuiteQuickClosePolicy.CloseTopmost(_quickCloseModuleIds, this);
        }

        // ISuiteQuickCloseRuntime ---------------------------------------------------------------
        // These explicit implementations keep the coordinator's authority intentionally tiny.
        SuiteUiStateDescriptor ISuiteQuickCloseRuntime.ReadHubUiState()
        {
            if (_ui == null || !_ui.IsWindowOpen) return null;
            return new SuiteUiStateDescriptor
            {
                ProtocolVersion = 1,
                ModuleId = SuiteQuickClosePolicy.HubModuleId,
                Open = true,
                Closeable = true,
                SortOrder = _ui.SortingOrder,
                Activated = _ui.WindowActivatedAt
            };
        }

        SuiteUiStateDescriptor ISuiteQuickCloseRuntime.ReadUiState(string moduleId)
        {
            AuraModuleBridge bridge;
            if (!_bridges.TryGetValue(moduleId, out bridge) || bridge == null) return null;
            return bridge.RefreshUiStateForQuickClose();
        }

        bool ISuiteQuickCloseRuntime.HasClosePanelAction(string moduleId)
        {
            SuiteModuleDescriptor descriptor = _registry.Get(moduleId);
            bool has = descriptor != null && descriptor.HasAction(SuiteQuickClosePolicy.ClosePanelActionId);
            if (has) _quickCloseContractWarnings.Remove(moduleId);
            return has;
        }

        bool ISuiteQuickCloseRuntime.TryClosePanel(string moduleId, out string result)
        {
            result = "module bridge unavailable";
            AuraModuleBridge bridge;
            if (!_bridges.TryGetValue(moduleId, out bridge) || bridge == null) return false;
            bool invoked = bridge.TryInvokeAction(SuiteQuickClosePolicy.ClosePanelActionId, string.Empty, out result);
            if (!invoked)
            {
                LogQuickCloseFaultOnce(moduleId, "closePanel", result);
                return false;
            }

            // The native Prefix may suppress vanilla only for an actual visual close, not merely an
            // accepted/queued close request. The module contract therefore requires closePanel to
            // update ui.state synchronously. Failure remains fail-open to vanilla Escape.
            try
            {
                SuiteUiStateDescriptor after = bridge.RefreshUiStateForQuickClose();
                if (after == null || after.Open)
                {
                    result = after == null ? "closePanel returned ok but ui.state could not verify closure"
                        : "closePanel returned ok but ui.state remains open";
                    LogQuickCloseFaultOnce(moduleId, "closePanel.verify", result);
                    return false;
                }
            }
            catch (Exception ex)
            {
                result = "closePanel verification failed: " + ex.GetType().Name;
                LogQuickCloseFaultOnce(moduleId, "closePanel.verify", result);
                return false;
            }

            _quickCloseFaultWarnings.Remove(moduleId + "|closePanel");
            _quickCloseFaultWarnings.Remove(moduleId + "|closePanel.verify");
            return true;
        }

        bool ISuiteQuickCloseRuntime.TryCloseHub()
        {
            if (_ui == null || !_ui.IsWindowOpen) return false;
            CloseWindow();
            return _ui == null || !_ui.IsWindowOpen;
        }

        void ISuiteQuickCloseRuntime.ReportMissingClosePanel(string moduleId)
        {
            if (_quickCloseContractWarnings.Add(moduleId))
                Logging.LogWarning("Suite quick-close: " + moduleId +
                    " reports an open closeable panel but does not advertise closePanel; leaving that panel untouched.");
        }

        void ISuiteQuickCloseRuntime.ReportFault(string moduleId, string stage, Exception error)
        {
            LogQuickCloseFaultOnce(moduleId, stage, error == null ? "unknown failure" : error.GetType().Name);
        }

        private void LogQuickCloseFaultOnce(string moduleId, string stage, string detail)
        {
            string key = (moduleId ?? "?") + "|" + (stage ?? "?");
            if (!_quickCloseFaultWarnings.Add(key)) return;
            Logging.LogWarning("Suite quick-close: " + (moduleId ?? "?") + " " + (stage ?? "?") +
                " failed (" + (detail ?? "unknown") + ").");
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
            _quickCloseModuleIds.Clear();
            _quickCloseContractWarnings.Clear();
            _quickCloseFaultWarnings.Clear();
            try { if (_presenceProvider != null) _presenceProvider.UnregisterFunc(); } catch { }
            _presenceProvider = null;
            _registry.Clear();
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { }
            SuiteNativeEscapeCompatibility.ResetBindingState();
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
    // This is the ordinary Harmony patch the Suite Hub always installs. The previous global
    // input/camera compatibility patches were deleted when the Hub moved to retained uGUI. A
    // separate Escape prefix may be installed only by SuiteNativeEscapeCompatibility after an
    // exact current Assembly-CSharp target is verified; its target is intentionally unbound here.
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
