using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace ErenshorSuiteHub
{
    // Production Suite Hub UI: retained Unity uGUI + a mod-owned drag handler (SuiteDragGuard).
    //
    // This replaced the previous OnGUI (GUI.Window/GUILayout/GUI.DragWindow) implementation after
    // the prototype of this exact uGUI architecture passed live testing. The reason it works, and
    // the reason the OnGUI version could not be made to work reliably, is that every native input
    // gate in the game keys off the EventSystem:
    //
    //   CameraController.Update/Controls/ModernControls -> EventSystem.IsPointerOverGameObject()
    //                                                   -> GameData.DraggingUIElement
    //   PlayerControl.LeftClick/RightClick/LandMovement/WaterMovement/MouseLook
    //                                                   -> EventSystem.IsPointerOverGameObject()
    //
    // Legacy IMGUI never registers with the EventSystem, so the game was structurally blind to the
    // old Hub and treated clicks/drags over it as world and camera input. A real Canvas +
    // GraphicRaycaster is seen natively, for free, with no Harmony patches at all.
    //
    // Dragging uses the Suite's own SuiteDragGuard component, not native Erenshor DragUI - that
    // component ties its own graphic's visibility/raycastability to a global native flag
    // (GameData.EditUIMode) that also affects other native windows. See SuiteDragGuard.cs and
    // docs/SUITE_UI_ARCHITECTURE.md.
    internal sealed class SuiteHubUi
    {
        private const float LauncherWidth = 152f;
        private const float LauncherHeight = 32f;
        private const float GripSize = 16f;
        private const float GripInset = 15f;
        // Left edge of the MODS button. Must clear the grip entirely: the drag affordance and the
        // click target must never overlap, or a press near the boundary becomes ambiguous.
        private const float ModsButtonLeft = GripInset + GripSize * 0.75f + 6f;
        private const float HeaderHeight = 30f;
        private const float NavWidth = 150f;

        // Frames after (re)build during which we re-assert our restored position, as a safety
        // margin against anything else (layout groups settling, etc.) nudging the rect during the
        // first frames after creation.
        private const int RestoreFrameBudget = 3;

        private readonly SuiteHubView _view = new SuiteHubView();

        private GameObject _root;
        private Canvas _canvas;
        private RectTransform _launcherRect;
        private RectTransform _windowRect;
        private GameObject _window;
        private CanvasGroup _windowGroup;
        private RectTransform _navContent;
        private RectTransform _pageContent;
        private ScrollRect _pageScroll;
        private RectTransform _pageViewport;
        private TextMeshProUGUI _modsButtonLabel;

        private int _launcherRestoreFrames;
        private int _windowRestoreFrames;
        private Vector2 _launcherRestorePos;
        private Vector2 _windowRestorePos;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        // Nav rows are persistent GameObjects, one per installed module (+ Overview), tracked here
        // so a module SELECTION change can recolor the existing rows in place instead of
        // destroying and recreating the whole list. Only a STRUCTURAL change (a module
        // installed/uninstalled) rebuilds this dictionary.
        private readonly Dictionary<string, NavRowVisual> _navRows =
            new Dictionary<string, NavRowVisual>(StringComparer.Ordinal);

        private bool _navRebuildQueued;
        private bool _navSelectionDirty;
        private bool _pageRebuildQueued;
        private int _navSignature;
        private int _pageSignature;

        internal SuiteHubView View { get { return _view; } }
        internal bool IsBuilt { get { return _root != null; } }
        internal bool IsWindowOpen { get { return _window != null; } }

        internal Action OnRequestClose;
        internal Action OnRequestResetPosition;
        internal Action OnRequestToggle;
        internal Func<List<ModPresence>> GetMods;
        internal Func<string> GetHubVersion;
        internal Func<bool> GetDeveloperEnabled;
        internal Func<string> GetReadinessText;
        internal Action<Vector2> PersistLauncherNormalized;
        internal Action<Vector2> PersistWindowNormalized;

        // ---- lifecycle -------------------------------------------------------------------------

        internal bool Build(Vector2 launcherNormalized, Vector2 windowNormalized, Vector2 windowSize)
        {
            Destroy();

            if (EventSystem.current == null)
            {
                // Refuse rather than creating a second EventSystem: two active EventSystems fight
                // each other and are a known source of total UI breakage. The game always has one
                // in a loaded world, so this only trips in genuinely broken states.
                return false;
            }

            _root = new GameObject("ErenshorSuiteHubCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);

            _canvas = _root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            // 500 is the value the live-verified prototype used. Production shipped 300, which can
            // place the Hub UNDER native Erenshor UI - a native graphic on top would then absorb the
            // pointer event before it ever reaches the drag grip. Do not lower this without
            // re-testing drag against native windows.
            _canvas.sortingOrder = 500;

            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            // Constant pixel size: the Hub is chrome, not world content. Anchors + normalized
            // persistence handle resolution safety; scaling would only blur the fixed-size grip.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            SuiteHubDiagnostics.RootCreates++;
            SuiteHubDiagnostics.Log("root created canvas=" + _root.name +
                " sortingOrder=" + _canvas.sortingOrder +
                " eventSystem=" + EventSystem.current.name +
                " screen=" + Screen.width + "x" + Screen.height);

            BuildLauncher();
            SuiteHubDiagnostics.LauncherCreates++;
            SuiteHubDiagnostics.Log("launcher created rect=" + _launcherRect.sizeDelta +
                " children=" + _launcherRect.childCount);
            ApplyLauncherPosition(launcherNormalized);

            _windowRestorePos = ResolvePanelVector(windowNormalized, windowSize,
                Screen.width, Screen.height);

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            return true;
        }

        internal void Destroy()
        {
            CloseWindow();
            _launcherRect = null;
            _canvas = null;
            _navContent = null;
            _pageContent = null;
            _pageScroll = null;
            _pageViewport = null;
            _modsButtonLabel = null;
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
                SuiteHubDiagnostics.RootDestroys++;
                SuiteHubDiagnostics.Log("root destroyed");
            }
            // Never leave the game believing a Hub drag is in progress.
            SuiteDragGuard.ForceReleaseIfHubOwned();
        }

        internal void SetVisible(bool visible)
        {
            if (_root == null) return;
            if (_root.activeSelf == visible) return;
            _root.SetActive(visible);
            SuiteHubDiagnostics.SetActiveChanges++;
            SuiteHubDiagnostics.Log("root SetActive(" + visible + ")");
            if (!visible) SuiteDragGuard.ForceReleaseIfHubOwned();
        }

        // Driven from the plugin's Update.
        internal void Tick()
        {
            if (_root == null) return;

            if (_launcherRestoreFrames > 0 && _launcherRect != null)
            {
                _launcherRestoreFrames--;
                if (!SuiteDragGuard.HubOwnsDrag) _launcherRect.anchoredPosition = _launcherRestorePos;
            }
            if (_windowRestoreFrames > 0 && _windowRect != null)
            {
                _windowRestoreFrames--;
                if (!SuiteDragGuard.HubOwnsDrag) _windowRect.anchoredPosition = _windowRestorePos;
            }

            // Resolution change: re-clamp both panels back on-screen.
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;
                ReclampAfterResolutionChange();
            }

            if (_navRebuildQueued)
            {
                _navRebuildQueued = false;
                _navSelectionDirty = false; // full rebuild already applies current selection colors
                RebuildNav();
            }
            else if (_navSelectionDirty)
            {
                _navSelectionDirty = false;
                RefreshNavSelectionVisual();
            }
            if (_pageRebuildQueued)
            {
                _pageRebuildQueued = false;
                RebuildPage();
            }
        }

        private void ReclampAfterResolutionChange()
        {
            if (_launcherRect != null)
            {
                Vector2 size = _launcherRect.sizeDelta;
                Vector2 p = _launcherRect.anchoredPosition;
                SuiteRect r = SuiteUiGeometry.ResolvePanel(
                    SuiteUiGeometry.NormalizeAxis(p.x, _lastScreenWidth),
                    SuiteUiGeometry.NormalizeAxis(p.y, _lastScreenHeight),
                    size.x, size.y, Screen.width, Screen.height);
                _launcherRect.anchoredPosition = new Vector2(r.X, r.Y);
            }
            if (_windowRect != null)
            {
                Vector2 size = _windowRect.sizeDelta;
                Vector2 p = _windowRect.anchoredPosition;
                SuiteRect r = SuiteUiGeometry.ResolvePanel(
                    SuiteUiGeometry.NormalizeAxis(p.x, _lastScreenWidth),
                    SuiteUiGeometry.NormalizeAxis(p.y, _lastScreenHeight),
                    size.x, size.y, Screen.width, Screen.height);
                _windowRect.anchoredPosition = new Vector2(r.X, r.Y);
            }
        }

        internal void ApplyLauncherPosition(Vector2 normalized)
        {
            if (_launcherRect == null) return;
            SuiteRect r = SuiteUiGeometry.ResolvePanel(normalized.x, normalized.y,
                LauncherWidth, LauncherHeight, Screen.width, Screen.height);
            _launcherRestorePos = new Vector2(r.X, r.Y);
            _launcherRect.anchoredPosition = _launcherRestorePos;
            _launcherRestoreFrames = RestoreFrameBudget;
        }

        // Unity-side bridge to the Unity-free geometry helpers (SuiteUiGeometry must stay free of
        // UnityEngine types so it remains directly testable).
        private static Vector2 ResolvePanelVector(Vector2 normalized, Vector2 size, float screenWidth, float screenHeight)
        {
            SuiteRect r = SuiteUiGeometry.ResolvePanel(normalized.x, normalized.y, size.x, size.y, screenWidth, screenHeight);
            return new Vector2(r.X, r.Y);
        }

        internal void ApplyWindowPosition(Vector2 normalized, Vector2 size)
        {
            _windowRestorePos = ResolvePanelVector(normalized, size, Screen.width, Screen.height);
            if (_windowRect == null) return;
            _windowRect.anchoredPosition = _windowRestorePos;
            _windowRestoreFrames = RestoreFrameBudget;
        }

        // ---- launcher --------------------------------------------------------------------------

        private void BuildLauncher()
        {
            GameObject panel = NewUi("SuiteHubLauncher", _root.transform);
            Image bg = AddImage(panel, new Color(0.015f, 0.09f, 0.125f, 0.88f));
            bg.raycastTarget = true;

            _launcherRect = panel.GetComponent<RectTransform>();
            AnchorBottomLeft(_launcherRect);
            _launcherRect.sizeDelta = new Vector2(LauncherWidth, LauncherHeight);

            // --- diamond drag grip: the ONLY drag surface -------------------------------------
            // Buttons are deliberately never draggable, so a click can never be swallowed by a drag.
            // This affordance must stay VISIBLE: the player is told to drag by the diamond, so it is
            // sized and coloured to read clearly against the panel rather than blending into it.
            GameObject grip = NewUi("SuiteHubLauncherDragHandle", panel.transform);
            Image gripImage = AddImage(grip, new Color(0.20f, 0.78f, 1f, 1f));
            gripImage.raycastTarget = true; // required: no raycast target means no pointer events

            RectTransform gripRect = grip.GetComponent<RectTransform>();
            gripRect.anchorMin = new Vector2(0f, 0.5f);
            gripRect.anchorMax = new Vector2(0f, 0.5f);
            gripRect.pivot = new Vector2(0.5f, 0.5f);
            gripRect.sizeDelta = new Vector2(GripSize, GripSize);
            gripRect.anchoredPosition = new Vector2(GripInset, 0f);
            grip.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            AttachDrag(grip, _launcherRect, PersistLauncherPosition, "launcher");

            // --- MODS button: the ONLY click surface -------------------------------------------
            GameObject button = NewUi("SuiteHubModsButton", panel.transform);
            Image buttonImage = AddImage(button, new Color(0.12f, 0.38f, 0.48f, 0.95f));
            buttonImage.raycastTarget = true;

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.offsetMin = new Vector2(ModsButtonLeft, 3f);
            buttonRect.offsetMax = new Vector2(-3f, -3f);

            Button b = button.AddComponent<Button>();
            b.targetGraphic = buttonImage;
            SetButtonColors(b);
            b.onClick.AddListener(delegate { if (OnRequestToggle != null) OnRequestToggle(); });

            _modsButtonLabel = NewLabel("SuiteHubModsLabel", button.transform, "MODS", 12f, FontStyles.Bold);
            Stretch(_modsButtonLabel.rectTransform);
            _modsButtonLabel.alignment = TextAlignmentOptions.Center;
        }

        private void PersistLauncherPosition()
        {
            if (_launcherRect == null || PersistLauncherNormalized == null) return;
            Vector2 p = _launcherRect.anchoredPosition;
            PersistLauncherNormalized(new Vector2(
                SuiteUiGeometry.NormalizeAxis(p.x, Screen.width),
                SuiteUiGeometry.NormalizeAxis(p.y, Screen.height)));
        }

        private void PersistWindowPosition()
        {
            if (_windowRect == null || PersistWindowNormalized == null) return;
            Vector2 p = _windowRect.anchoredPosition;
            PersistWindowNormalized(new Vector2(
                SuiteUiGeometry.NormalizeAxis(p.x, Screen.width),
                SuiteUiGeometry.NormalizeAxis(p.y, Screen.height)));
        }

        // Mounts the Suite's own mod-owned drag component (SuiteDragGuard). Does NOT use native
        // Erenshor DragUI: that component ties its own Image's visibility/raycastability to
        // GameData.EditUIMode (see docs/SUITE_UI_ARCHITECTURE.md), and forcing that flag globally
        // to keep it working was found live to unlock/decorate OTHER native windows too. This
        // component instead implements the drag interfaces directly and never touches EditUIMode.
        // Verification is done by reading the component back off the GameObject after the fact,
        // not by trusting that the AddComponent line exists in source.
        private void AttachDrag(GameObject handle, RectTransform target, Action onCompleted, string label)
        {
            SuiteDragGuard guard = handle.AddComponent<SuiteDragGuard>();
            guard.Target = target;
            guard.OnDragCompleted = onCompleted;
            guard.DiagnosticLabel = label;

            Image graphic = handle.GetComponent<Image>();
            SuiteHubDiagnostics.Log(label + " drag component=" + guard.GetType().Name +
                " target=" + (guard.Target != null ? guard.Target.name : "NULL") +
                " graphic=" + (graphic == null ? "MISSING" : graphic.GetType().Name) +
                " raycastTarget=" + (graphic != null && graphic.raycastTarget) +
                " size=" + handle.GetComponent<RectTransform>().sizeDelta +
                " activeInHierarchy=" + handle.activeInHierarchy);
        }

        // ---- window ----------------------------------------------------------------------------

        internal void OpenWindow(Vector2 normalized, Vector2 size)
        {
            if (_root == null || _window != null) return;

            GameObject panel = NewUi("SuiteHubWindow", _root.transform);
            Image bg = AddImage(panel, new Color(0.015f, 0.09f, 0.125f, 0.96f));
            bg.raycastTarget = true;

            _windowRect = panel.GetComponent<RectTransform>();
            AnchorBottomLeft(_windowRect);
            _windowRect.sizeDelta = size;

            _windowGroup = panel.AddComponent<CanvasGroup>();
            _windowGroup.blocksRaycasts = true;
            _windowGroup.interactable = true;

            BuildHeader(panel.transform);
            BuildNavigation(panel.transform);
            BuildPageArea(panel.transform);

            _window = panel;
            SuiteHubDiagnostics.WindowCreates++;
            SuiteHubDiagnostics.Log("window created size=" + size + " normalized=" + normalized);

            ApplyWindowPosition(normalized, size);
            RebuildNav();
            RebuildPage();
            _navSignature = ComputeNavSignature();
            _pageSignature = ComputePageSignature();
        }

        internal void CloseWindow()
        {
            if (_window == null) { _windowRect = null; return; }
            PersistWindowPosition();
            SuiteHubDiagnostics.WindowDestroys++;
            SuiteHubDiagnostics.Log("window destroyed");
            UnityEngine.Object.Destroy(_window);
            _window = null;
            _windowRect = null;
            _windowGroup = null;
            _navContent = null;
            _pageContent = null;
            _pageScroll = null;
            _pageViewport = null;
        }

        // Selection changed: the nav list's highlighted row AND the page content both need to
        // update. Both are still surgical - each rebuilds only its own content root, not the whole
        // window (header/CanvasGroup/ScrollRect scaffolding is untouched).
        // The user clicked a different module in the nav list. This is NOT a structural change -
        // the same set of nav row GameObjects stays put; only the highlighted row moves and the
        // page content changes.
        internal void QueueSelectionChanged()
        {
            SuiteHubDiagnostics.SelectionChanges++;
            _navSelectionDirty = true;
            _pageRebuildQueued = true;
        }

        // A module was installed/uninstalled: the nav list's actual row set must change. Full
        // teardown/rebuild is unavoidable here, but this should fire rarely (a discovery poll
        // noticing a plugin file appear/disappear), not on every module selection.
        internal void QueueNavStructureRebuild()
        {
            _navRebuildQueued = true;
            _pageRebuildQueued = true;
        }

        // Page-only state changed (a setting toggled, a disclosure section opened, an action ran).
        // The nav list's content is unaffected and must not be touched - rebuilding it too was
        // unnecessary churn and part of what made module switching feel heavier than it needed to.
        internal void QueuePageRebuild()
        {
            _pageRebuildQueued = true;
        }

        // Polling-driven refresh. The bridge poll runs every second whether or not anything
        // changed; rebuilding the whole window on that cadence tore down and recreated every child
        // once per second, which is the main cause of the 0.3.0 flicker. Nav and page are tracked
        // with separate signatures so a module's setting changing does not also re-render the
        // (unrelated, unchanged) nav list.
        internal void QueueRebuildIfContentChanged()
        {
            if (_window == null) return;

            int navSig = ComputeNavSignature();
            if (navSig != _navSignature)
            {
                _navSignature = navSig;
                _navRebuildQueued = true;
                SuiteHubDiagnostics.ModuleRefreshes++;
                SuiteHubDiagnostics.Log("nav signature changed -> nav rebuild queued");
            }

            int pageSig = ComputePageSignature();
            if (pageSig != _pageSignature)
            {
                _pageSignature = pageSig;
                _pageRebuildQueued = true;
                SuiteHubDiagnostics.Log("page signature changed -> page rebuild queued");
            }
        }

        // Structural hash of what the nav list renders: installed modules and whether each has a
        // live bridge (shown as a status dot), plus the current selection (for highlight).
        private int ComputeNavSignature()
        {
            unchecked
            {
                int h = 13;
                List<ModPresence> mods = GetMods != null ? GetMods() : null;
                if (mods != null)
                {
                    for (int i = 0; i < mods.Count; i++)
                    {
                        h = h * 31 + (mods[i].ModuleId != null ? mods[i].ModuleId.GetHashCode() : 0);
                        h = h * 31 + (mods[i].Installed ? 1 : 0);
                    }
                }
                h = h * 31 + (_view.SelectedModuleId != null ? _view.SelectedModuleId.GetHashCode() : 0);
                return h;
            }
        }

        // Structural hash of what the page renders: the selected module's descriptor/settings plus
        // view disclosure state. Deliberately includes bridge settings values so a real module
        // update still refreshes the page even while the selection itself hasn't changed.
        private int ComputePageSignature()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (_view.SelectedModuleId != null ? _view.SelectedModuleId.GetHashCode() : 0);

                if (_view.IsOverviewSelected)
                {
                    List<ModPresence> mods = GetMods != null ? GetMods() : null;
                    if (mods != null)
                    {
                        for (int i = 0; i < mods.Count; i++)
                        {
                            if (!mods[i].Installed) continue;
                            h = h * 31 + (mods[i].ModuleId != null ? mods[i].ModuleId.GetHashCode() : 0);
                            SuiteModuleDescriptor runtime = ErenshorSuiteHubPlugin.GetRegisteredModule(mods[i].ModuleId);
                            h = h * 31 + (runtime == null ? 0 : 1);
                            if (runtime != null) h = h * 31 + (runtime.Version != null ? runtime.Version.GetHashCode() : 0);
                        }
                    }
                }
                else
                {
                    SuiteModuleDescriptor runtime = ErenshorSuiteHubPlugin.GetRegisteredModule(_view.SelectedModuleId);
                    h = h * 31 + (runtime == null ? 0 : 1);
                    if (runtime != null)
                    {
                        h = h * 31 + (runtime.Version != null ? runtime.Version.GetHashCode() : 0);
                        h = h * 31 + (runtime.Status != null ? runtime.Status.GetHashCode() : 0);
                        h = h * 31 + (runtime.Warning != null ? runtime.Warning.GetHashCode() : 0);
                    }

                    AuraModuleBridge bridge = ErenshorSuiteHubPlugin.GetModuleBridge(_view.SelectedModuleId);
                    h = h * 31 + SettingsSignature(bridge == null ? null : bridge.CachedBasicSettings);
                    h = h * 31 + SettingsSignature(bridge == null ? null : bridge.CachedAdvancedSettings);
                    h = h * 31 + SettingsSignature(bridge == null ? null : bridge.CachedDeveloperSettings);
                    if (bridge != null && bridge.LastError != null) h = h * 31 + bridge.LastError.GetHashCode();
                }

                h = h * 31 + (_view.ShowAdvanced ? 1 : 0);
                h = h * 31 + (_view.ShowDeveloper ? 1 : 0);
                h = h * 31 + (_view.LastActionResult != null ? _view.LastActionResult.GetHashCode() : 0);
                return h;
            }
        }

        private static int SettingsSignature(List<SuiteSettingDescriptor> settings)
        {
            if (settings == null) return 0;
            unchecked
            {
                int h = 7 + settings.Count;
                for (int i = 0; i < settings.Count; i++)
                {
                    SuiteSettingDescriptor s = settings[i];
                    h = h * 31 + (s.Id != null ? s.Id.GetHashCode() : 0);
                    h = h * 31 + (s.Value != null ? s.Value.GetHashCode() : 0);
                    h = h * 31 + (s.Mutable ? 1 : 0);
                }
                return h;
            }
        }

        private void BuildHeader(Transform parent)
        {
            GameObject header = NewUi("SuiteHubWindowHeader", parent);
            Image headerImage = AddImage(header, new Color(0.07f, 0.28f, 0.34f, 0.98f));
            headerImage.raycastTarget = true;

            RectTransform r = header.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(0f, HeaderHeight);
            r.anchoredPosition = Vector2.zero;

            // Header is the window's drag surface. The Reset/Close buttons sit on top of it as
            // separate raycast targets, so pressing them never starts a drag.
            AttachDrag(header, _windowRect, PersistWindowPosition, "header");

            // Decorative diamond matching the launcher grip, so the header reads as draggable.
            // raycastTarget stays FALSE: the header itself is the drag surface, and this must not
            // intercept the pointer event that SuiteDragGuard on the header needs to receive.
            GameObject mark = NewUi("SuiteHubHeaderGripMark", header.transform);
            Image markImage = AddImage(mark, new Color(0.20f, 0.78f, 1f, 1f));
            markImage.raycastTarget = false;
            RectTransform markRect = mark.GetComponent<RectTransform>();
            markRect.anchorMin = new Vector2(0f, 0.5f);
            markRect.anchorMax = new Vector2(0f, 0.5f);
            markRect.pivot = new Vector2(0.5f, 0.5f);
            markRect.sizeDelta = new Vector2(GripSize * 0.75f, GripSize * 0.75f);
            markRect.anchoredPosition = new Vector2(GripInset, 0f);
            mark.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            TextMeshProUGUI title = NewLabel("SuiteHubTitle", header.transform,
                "ERENSHOR MOD SUITE", 13f, FontStyles.Bold);
            RectTransform tr = title.rectTransform;
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.offsetMin = new Vector2(ModsButtonLeft + 6f, 0f);
            tr.offsetMax = new Vector2(-120f, 0f);
            title.alignment = TextAlignmentOptions.Left;
            title.color = new Color(0.56f, 0.88f, 1f, 1f);

            MakeHeaderButton(header.transform, "RESET", 58f, -62f,
                delegate { if (OnRequestResetPosition != null) OnRequestResetPosition(); });
            MakeHeaderButton(header.transform, "X", 26f, -4f,
                delegate { if (OnRequestClose != null) OnRequestClose(); });
        }

        private void MakeHeaderButton(Transform parent, string text, float width, float rightOffset, UnityEngine.Events.UnityAction action)
        {
            GameObject go = NewUi("SuiteHubHeaderButton_" + text, parent);
            Image img = AddImage(go, new Color(0.12f, 0.38f, 0.48f, 0.95f));
            img.raycastTarget = true;

            RectTransform r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(1f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.sizeDelta = new Vector2(width, 20f);
            r.anchoredPosition = new Vector2(rightOffset, 0f);

            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            SetButtonColors(b);
            b.onClick.AddListener(action);

            TextMeshProUGUI label = NewLabel("Label", go.transform, text, 10f, FontStyles.Bold);
            Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
        }

        private void BuildNavigation(Transform parent)
        {
            GameObject area = NewUi("SuiteHubNav", parent);
            RectTransform r = area.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(NavWidth, 0f);
            r.offsetMin = new Vector2(8f, 8f);
            r.offsetMax = new Vector2(8f + NavWidth, -(HeaderHeight + 6f));

            ScrollRect navScroll;
            RectTransform navViewport;
            _navContent = BuildScrollArea(area.transform, "NavScroll", out navScroll, out navViewport);
        }

        private void BuildPageArea(Transform parent)
        {
            GameObject area = NewUi("SuiteHubPage", parent);
            RectTransform r = area.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.offsetMin = new Vector2(NavWidth + 16f, 8f);
            r.offsetMax = new Vector2(-8f, -(HeaderHeight + 6f));

            RectTransform viewport;
            _pageContent = BuildScrollArea(area.transform, "PageScroll", out _pageScroll, out viewport);
            _pageViewport = viewport;
        }

        // Minimal hand-built ScrollRect: viewport with RectMask2D, content driven by a vertical
        // layout group + content size fitter so rows can be added without manual measurement.
        private RectTransform BuildScrollArea(Transform parent, string name, out ScrollRect scroll, out RectTransform viewport)
        {
            GameObject scrollGo = NewUi(name, parent);
            RectTransform scrollRect = scrollGo.GetComponent<RectTransform>();
            Stretch(scrollRect);

            scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 18f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewportGo = NewUi("Viewport", scrollGo.transform);
            viewport = viewportGo.GetComponent<RectTransform>();
            Stretch(viewport);
            viewportGo.AddComponent<RectMask2D>();
            // A viewport needs a graphic to act as a raycast target for wheel/drag scrolling.
            Image viewportImage = AddImage(viewportGo, new Color(0f, 0f, 0f, 0.01f));
            viewportImage.raycastTarget = true;

            RectTransform content = CreateContentRoot(viewportGo.transform);
            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        // Content root construction shared by the initial ScrollRect build and by the atomic page
        // swap (which builds a brand new one of these rather than clearing the existing content).
        private static RectTransform CreateContentRoot(Transform viewportParent)
        {
            GameObject contentGo = NewUi("Content", viewportParent);
            RectTransform content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 3f;
            layout.padding = new RectOffset(2, 2, 2, 2);

            ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return content;
        }

        // ---- content ---------------------------------------------------------------------------

        private List<ModPresence> CurrentMods()
        {
            List<ModPresence> mods = GetMods != null ? GetMods() : new List<ModPresence>();
            return mods ?? new List<ModPresence>();
        }

        // STRUCTURAL nav rebuild: destroys and recreates every row. Only called when the installed
        // module set actually changed (QueueNavStructureRebuild) - NOT on ordinary module
        // selection, which uses RefreshNavSelectionVisual instead and never touches these
        // GameObjects.
        private void RebuildNav()
        {
            if (_window == null || _navContent == null) return;
            List<ModPresence> mods = CurrentMods();
            _view.EnsureSelectionValid(mods);

            ClearChildren(_navContent);
            _navRows.Clear();
            BuildNavList(mods);
            SuiteHubDiagnostics.NavRebuilds++;

            // Force the VerticalLayoutGroup/ContentSizeFitter to compute sizes THIS frame rather
            // than over the next 1-2 frames, so there is no frame where the ScrollRect content has
            // stale/zero height - the "layout temporarily has invalid dimensions" symptom.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_navContent);
        }

        // Lightweight selection update: recolors the existing persistent nav rows in place. Row
        // count and layout are unaffected, so no LayoutRebuilder pass is needed here.
        private void RefreshNavSelectionVisual()
        {
            foreach (KeyValuePair<string, NavRowVisual> pair in _navRows)
            {
                bool selected = string.Equals(_view.SelectedModuleId, pair.Key, StringComparison.Ordinal);
                ApplyNavRowStyle(pair.Value, selected);
            }
        }

        private static void ApplyNavRowStyle(NavRowVisual row, bool selected)
        {
            if (row.Background != null)
                row.Background.color = selected
                    ? new Color(0.07f, 0.28f, 0.34f, 0.98f)
                    : new Color(0.035f, 0.17f, 0.22f, 0.90f);
            if (row.Label != null)
                row.Label.color = selected ? new Color(0.88f, 1f, 0.98f, 1f) : new Color(0.84f, 0.94f, 1f, 1f);
        }

        // Page content only. Rebuilding this must never touch the nav list.
        // Atomic swap: the new page is built completely under a brand new content root (a sibling
        // of the old one, both under the persistent viewport) before the ScrollRect is ever pointed
        // at it. The old root is deactivated and detached immediately afterward, in the same
        // synchronous call, so no frame is ever rendered with both participating in layout - and if
        // building the new page throws partway through, the old page is untouched and stays on
        // screen rather than showing a half-built page.
        private void RebuildPage()
        {
            if (_window == null || _pageViewport == null || _pageScroll == null) return;
            List<ModPresence> mods = CurrentMods();
            _view.EnsureSelectionValid(mods);

            RectTransform oldContent = _pageContent;
            RectTransform newContent = CreateContentRoot(_pageViewport);
            _pageContent = newContent; // page builder methods below write into this

            try
            {
                if (_view.IsOverviewSelected)
                {
                    BuildOverview(mods);
                }
                else
                {
                    LogModuleDescriptor(_view.SelectedModuleId);
                    BuildModulePage(_view.SelectedModuleId, mods);
                }
            }
            catch (Exception)
            {
                // Building the new page failed: discard it and keep the old one displayed rather
                // than swap to a broken/half-built page.
                _pageContent = oldContent;
                UnityEngine.Object.Destroy(newContent.gameObject);
                throw;
            }

            SuiteHubDiagnostics.PageRebuilds++;
            // Build new page completely before exposing it: compute its final layout while it is
            // still just a sibling, not yet the ScrollRect's active content.
            LayoutRebuilder.ForceRebuildLayoutImmediate(newContent);

            _pageScroll.content = newContent;
            _pageScroll.verticalNormalizedPosition = 1f; // new page starts scrolled to top

            if (oldContent != null)
            {
                oldContent.gameObject.SetActive(false); // stop participating in layout/render now
                oldContent.SetParent(null, false);
                UnityEngine.Object.Destroy(oldContent.gameObject);
            }
        }

        // Answers "is the Hub RECEIVING controls" separately from "is the Hub RENDERING them".
        // Compare these counts against what actually appears on the page: if counts are 0, the
        // regression is in the module's provider/registration, not in this UI.
        private static void LogModuleDescriptor(string moduleId)
        {
            if (!SuiteHubDiagnostics.Enabled) return;
            SuiteModuleDescriptor runtime = ErenshorSuiteHubPlugin.GetRegisteredModule(moduleId);
            AuraModuleBridge bridge = ErenshorSuiteHubPlugin.GetModuleBridge(moduleId);
            int basic = bridge != null && bridge.CachedBasicSettings != null ? bridge.CachedBasicSettings.Count : 0;
            int advanced = bridge != null && bridge.CachedAdvancedSettings != null ? bridge.CachedAdvancedSettings.Count : 0;
            int developer = bridge != null && bridge.CachedDeveloperSettings != null ? bridge.CachedDeveloperSettings.Count : 0;

            SuiteHubDiagnostics.Log("module=" + moduleId +
                " descriptor=" + (runtime != null) +
                " settings=" + (basic + advanced + developer) +
                " (basic=" + basic + " advanced=" + advanced + " developer=" + developer + ")" +
                " openPanel=" + (runtime != null && runtime.HasAction("openPanel")) +
                " status=" + (runtime != null && runtime.Status != null ? runtime.Status : "-") +
                " bridgeError=" + (bridge != null && !string.IsNullOrEmpty(bridge.LastError) ? bridge.LastError : "-"));
        }

        private void BuildNavList(List<ModPresence> mods)
        {
            AddSectionLabel(_navContent, "MODULES");
            AddNavButton("Overview", string.Empty);
            for (int i = 0; i < mods.Count; i++)
            {
                if (!mods[i].Installed) continue;
                AddNavButton(mods[i].DisplayName, mods[i].ModuleId);
            }
        }

        private void AddNavButton(string label, string moduleId)
        {
            bool selected = string.Equals(_view.SelectedModuleId, moduleId, StringComparison.Ordinal);
            string captured = moduleId;
            NavRowVisual row = AddNavRow(_navContent, label, selected, delegate
            {
                _view.Select(captured);
                QueueSelectionChanged();
            });
            _navRows[moduleId] = row;
        }

        private void BuildOverview(List<ModPresence> mods)
        {
            int installed = 0;
            int connected = 0;
            for (int i = 0; i < mods.Count; i++)
            {
                if (!mods[i].Installed) continue;
                installed++;
                if (ErenshorSuiteHubPlugin.GetRegisteredModule(mods[i].ModuleId) != null) connected++;
            }

            AddSectionLabel(_pageContent, "OVERVIEW");
            AddBodyLabel(_pageContent, "Suite Hub " + (GetHubVersion != null ? GetHubVersion() : string.Empty));
            AddBodyLabel(_pageContent, installed.ToString() + " suite modules installed; " +
                connected.ToString() + " currently exposing the optional Suite bridge.");

            AddSpacer(_pageContent, 6f);
            AddSectionLabel(_pageContent, "INSTALLED");
            if (installed == 0)
                AddMutedLabel(_pageContent, "No sibling suite modules were found in the plugins folder.");

            for (int i = 0; i < mods.Count; i++)
            {
                if (!mods[i].Installed) continue;
                SuiteModuleDescriptor runtime = ErenshorSuiteHubPlugin.GetRegisteredModule(mods[i].ModuleId);
                string state = runtime == null ? "installed; bridge unavailable" : "available  v" + runtime.Version;
                AddBodyLabel(_pageContent, (runtime == null ? "-  " : "+  ") + mods[i].DisplayName + " - " + state);
            }

            AddSpacer(_pageContent, 6f);
            AddSectionLabel(_pageContent, "HOW THIS WORKS");
            AddMutedLabel(_pageContent,
                "The Hub never edits another mod's config file and never performs gameplay actions itself. " +
                "Controls appear only when the installed mod exposes the versioned optional bridge and remains " +
                "authoritative for validation, persistence, and actions.");

            if (GetDeveloperEnabled != null && GetDeveloperEnabled())
            {
                AddSpacer(_pageContent, 6f);
                AddSectionLabel(_pageContent, "DEVELOPER");
                AddMutedLabel(_pageContent, "Readiness: " + (GetReadinessText != null ? GetReadinessText() : string.Empty));
                AddMutedLabel(_pageContent, "File discovery is presence-only; bridge availability is runtime Aura IPC.");
            }
        }

        private void BuildModulePage(string moduleId, List<ModPresence> mods)
        {
            ModPresence presence = default(ModPresence);
            bool found = false;
            for (int i = 0; i < mods.Count; i++)
            {
                if (string.Equals(mods[i].ModuleId, moduleId, StringComparison.Ordinal))
                {
                    presence = mods[i];
                    found = true;
                    break;
                }
            }
            if (!found || !presence.Installed)
            {
                _view.SelectOverview();
                BuildOverview(mods);
                return;
            }

            SuiteModuleDefinition def = presence.Definition;
            SuiteModuleDescriptor runtime = ErenshorSuiteHubPlugin.GetRegisteredModule(moduleId);
            AuraModuleBridge bridge = ErenshorSuiteHubPlugin.GetModuleBridge(moduleId);

            AddSectionLabel(_pageContent, def.DisplayName.ToUpperInvariant());
            AddBodyLabel(_pageContent, def.Summary);

            AddSpacer(_pageContent, 6f);
            AddSectionLabel(_pageContent, "STATUS");
            AddBodyLabel(_pageContent, "Installed: yes (" + def.DllName + ")");
            if (runtime == null)
            {
                AddMutedLabel(_pageContent,
                    "Suite integration: unavailable. The mod remains standalone and usable through its own interface.");
                AddMutedLabel(_pageContent, "Fallback: " + def.FallbackInterface);
                if (bridge != null && !string.IsNullOrEmpty(bridge.LastError))
                    AddWarningLabel(_pageContent, "Bridge rejected: " + bridge.LastError);
            }
            else
            {
                AddBodyLabel(_pageContent, "Version: " + runtime.Version);
                if (!string.IsNullOrEmpty(runtime.Status)) AddBodyLabel(_pageContent, runtime.Status);
                if (!string.IsNullOrEmpty(runtime.Warning)) AddWarningLabel(_pageContent, runtime.Warning);
            }

            AddSpacer(_pageContent, 6f);
            AddSectionLabel(_pageContent, "COMMON CONTROLS");
            if (runtime != null && runtime.HasAction("openPanel"))
            {
                string captured = moduleId;
                AddRowButton(_pageContent, "Open dedicated panel", 24f, false, delegate
                {
                    string result;
                    ErenshorSuiteHubPlugin.TryInvokeModuleAction(captured, "openPanel", string.Empty, out result);
                    _view.SetActionResult(result);
                    QueuePageRebuild();
                });
            }
            else if (def.HasDedicatedPanel)
            {
                AddMutedLabel(_pageContent,
                    "A dedicated interface exists, but the Hub will not guess how to open it until this mod exposes the bridge action.");
            }
            else
            {
                AddMutedLabel(_pageContent,
                    "No dedicated panel is required for this module. Use its ordinary controls until bridge actions are exposed.");
            }

            if (!string.IsNullOrEmpty(_view.LastActionResult))
                AddMutedLabel(_pageContent, _view.LastActionResult);

            AddSpacer(_pageContent, 6f);
            AddSectionLabel(_pageContent, "COMMON SETTINGS");
            BuildSettings(moduleId, bridge == null ? null : bridge.CachedBasicSettings);

            AddSpacer(_pageContent, 6f);
            AddRowButton(_pageContent, "Advanced  " + (_view.ShowAdvanced ? "[-]" : "[+]"), 22f, _view.ShowAdvanced,
                delegate { _view.SetAdvanced(!_view.ShowAdvanced); QueuePageRebuild(); });
            if (_view.ShowAdvanced)
                BuildSettings(moduleId, bridge == null ? null : bridge.CachedAdvancedSettings);

            if (GetDeveloperEnabled != null && GetDeveloperEnabled())
            {
                AddRowButton(_pageContent, "Developer  " + (_view.ShowDeveloper ? "[-]" : "[+]"), 22f, _view.ShowDeveloper,
                    delegate { _view.SetDeveloper(!_view.ShowDeveloper); QueuePageRebuild(); });
                if (_view.ShowDeveloper)
                {
                    BuildSettings(moduleId, bridge == null ? null : bridge.CachedDeveloperSettings);
                    if (bridge != null && !string.IsNullOrEmpty(bridge.LastError))
                        AddWarningLabel(_pageContent, "Last bridge error: " + bridge.LastError);
                }
            }
        }

        private void BuildSettings(string moduleId, List<SuiteSettingDescriptor> settings)
        {
            if (settings == null || settings.Count == 0)
            {
                AddMutedLabel(_pageContent, "No settings advertised at this level.");
                return;
            }

            for (int i = 0; i < settings.Count; i++)
            {
                SuiteSettingDescriptor s = settings[i];
                if (s.Kind == SuiteSettingKind.Bool && s.Mutable)
                {
                    bool current = string.Equals(s.Value, "true", StringComparison.OrdinalIgnoreCase);
                    SuiteSettingDescriptor captured = s;
                    string capturedModule = moduleId;
                    AddToggleRow(_pageContent, s.Label, current, delegate
                    {
                        bool next = !string.Equals(captured.Value, "true", StringComparison.OrdinalIgnoreCase);
                        string result;
                        if (ErenshorSuiteHubPlugin.TrySetModuleSetting(capturedModule, captured.Id,
                                next ? "true" : "false", out result))
                            captured.Value = next ? "true" : "false";
                        _view.SetActionResult(result);
                        QueuePageRebuild();
                    });
                }
                else if (s.Kind == SuiteSettingKind.Choice && s.Mutable && s.Options.Count > 0)
                {
                    SuiteSettingDescriptor captured = s;
                    string capturedModule = moduleId;
                    AddChoiceRow(_pageContent, s.Label, s.Value, delegate
                    {
                        CycleChoice(capturedModule, captured, 1);
                        QueuePageRebuild();
                    });
                }
                else
                {
                    AddBodyLabel(_pageContent, s.Label + ": " + s.Value + (s.Mutable ? "" : " (read-only)"));
                }
            }
        }

        private void CycleChoice(string moduleId, SuiteSettingDescriptor s, int direction)
        {
            if (s.Options.Count == 0) return;
            int index = s.Options.IndexOf(s.Value);
            if (index < 0) index = 0;
            index = (index + direction + s.Options.Count) % s.Options.Count;
            string nextValue = s.Options[index];
            string result;
            if (ErenshorSuiteHubPlugin.TrySetModuleSetting(moduleId, s.Id, nextValue, out result))
                s.Value = nextValue;
            _view.SetActionResult(result);
        }

        // ---- widget helpers ---------------------------------------------------------------------

        // Object.Destroy is DEFERRED to the end of the frame. Rebuilding content with plain
        // Destroy() therefore leaves the old children parented and laid out alongside the new ones
        // for one full frame, so a VerticalLayoutGroup renders double content and visibly jumps -
        // a direct cause of the 0.3.0 flicker. Detaching immediately removes them from layout in
        // the same frame; Destroy then just reclaims them.
        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(child);
            }
        }

        private static GameObject NewUi(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Image AddImage(GameObject go, Color color)
        {
            Image img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static void AnchorBottomLeft(RectTransform r)
        {
            // Bottom-left anchor + bottom-left pivot makes anchoredPosition equal to pixels from the
            // bottom-left corner, which is exactly what the normalized persistence math assumes.
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.zero;
            r.pivot = Vector2.zero;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        private static void SetButtonColors(Button b)
        {
            ColorBlock c = b.colors;
            c.normalColor = Color.white;
            c.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            c.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            c.fadeDuration = 0.05f;
            b.colors = c;
        }

        private static TextMeshProUGUI NewLabel(string name, Transform parent, string text, float size, FontStyles style)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = new Color(0.88f, 0.92f, 0.91f, 1f);
            t.alignment = TextAlignmentOptions.Left;
            // Labels must never eat clicks intended for the control underneath them.
            t.raycastTarget = false;
            return t;
        }

        private static void AddSpacer(RectTransform parent, float height)
        {
            GameObject go = NewUi("Spacer", parent);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        private static TextMeshProUGUI AddTextRow(RectTransform parent, string text, float size,
            Color color, FontStyles style)
        {
            TextMeshProUGUI t = NewLabel("Row", parent, text, size, style);
            t.color = color;
            t.enableWordWrapping = true;
            LayoutElement le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 16f;
            // ContentSizeFitter on the parent needs a preferred height; let TMP report it.
            le.preferredHeight = -1f;
            ContentSizeFitter fitter = t.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return t;
        }

        private static void AddSectionLabel(RectTransform parent, string text)
        {
            AddTextRow(parent, text, 11f, new Color(0.56f, 0.78f, 0.88f, 1f), FontStyles.Bold);
        }

        private static void AddBodyLabel(RectTransform parent, string text)
        {
            AddTextRow(parent, text, 11f, new Color(0.88f, 0.92f, 0.91f, 1f), FontStyles.Normal);
        }

        private static void AddMutedLabel(RectTransform parent, string text)
        {
            AddTextRow(parent, text, 10f, new Color(0.63f, 0.73f, 0.74f, 1f), FontStyles.Normal);
        }

        private static void AddWarningLabel(RectTransform parent, string text)
        {
            AddTextRow(parent, text, 10f, new Color(1f, 0.80f, 0.42f, 1f), FontStyles.Normal);
        }

        // References kept for a persistent nav row so its selected/unselected style can be applied
        // in place (RefreshNavSelectionVisual) without destroying and recreating the row.
        private struct NavRowVisual
        {
            internal Image Background;
            internal TextMeshProUGUI Label;
        }

        private static NavRowVisual AddNavRow(RectTransform parent, string text, bool selected,
            UnityEngine.Events.UnityAction action)
        {
            NavRowVisual row = new NavRowVisual();
            row.Background = AddRowButton(parent, text, 24f, selected, action);
            row.Label = row.Background != null ? row.Background.GetComponentInChildren<TextMeshProUGUI>() : null;
            return row;
        }

        private static Image AddRowButton(RectTransform parent, string text, float height, bool selected,
            UnityEngine.Events.UnityAction action)
        {
            GameObject go = NewUi("RowButton", parent);
            Image img = AddImage(go, selected
                ? new Color(0.07f, 0.28f, 0.34f, 0.98f)
                : new Color(0.035f, 0.17f, 0.22f, 0.90f));
            img.raycastTarget = true;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            SetButtonColors(b);
            b.onClick.AddListener(action);

            TextMeshProUGUI label = NewLabel("Label", go.transform, text, 11f, FontStyles.Normal);
            RectTransform lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(6f, 0f);
            lr.offsetMax = new Vector2(-6f, 0f);
            label.alignment = TextAlignmentOptions.Left;
            label.color = selected ? new Color(0.88f, 1f, 0.98f, 1f) : new Color(0.84f, 0.94f, 1f, 1f);
            return img;
        }

        // A bool setting row: label on the left, an unmistakable colored ON/OFF pill on the right
        // that a plain status label never has. The whole row is one Button so the click target is
        // generous, not just the small pill text.
        private static void AddToggleRow(RectTransform parent, string label, bool on, UnityEngine.Events.UnityAction action)
        {
            const float height = 24f;
            const float pillWidth = 46f;

            GameObject go = NewUi("ToggleRow", parent);
            Image rowImg = AddImage(go, new Color(0.035f, 0.17f, 0.22f, 0.90f));
            rowImg.raycastTarget = true;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            Button b = go.AddComponent<Button>();
            b.targetGraphic = rowImg;
            SetButtonColors(b);
            b.onClick.AddListener(action);

            TextMeshProUGUI text = NewLabel("Label", go.transform, label, 11f, FontStyles.Normal);
            RectTransform tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(8f, 0f);
            tr.offsetMax = new Vector2(-(pillWidth + 10f), 0f);
            text.alignment = TextAlignmentOptions.Left;
            text.color = new Color(0.84f, 0.94f, 1f, 1f);

            GameObject pill = NewUi("Pill", go.transform);
            Image pillImg = AddImage(pill, on
                ? new Color(0.20f, 0.62f, 0.34f, 1f)
                : new Color(0.30f, 0.32f, 0.34f, 1f));
            pillImg.raycastTarget = false; // the row Button already covers the whole area
            RectTransform pr = pill.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(1f, 0.5f);
            pr.anchorMax = new Vector2(1f, 0.5f);
            pr.pivot = new Vector2(1f, 0.5f);
            pr.sizeDelta = new Vector2(pillWidth, 16f);
            pr.anchoredPosition = new Vector2(-6f, 0f);

            TextMeshProUGUI pillLabel = NewLabel("PillLabel", pill.transform, on ? "ON" : "OFF", 10f, FontStyles.Bold);
            Stretch(pillLabel.rectTransform);
            pillLabel.alignment = TextAlignmentOptions.Center;
            pillLabel.color = Color.white;
        }

        // A choice setting row: label on the left, current value + a ">" cycle affordance on the
        // right in its own chip, distinct from plain body text.
        private static void AddChoiceRow(RectTransform parent, string label, string value, UnityEngine.Events.UnityAction action)
        {
            const float height = 24f;
            const float chipWidth = 96f;

            GameObject go = NewUi("ChoiceRow", parent);
            Image rowImg = AddImage(go, new Color(0.035f, 0.17f, 0.22f, 0.90f));
            rowImg.raycastTarget = true;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            Button b = go.AddComponent<Button>();
            b.targetGraphic = rowImg;
            SetButtonColors(b);
            b.onClick.AddListener(action);

            TextMeshProUGUI text = NewLabel("Label", go.transform, label, 11f, FontStyles.Normal);
            RectTransform tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(8f, 0f);
            tr.offsetMax = new Vector2(-(chipWidth + 10f), 0f);
            text.alignment = TextAlignmentOptions.Left;
            text.color = new Color(0.84f, 0.94f, 1f, 1f);

            GameObject chip = NewUi("Chip", go.transform);
            Image chipImg = AddImage(chip, new Color(0.12f, 0.38f, 0.48f, 0.95f));
            chipImg.raycastTarget = false;
            RectTransform cr = chip.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(1f, 0.5f);
            cr.anchorMax = new Vector2(1f, 0.5f);
            cr.pivot = new Vector2(1f, 0.5f);
            cr.sizeDelta = new Vector2(chipWidth, 18f);
            cr.anchoredPosition = new Vector2(-6f, 0f);

            TextMeshProUGUI chipLabel = NewLabel("ChipLabel", chip.transform, value + "  >", 10f, FontStyles.Bold);
            Stretch(chipLabel.rectTransform);
            chipLabel.alignment = TextAlignmentOptions.Center;
            chipLabel.color = Color.white;
        }
    }
}
