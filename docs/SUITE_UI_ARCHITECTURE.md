# Erenshor Mod Suite — UI architecture

## Status

| | |
|---|---|
| **SELECTED** | **Unity retained uGUI + a mod-owned drag handler (`SuiteDragGuard`)** |
| **LIVE VERIFIED** | click, toggle, module navigation, launcher/window drag, no camera leak, no top-left snap, no native-UI side effects |
| **DEPRECATED** | custom Unity OnGUI (`GUI.Window`/`GUILayout`/`GUI.DragWindow`) as primary player-facing suite UI |
| **DEPRECATED (0.4.0)** | native Erenshor `DragUI` for player-facing Hub dragging — see "Why not native DragUI" below |

Lunaris Dear ImGui remains appropriate for possible future **developer/debug** tooling. It is not
needed for, and is not used by, the production Suite Hub.

---

## Why uGUI — the mechanism, verified from IL

Every native input gate in Erenshor keys off the EventSystem. Confirmed by disassembling the live
`Assembly-CSharp.dll`:

| Type | Method | Gate |
|---|---|---|
| `CameraController` | `Update` | `GetMouseButtonDown` → `GameData.DraggingUIElement` → `EventSystem.IsPointerOverGameObject` → `freeClick` → **`Cursor.set_lockState`** |
| `CameraController` | `Controls`, `ModernControls` | `IsPointerOverGameObject`, `DraggingUIElement`, `Cursor.set_lockState/set_visible` |
| `PlayerControl` | `LeftClick`, `RightClick`, `LandMovement`, `WaterMovement`, `MouseLook` | `IsPointerOverGameObject` |

Legacy IMGUI never registers with the EventSystem, so the game was **structurally blind** to the old
Hub: hover worked (OnGUI renders independently) but every press fell through to camera free-look,
which called `Cursor.set_lockState`, which corrupted `Input.mousePosition`, which made
`GUI.DragWindow` compute a garbage delta — the observed "snap toward top-left", and the reason
clicks did not land.

A real `Canvas` + `GraphicRaycaster` is seen by all of the above **natively, with no patches**.

---

## Production stack

```
ErenshorSuiteHubCanvas   Canvas (ScreenSpaceOverlay, sortingOrder 500)
                         CanvasScaler (ConstantPixelSize)
                         GraphicRaycaster
├── SuiteHubLauncher                 Image, RectTransform (anchored+pivot bottom-left)
│   ├── SuiteHubLauncherDragHandle   Image(raycastTarget) + SuiteDragGuard, rotated 45° = ◇
│   └── SuiteHubModsButton           Image + Button + TextMeshProUGUI "MODS"
└── SuiteHubWindow                   Image + CanvasGroup
    ├── SuiteHubWindowHeader         Image + SuiteDragGuard  (+ RESET, X buttons on top)
    ├── SuiteHubNav                  ScrollRect → RectMask2D viewport → persistent VerticalLayoutGroup content
    └── SuiteHubPage                 ScrollRect → RectMask2D viewport → swappable VerticalLayoutGroup content
```

The existing `EventSystem` is reused. `SuiteHubUi.Build()` **refuses to build** and retries later if
`EventSystem.current` is null — it never creates a second one, because two active EventSystems fight
each other and break UI wholesale.

### Drag

`SuiteDragGuard` only, mounted on a dedicated grip. **Buttons are never draggable**, so a click can
never be swallowed by a drag gesture. Implements `IPointerDownHandler`/`IBeginDragHandler`/
`IDragHandler`/`IEndDragHandler`/`IPointerUpHandler` directly and moves `Target.anchoredPosition`
using `RectTransformUtility.ScreenPointToLocalPointInRectangle` against Canvas-space pointer
positions — never polled `Input.mousePosition`, which is what corrupted under cursor lock in the
original OnGUI Hub.

### Why not native `DragUI`

