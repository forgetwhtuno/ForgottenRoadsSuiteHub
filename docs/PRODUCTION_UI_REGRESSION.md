# Production uGUI regression vs. the working prototype

The architecture is **not** in question — the prototype (`SuiteUiLab`, variant `dragui`) passed live
testing with this exact stack. This document isolates what the 0.3.0 production migration changed.

The prototype was untracked and deleted during migration, so it is not recoverable from git. It is
reconstructed here from the migration session's own source, and every production claim below is read
off the current working tree or the compiled DLL.

## Comparison

| Feature | Working lab (`SuiteUiLab`) | Production 0.3.0 as shipped | Regression? |
|---|---|---|---|
| Launcher hierarchy | panel → grip + label + Open button | panel → grip + label + MODS button | no |
| Diamond `Image` | 14×14, rotated 45°, `#1188C0` | 13×13, rotated 45°, `#1188C0` at 0.95α | **cosmetic** — small and low-contrast against the panel; reads as part of the rectangle |
| `raycastTarget` on grip | `true` | `true` | no |
| Native `DragUI` on grip | added | added (confirmed in IL) | no |
| `DragUI.Parent` | `_launcherRect` (RectTransform) | `_launcherRect` (RectTransform) | no |
| Window header | header `Image`, `raycastTarget = true` | same | no |
| Header `DragUI` | added | added (confirmed in IL) | no |
| Canvas sorting | `overrideSorting`, order **500** | `overrideSorting`, order **300** | possible — 300 may sit under native UI |
| `CanvasGroup` | none | on window, `blocksRaycasts`/`interactable` true | no |
| EventSystem | reused, never created | reused, refuses to build without one | no |
| Button listeners | `onClick.AddListener` | `onClick.AddListener` | no |
| **Layout rebuilding** | **never rebuilt** — static content | **full teardown + rebuild every 1 s** | **YES — primary flicker cause** |
| **Child clearing** | n/a | `Object.Destroy` (deferred) then immediate re-add | **YES — one frame of doubled content** |
| Module binding | none (static test labels) | real registry/bridge binding | new code, unproven |
| Settings binding | none | real, but only renders if bridge supplies data | unproven — needs data check |
| Actions binding | none | `openPanel` only if `runtime.HasAction` | unproven — needs data check |
| Registry refresh | none | 1 Hz poll → unconditional rebuild | **YES** |
| Position storage | PlayerPrefs, pixels | Lunaris config, normalized bottom-left | **YES — legacy values mirrored (see below)** |

## Confirmed root causes and fixes

### 1. Flicker — unconditional 1 Hz full rebuild

`ErenshorSuiteHubPlugin.Update` called `_ui.QueueRebuild()` every bridge poll (once per second)
**whether or not anything had changed**. `RebuildWindowContents()` destroys and recreates every nav
and page child. The window was therefore torn down and rebuilt once per second, forever.

The prototype never rebuilt anything, which is precisely why it did not flicker.

**Fix:** `QueueRebuildIfContentChanged()` computes a structural signature over installed modules,
descriptor presence/version/status/warning, the selected module's settings (id + value + mutability)
and view state, and rebuilds only when that signature changes.

### 2. Flicker — deferred `Destroy` double-render

`ClearChildren` used `UnityEngine.Object.Destroy`, which is **deferred to end of frame**. New rows
were added immediately in the same frame, so for one frame the `VerticalLayoutGroup` contained both
the old and new children and laid them all out — visible doubling and jumping on every rebuild.

**Fix:** detach with `SetParent(null, false)` before `Destroy`, removing them from layout in the same
frame.

### 3. Position — legacy values were being mirrored vertically

The OnGUI Hub stored **GUI-space, top-left origin, Y-down** pixels. The uGUI Hub anchors panels
bottom-left with **Y-up**. `InterpretStoredAxis` was migrating legacy pixels by simply dividing by
the screen extent, which silently produced a **vertically mirrored** position — the panel appears
somewhere the player never put it.

**Fix:** legacy pixel values (`> 1`) are no longer migrated; they return `Unset` and the Hub falls
back to its known-good default placement once. Tests updated to assert this.

### 4. Diamond visibility

The grip was created correctly (`AddComponent<DragUI>()` and `DragUI::Parent` are both present in
the shipped IL), but at 13×13 with 0.95 alpha against the panel it did not read as a distinct
diamond — matching the report that the launcher "looks like a plain rectangle".

**Fix:** grip enlarged to 16×16 at full opacity in a brighter cyan; launcher widened to 152×32; the
MODS button's left edge is now derived from the grip geometry (`ModsButtonLeft`) so the drag
affordance and the click target provably cannot overlap. The window header gained a matching
decorative diamond with `raycastTarget = false`, so it advertises draggability without intercepting
the pointer event the header's own `DragUI` needs.

## 0.3.2 — the actual drag root cause, found by disassembly

0.3.1's diagnostics were never needed to find this: disassembling `DragUI.Update()` in the current
`Assembly-CSharp.dll` (Mono.Cecil, not guessed) shows:

```
Awake():  MyImg = GetComponent<Image>();   // captures whatever Image lives on the SAME GameObject

Update(), every frame:
    if (!GameData.EditUIMode) {
        if (MyImg.enabled) { MyImg.enabled = false; foreach (Borders) enabled = false; }
    } else {
        if (!MyImg.enabled) MyImg.enabled = true;
    }
```

