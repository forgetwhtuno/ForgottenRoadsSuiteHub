# Erenshor Suite Hub

**Version:** 0.5.2 RC camera-containment candidate
**Author:** forgetwhtuno
**Loader:** native Lunaris
**License:** Apache-2.0

Optional shared launcher and configuration surface for the Erenshor mod suite. The Hub never owns
sibling gameplay state: each mod remains authoritative and usable when the Hub is absent, unloaded,
or incompatible.

## Player UI

Normal access is the visible **◆ MODS** dock launcher. Clicking `MODS` expands/collapses a compact
launcher menu; it does **not** open the full Suite window. `Mod Suite` is the first expanded row and
opens the existing full Hub. Safe module rows are discovered from live Suite descriptors and appear
only when the module advertises literal `openPanel` and its Aura action endpoint is currently live.
`/mods` and `/suitehub` remain developer/recovery commands only; there is no global access hotkey.

Dock shortcut visibility is a Suite-owned preference and is separate from each sibling mod's
standalone-launcher preference. Newly available safe panel shortcuts appear by default; `Customize...`
can hide/show them without enabling/disabling any mod. A successful launcher click auto-collapses the
dock. The dock never invokes arbitrary module actions such as follow/stop/challenge/roll/reset.

Production UI uses retained Unity uGUI only:

- `Canvas` / `CanvasScaler` / `GraphicRaycaster`;
- `RectTransform` / `CanvasGroup`;
- TextMeshProUGUI;
- `Button`;
- `ScrollRect`, masks, and layout components where needed.

The Hub reuses Erenshor's existing `EventSystem`. It never creates a competing EventSystem, never
forces `GameData.EditUIMode`, never uses native `DragUI`, and does not use OnGUI/GUILayout for new
player-facing UI.

Dragging is handled by the mod-owned `SuiteDragGuard`. It claims `GameData.DraggingUIElement` on
left pointer-down on the dedicated drag grip, before Unity's drag threshold, and reasserts that
ownership only while the Hub owns the gesture. The first Hub owner remembers the prior native flag
state; the last owner restores it rather than blindly forcing `false`. Ownership is released on
pointer-up, drag completion, physical-button loss, focus/pause loss, hide, zoning, unload, disable,
destruction, and exception recovery. The MODS button/menu rows are separate click-only raycast
targets, so a drag cannot become a launcher action.

## Shared visual family

The palette is translated directly from the existing Follow **SIM ACTIONS** menu rather than from a
modern/mobile UI style. `SuiteUiTheme` centralizes the dark translucent panel, cyan framing,
control/hover/pressed states, selected rows, primary/secondary text, and warning text. Geometry is
square/crisp; there are no rounded-card surfaces.

Disclosure and boolean state are deliberately different:

- section disclosure uses a small right/down chevron built from uGUI primitives and a full-row hit
  target;
- boolean settings use PvP-style whole-row controls whose clickable text contains the state, for example **`Show Journal Launcher [OFF]`**; they never reuse the disclosure glyph.

## Compact and stable Hub

The configured/default window height is a **maximum envelope**, not permanently reserved space.
The Hub sizes from an explicit structural row model (title/status/setting/action/disclosure rows plus
known spacing/chrome), not `LayoutUtility.GetPreferredHeight` from a stretched `ScrollRect` content
root. Inside the ScrollRect, one `VerticalLayoutGroup` owns child heights so the same 6px/16px/22px/
24px metrics are actually enforced at runtime instead of inheriting default ~100px RectTransform
heights. Sections flow sequentially and are omitted when empty; `PANEL` exists only when the module
advertises `openPanel`. A one-setting/status page resolves to the 230px usable minimum; the current
Journal one-bool + Open Panel + collapsed reset disclosure shape resolves to about 261px; larger
pages grow only to the configured/screen cap and then scroll. Structural page changes such as module
selection or opening an Advanced disclosure may resize the existing retained window in place while
preserving its top edge. Selecting a different module also resets the page ScrollRect to the top;
dynamic refresh does not touch scroll position.

Dynamic status/settings polling never resizes or reconstructs the window, so ordinary bridge refreshes
do not make the panel breathe or flicker.

Refresh is split into structural and dynamic state:

- navigation structure: ordered installed module rows only;
- selection: recolor the retained nav rows in place;
- page structure: selected module, bridge presence, action schema, setting schema, disclosure state,
  and Developer UI availability;
