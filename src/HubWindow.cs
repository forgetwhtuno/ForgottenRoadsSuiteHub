using System.Collections.Generic;
using UnityEngine;

namespace ErenshorSuiteHub
{
    // The Hub's single movable window. Phase 1 has exactly one working tab -- Overview -- so
    // there is no left-hand tab rail yet (the suite's planned per-mod tab list is Phase 2+
    // scope; see docs/UI_DESIGN.md in Erenshor-Mod-Suite). Header-drag only: the title bar is the
    // only draggable region, body/controls never drag, matching every dedicated panel across the
    // suite (Journal/Contracts/Guild Life/PvP).
    internal sealed class HubWindow
    {
        private const int WindowId = 0x45534857; // "ESHW"
        private const float HeaderHeight = 30f;

        private string _hubVersion = string.Empty;
        private List<ModPresence> _mods = new List<ModPresence>();
        private bool _requestClose;
        private Vector2 _scroll;
        private Rect _currentWindowRect;

        private Texture2D _panelTexture;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _dotOnTexture;
        private Texture2D _dotOffTexture;
        private GUIStyle _windowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _closeButtonStyle;
        private GUIStyle _modRowStyle;
        private GUIStyle _modOnStyle;
        private GUIStyle _modOffStyle;
        private GUIStyle _footerStyle;

        internal bool RequestClose
        {
            get { return _requestClose; }
        }

        internal Rect Draw(Rect rect, string hubVersion, List<ModPresence> mods)
        {
            EnsureStyles();
            _hubVersion = hubVersion;
            _mods = mods;
            _requestClose = false;
            _currentWindowRect = rect;

            int previousDepth = GUI.depth;
            Rect result;
            try
            {
                GUI.depth = -70;
                result = GUI.Window(WindowId, rect, DrawWindowContents, GUIContent.none, _windowStyle);
            }
            finally
            {
                GUI.depth = previousDepth;
            }
            return result;
        }

        internal void Dispose()
        {
            DestroyTexture(ref _panelTexture);
            DestroyTexture(ref _buttonTexture);
            DestroyTexture(ref _buttonHoverTexture);
            DestroyTexture(ref _dotOnTexture);
            DestroyTexture(ref _dotOffTexture);
            _windowStyle = null;
            _titleStyle = null;
            _sectionStyle = null;
            _closeButtonStyle = null;
            _modRowStyle = null;
            _modOnStyle = null;
            _modOffStyle = null;
            _footerStyle = null;
        }

        private void DrawWindowContents(int id)
        {
            GUILayout.BeginVertical();
            DrawHeader();
            GUILayout.Space(4f);
            DrawOverview();
            GUILayout.EndVertical();

            // Dragging is limited to the title bar (minus the close button's width). Buttons and
            // the mod list never double as a drag surface.
            GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(0f, _currentWindowRect.width - 36f), HeaderHeight));
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(HeaderHeight));
            GUILayout.Label("ERENSHOR SUITE HUB", _titleStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("X", _closeButtonStyle, GUILayout.Width(26f), GUILayout.Height(20f)))
                _requestClose = true;
            GUILayout.EndHorizontal();
        }

        private void DrawOverview()
        {
            GUILayout.Label("OVERVIEW", _sectionStyle);
            GUILayout.Label("Suite Hub version " + _hubVersion, _modRowStyle);
            GUILayout.Space(6f);

            GUILayout.Label("SUITE MODS", _sectionStyle);
            GUILayout.Label(
                "Detected by checking for each mod's plugin DLL in this game's plugins folder. " +
                "No other interaction with these mods happens yet.",
                _footerStyle);
            GUILayout.Space(3f);

            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUILayout.ExpandHeight(true));
            if (_mods != null)
            {
                for (int i = 0; i < _mods.Count; i++)
                {
                    ModPresence mod = _mods[i];
                    GUILayout.BeginHorizontal();
                    Texture2D dot = mod.Installed ? _dotOnTexture : _dotOffTexture;
                    GUILayout.Label(dot, GUILayout.Width(10f), GUILayout.Height(10f));
                    GUILayout.Space(4f);
                    GUIStyle style = mod.Installed ? _modOnStyle : _modOffStyle;
                    string status = mod.Installed ? "installed" : "absent";
                    GUILayout.Label(mod.DisplayName + " - " + status, style, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        private void EnsureStyles()
        {
            if (_windowStyle != null) return;

            Color cyanEdge = new Color(0.03f, 0.67f, 0.86f, 0.95f);
            Color softEdge = new Color(0.13f, 0.55f, 0.68f, 0.90f);
            _panelTexture = FramedTexture(new Color(0.015f, 0.09f, 0.125f, 0.92f), cyanEdge);
            _buttonTexture = FramedTexture(new Color(0.035f, 0.17f, 0.22f, 0.88f), softEdge);
            _buttonHoverTexture = FramedTexture(new Color(0.12f, 0.38f, 0.48f, 0.94f), cyanEdge);
            _dotOnTexture = SolidTexture(new Color(0.42f, 0.92f, 0.58f, 1f));
            _dotOffTexture = SolidTexture(new Color(0.45f, 0.48f, 0.50f, 0.85f));

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _panelTexture;
            _windowStyle.border = new RectOffset(1, 1, 1, 1);
            _windowStyle.padding = new RectOffset(12, 12, 8, 10);

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 14;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.normal.textColor = new Color(0.56f, 0.88f, 1f, 1f);

            _sectionStyle = new GUIStyle(GUI.skin.label);
            _sectionStyle.fontSize = 11;
            _sectionStyle.fontStyle = FontStyle.Bold;
            _sectionStyle.normal.textColor = new Color(0.56f, 0.78f, 0.88f, 1f);

            _closeButtonStyle = new GUIStyle(GUI.skin.button);
            _closeButtonStyle.normal.background = _buttonTexture;
            _closeButtonStyle.hover.background = _buttonHoverTexture;
            _closeButtonStyle.active.background = _buttonHoverTexture;
            _closeButtonStyle.normal.textColor = new Color(0.84f, 0.94f, 1f, 1f);
            _closeButtonStyle.hover.textColor = Color.white;
            _closeButtonStyle.fontSize = 11;
            _closeButtonStyle.border = new RectOffset(1, 1, 1, 1);

            _modRowStyle = new GUIStyle(GUI.skin.label);
            _modRowStyle.fontSize = 12;
            _modRowStyle.normal.textColor = new Color(0.88f, 0.92f, 0.91f, 1f);

            _modOnStyle = new GUIStyle(_modRowStyle);
            _modOnStyle.normal.textColor = new Color(0.82f, 0.98f, 0.88f, 1f);

            _modOffStyle = new GUIStyle(_modRowStyle);
            _modOffStyle.normal.textColor = new Color(0.62f, 0.65f, 0.66f, 1f);

            _footerStyle = new GUIStyle(GUI.skin.label);
            _footerStyle.fontSize = 10;
            _footerStyle.wordWrap = true;
            _footerStyle.normal.textColor = new Color(0.63f, 0.73f, 0.74f, 1f);
        }

        private static Texture2D FramedTexture(Color center, Color edge)
        {
            Texture2D texture = new Texture2D(3, 3, TextureFormat.RGBA32, false);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    texture.SetPixel(x, y, x == 0 || x == 2 || y == 0 || y == 2 ? edge : center);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D SolidTexture(Color color)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    texture.SetPixel(x, y, color);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.Apply(false, true);
            return texture;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null) return;
            Object.Destroy(texture);
            texture = null;
        }
    }
}
