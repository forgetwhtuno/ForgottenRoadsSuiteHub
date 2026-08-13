# AGENTS.md — Erenshor Suite Hub

Read before modifying this repo.

## Role

The Hub is optional UI/orchestration for the `forgetwhtuno` Erenshor mod suite. It never owns
combat, movement, loot, saves, targeting, faction, or any sibling mod's gameplay state. Every
sibling mod must remain independently usable when the Hub is absent, unloaded, or incompatible.

## Current architecture (0.2.x candidate)

- One compact launcher with a **20px drag-only grip** and a separate non-overlapping button.
- One header-drag-only suite window with Overview plus installed-only module navigation.
- Native gameplay-readiness policy uses positive game state; player-object existence alone is not
  considered ready.
- File discovery means installed-on-disk only. It never proves a runtime control API exists.
- Optional cross-mod controls use verified Lunaris Aura APIs with a versioned string/primitive
  wire contract. The Hub may load first or second; no load order is assumed.
- The owning mod validates, mutates, and persists its own settings/actions. The Hub never edits
  sibling `.lpcfg` files.
- Developer diagnostics/settings are hidden unless `DeveloperUi` is enabled.

## Readiness rules

Do not regress to the old `!InCharSelect + PlayerControl/Myself/MyStats active` check. Current
acquisition requires:

- not `GameData.InCharSelect`;
- local `GameData.PlayerControl`, `Myself`, `MyStats`, active player object;
- not `GameData.Zoning`;
- `GameData.SimMngr` and `GameData.SimPlayerGrouping` rebuilt;
- `GameData.PlayerControl.CanMove` observed true at least once for this settled world;
- the candidate remains good for the bounded policy debounce.

`CanMove` is acquisition evidence, not a permanent visibility condition: normal native windows can
temporarily disable movement after gameplay has already become ready. Zoning, character select,
or loss of the required world graph revokes readiness and forces a fresh acquisition.

## UI architecture — retained uGUI + mod-owned drag handler (0.4.0)

**Full detail lives in `docs/SUITE_UI_ARCHITECTURE.md`. Read it before touching UI code.**

The Hub uses retained Unity uGUI (`Canvas` + `CanvasScaler` + `GraphicRaycaster` + `CanvasGroup` +
`TextMeshProUGUI` + `Button`) with the Suite's own `SuiteDragGuard` component for dragging. The
previous OnGUI (`GUI.Window`/`GUILayout`/`GUI.DragWindow`) implementation was **deleted**, not
disabled.

**Do not reintroduce OnGUI for player-facing Hub UI.** Legacy IMGUI never registers with Unity's
EventSystem, and every native input gate in Erenshor keys off it
(`CameraController.Update/Controls/ModernControls`,
`PlayerControl.LeftClick/RightClick/LandMovement/WaterMovement/MouseLook` all call
`EventSystem.IsPointerOverGameObject()`). That blindness — not a fixable bug in our drag code — is
why the OnGUI Hub leaked drags into the world camera and snapped toward the top-left: the camera took
its free-look branch and called `Cursor.set_lockState`, which corrupts `Input.mousePosition`.

Rules:

- Reuse the existing `EventSystem`. **Never create a second one.** `SuiteHubUi.Build()` refuses to
  build and retries if `EventSystem.current` is null.
- Drag lives on a dedicated grip (`◇` diamond / window header) with `SuiteDragGuard` only.
  **Buttons are never draggable** — a click must never be swallowed by a drag.
- `SuiteDragGuard` implements `IPointerDownHandler`/`IBeginDragHandler`/`IDragHandler`/
  `IEndDragHandler`/`IPointerUpHandler` directly, using `RectTransformUtility.ScreenPointToLocalPointInRectangle`
  against Canvas-space pointer positions - never polled `Input.mousePosition`.
- `GameData.DraggingUIElement` must never be left latched true. `SuiteDragGuard` sets it in
  `OnBeginDrag` and clears it on drag end, plain click, disable, destroy, unload, zoning, and
  exception recovery - and only clears the flag when the Hub owns the active drag.
- Positions persist **normalized (0..1)** from the bottom-left, rejecting NaN/infinity, clamped fully
  on-screen. Legacy pixel values from the pre-0.3.0 OnGUI Hub are **not** migrated (different
  coordinate origin - see `SuiteUiGeometry.InterpretStoredAxis`), they fall back to default placement.
  Written **once per completed drag**, never per frame.
- Open/close/reset requests are queued as pending flags and applied from `Update`; do not destroy UI
  objects from inside a `Button.onClick` callback that is still executing.
- Keep Reset Position reachable through the visible header `RESET` button.

### `GameData.EditUIMode` — DO NOT touch this flag for player-facing UI

0.3.x used native Erenshor `DragUI` for dragging. Disassembling `DragUI.Update()` showed it disables
its own `Image` every frame unless `GameData.EditUIMode` (a native, global "customize UI positions"
flag, default `false`) is true - and a disabled `Image` also stops being a raycast target. 0.3.2
worked around this by forcing `EditUIMode` true globally while the Hub was visible.