0.3.x used native Erenshor `DragUI` for this. Disassembling `DragUI.Update()` (Mono.Cecil against
the current `Assembly-CSharp.dll`) showed it captures `GetComponent<Image>()` on its own GameObject
in `Awake()` and, every frame, force-disables that `Image` unless `GameData.EditUIMode` is true — a
native "customize UI positions" flag, off by default, that governs the visibility of **every**
native drag-handle border in the game, not just the Hub's. 0.3.2 forced this flag true globally
while the Hub was visible to keep its own handles usable. **Live testing showed this unlocked and
decorated other native windows too** (large white edit-mode borders) — an unacceptable global side
effect for an always-on mod UI. `DragUI` is correct for the game's own edit-mode-only native
handles; it is the wrong tool for a persistently visible, persistently draggable mod launcher.
`SuiteDragGuard` was written to replace it and never reads or writes `GameData.EditUIMode`.

---

## Drag safety — `SuiteDragGuard`

`SuiteDragGuard` sets `GameData.DraggingUIElement = true` in `OnBeginDrag` and clears it once the
gesture ends, closing every path a native or third-party drag component typically misses:

| Path | Handled by |
|---|---|
| drag end / plain click | `OnEndDrag` / `OnPointerUp` → `EndDrag()` |
| `OnDisable` / `OnDestroy` | `EndDrag(true)` |
| plugin unload | plugin `OnDestroy` → `ForceReleaseIfHubOwned()` |
| scene/canvas teardown | `SuiteHubUi.Destroy()` → `ForceReleaseIfHubOwned()` |
| zoning / readiness loss | plugin `Update` → `ForceReleaseIfHubOwned()` |
| exception recovery | plugin `Update` catch → `ForceReleaseIfHubOwned()` |
| launcher hidden | `SetVisible(false)` → `ForceReleaseIfHubOwned()` |

The public `erenshor-minimap` reference implementation's resize handles have exactly this bug
(clearing the flag only in `OnPointerUp`, never on disable/destroy) — deliberately not copied.

**Ownership rule:** a static counter tracks how many *Hub* handles hold a gesture. The native flag is
only cleared when the Hub owns it, so a native window's or another mod's drag is never stomped.

---

## Position persistence

Stored **normalized (0..1 of screen extent)** from the bottom-left corner, so a saved layout survives
a resolution change. Panels are anchored and pivoted bottom-left, making `anchoredPosition` exactly
"pixels from bottom-left" and keeping the maths in `SuiteUiGeometry` free of `UnityEngine` types and
directly unit-testable.

- **NaN / infinity rejected** on both read and write.
- **Legacy migration:** values `> 1` are absolute pixels written by the pre-0.3.0 OnGUI Hub and are
  normalized on first read (`InterpretStoredAxis`).
- **Resolved against the current screen**, then **clamped fully on-screen** — this doubles as
  off-screen recovery.
- **Resolution changes** re-clamp both panels in `Tick()`.
- **Written once per completed drag** via `SuiteDragGuard.OnDragCompleted`, never per drag frame.
- **Reset Position** is exposed as the visible `RESET` button in the window header.

`SuiteDragGuard` has no PlayerPrefs side effects of its own (unlike native `DragUI`). The Hub still
re-asserts its own normalized position for `RestoreFrameBudget` (3) frames after build, purely as a
safety margin against layout groups settling during the first frames after creation.

---

## Harmony patches removed

The migration deleted four patches. Each existed **only** to fake what a real raycast target now
provides natively.

| Removed patch | What it did | Why it is now unnecessary |
|---|---|---|
| `SuiteHubEventSystemGuardPatch` | postfix on `EventSystem.IsPointerOverGameObject`, forced `true` when the pointer was inside a Hub screen rect | The Canvas + `GraphicRaycaster` makes this genuinely true. Patching a global engine method for every caller in the game was the single most invasive thing the Hub did. |
| `SuiteHubPanelLeftClickPatch` | prefix on `PlayerControl.LeftClick`, suppressed world clicks over Hub rects | `LeftClick` already gates on `IsPointerOverGameObject`, which is now correct on its own. |
| `SuiteHubMouseLookGuardPatch` | prefix on `PlayerControl.MouseLook` | Same gate, same reason. |
| `SuiteHubCameraLookPatch` | prefix/postfix on `csMouseOrbit.LateUpdate`, zeroed `xSpeed`/`ySpeed` | **Was always ineffective** — `csMouseOrbit` is not the live gameplay camera driver; `CameraController` is. It muted a different, likely-legacy script. |