- dynamic values: status, warning, setting values, and action-result text update retained controls
  in place.

A status/toggle/value change therefore does not destroy/rebuild the page. Successful setting
mutations synchronously re-poll that module before retained bindings are refreshed, so `[ON]` /
`[OFF]` and status feedback change in the click path; rejected/empty provider results still surface
visible action-result feedback.

## Optional module bridge

Hub presence includes `status=Ready|NotReady` plus `uiAvailable=true|false`. `uiAvailable=true` is
a truthful launcher-ownership claim: the retained Hub UI must exist and every installed catalogued
dedicated-panel module must currently have a registered descriptor, literal `openPanel`, and live
action endpoint. If any such registration/provider path is missing or malformed, Hub reports
`uiAvailable=false` and sibling fallback launchers remain/return visible.

For legacy modules whose own `showLauncher` preference defaults on, Hub performs a one-time
consolidation only after safe `openPanel` access is proven, using that module's existing validated
`setting.set` endpoint. The module owns validation and persistence; Hub never edits sibling config
files directly. A small Hub-side migration ledger prevents repeated enforcement, so a player who
later explicitly re-enables a standalone launcher keeps that choice. Standalone fallback logic still
forces launchers visible whenever Hub or that module's bridge is unavailable.

Lunaris Aura transports strict bounded string/primitive contracts. For module `<id>` the Hub can
consume:

```text
forgetwhtuno.erenshor.suite.<id>.v1.describe
forgetwhtuno.erenshor.suite.<id>.v1.settings.basic
forgetwhtuno.erenshor.suite.<id>.v1.settings.advanced
forgetwhtuno.erenshor.suite.<id>.v1.settings.developer
forgetwhtuno.erenshor.suite.<id>.v1.setting.set
forgetwhtuno.erenshor.suite.<id>.v1.action
forgetwhtuno.erenshor.suite.<id>.v1.ui.state
```

`ui.state` is optional and supports the centralized quick-close contract. A module that reports an
open closeable UI must also advertise `closePanel`.

## Escape / quick close

The Hub defines the single Suite quick-close coordinator, but it only owns the **Escape key itself**
when an exact current native menu/Escape handler has been proven and bound. The coordinator dismisses
**one topmost Suite visual surface per verified Escape** using live `ui.state` Canvas sort order and
activation time. It can call only the literal visual `closePanel` action; launchers, settings,
contracts, expeditions, Follow state, duels, PvP encounters, and other gameplay actions are unreachable
from this path.

Native Escape consumption remains **fail-closed**. This source packet contains no current
`Assembly-CSharp.dll`, and `SuiteNativeEscapeCompatibility` therefore keeps its verified declaring
type/method empty. Hub presence advertises:

```text
quickCloseContract=1
quickCloseCentral=1
quickClose=0
```

While `quickClose=0`, **Hub does not poll `Input.GetKeyDown(Escape)` at all**. Vanilla Escape is fully
untouched and Suite windows use their explicit close controls. A verified future native Prefix may
return false only when `SuiteQuickClosePolicy` reports that the selected topmost visual surface was
actually closed; a failed/missing close action or no open Suite UI passes the original native behavior
unchanged.

See `docs/NATIVE_ESCAPE_EVIDENCE.md` and `docs/QUICK_CLOSE_CONTRACT.md`.

## Build and deterministic tests

```powershell
powershell -ExecutionPolicy Bypass -File .\RUN_TESTS.ps1
powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1 -GameDir <target> -LunarisLibDir <refs>
```

The deterministic runner covers readiness, registry/wire validation, dock action/ownership/order/
visibility policy, dock expand-collapse/customize state, pointer-ownership idempotence, navigation/page
structural signatures, compact geometry, and topmost quick-close coordination and native-consume gating
policy. Full plugin compilation requires
the current installed Erenshor/Lunaris assemblies. Build/test success is not a substitute for live
UI verification.

See `docs/SUITE_UI_ARCHITECTURE.md`, `docs/SUITE_UI_MIGRATION_CONTRACT.md`, and the suite-level
`docs/HUB_INTEGRATION_CONTRACT.md` for the authoritative runtime contract.

This is an unofficial community mod and is not affiliated with or endorsed by Burgee Media.