**Live testing showed this unlocks and decorates OTHER native windows too** (large white edit-mode
borders) - an unacceptable global side effect. 0.4.0 replaced native `DragUI` with the Suite's own
`SuiteDragGuard` specifically to eliminate this. **Do not read or write `GameData.EditUIMode` from
this codebase again**, and do not reintroduce native `DragUI` on any player-facing Hub/panel
GameObject for the same reason. If a future panel genuinely needs native `DragUI` semantics, that is
a sign it belongs to the game's own edit-mode UI, not a mod's always-on UI.

### Harmony input patches — deleted, do not re-add

`SuiteHubEventSystemGuardPatch`, `SuiteHubPanelLeftClickPatch`, `SuiteHubMouseLookGuardPatch` and
`SuiteHubCameraLookPatch` were all removed in 0.3.0. Each only faked what a real raycast target now
provides natively; the `csMouseOrbit` one was always ineffective because `CameraController`, not
`csMouseOrbit`, is the live camera driver. `SuiteHubChatCommandPatch` is the **only** patch the Hub
still installs. If you find yourself wanting to patch `EventSystem.IsPointerOverGameObject` again,
the real bug is that something is not a proper raycast target.

## Access model — visible UI only

**The Suite Hub is reached by mouse, through the visible MODS control. That is the only supported
player access route.** Definition of Done for any UI work here: a fully loaded, gameplay-ready
player sees a MODS control and can open, operate, and move the Hub entirely with the mouse.

**Do NOT add a keyboard hotkey.** Erenshor and the sibling suite mods already consume many keys, and
F-keys in particular collide with native UI hide/toggle functions. A previous `HubSettings.ToggleHotkey`
(default `F9`) was removed for this reason before it ever shipped or was written to a live config
file - do not reintroduce it, and do not "temporarily" add one to work around a broken pointer path.

- `/mods` (also `/suitehub`) chat command toggles the Hub directly. Registered via a Harmony prefix
  on `TypeText.CheckCommands`, following the exact pattern already used natively by Deep Sims
  (`mods\DeepSim-erenshor\src\DeepSimsPlugin.cs`, `TypeTextCheckCommandsPatch`) - do not invent a
  different chat-command mechanism here. Returns `true` (unhandled) for everything else so
  vanilla/other mods' commands are unaffected.
  **This is a developer/debug recovery tool only.** If the visible MODS control cannot be clicked,
  the feature is broken even though `/mods` works. Never cite `/mods` as evidence that the UI works.
- `HubSettings.HubInteractionValidated` (default `false`) and the `interactionValidated` field now
  included in the Hub's Aura presence descriptor (`forgetwhtuno.erenshor.suitehub.v1.describe`) are
  reserved for future consumption by sibling mods' own `SuiteUiPolicy.IsHubAvailable()`. **They have
  no effect today**: every sibling mod's policy currently checks only for the Hub's mere presence
  (`FindObjectsOfType<LunarisPlugin>()` for the exact type `ErenshorSuiteHub.ErenshorSuiteHubPlugin`
  - see e.g. `mods\ErenshorContracts\src\SuiteUiPolicy.cs`), so it hides those mods' standalone
  launchers as soon as Hub exists, regardless of whether Hub's own click/drag actually works. This
  pass deliberately does **not** touch any of the 10 sibling repos to change that. Until a live run
  confirms the Hub interaction model end to end, the coordinator should keep each sibling mod's own
  `ShowStandaloneLauncherWithHub` config (present on at least Contracts/Guild Life/PvP/Journal, off
  by default) set to `true`, so players always retain a mouse-clickable entry point even if Hub's
  own launcher is still broken for them.

## Aura contract rules

Current verified Lunaris 0.1.9 Aura supports typed providers/subscribers and `HasFunction` /
`HasAction`. Do not invent additional methods. Module endpoints use the documented
`forgetwhtuno.erenshor.suite.<module>.v1.*` namespace and BCL primitives/strings so no shared
contract DLL creates a hard dependency.

A provider mod **must unregister every provider handler in `OnDestroy`**. Current Lunaris unload
cleanup does not automatically clear Aura handlers for the plugin. Subscribers that call
`Subscribe(...)` must likewise unsubscribe; this Hub currently polls functions and does not
register event subscriptions.

## Build/test

```powershell
powershell -ExecutionPolicy Bypass -File .\RUN_TESTS.ps1
powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1 -GameDir <target> -LunarisLibDir <refs>
```

The deterministic runner compiles only Unity-free policy/registry/codec/discovery/geometry code.
Full plugin compilation must use the current Erenshor/Lunaris assemblies. Do not claim live
verification from a compile or deterministic pass.

## Privacy

Public identity: `forgetwhtuno`. Do not add personal names, emails, absolute user paths, tokens,
secrets, memory exports, or private endpoints.
