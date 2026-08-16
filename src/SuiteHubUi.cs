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
        private const float LauncherHeight = SuiteUiTheme.LauncherHeight;
        private const float GripSize = 16f;
        private const float GripInset = 15f;
        // Left edge of the MODS button. Must clear the grip entirely: the drag affordance and the
        // click target must never overlap, or a press near the boundary becomes ambiguous.
        private const float ModsButtonLeft = GripInset + GripSize * 0.75f + 6f;
        private const float HeaderHeight = SuiteUiTheme.HeaderHeight;
        private const float NavWidth = 150f;
        private const float DockRowHeight = 28f;
        private const float DockRowGap = 2f;
        private const float DockPadding = 3f;
        private const float DockHeadingHeight = 18f;
        private const float DockFeedbackHeight = 28f;

        // Frames after (re)build during which we re-assert our restored position, as a safety
        // margin against anything else (layout groups settling, etc.) nudging the rect during the
        // first frames after creation.
        private const int RestoreFrameBudget = 3;

        private readonly SuiteHubView _view = new SuiteHubView();

        private GameObject _root;
        private Canvas _canvas;
        private RectTransform _launcherRect;
        private GameObject _dockMenu;
        private RectTransform _dockMenuRect;
        private readonly SuiteDockInteractionState _dockState = new SuiteDockInteractionState();
        private bool _dockToggleQueued;
        private bool _dockRebuildQueued;
        private int _dockSignature;
        private string _dockFeedback = string.Empty;
        private RectTransform _windowRect;
        private GameObject _window;
        private CanvasGroup _windowGroup;
        private RectTransform _navContent;
        private RectTransform _pageContent;
        private ScrollRect _pageScroll;
        private RectTransform _pageViewport;
        private TextMeshProUGUI _modsButtonLabel;
        private TextMeshProUGUI _moduleVersionLabel;
        private TextMeshProUGUI _statusLabel;
        private TextMeshProUGUI _warningLabel;
        private TextMeshProUGUI _actionResultLabel;
        private TextMeshProUGUI _bridgeErrorLabel;
        private TextMeshProUGUI _developerBridgeErrorLabel;
        private double _windowActivatedAt;
        private Vector2 _windowMaximumEnvelope;
        private bool _contentFitEnabled;

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
        private Dictionary<string, SettingValueVisual> _settingVisuals =
            new Dictionary<string, SettingValueVisual>(StringComparer.Ordinal);

        private bool _navRebuildQueued;
        private bool _navSelectionDirty;
        private bool _pageRebuildQueued;
        private int _navSignature;
        private int _pageSignature;
        // Only module identity changes (and initial construction/recovery) reset the page to the
        // top. Disclosure/schema rebuilds preserve the player's current scroll, while dynamic
        // value refreshes never touch ScrollRect position at all.
        private bool _resetPageScrollOnNextRebuild;

        internal SuiteHubView View { get { return _view; } }
        internal bool IsBuilt { get { return _root != null; } }
        internal bool IsWindowOpen { get { return _window != null; } }
        internal int SortingOrder { get { return _canvas != null ? _canvas.sortingOrder : SuiteDockPolicy.DockCanvasSortingOrder; } }
        internal double WindowActivatedAt { get { return _windowActivatedAt; } }

        internal Action OnRequestClose;
        internal Action OnRequestResetPosition;
        internal Action OnRequestToggle;
        internal Action OnRequestOpenSuite;
        internal Action<string> OnRequestDockModuleOpen;
        internal Func<List<SuiteDockModuleState>> GetDockModules;
        internal Func<string, bool, bool> SetDockShortcutVisible;
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
            // Keep the dock above the suite's current retained module surfaces (which top out at
            // 540 in this export) so dragging it across another open mod panel remains a dock gesture
            // rather than hitting the underlying window. This still needs the normal live check
            // against native Erenshor overlays after a game update.
            _canvas.sortingOrder = SuiteDockPolicy.DockCanvasSortingOrder;

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
            _dockSignature = ComputeDockSignature();

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
            _dockMenu = null;
            _dockMenuRect = null;
            _dockState.Collapse();
            _dockToggleQueued = false;
            _dockRebuildQueued = false;
            _dockFeedback = string.Empty;
            _canvas = null;
            _navContent = null;
            _pageContent = null;
            _pageScroll = null;
            _pageViewport = null;
            _modsButtonLabel = null;
            ClearPageBindings();
            if (_root != null)
            {
                // Unity Destroy is end-of-frame. Deactivate first so an immediate rebuild/hot reload
                // can never leave two active Hub/dock roots raycasting during that frame.
                _root.SetActive(false);
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
            if (!visible)
            {
                CollapseDock();
                SuiteDragGuard.ForceReleaseIfHubOwned();
            }
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
                int previousWidth = _lastScreenWidth;
                int previousHeight = _lastScreenHeight;
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;
                ReclampAfterResolutionChange(previousWidth, previousHeight);
            }

            if (_dockToggleQueued)
            {
                _dockToggleQueued = false;
                if (_dockState.IsExpanded) CollapseDock(); else ExpandDock(false);
            }
            if (_dockRebuildQueued)
            {
                _dockRebuildQueued = false;
                if (_dockState.IsExpanded) RebuildDockMenu();
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

        private void ReclampAfterResolutionChange(int previousWidth, int previousHeight)
        {
            if (previousWidth <= 0) previousWidth = Screen.width;
            if (previousHeight <= 0) previousHeight = Screen.height;

            if (_launcherRect != null)
            {
                Vector2 size = _launcherRect.sizeDelta;
                Vector2 p = _launcherRect.anchoredPosition;
                SuiteRect r = SuiteUiGeometry.ResolvePanel(
                    SuiteUiGeometry.NormalizeAxis(p.x, previousWidth),
                    SuiteUiGeometry.NormalizeAxis(p.y, previousHeight),
                    size.x, size.y, Screen.width, Screen.height);
                _launcherRestorePos = new Vector2(r.X, r.Y);
                _launcherRect.anchoredPosition = _launcherRestorePos;
                if (_dockState.IsExpanded) RebuildDockMenu();
            }
            if (_windowRect != null)
            {
                Vector2 size = _windowRect.sizeDelta;
                Vector2 p = _windowRect.anchoredPosition;
                SuiteRect r = SuiteUiGeometry.ResolvePanel(
                    SuiteUiGeometry.NormalizeAxis(p.x, previousWidth),
                    SuiteUiGeometry.NormalizeAxis(p.y, previousHeight),
                    size.x, size.y, Screen.width, Screen.height);
                _windowRestorePos = new Vector2(r.X, r.Y);
                _windowRect.anchoredPosition = _windowRestorePos;
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
            if (_dockState.IsExpanded) RebuildDockMenu();
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
            Vector2 effectiveSize = _windowRect != null ? _windowRect.sizeDelta : size;
            _windowRestorePos = ResolvePanelVector(normalized, effectiveSize, Screen.width, Screen.height);
            if (_windowRect == null) return;
            _windowRect.anchoredPosition = _windowRestorePos;
            _windowRestoreFrames = RestoreFrameBudget;
        }

        // ---- launcher --------------------------------------------------------------------------

        private void BuildLauncher()
        {
            GameObject panel = NewUi("SuiteHubLauncher", _root.transform);
            Image bg = AddImage(panel, SuiteUiTheme.PanelBackground);
            bg.raycastTarget = true;
            AddCrispBorder(panel, SuiteUiTheme.PanelBorder);

            _launcherRect = panel.GetComponent<RectTransform>();
            AnchorBottomLeft(_launcherRect);
            _launcherRect.sizeDelta = new Vector2(LauncherWidth, LauncherHeight);

            // --- diamond drag grip: the ONLY drag surface -------------------------------------
            // Buttons are deliberately never draggable, so a click can never be swallowed by a drag.
            // This affordance must stay VISIBLE: the player is told to drag by the diamond, so it is
            // sized and coloured to read clearly against the panel rather than blending into it.
            GameObject grip = NewUi("SuiteHubLauncherDragHandle", panel.transform);
            Image gripImage = AddImage(grip, SuiteUiTheme.PanelBorder);
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
            Image buttonImage = AddImage(button, SuiteUiTheme.ControlBackground);
            buttonImage.raycastTarget = true;
            AddCrispBorder(button, SuiteUiTheme.ControlBorder);

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.offsetMin = new Vector2(ModsButtonLeft, 3f);
            buttonRect.offsetMax = new Vector2(-3f, -3f);

            Button b = button.AddComponent<Button>();
            b.targetGraphic = buttonImage;
            SetButtonColors(b, SuiteUiTheme.ControlBackground);
            b.onClick.AddListener(delegate { _dockToggleQueued = true; });

            _modsButtonLabel = NewLabel("SuiteHubModsLabel", button.transform, "MODS", 12f, FontStyles.Bold);
            Stretch(_modsButtonLabel.rectTransform);
            _modsButtonLabel.rectTransform.offsetMax = new Vector2(-18f, 0f);
            _modsButtonLabel.alignment = TextAlignmentOptions.Center;
            _modsButtonLabel.color = SuiteUiTheme.TextAccent;
            AddDockChevron(button.transform, false);
        }

        // The launcher uses a tiny owned Image-chevron instead of a TMP triangle glyph.  It is a
        // disclosure affordance only; the whole MODS button remains the click target.
        private static void AddDockChevron(Transform parent, bool pointsUp)
        {
            GameObject icon = NewUi("SuiteHubDockChevron", parent);
            RectTransform r = icon.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(12f, 10f); r.anchoredPosition = new Vector2(-10f, 0f);
            AddDockChevronBar(icon.transform, new Vector2(-2.2f, pointsUp ? -1f : 1f), pointsUp ? -45f : 45f);
            AddDockChevronBar(icon.transform, new Vector2(2.2f, pointsUp ? -1f : 1f), pointsUp ? 45f : -45f);
        }

        private static void AddDockChevronBar(Transform parent, Vector2 position, float rotation)
        {
            GameObject bar = NewUi("Bar", parent); Image image = AddImage(bar, SuiteUiTheme.TextSecondary);
            image.raycastTarget = false;
            RectTransform r = bar.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(2f, 7f); r.anchoredPosition = position;
            r.localRotation = Quaternion.Euler(0f, 0f, rotation);
        }

        private void SetDockChevron(bool expanded)
        {
            if (_modsButtonLabel == null) return;
            Transform button = _modsButtonLabel.transform.parent;
            Transform existing = button.Find("SuiteHubDockChevron");
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
            // Expanded means click to collapse upward; collapsed means click to expand down.
            AddDockChevron(button, expanded);
        }

        internal void CollapseDock()
        {
            _dockState.Collapse();
            _dockToggleQueued = false;
            _dockRebuildQueued = false;
            _dockFeedback = string.Empty;
            if (_dockMenu != null)
            {
                _dockMenu.SetActive(false);
                _dockMenu.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(_dockMenu);
                _dockMenu = null;
                _dockMenuRect = null;
            }
            if (_modsButtonLabel != null) _modsButtonLabel.text = "MODS";
            SetDockChevron(false);
            _dockSignature = ComputeDockSignature();
        }

        internal void CompleteDockLaunch(bool succeeded)
        {
            if (!_dockState.CompleteLaunch(succeeded)) return;
            CollapseDock();
        }

        internal void SetDockFeedback(string text)
        {
            _dockFeedback = text ?? string.Empty;
            if (_dockState.IsExpanded) _dockRebuildQueued = true;
        }

        internal void QueueDockRebuildIfContentChanged()
        {
            int signature = ComputeDockSignature();
            if (signature == _dockSignature) return;
            _dockSignature = signature;
            if (_dockState.IsExpanded) _dockRebuildQueued = true;
        }

        private void ExpandDock(bool customize)
        {
            if (_launcherRect == null) return;
            _dockState.Expand(customize);
            _dockFeedback = string.Empty;
            RebuildDockMenu();
            if (_modsButtonLabel != null) _modsButtonLabel.text = "MODS";
            SetDockChevron(true);
        }

        private int ComputeDockSignature()
        {
            List<SuiteDockModuleState> states = GetDockModules != null ? GetDockModules() : null;
            return SuiteDockPolicy.ComputeStructureSignature(states, _dockState.IsCustomizing);
        }

        private void RebuildDockMenu()
        {
            if (!_dockState.IsExpanded || _launcherRect == null) return;
            if (_dockMenu != null)
            {
                _dockMenu.SetActive(false);
                _dockMenu.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(_dockMenu);
                _dockMenu = null;
                _dockMenuRect = null;
            }

            List<SuiteDockModuleState> all = GetDockModules != null
                ? GetDockModules() : new List<SuiteDockModuleState>();
            List<SuiteDockModuleState> rows = SuiteDockPolicy.OrderedLaunchable(all, _dockState.IsCustomizing);
            int interactiveRows = 2 + rows.Count; // Mod Suite + Customize/Done + modules
            bool hasHeading = _dockState.IsCustomizing;
            bool hasFeedback = !string.IsNullOrEmpty(_dockFeedback);
            float height = DockPadding * 2f + interactiveRows * DockRowHeight +
                Math.Max(0, interactiveRows - 1) * DockRowGap;
            if (hasHeading) height += DockHeadingHeight + DockRowGap;
            if (hasFeedback) height += DockFeedbackHeight + DockRowGap;

            GameObject menu = NewUi("SuiteHubDockMenu", _launcherRect);
            Image bg = AddImage(menu, SuiteUiTheme.PanelBackground);
            bg.raycastTarget = true;
            AddCrispBorder(menu, SuiteUiTheme.PanelBorder);
            _dockMenu = menu;
            _dockMenuRect = menu.GetComponent<RectTransform>();
            bool upward = SuiteDockPolicy.ShouldOpenUpward(
                _launcherRect.anchoredPosition.y, LauncherHeight, height, Screen.height);
            _dockMenuRect.anchorMin = upward ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
            _dockMenuRect.anchorMax = _dockMenuRect.anchorMin;
            _dockMenuRect.pivot = upward ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
            _dockMenuRect.sizeDelta = new Vector2(LauncherWidth, height);
            _dockMenuRect.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup layout = menu.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = DockRowGap;
            layout.padding = new RectOffset((int)DockPadding, (int)DockPadding, (int)DockPadding, (int)DockPadding);

            AddRowButton(_dockMenuRect, "Forgotten Roads", DockRowHeight, false, delegate
            {
                if (OnRequestOpenSuite != null) OnRequestOpenSuite();
            });

            if (_dockState.IsCustomizing)
            {
                TextMeshProUGUI heading = NewLabel("DockCustomizeHeading", menu.transform,
                    "CUSTOMIZE SHORTCUTS", 9f, FontStyles.Bold);
                LayoutElement headingLayout = heading.gameObject.AddComponent<LayoutElement>();
                headingLayout.minHeight = DockHeadingHeight;
                headingLayout.preferredHeight = DockHeadingHeight;
                heading.alignment = TextAlignmentOptions.Left;
                heading.color = SuiteUiTheme.TextSecondary;

                for (int i = 0; i < rows.Count; i++)
                {
                    SuiteDockModuleState state = rows[i];
                    string capturedId = state.ModuleId;
                    bool visible = !state.Hidden;
                    string label = state.DisplayName + (visible ? "  [ON]" : "  [OFF]");
                    AddRowButton(_dockMenuRect, label, DockRowHeight, visible, delegate
                    {
                        if (SetDockShortcutVisible != null && SetDockShortcutVisible(capturedId, !visible))
                        {
                            _dockFeedback = string.Empty;
                            _dockRebuildQueued = true;
                        }
                    });
                }

                AddRowButton(_dockMenuRect, "Done", DockRowHeight, false, delegate
                {
                    _dockState.DoneCustomize();
                    _dockFeedback = string.Empty;
                    _dockRebuildQueued = true;
                });
            }
            else
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    SuiteDockModuleState state = rows[i];
                    string capturedId = state.ModuleId;
                    AddRowButton(_dockMenuRect, state.DisplayName, DockRowHeight, false, delegate
                    {
                        if (OnRequestDockModuleOpen != null) OnRequestDockModuleOpen(capturedId);
                    });
                }

                AddRowButton(_dockMenuRect, "Customize...", DockRowHeight, false, delegate
                {
                    _dockState.ShowCustomize();
                    _dockFeedback = string.Empty;
                    _dockRebuildQueued = true;
                });
            }

            if (hasFeedback)
            {
                TextMeshProUGUI feedback = NewLabel("DockFeedback", menu.transform,
                    _dockFeedback, 9f, FontStyles.Normal);
                LayoutElement feedbackLayout = feedback.gameObject.AddComponent<LayoutElement>();
                feedbackLayout.minHeight = DockFeedbackHeight;
                feedbackLayout.preferredHeight = DockFeedbackHeight;
                feedback.enableWordWrapping = true;
                feedback.alignment = TextAlignmentOptions.Left;
                feedback.color = SuiteUiTheme.TextWarning;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_dockMenuRect);
            _dockSignature = ComputeDockSignature();
            if (_modsButtonLabel != null) _modsButtonLabel.text = "MODS";
        }

        private void PersistLauncherPosition()
        {
            if (_launcherRect == null || PersistLauncherNormalized == null) return;
            Vector2 p = _launcherRect.anchoredPosition;
            SuiteRect clamped = SuiteUiGeometry.ClampLauncher(
                new SuiteRect(p.x, p.y, LauncherWidth, LauncherHeight),
                Screen.width, Screen.height, LauncherWidth);
            p = new Vector2(clamped.X, clamped.Y);
            _launcherRect.anchoredPosition = p;
            _launcherRestorePos = p;
            PersistLauncherNormalized(new Vector2(
                SuiteUiGeometry.NormalizeAxis(p.x, Screen.width),
                SuiteUiGeometry.NormalizeAxis(p.y, Screen.height)));
            if (_dockState.IsExpanded) RebuildDockMenu();
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
            guard.OnPointerActivated = string.Equals(label, "header", StringComparison.Ordinal)
                ? (Action)MarkWindowActivated : null;
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
            _windowMaximumEnvelope = size;
            _contentFitEnabled = false;

            GameObject panel = NewUi("SuiteHubWindow", _root.transform);
            Image bg = AddImage(panel, SuiteUiTheme.PanelBackground);
            bg.raycastTarget = true;
            AddCrispBorder(panel, SuiteUiTheme.PanelBorder);

            _windowRect = panel.GetComponent<RectTransform>();
            AnchorBottomLeft(_windowRect);
            // Start from the configured/default maximum envelope. Initial content is laid out
            // synchronously below, then the window is fit once before the frame is presented.
            _windowRect.sizeDelta = size;

            _windowGroup = panel.AddComponent<CanvasGroup>();
            _windowGroup.blocksRaycasts = true;
            _windowGroup.interactable = true;

            BuildHeader(panel.transform);
            BuildNavigation(panel.transform);
            BuildPageArea(panel.transform);

            _window = panel;
            MarkWindowActivated();
            SuiteHubDiagnostics.WindowCreates++;

            RebuildNav();
            _resetPageScrollOnNextRebuild = true;
            RebuildPage();

            Vector2 fitted = ResolveInitialCompactWindowSize(size);
            _windowRect.sizeDelta = fitted;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_windowRect);
            ApplyWindowPosition(normalized, fitted);
            _contentFitEnabled = true;

            _navSignature = ComputeNavSignature();
            _pageSignature = ComputePageSignature();
            SuiteHubDiagnostics.Log("window created maxSize=" + size + " fittedSize=" + fitted + " normalized=" + normalized);
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
            ClearPageBindings();
            _windowActivatedAt = 0d;
            _contentFitEnabled = false;
            _windowMaximumEnvelope = Vector2.zero;
            _resetPageScrollOnNextRebuild = false;
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
            _resetPageScrollOnNextRebuild = SuiteHubScrollPolicy.ShouldResetFor(
                SuiteHubPageChangeReason.ModuleSelection);
            _pageRebuildQueued = true;
        }

        // A module was installed/uninstalled: the nav list's actual row set must change. Full
        // teardown/rebuild is unavoidable here, but this should fire rarely (a discovery poll
        // noticing a plugin file appear/disappear), not on every module selection.
        internal void QueueNavStructureRebuild()
        {
            _navRebuildQueued = true;
            // Installed-row changes can invalidate the selected module and force Overview. Treat
            // that recovery like an identity change so the replacement page starts at the top.
            _resetPageScrollOnNextRebuild = true;
            _pageRebuildQueued = true;
        }

        // Page STRUCTURE changed (selection/disclosure/schema availability). Dynamic status,
        // warning, action-result, bool and choice values are retained bindings and must update in
        // place instead of taking this rebuild path. The nav list is never touched here.
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
                SuiteHubDiagnostics.Log("page structure signature changed -> page rebuild queued");
            }

            // Always apply values that still have retained bindings now, even if the same mutation
            // also changed schema and queued an atomic structural rebuild for the next Update. This
            // gives bool/status feedback in the click frame instead of waiting for reconstruction.
            RefreshDynamicPageValues();
        }

        // Navigation structure contains only the ordered installed rows. Selection is dynamic
        // visual state and is deliberately excluded; otherwise the next bridge poll would undo the
        // in-place highlight update by scheduling a delayed full nav rebuild.
        private int ComputeNavSignature()
        {
            return SuiteHubRefreshPolicy.ComputeNavStructureSignature(GetMods != null ? GetMods() : null);
        }

        private int ComputePageSignature()
        {
            bool developerEnabled = GetDeveloperEnabled != null && GetDeveloperEnabled();
            if (_view.IsOverviewSelected)
            {
                List<SuiteHubRefreshPolicy.OverviewModuleShape> shapes =
                    new List<SuiteHubRefreshPolicy.OverviewModuleShape>();
                List<ModPresence> mods = CurrentMods();
                for (int i = 0; i < mods.Count; i++)
                {
                    if (!mods[i].Installed) continue;
                    SuiteModuleDescriptor runtime = ErenshorSuiteHubPlugin.GetRegisteredModule(mods[i].ModuleId);
                    shapes.Add(new SuiteHubRefreshPolicy.OverviewModuleShape(
                        mods[i].ModuleId, runtime != null, runtime == null ? string.Empty : runtime.Version));
                }
                return SuiteHubRefreshPolicy.ComputeOverviewStructureSignature(shapes, developerEnabled);
            }

            SuiteModuleDescriptor descriptor = ErenshorSuiteHubPlugin.GetRegisteredModule(_view.SelectedModuleId);
            AuraModuleBridge bridge = ErenshorSuiteHubPlugin.GetModuleBridge(_view.SelectedModuleId);
            return SuiteHubRefreshPolicy.ComputePageStructureSignature(
                _view.SelectedModuleId,
                descriptor != null,
                descriptor == null ? null : descriptor.Actions,
                bridge == null ? null : bridge.CachedBasicSettings,
                bridge == null ? null : bridge.CachedAdvancedSettings,
                bridge == null ? null : bridge.CachedDeveloperSettings,
                _view.ShowAdvanced,
                _view.ShowDeveloper,
                developerEnabled);
        }

        private void BuildHeader(Transform parent)
        {
            GameObject header = NewUi("SuiteHubWindowHeader", parent);
            Image headerImage = AddImage(header, SuiteUiTheme.HeaderBackground);
            headerImage.raycastTarget = true;
            AddCrispBorder(header, SuiteUiTheme.PanelBorder);

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
            Image markImage = AddImage(mark, SuiteUiTheme.PanelBorder);
            markImage.raycastTarget = false;
            RectTransform markRect = mark.GetComponent<RectTransform>();
            markRect.anchorMin = new Vector2(0f, 0.5f);
            markRect.anchorMax = new Vector2(0f, 0.5f);
            markRect.pivot = new Vector2(0.5f, 0.5f);
            markRect.sizeDelta = new Vector2(GripSize * 0.75f, GripSize * 0.75f);
            markRect.anchoredPosition = new Vector2(GripInset, 0f);
            mark.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            TextMeshProUGUI title = NewLabel("SuiteHubTitle", header.transform,
                "FORGOTTEN ROADS", 13f, FontStyles.Bold);
            RectTransform tr = title.rectTransform;
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.offsetMin = new Vector2(ModsButtonLeft + 6f, 0f);
            tr.offsetMax = new Vector2(-120f, 0f);
            title.alignment = TextAlignmentOptions.Left;
            title.color = SuiteUiTheme.TextAccent;

            MakeHeaderButton(header.transform, "RESET", 58f, -62f,
                delegate { if (OnRequestResetPosition != null) OnRequestResetPosition(); });
            MakeHeaderButton(header.transform, "X", 26f, -4f,
                delegate { if (OnRequestClose != null) OnRequestClose(); });
        }

        private void MakeHeaderButton(Transform parent, string text, float width, float rightOffset, UnityEngine.Events.UnityAction action)
        {
            GameObject go = NewUi("SuiteHubHeaderButton_" + text, parent);
            Image img = AddImage(go, SuiteUiTheme.ControlBackground);
            img.raycastTarget = true;
            AddCrispBorder(go, SuiteUiTheme.ControlBorder);

            RectTransform r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(1f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.sizeDelta = new Vector2(width, 20f);
            r.anchoredPosition = new Vector2(rightOffset, 0f);

            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            SetButtonColors(b, SuiteUiTheme.ControlBackground);
            b.onClick.AddListener(delegate
            {
                MarkWindowActivated();
                if (action != null) action();
            });

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
            r.offsetMin = new Vector2(SuiteUiTheme.OuterPadding, SuiteUiTheme.OuterPadding);
            r.offsetMax = new Vector2(SuiteUiTheme.OuterPadding + NavWidth,
                -(HeaderHeight + SuiteUiTheme.OuterPadding));

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
            r.offsetMin = new Vector2(NavWidth + SuiteUiTheme.OuterPadding * 2f, SuiteUiTheme.OuterPadding);
            r.offsetMax = new Vector2(-SuiteUiTheme.OuterPadding,
                -(HeaderHeight + SuiteUiTheme.OuterPadding));

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
            // The parent must own child heights. With this false, Unity uses each RectTransform's
            // existing/default height (often ~100px) and ignores our 6px spacer / 24px row
            // LayoutElements, which creates the large dead gaps seen live inside otherwise-compact
            // windows. TextMeshPro and LayoutElement now report preferred/min heights to this group.
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = SuiteUiTheme.RowGap;
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
            _navSignature = ComputeNavSignature();
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
            Color background = selected ? SuiteUiTheme.SelectedBackground : SuiteUiTheme.ControlBackground;
            if (row.Background != null) row.Background.color = background;
            if (row.Button != null) SetButtonColors(row.Button, background);
            if (row.Label != null) row.Label.color = selected ? SuiteUiTheme.TextAccent : SuiteUiTheme.TextPrimary;
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
            PageBindingSnapshot oldBindings = CapturePageBindings();
            float previousScroll = _pageScroll.verticalNormalizedPosition;
            bool resetScrollToTop = oldContent == null || _resetPageScrollOnNextRebuild;
            RectTransform newContent = CreateContentRoot(_pageViewport);
            BeginPageBindings();
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
                RestorePageBindings(oldBindings);
                UnityEngine.Object.Destroy(newContent.gameObject);
                throw;
            }

            SuiteHubDiagnostics.PageRebuilds++;
            // Build new page completely before exposing it: compute its final layout while it is
            // still just a sibling, not yet the ScrollRect's active content.
            LayoutRebuilder.ForceRebuildLayoutImmediate(newContent);

            _pageScroll.content = newContent;
            if (resetScrollToTop) _pageScroll.StopMovement();
            _pageScroll.verticalNormalizedPosition = SuiteHubScrollPolicy.ResolveAfterStructuralRebuild(
                previousScroll, resetScrollToTop);
            _resetPageScrollOnNextRebuild = false;

            if (oldContent != null)
            {
                oldContent.gameObject.SetActive(false); // stop participating in layout/render now
                oldContent.SetParent(null, false);
                UnityEngine.Object.Destroy(oldContent.gameObject);
            }

            // Structural page changes (module selection, disclosure, schema) are the only time the
            // open Hub changes height. Dynamic status/value refreshes never call this path, so the
            // panel does not breathe or flicker on the one-second bridge poll.
            if (_contentFitEnabled) FitWindowToCurrentStructure();

            _pageSignature = ComputePageSignature();
            RefreshDynamicPageValues();
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
                MarkWindowActivated();
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
            AddSpacer(_pageContent, SuiteUiTheme.SectionGap);
            AddSectionLabel(_pageContent, "STATUS");

            if (runtime == null)
            {
                AddMutedLabel(_pageContent,
                    "Suite controls unavailable. The standalone interface remains authoritative and available.");
                _bridgeErrorLabel = AddWarningLabel(_pageContent, string.Empty);
                SetOptionalText(_bridgeErrorLabel,
                    bridge != null && !string.IsNullOrEmpty(bridge.LastError) ? "Bridge rejected: " + bridge.LastError : string.Empty);
                return;
            }

            _statusLabel = AddBodyLabel(_pageContent, string.Empty);
            _warningLabel = AddWarningLabel(_pageContent, string.Empty);
            SetOptionalText(_statusLabel, string.IsNullOrEmpty(runtime.Status) ? "Available" : runtime.Status);
            SetOptionalText(_warningLabel, runtime.Warning);

            List<SuiteSettingDescriptor> basic = bridge == null ? null : bridge.CachedBasicSettings;
            List<SuiteSettingDescriptor> advanced = bridge == null ? null : bridge.CachedAdvancedSettings;
            List<SuiteSettingDescriptor> developer = bridge == null ? null : bridge.CachedDeveloperSettings;

            // Compact sequential player flow. Sections only exist when they contain something:
            // title -> status -> basic -> panel/actions -> disclosure rows. No fixed vertical
            // regions and no empty PANEL/CONTROLS reservation.
            if (SuiteHubPagePolicy.HasBasicSection(basic))
            {
                AddSpacer(_pageContent, SuiteUiTheme.SectionGap);
                AddSectionLabel(_pageContent, "BASIC");
                BuildSettings(moduleId, basic);
            }

            if (SuiteHubPagePolicy.HasPanelSection(runtime.Actions))
            {
                AddSpacer(_pageContent, SuiteUiTheme.SectionGap);
                AddSectionLabel(_pageContent, "PANEL");
                AddModuleActionButton(moduleId, "openPanel", "Open " + def.DisplayName);
            }

            if (SuiteHubPagePolicy.HasCompactActionSection(moduleId, runtime.Actions))
            {
                AddSpacer(_pageContent, SuiteUiTheme.SectionGap);
                AddSectionLabel(_pageContent, "ACTIONS");
                BuildCompactModuleActions(moduleId, runtime);
            }

            _actionResultLabel = AddMutedLabel(_pageContent, string.Empty);
            SetOptionalText(_actionResultLabel, _view.LastActionResult);

            if (SuiteHubPagePolicy.HasAdvancedSection(advanced, runtime.Actions))
            {
                AddSpacer(_pageContent, SuiteUiTheme.SectionGap);
                AddDisclosureRow(_pageContent, "Advanced", _view.ShowAdvanced,
                    delegate
                    {
                        MarkWindowActivated();
                        _view.SetAdvanced(!_view.ShowAdvanced);
                        QueuePageRebuild();
                    });
                if (_view.ShowAdvanced)
                {
                    BuildSettings(moduleId, advanced);
                    if (runtime.HasAction("resetPanel")) AddModuleActionButton(moduleId, "resetPanel", "Reset panel position");
                    if (runtime.HasAction("resetLauncher")) AddModuleActionButton(moduleId, "resetLauncher", "Reset launcher position");
                }
            }

            if (GetDeveloperEnabled != null && GetDeveloperEnabled())
            {
                AddSpacer(_pageContent, SuiteUiTheme.SectionGap);
                AddDisclosureRow(_pageContent, "Developer", _view.ShowDeveloper,
                    delegate
                    {
                        MarkWindowActivated();
                        _view.SetDeveloper(!_view.ShowDeveloper);
                        QueuePageRebuild();
                    });
                if (_view.ShowDeveloper)
                {
                    _moduleVersionLabel = AddMutedLabel(_pageContent, "Version: " + runtime.Version);
                    BuildSettings(moduleId, developer);
                    _developerBridgeErrorLabel = AddWarningLabel(_pageContent, string.Empty);
                    SetOptionalText(_developerBridgeErrorLabel,
                        bridge != null && !string.IsNullOrEmpty(bridge.LastError) ? "Last bridge error: " + bridge.LastError : string.Empty);
                }
            }
        }

        // Only argument-free actions with already-established player semantics are rendered here.
        // In particular, Nemesis 'select' intentionally remains transport-only because it requires
        // a current candidate name; the Hub must not invent a text-entry or candidate-selection API.
        private void BuildCompactModuleActions(string moduleId, SuiteModuleDescriptor runtime)
        {
            if (runtime == null || !string.Equals(moduleId, "nemesis", StringComparison.Ordinal)) return;
            if (runtime.HasAction("clear")) AddModuleActionButton(moduleId, "clear", "Clear rival");
            if (runtime.HasAction("confirm")) AddModuleActionButton(moduleId, "confirm", "Confirm change");
            if (runtime.HasAction("cancel")) AddModuleActionButton(moduleId, "cancel", "Cancel change");
        }

        private void AddModuleActionButton(string moduleId, string actionId, string label)
        {
            string capturedModule = moduleId;
            string capturedAction = actionId;
            AddRowButton(_pageContent, label, SuiteUiTheme.RowHeight, false, delegate
            {
                MarkWindowActivated();
                string result;
                ErenshorSuiteHubPlugin.TryInvokeModuleAction(capturedModule, capturedAction, string.Empty, out result);
                _view.SetActionResult(result);
                QueueRebuildIfContentChanged();
            });
        }

        private void BuildSettings(string moduleId, List<SuiteSettingDescriptor> settings)
        {
            if (settings == null || settings.Count == 0) return;

            for (int i = 0; i < settings.Count; i++)
            {
                SuiteSettingDescriptor s = settings[i];
                string capturedModule = moduleId;
                string capturedId = s.Id;
                SettingValueVisual visual;

                if (s.Kind == SuiteSettingKind.Bool)
                {
                    bool current = SuiteSettingDisplayPolicy.IsOn(s.Value);
                    if (s.Mutable)
                    {
                        visual = AddToggleRow(_pageContent, s.Label, current, delegate
                        {
                            MarkWindowActivated();
                            ToggleSetting(capturedModule, capturedId);
                        });
                    }
                    else
                    {
                        visual = AddBooleanStateRow(_pageContent, s.Label, current);
                    }
                }
                else if (s.Kind == SuiteSettingKind.Choice && s.Mutable && s.Options.Count > 0)
                {
                    visual = AddChoiceRow(_pageContent, s.Label, s.Value, delegate
                    {
                        MarkWindowActivated();
                        CycleChoice(capturedModule, capturedId, 1);
                    });
                }
                else
                {
                    visual = AddReadOnlySettingRow(_pageContent, s.Label, s.Value,
                        s.Mutable ? " (display only)" : " (read-only)");
                }

                visual.Tier = s.Tier;
                visual.SettingId = s.Id;
                _settingVisuals[SettingBindingKey(s.Tier, s.Id)] = visual;
            }
        }

        private void ToggleSetting(string moduleId, string settingId)
        {
            AuraModuleBridge bridge = ErenshorSuiteHubPlugin.GetModuleBridge(moduleId);
            SuiteSettingDescriptor current = bridge == null ? null : bridge.FindCachedSetting(settingId);
            if (current == null || current.Kind != SuiteSettingKind.Bool)
            {
                _view.SetActionResult("Setting is no longer available");
                QueuePageRebuild();
                return;
            }

            bool next = !SuiteSettingDisplayPolicy.IsOn(current.Value);
            string result;
            bool succeeded = ErenshorSuiteHubPlugin.TrySetModuleSetting(
                moduleId, settingId, next ? "true" : "false", out result);
            _view.SetActionResult(SuiteSettingMutationPolicy.VisibleResult(succeeded, result));
            // Success re-reads authoritative module state synchronously; failure still needs its
            // rejection text surfaced immediately. Both paths reconcile retained dynamic bindings
            // now, while genuine schema changes use the normal atomic rebuild path.
            SuiteSettingMutationRefreshPlan plan = SuiteSettingMutationPolicy.Resolve(succeeded);
            if (plan.RefreshRetainedValues) QueueRebuildIfContentChanged();
        }

        private void CycleChoice(string moduleId, string settingId, int direction)
        {
            AuraModuleBridge bridge = ErenshorSuiteHubPlugin.GetModuleBridge(moduleId);
            SuiteSettingDescriptor current = bridge == null ? null : bridge.FindCachedSetting(settingId);
            if (current == null || current.Kind != SuiteSettingKind.Choice || current.Options.Count == 0)
            {
                _view.SetActionResult("Setting is no longer available");
                QueuePageRebuild();
                return;
            }

            int index = current.Options.IndexOf(current.Value);
            if (index < 0) index = 0;
            index = (index + direction + current.Options.Count) % current.Options.Count;
            string nextValue = current.Options[index];
            string result;
            bool succeeded = ErenshorSuiteHubPlugin.TrySetModuleSetting(moduleId, settingId, nextValue, out result);
            _view.SetActionResult(SuiteSettingMutationPolicy.VisibleResult(succeeded, result));
            SuiteSettingMutationRefreshPlan plan = SuiteSettingMutationPolicy.Resolve(succeeded);
            if (plan.RefreshRetainedValues) QueueRebuildIfContentChanged();
        }

        private void RefreshDynamicPageValues()
        {
            if (_window == null || _pageContent == null || _view.IsOverviewSelected) return;

            SuiteModuleDescriptor runtime = ErenshorSuiteHubPlugin.GetRegisteredModule(_view.SelectedModuleId);
            AuraModuleBridge bridge = ErenshorSuiteHubPlugin.GetModuleBridge(_view.SelectedModuleId);
            bool layoutDirty = false;

            if (runtime == null)
            {
                layoutDirty |= SetOptionalText(_bridgeErrorLabel,
                    bridge != null && !string.IsNullOrEmpty(bridge.LastError) ? "Bridge rejected: " + bridge.LastError : string.Empty);
            }
            else
            {
                if (_moduleVersionLabel != null)
                {
                    string next = "Version: " + (runtime.Version ?? string.Empty);
                    if (!string.Equals(_moduleVersionLabel.text, next, StringComparison.Ordinal))
                    {
                        _moduleVersionLabel.text = next;
                        layoutDirty = true;
                    }
                }
                layoutDirty |= SetOptionalText(_statusLabel, string.IsNullOrEmpty(runtime.Status) ? "Available" : runtime.Status);
                layoutDirty |= SetOptionalText(_warningLabel, runtime.Warning);
            }

            layoutDirty |= SetOptionalText(_actionResultLabel, _view.LastActionResult);
            layoutDirty |= SetOptionalText(_developerBridgeErrorLabel,
                bridge != null && !string.IsNullOrEmpty(bridge.LastError) ? "Last bridge error: " + bridge.LastError : string.Empty);

            if (bridge != null)
            {
                layoutDirty |= RefreshSettingVisuals(bridge.CachedBasicSettings);
                layoutDirty |= RefreshSettingVisuals(bridge.CachedAdvancedSettings);
                layoutDirty |= RefreshSettingVisuals(bridge.CachedDeveloperSettings);
            }

            if (layoutDirty && _pageContent != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_pageContent);
        }

        private bool RefreshSettingVisuals(List<SuiteSettingDescriptor> settings)
        {
            if (settings == null) return false;
            bool changed = false;
            for (int i = 0; i < settings.Count; i++)
            {
                SuiteSettingDescriptor s = settings[i];
                SettingValueVisual visual;
                if (!_settingVisuals.TryGetValue(SettingBindingKey(s.Tier, s.Id), out visual)) continue;
                changed |= ApplySettingValue(visual, s.Value);
            }
            return changed;
        }

        private static bool ApplySettingValue(SettingValueVisual visual, string value)
        {
            if (visual == null || visual.ValueLabel == null) return false;
            value = value ?? string.Empty;
            string nextText;

            if (visual.Kind == SuiteSettingKind.Bool)
            {
                bool on = SuiteSettingDisplayPolicy.IsOn(value);
                nextText = SuiteSettingDisplayPolicy.BooleanButtonText(visual.SettingLabel, on);
                Color nextColor = on ? SuiteUiTheme.SelectedBackground : SuiteUiTheme.ControlBackground;
                if (visual.StateBackground != null) visual.StateBackground.color = nextColor;
                if (visual.Button != null) SetButtonColors(visual.Button, nextColor);
                visual.ValueLabel.color = on ? SuiteUiTheme.TextAccent : SuiteUiTheme.TextPrimary;
            }
            else if (visual.Kind == SuiteSettingKind.Choice)
            {
                nextText = value + "  >";
            }
            else
            {
                nextText = visual.Prefix + value + visual.Suffix;
            }

            if (string.Equals(visual.ValueLabel.text, nextText, StringComparison.Ordinal)) return false;
            visual.ValueLabel.text = nextText;
            return true;
        }

        private static string SettingBindingKey(SuiteSettingTier tier, string id)
        {
            return ((int)tier).ToString() + ":" + (id ?? string.Empty);
        }

        private void MarkWindowActivated()
        {
            _windowActivatedAt = Time.realtimeSinceStartup;
        }

        private void FitWindowToCurrentStructure()
        {
            if (_windowRect == null || _pageContent == null) return;
            float structuralContent = EstimateCurrentStructuralContentHeight();
            float targetHeight = SuiteHubLayoutPolicy.ResolveWindowHeight(
                structuralContent, _windowMaximumEnvelope.y, Screen.height);
            Vector2 currentSize = _windowRect.sizeDelta;
            if (Math.Abs(currentSize.y - targetHeight) < 0.5f) return;

            SuiteRect resized = SuiteUiGeometry.ResizeWindowKeepingTop(
                new SuiteRect(_windowRect.anchoredPosition.x, _windowRect.anchoredPosition.y, currentSize.x, currentSize.y),
                targetHeight, Screen.width, Screen.height);
            _windowRect.sizeDelta = new Vector2(currentSize.x, resized.Height);
            _windowRestorePos = new Vector2(resized.X, resized.Y);
            _windowRect.anchoredPosition = _windowRestorePos;
            _windowRestoreFrames = RestoreFrameBudget;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_windowRect);
            SuiteHubDiagnostics.Log("window structural-fit height=" + resized.Height +
                " modelContent=" + structuralContent);
        }

        private Vector2 ResolveInitialCompactWindowSize(Vector2 maximumEnvelope)
        {
            // Never ask the ScrollRect's stretched content root how tall the outer window should
            // be. Its preferred height may inherit the already-reserved maximum envelope. Instead
            // use the same explicit structural model used for module switches/disclosure changes.
            float structuralContent = EstimateCurrentStructuralContentHeight();
            float height = SuiteHubLayoutPolicy.ResolveWindowHeight(
                structuralContent, maximumEnvelope.y, Screen.height);
            return new Vector2(maximumEnvelope.x, height);
        }

        private float EstimateCurrentStructuralContentHeight()
        {
            bool developerEnabled = GetDeveloperEnabled != null && GetDeveloperEnabled();
            List<ModPresence> mods = CurrentMods();
            if (_view.IsOverviewSelected)
            {
                int installed = 0;
                for (int i = 0; i < mods.Count; i++) if (mods[i].Installed) installed++;
                return SuiteHubLayoutPolicy.EstimateOverviewContentHeight(installed, developerEnabled);
            }

            ModPresence presence = default(ModPresence);
            bool found = false;
            for (int i = 0; i < mods.Count; i++)
            {
                if (!mods[i].Installed || !string.Equals(mods[i].ModuleId, _view.SelectedModuleId, StringComparison.Ordinal))
                    continue;
                presence = mods[i];
                found = true;
                break;
            }
            if (!found || presence.Definition == null)
            {
                int installed = 0;
                for (int i = 0; i < mods.Count; i++) if (mods[i].Installed) installed++;
                return SuiteHubLayoutPolicy.EstimateOverviewContentHeight(installed, developerEnabled);
            }

            SuiteModuleDescriptor runtime = ErenshorSuiteHubPlugin.GetRegisteredModule(_view.SelectedModuleId);
            AuraModuleBridge bridge = ErenshorSuiteHubPlugin.GetModuleBridge(_view.SelectedModuleId);
            return SuiteHubLayoutPolicy.EstimateModuleContentHeight(
                _view.SelectedModuleId,
                runtime != null,
                runtime == null ? null : runtime.Actions,
                bridge == null ? null : bridge.CachedBasicSettings,
                bridge == null ? null : bridge.CachedAdvancedSettings,
                bridge == null ? null : bridge.CachedDeveloperSettings,
                _view.ShowAdvanced,
                _view.ShowDeveloper,
                developerEnabled);
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

        private static void AddCrispBorder(GameObject go, Color color)
        {
            if (go == null) return;
            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(SuiteUiTheme.BorderPixels, -SuiteUiTheme.BorderPixels);
            outline.useGraphicAlpha = false;
        }

        private static void SetButtonColors(Button b, Color normal)
        {
            ColorBlock c = b.colors;
            c.normalColor = normal;
            c.highlightedColor = SuiteUiTheme.ControlHover;
            c.pressedColor = SuiteUiTheme.ControlPressed;
            c.disabledColor = SuiteUiTheme.HeaderBackground;
            c.fadeDuration = 0.05f;
            b.colors = c;
            if (b.targetGraphic != null) b.targetGraphic.color = normal;
        }

        private static TextMeshProUGUI NewLabel(string name, Transform parent, string text, float size, FontStyles style)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = SuiteUiTheme.TextPrimary;
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
            le.minHeight = SuiteUiMetrics.TextRowHeight;
            // Leave preferredHeight unset so the parent VerticalLayoutGroup can query TMP's own
            // preferred height (including wrapping). A child ContentSizeFitter would fight the
            // parent now that childControlHeight=true.
            le.preferredHeight = -1f;
            le.flexibleHeight = 0f;
            return t;
        }

        private static void AddSectionLabel(RectTransform parent, string text)
        {
            AddTextRow(parent, text, 11f, SuiteUiTheme.TextSecondary, FontStyles.Bold);
        }

        private static TextMeshProUGUI AddBodyLabel(RectTransform parent, string text)
        {
            return AddTextRow(parent, text, 11f, SuiteUiTheme.TextPrimary, FontStyles.Normal);
        }

        private static TextMeshProUGUI AddMutedLabel(RectTransform parent, string text)
        {
            return AddTextRow(parent, text, 10f, SuiteUiTheme.TextSecondary, FontStyles.Normal);
        }

        private static TextMeshProUGUI AddWarningLabel(RectTransform parent, string text)
        {
            return AddTextRow(parent, text, 10f, SuiteUiTheme.TextWarning, FontStyles.Normal);
        }

        // References kept for a persistent nav row so its selected/unselected style can be applied
        // in place (RefreshNavSelectionVisual) without destroying and recreating the row.
        private struct NavRowVisual
        {
            internal Image Background;
            internal Button Button;
            internal TextMeshProUGUI Label;
        }

        private sealed class SettingValueVisual
        {
            internal SuiteSettingTier Tier;
            internal string SettingId;
            internal SuiteSettingKind Kind;
            internal TextMeshProUGUI ValueLabel;
            internal Image StateBackground;
            internal Button Button;
            internal string SettingLabel = string.Empty;
            internal string Prefix = string.Empty;
            internal string Suffix = string.Empty;
        }

        private sealed class PageBindingSnapshot
        {
            internal TextMeshProUGUI ModuleVersion;
            internal TextMeshProUGUI Status;
            internal TextMeshProUGUI Warning;
            internal TextMeshProUGUI ActionResult;
            internal TextMeshProUGUI BridgeError;
            internal TextMeshProUGUI DeveloperBridgeError;
            internal Dictionary<string, SettingValueVisual> Settings;
        }

        private PageBindingSnapshot CapturePageBindings()
        {
            return new PageBindingSnapshot
            {
                ModuleVersion = _moduleVersionLabel,
                Status = _statusLabel,
                Warning = _warningLabel,
                ActionResult = _actionResultLabel,
                BridgeError = _bridgeErrorLabel,
                DeveloperBridgeError = _developerBridgeErrorLabel,
                Settings = _settingVisuals
            };
        }

        private void RestorePageBindings(PageBindingSnapshot snapshot)
        {
            if (snapshot == null) { ClearPageBindings(); return; }
            _moduleVersionLabel = snapshot.ModuleVersion;
            _statusLabel = snapshot.Status;
            _warningLabel = snapshot.Warning;
            _actionResultLabel = snapshot.ActionResult;
            _bridgeErrorLabel = snapshot.BridgeError;
            _developerBridgeErrorLabel = snapshot.DeveloperBridgeError;
            _settingVisuals = snapshot.Settings ?? new Dictionary<string, SettingValueVisual>(StringComparer.Ordinal);
        }

        private void BeginPageBindings()
        {
            _moduleVersionLabel = null;
            _statusLabel = null;
            _warningLabel = null;
            _actionResultLabel = null;
            _bridgeErrorLabel = null;
            _developerBridgeErrorLabel = null;
            _settingVisuals = new Dictionary<string, SettingValueVisual>(StringComparer.Ordinal);
        }

        private void ClearPageBindings()
        {
            BeginPageBindings();
        }

        private static bool SetOptionalText(TextMeshProUGUI label, string text)
        {
            if (label == null) return false;
            text = text ?? string.Empty;
            bool shouldShow = text.Length > 0;
            bool changed = !string.Equals(label.text, text, StringComparison.Ordinal) ||
                label.gameObject.activeSelf != shouldShow;
            if (!changed) return false;
            label.text = text;
            label.gameObject.SetActive(shouldShow);
            return true;
        }

        private static NavRowVisual AddNavRow(RectTransform parent, string text, bool selected,
            UnityEngine.Events.UnityAction action)
        {
            NavRowVisual row = new NavRowVisual();
            row.Background = AddRowButton(parent, text, SuiteUiTheme.RowHeight, selected, action, out row.Button);
            row.Label = row.Background != null ? row.Background.GetComponentInChildren<TextMeshProUGUI>() : null;
            return row;
        }

        private static Image AddRowButton(RectTransform parent, string text, float height, bool selected,
            UnityEngine.Events.UnityAction action)
        {
            Button ignored;
            return AddRowButton(parent, text, height, selected, action, out ignored);
        }

        private static Image AddRowButton(RectTransform parent, string text, float height, bool selected,
            UnityEngine.Events.UnityAction action, out Button button)
        {
            GameObject go = NewUi("RowButton", parent);
            Color normal = selected ? SuiteUiTheme.SelectedBackground : SuiteUiTheme.ControlBackground;
            Image img = AddImage(go, normal);
            img.raycastTarget = true;
            AddCrispBorder(go, SuiteUiTheme.ControlBorder);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            button = go.AddComponent<Button>();
            button.targetGraphic = img;
            SetButtonColors(button, normal);
            button.onClick.AddListener(action);

            TextMeshProUGUI label = NewLabel("Label", go.transform, text, 11f, FontStyles.Normal);
            RectTransform lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(8f, 0f);
            lr.offsetMax = new Vector2(-8f, 0f);
            label.alignment = TextAlignmentOptions.Left;
            label.color = selected ? SuiteUiTheme.TextAccent : SuiteUiTheme.TextPrimary;
            return img;
        }

        // Disclosure is semantically different from boolean state. The visible affordance is a
        // small chevron built from ordinary Image bars, so it does not depend on a particular TMP
        // font containing triangle glyphs. The entire row is the hit target.
        private static void AddDisclosureRow(RectTransform parent, string label, bool expanded,
            UnityEngine.Events.UnityAction action)
        {
            GameObject go = NewUi("DisclosureRow", parent);
            Color normal = expanded ? SuiteUiTheme.SelectedBackground : SuiteUiTheme.ControlBackground;
            Image rowImg = AddImage(go, normal);
            rowImg.raycastTarget = true;
            AddCrispBorder(go, expanded ? SuiteUiTheme.PanelBorder : SuiteUiTheme.ControlBorder);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = SuiteUiTheme.DisclosureRowHeight;
            le.preferredHeight = SuiteUiTheme.DisclosureRowHeight;

            Button b = go.AddComponent<Button>();
            b.targetGraphic = rowImg;
            SetButtonColors(b, normal);
            b.onClick.AddListener(action);

            GameObject icon = NewUi("Chevron", go.transform);
            RectTransform ir = icon.GetComponent<RectTransform>();
            ir.anchorMin = new Vector2(0f, 0.5f);
            ir.anchorMax = new Vector2(0f, 0.5f);
            ir.pivot = new Vector2(0.5f, 0.5f);
            ir.sizeDelta = new Vector2(14f, 14f);
            ir.anchoredPosition = new Vector2(11f, 0f);
            AddChevronBars(icon.transform, expanded);

            TextMeshProUGUI text = NewLabel("Label", go.transform, label, 11f, FontStyles.Normal);
            RectTransform tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(26f, 0f);
            tr.offsetMax = new Vector2(-8f, 0f);
            text.alignment = TextAlignmentOptions.Left;
            text.color = expanded ? SuiteUiTheme.TextAccent : SuiteUiTheme.TextPrimary;
        }

        private static void AddChevronBars(Transform parent, bool expanded)
        {
            if (expanded)
            {
                // Down chevron: two upper arms meet at the lower center.
                AddChevronBar(parent, new Vector2(-2.3f, 1f), 45f);
                AddChevronBar(parent, new Vector2(2.3f, 1f), -45f);
            }
            else
            {
                // Right chevron: two left arms meet at the right center.
                AddChevronBar(parent, new Vector2(-1.5f, 2.3f), 45f);
                AddChevronBar(parent, new Vector2(-1.5f, -2.3f), -45f);
            }
        }

        private static void AddChevronBar(Transform parent, Vector2 position, float rotation)
        {
            GameObject bar = NewUi("Bar", parent);
            Image image = AddImage(bar, SuiteUiTheme.TextSecondary);
            image.raycastTarget = false;
            RectTransform r = bar.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f);
            r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(2f, 7f);
            r.anchoredPosition = position;
            r.localRotation = Quaternion.Euler(0f, 0f, rotation);
        }

        // Mutable bools follow the dedicated PvP panel convention: the state is part of the
        // clickable control text itself ("Label [ON]" / "Label [OFF]"), not a detached checkbox or
        // ambiguous color-only pill. Color remains a secondary cue.
        private static SettingValueVisual AddToggleRow(RectTransform parent, string label, bool on,
            UnityEngine.Events.UnityAction action)
        {
            GameObject go = NewUi("ToggleRow", parent);
            Color normal = on ? SuiteUiTheme.SelectedBackground : SuiteUiTheme.ControlBackground;
            Image rowImg = AddImage(go, normal);
            rowImg.raycastTarget = true;
            AddCrispBorder(go, SuiteUiTheme.ControlBorder);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = SuiteUiTheme.RowHeight;
            le.preferredHeight = SuiteUiTheme.RowHeight;

            Button b = go.AddComponent<Button>();
            b.targetGraphic = rowImg;
            SetButtonColors(b, normal);
            b.onClick.AddListener(action);

            TextMeshProUGUI text = NewLabel("Label", go.transform,
                SuiteSettingDisplayPolicy.BooleanButtonText(label, on), 11f, FontStyles.Bold);
            RectTransform tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(8f, 0f);
            tr.offsetMax = new Vector2(-8f, 0f);
            text.alignment = TextAlignmentOptions.Left;
            text.color = on ? SuiteUiTheme.TextAccent : SuiteUiTheme.TextPrimary;

            return new SettingValueVisual
            {
                ValueLabel = text,
                StateBackground = rowImg,
                Button = b,
                SettingLabel = label ?? string.Empty,
                Kind = SuiteSettingKind.Bool
            };
        }

        // Read-only booleans preserve the same unambiguous text shape without pretending the row
        // is interactive.
        private static SettingValueVisual AddBooleanStateRow(RectTransform parent, string label, bool on)
        {
            GameObject go = NewUi("BooleanStateRow", parent);
            Color normal = on ? SuiteUiTheme.SelectedBackground : SuiteUiTheme.ControlBackground;
            Image rowImg = AddImage(go, normal);
            rowImg.raycastTarget = false;
            AddCrispBorder(go, SuiteUiTheme.ControlBorder);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = SuiteUiTheme.RowHeight;
            le.preferredHeight = SuiteUiTheme.RowHeight;

            TextMeshProUGUI text = NewLabel("Label", go.transform,
                SuiteSettingDisplayPolicy.BooleanButtonText(label, on), 11f, FontStyles.Normal);
            RectTransform tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(8f, 0f);
            tr.offsetMax = new Vector2(-8f, 0f);
            text.alignment = TextAlignmentOptions.Left;
            text.color = on ? SuiteUiTheme.TextAccent : SuiteUiTheme.TextPrimary;

            return new SettingValueVisual
            {
                ValueLabel = text,
                StateBackground = rowImg,
                SettingLabel = label ?? string.Empty,
                Kind = SuiteSettingKind.Bool
            };
        }

        // A choice setting row: label on the left, current value + a simple cycle affordance on the
        // right in its own chip, distinct from both disclosure and boolean state.
        private static SettingValueVisual AddChoiceRow(RectTransform parent, string label, string value,
            UnityEngine.Events.UnityAction action)
        {
            const float chipWidth = 96f;

            GameObject go = NewUi("ChoiceRow", parent);
            Image rowImg = AddImage(go, SuiteUiTheme.ControlBackground);
            rowImg.raycastTarget = true;
            AddCrispBorder(go, SuiteUiTheme.ControlBorder);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = SuiteUiTheme.RowHeight;
            le.preferredHeight = SuiteUiTheme.RowHeight;

            Button b = go.AddComponent<Button>();
            b.targetGraphic = rowImg;
            SetButtonColors(b, SuiteUiTheme.ControlBackground);
            b.onClick.AddListener(action);

            TextMeshProUGUI text = NewLabel("Label", go.transform, label, 11f, FontStyles.Normal);
            RectTransform tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(8f, 0f);
            tr.offsetMax = new Vector2(-(chipWidth + 10f), 0f);
            text.alignment = TextAlignmentOptions.Left;
            text.color = SuiteUiTheme.TextPrimary;

            GameObject chip = NewUi("Choice", go.transform);
            Image chipImg = AddImage(chip, SuiteUiTheme.SelectedBackground);
            chipImg.raycastTarget = false;
            AddCrispBorder(chip, SuiteUiTheme.ControlBorder);
            RectTransform cr = chip.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(1f, 0.5f);
            cr.anchorMax = new Vector2(1f, 0.5f);
            cr.pivot = new Vector2(1f, 0.5f);
            cr.sizeDelta = new Vector2(chipWidth, 18f);
            cr.anchoredPosition = new Vector2(-6f, 0f);

            TextMeshProUGUI chipLabel = NewLabel("ChoiceLabel", chip.transform, (value ?? string.Empty) + "  >", 10f, FontStyles.Bold);
            Stretch(chipLabel.rectTransform);
            chipLabel.alignment = TextAlignmentOptions.Center;
            chipLabel.color = SuiteUiTheme.TextAccent;

            return new SettingValueVisual { ValueLabel = chipLabel, StateBackground = chipImg, Kind = SuiteSettingKind.Choice };
        }

        private static SettingValueVisual AddReadOnlySettingRow(RectTransform parent, string label,
            string value, string suffix)
        {
            string prefix = (label ?? string.Empty) + ": ";
            suffix = suffix ?? string.Empty;
            TextMeshProUGUI row = AddBodyLabel(parent, prefix + (value ?? string.Empty) + suffix);
            return new SettingValueVisual
            {
                ValueLabel = row,
                Prefix = prefix,
                Suffix = suffix,
                Kind = SuiteSettingKind.Text
            };
        }
    }
}