Also removed with the OnGUI path: manual pointer capture (`_pointerCaptured`), geometric hit-testing
(`PointerIsOverUi` / `PointerOwnsUi`), manual `GameData.DraggingUIElement` claim/release, and cursor
save/force/restore (`Cursor.visible` / `Cursor.lockState`) — the native gates handle the cursor now.

**Retained:** `SuiteHubChatCommandPatch` (`TypeText.CheckCommands`). Unrelated to input ownership.
It is the only Harmony patch the Hub still installs.

Unrelated gameplay/readiness logic (`GameplayReadinessPolicy`, Aura bridges, module registry) was not
touched.

---

## Access model

**Visible UI only.** A compact persistent `◇ MODS` launcher appears once gameplay readiness is
established. There is **no keyboard hotkey and must never be one** — Erenshor and the sibling suite
mods already consume many keys, and F-keys collide with native UI hide/toggle functions. A previous
`ToggleHotkey` (default `F9`) was removed before it ever shipped or reached a live config file.

`/mods` and `/suitehub` remain as **developer/debug recovery only**. If the visible MODS control
cannot be clicked, the feature is broken even though the command works. Never cite `/mods` as
evidence the UI works.

### Placement: current vs. future

Two legitimate placements were compared (see `WORKING_MOD_UI_FINDINGS.md`):

- **A. Own uGUI launcher (implemented).** Self-contained, draggable, no dependency on native layout.
- **B. Inject into the native button bar** (`UI/UIElements/InvButton`, `SpellsButton`,
  `JournalButton`, `WorldMapButton`, `SettingsButton`). More discoverable, and proven safe at runtime
  by the installed `Auto_Sort` mod, which re-parents a `Button` with **no prefab or save
  modification**. Deferred: it couples to transform paths that a game patch could rename.

B can later be added as a *second* entry point alongside A, with fallback to A when the native path
is not found. Both open the same window.

---

## Rendering: persistent nav, atomic page swap

Live testing of 0.3.2 showed selecting a different module still visibly flickered even after fixing
the 1 Hz poll-driven full rebuild. Root cause: module selection called the same rebuild path as
every other change, destroying and recreating the **entire nav list** (10+ row GameObjects) on every
click, purely to move a highlight.

0.4.0 splits this into three tiers, each with its own change-detection signature
(`ComputeNavSignature`/`ComputePageSignature`) so one tier changing never touches another:

- **Module structure** (which modules are installed) — `_navRows` (a persistent
  `Dictionary<moduleId, NavRowVisual>`) is only destroyed and rebuilt via `QueueNavStructureRebuild()`
  when this actually changes (a plugin file appearing/disappearing between discovery polls).
- **Selection** (which module is highlighted) — `QueueSelectionChanged()` never touches the nav
  GameObjects; `RefreshNavSelectionVisual()` just recolors the existing `Image`/`TextMeshProUGUI`
  components in place.
- **Page content / dynamic values** (settings, status, action results) — `QueuePageRebuild()`, and
  the page itself uses an atomic build-then-swap: `RebuildPage()` builds an entirely new content
  root as a sibling under the persistent `_pageViewport`, populates and layout-forces it *before*
  pointing `_pageScroll.content` at it, then deactivates and destroys the old root. If population
  throws partway through, the old page is left untouched rather than showing a half-built one.

---

## Scope boundary

Only `ErenshorSuiteHub` was migrated. Journal, Contracts, Guild Life, Crafting and PvP still use
their own OnGUI panels and their gameplay was not touched. The next step is to migrate dedicated
panels progressively, following `docs/SUITE_UI_MIGRATION_CONTRACT.md` — the authoritative contract
for the parallel migration workstreams.