`GameData.EditUIMode` is a native flag, default `false`, touched nowhere else in the entire assembly
except `GameManager.ToggleUIEditing` (a bare toggle - presumably the game's own "customize UI
positions" mode). So: **any `Image` sharing a GameObject with a `DragUI` component gets disabled one
frame after creation, in normal play, permanently, until `EditUIMode` becomes true.** Unity
deregisters a disabled `Graphic` from `GraphicRegistry`, so it also **stops being a raycast target**
- not just invisible, but permanently unclickable.

This explains both remaining symptoms with one mechanism:
- **Launcher diamond invisible**: the diamond *is* the `DragUI` GameObject's own `Image`. Disabled.
- **Header has a diamond but doesn't drag**: the header's *decorative* diamond is a separate
  non-interactive child (never touched), but the header's own background `Image` - the one `DragUI`
  actually captured as `MyImg` - is disabled, so the header stops receiving `OnPointerDown` at all.

**Fix (0.3.2):** `SuiteHubUi` now saves the pre-existing `GameData.EditUIMode` value and forces it
`true` for as long as the Hub canvas is visible (`Build`/`SetVisible(true)`), restoring the saved
value on `SetVisible(false)`/`Destroy`. This is not a Harmony patch - it is a direct read/write of a
`public static bool`, the same pattern already used for `GameData.DraggingUIElement`. Scope is
verified minimal: `EditUIMode` has exactly one other reader in the whole assembly (`DragUI.Update`
itself), so forcing it only affects native drag-handle visibility, nothing else.

A per-frame "fight" (re-enabling the Image every frame ourselves) was considered and rejected: since
`DragUI.Update()` and Unity's `EventSystem.Update()` both run in the same Update phase with
undefined relative order, a per-frame toggle race would make clicks land only when the two happened
to execute in the right order that frame - unreliable, not fixable without engine-level script
execution order control we don't have. Forcing the flag once, for the Hub's lifetime, is the only
deterministic fix.

## Module-selection flicker (0.3.2)

`AddNavButton`'s click handler called the same `QueueRebuild()` as every other page-only change,
which rebuilt the **entire window** (nav + page) on every module selection. Fixed by splitting
rebuild scope:

- `QueueSelectionChanged()` - nav highlight + page content both change (module selection, mod
  install/uninstall detected during discovery poll).
- `QueuePageRebuild()` - page content only (setting toggled, disclosure opened, action invoked).
  Never touches the nav list, per the instruction that navigation should stay persistent.

Both `RebuildNav()` and `RebuildPage()` now call `LayoutRebuilder.ForceRebuildLayoutImmediate()` on
their own content root immediately after populating it, so the `VerticalLayoutGroup`/
`ContentSizeFitter` compute final sizes synchronously within the same frame instead of settling over
the next 1-2 frames - eliminating the "layout temporarily has invalid dimensions" pop that (combined
with the deferred-Destroy fix from 0.3.1) was the rest of the module-selection flicker.

`ComputeContentSignature` (poll-driven change detection) was likewise split into
`ComputeNavSignature`/`ComputePageSignature`, so a setting changing on the selected module no longer
also re-renders the unrelated nav list.

## Settings visual affordance (0.3.2)

Bool/choice settings were already real `Button` components wired to
`ErenshorSuiteHubPlugin.TrySetModuleSetting` (verified: Crafting's `CraftingSuiteAuraProvider.cs`
sends `mutable=true` for both `Crafting Expanded` and `Foraging`, which routes them into the
button-producing branch, not the read-only-text branch) - clicking them already worked. The
uncertainty was purely visual: a plain `[ON]`/`[OFF]` suffix on a row that looked identical to a
status line. Replaced with `AddToggleRow`/`AddChoiceRow`: the same clickable row, now with a
distinct colored pill (green ON / gray OFF, or a value chip with `>`) on the right that a plain
label can never have. No new logic, no change to the mutation path - purely making the existing
control legible as a control.

## Resolved in 0.3.2, still worth watching on the next live test

- **Why drag did not work — resolved.** See the `GameData.EditUIMode` section above. Not a raycast
  ordering problem after all; the `[HubUI]` pointer-down diagnostic from 0.3.1 would have shown the
  handle receiving events fine right up until `DragUI` disabled its own graphic. The `[HubUI]`
  component read-back and pointer-down logging stays in place as a permanent regression guard - if
  this ever breaks again, the log will show whether the component is missing, un-raycastable, or
  receiving events without moving.
- **Settings/actions rendering — confirmed working**, per the 0.3.1 live test (Crafting shows both
  bool settings, Party Tools' `openPanel` action opens the real panel). The remaining 0.3.2 work was
  purely visual affordance (see above), not a data/plumbing problem.

## Instrumentation added (bounded, never per-frame)

`HubSettings.UiDiagnostics` (default on) emits `[HubUI]` lines:

- root create/destroy, launcher create, window create/destroy — with canvas name, sorting order,
  EventSystem name, screen size
- per drag handle at creation: actual component type read back, `Parent` name, graphic type,
  `raycastTarget`, size, `activeInHierarchy`
- one line per pointer-down on each grip, including which GameObject the raycast actually hit
- module descriptor counts on selection
- readiness stage transitions (a flickering readiness would toggle `SetActive` and close the window)
- a counter summary at most every 10 s, and **only when a counter changed**

Idle expectation with the Hub open and untouched for 30 s: `rootCreate=1 rootDestroy=0
launcherCreate=1 windowCreate=1 navRebuild=1 pageRebuild=1` and **no further counter lines at all**.
Continuously rising rebuild counts would mean the signature gate is still being defeated.

## Not attributed to the Hub

The `NullReferenceException` in `NPC.Start()` following a PvP 5v5 lethal encounter spawning proxy
NPCs is recorded as a separate PvP/runtime issue. PvP was not touched.
