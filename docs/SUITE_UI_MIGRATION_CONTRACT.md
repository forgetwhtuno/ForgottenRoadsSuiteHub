# Suite UI migration contract

Authoritative contract for migrating dedicated mod panels (Journal, Contracts, Guild Life, Crafting,
PvP, Party Tools, ...) from OnGUI to the architecture proven by the Suite Hub in this pass. Every
parallel migration workstream must follow this document so the six mods stay visually and
behaviourally consistent and remain independently correct without the Hub present.

This is a contract, not a suggestion: deviating from it recreates the exact bugs this pass fixed
(camera leak, native-UI side effects, flicker). If a requirement here seems wrong for a specific mod,
raise it before deviating rather than silently doing something else.

---

## 1. Technology stack — required

```
Canvas (ScreenSpaceOverlay, overrideSorting, sortingOrder ~500)
CanvasScaler (ConstantPixelSize, scaleFactor 1)
GraphicRaycaster
RectTransform, CanvasGroup, TextMeshProUGUI, Button
ScrollRect + RectMask2D viewport + VerticalLayoutGroup/ContentSizeFitter content, where needed
Suite-style custom drag handler (see §3)
```

## 1a. Technology stack — forbidden

```
OnGUI / GUILayout / GUI.Window / GUI.DragWindow      - never registers with EventSystem; every
                                                        native input gate keys off IsPointerOverGameObject
native Erenshor DragUI                                - ties its own Image's visibility/raycastability
                                                        to GameData.EditUIMode, a GLOBAL native flag
GameData.EditUIMode (read OR write)                   - forcing it decorates/unlocks OTHER native
                                                        windows too (verified live, 0.3.2)
global keyboard hotkeys for opening a panel            - collide with native UI toggles; see §6
Harmony patches on EventSystem.IsPointerOverGameObject,
PlayerControl.LeftClick/RightClick/MouseLook, or
any camera method                                      - unnecessary once the panel is a real
                                                        Canvas/GraphicRaycaster raycast target
```

Each mod must remain independently usable without the Hub present. No dedicated panel may take a
compile-time reference to `ErenshorSuiteHub.dll`.

---

## 2. Why this stack — the mechanism (do not re-derive this, cite it)

Every native input gate in Erenshor keys off the EventSystem:

```
CameraController.Update/Controls/ModernControls  -> EventSystem.IsPointerOverGameObject()
                                                   -> GameData.DraggingUIElement
PlayerControl.LeftClick/RightClick/LandMovement/
             WaterMovement/MouseLook              -> EventSystem.IsPointerOverGameObject()
```

Legacy IMGUI never registers with the EventSystem, so it is structurally invisible to all of the
above: a mouse-down over an OnGUI panel falls straight through to the world/camera, the camera takes
its free-look branch and calls `Cursor.set_lockState`, which corrupts `Input.mousePosition`, which is
what produces snapping/drag corruption in the panel itself. A real `Canvas` + `GraphicRaycaster` is
seen by all of the above natively, with **zero Harmony patches**.

---

## 3. Drag handling — mod-owned, not native `DragUI`

Copy the shape of `ErenshorSuiteHub/src/SuiteDragGuard.cs`, not native `DragUI`. Native `DragUI` was
used in Suite Hub 0.3.x and removed in 0.4.0 after live testing showed forcing `GameData.EditUIMode`
true (required to keep `DragUI`'s own graphic visible/raycastable) unlocked and decorated *other*
native windows with large white edit-mode borders — an unacceptable global side effect for an
always-on mod UI.

Required shape:

- Implement `IPointerDownHandler`, `IBeginDragHandler`, `IDragHandler`, `IEndDragHandler`,
  `IPointerUpHandler` directly on the drag handle's own `MonoBehaviour`. Do not use `EventTrigger`
  indirection.
- Convert pointer position via `RectTransformUtility.ScreenPointToLocalPointInRectangle` against the
  target's parent `RectTransform`, in `OnBeginDrag`/`OnDrag`. **Never** poll `Input.mousePosition` —
  that is what corrupted under cursor lock in the original OnGUI implementation.
- Set `GameData.DraggingUIElement = true` in `OnBeginDrag`; clear it when the gesture ends.
- Clear it (and never leave it latched) on: `OnEndDrag`, `OnPointerUp` (plain click with no drag),
  `OnDisable`, `OnDestroy`, plugin unload, scene/zoning teardown, and any caught exception in the
  panel's own `Update`.
- Use a static per-type ownership counter so multiple drag handles in the same panel (e.g. launcher
  grip + window header) don't release the flag while a sibling handle still owns a gesture, and so
  the flag is only ever cleared when **this mod's own handle** set it — never stomp a native or
  another mod's drag.
- Never read or write `GameData.EditUIMode`.
- Drag lives on a dedicated grip (a small `◇` diamond, or the header bar). **Buttons are never
  draggable** — a click must never be swallowed by a drag gesture, and a drag press must never
  register as a click.

---

## 4. Position persistence

- Store position **normalized (0..1 of screen extent)**, measured from the panel's anchor corner
  (bottom-left is the established convention — anchor and pivot the panel bottom-left so
  `anchoredPosition` is directly "pixels from bottom-left", keeping persistence math simple and
  testable).
- **Reject NaN/infinity** on both read and write.
- **Do not migrate legacy OnGUI pixel positions.** The OnGUI convention was top-left origin, Y-down;
  the uGUI convention here is bottom-left origin, Y-up. Blindly rescaling a legacy value produces a
  silently mirrored position. Treat any stored value `> 1` as unusable and fall back to the panel's
  default placement, exactly once.
- **Resolve against the current screen, then clamp fully on-screen.** This clamp doubles as
  off-screen recovery — there is no separate recovery code path.
- **Re-clamp on resolution change**, checked each frame/tick cheaply (compare `Screen.width`/
  `Screen.height` against last-seen values).
- **Write position once per completed drag gesture**, never per drag frame.
- Expose a visible **Reset Position** control (see §7) that restores default placement and updates
  the live `RectTransform` immediately, without triggering a rebuild/flicker.

---

## 5. Rendering — no flicker

Three tiers of change, each independently detected, each rebuilding only what actually changed:

| Tier | Example | What may rebuild |
|---|---|---|
| Structure | a settings section becomes available/unavailable | that section's row set only |
| Selection | switching tabs/pages within one panel | recolor existing controls in place; swap page content only |
| Dynamic values | a status string or toggle value changes | update the existing `Text`/`Button` widget's value/color in place — do not destroy/recreate |

Concretely:

- **Persistent lists** (tab bars, nav lists) are built once. Selecting a different tab/item recolors
  the existing row `Image`/`TextMeshProUGUI` components in place; it must not destroy and recreate
  the row set.
- **Poll-driven refresh must be gated on an actual-content-changed check** (a structural hash/
  signature), not run unconditionally on a timer. An unconditional rebuild on a 1 Hz (or any) poll
  was the primary flicker cause found in this pass.
- **`Object.Destroy` is deferred to end of frame.** If you must tear down and rebuild a content root,
  detach it (`transform.SetParent(null, false)`) or `SetActive(false)` it *before* populating the
  replacement, so the old and new content never coexist in the same layout pass.
- **Atomic page/tab switch**: build the entire new content root as a sibling under the persistent
  viewport, populate it completely, call `LayoutRebuilder.ForceRebuildLayoutImmediate` on it, *then*
  point the `ScrollRect.content` (or equivalent) at it, and only then deactivate+detach+destroy the
  old root. If population throws partway through, discard the new root and leave the old one
  displayed rather than showing a half-built panel.
- Call `LayoutRebuilder.ForceRebuildLayoutImmediate` on any content root you rebuild, immediately
  after populating it, so `ContentSizeFitter`/`VerticalLayoutGroup` settle within the same frame
  instead of visibly popping over the next 1–2 frames.

---

## 6. Access model — visible UI only, no hotkeys

- The panel's on-screen launcher/entry point is the only supported player access route. If it cannot
  be clicked, the panel is broken regardless of any chat-command fallback working.
- **No global keyboard hotkey**, ever, for opening/toggling a panel. Erenshor and the sibling suite
  mods already consume many keys; F-keys in particular collide with native UI hide/toggle functions.
  A chat command (`/<mod>`) may exist as a **developer/debug recovery tool only** — never cite it as
  evidence the visible UI works.
- Definition of Done for any panel migration: a fully loaded, gameplay-ready player can open,
  operate, drag, and close the panel **entirely with the mouse**.

### Launcher visibility setting + Hub fallback semantics

For any module that owns a dedicated player-facing panel, expose a **basic-tier bool setting**
through its Aura descriptor:

```
id=showLauncher, label="Show on-screen launcher", tier=basic, type=bool, mutable=true
```

Semantics, matching the existing `SuiteUiPolicy.ShouldShowStandaloneLauncher(bridgeRegistered,
explicitlyVisibleWithHub)` pattern already present in the sibling mods (e.g.
`mods/Erenshor-PvP/src/SuiteUiPolicy.cs`):

- **If the Hub is present, usable, and this module's own Aura bridge is registered**: the module's
  standalone launcher visibility follows `showLauncher`. Default `false` is acceptable once the Hub
  is confirmed the primary entry point — the module's page inside the Hub remains fully functional
  either way.
- **If the Hub is absent, or this module's own bridge failed to register**: the standalone launcher
  is **always visible**, regardless of `showLauncher`. A player must never be locked out of a module
  because the Hub happens to be missing or broken. This is exactly what
  `!IsHubAvailable() || !bridgeRegistered` already encodes — `showLauncher` only ever adds an
  *additional* reason to show the launcher, never a reason to hide the only entry point.

`showLauncher`'s wire value feeds directly into that existing `explicitlyVisibleWithHub` parameter —
no new detection mechanism is needed, only the Aura-exposed setting and its Hub-side toggle UI.

Modules expected to implement this pattern in the parallel migration: Party Tools, PvP, Crafting,
Contracts, Guild Life, Journal. Not all six need to ship in one pass — implement and document
identically as each panel migrates, so the setting behaves the same everywhere.

---

## 7. Close requirement

Every production panel must have a visible `X`:

- Always mouse-clickable, at a fixed position that never overlaps the drag surface (so a press near
  the corner is never ambiguous between "close" and "drag").
- Closes the panel exactly once per click.
- Must **never** start a drag (it is a separate raycast target from the drag handle, not part of it).
- Must **never** leave `GameData.DraggingUIElement` latched true — if a drag happened to be active
  when X is pressed, closing must still release ownership correctly (see §3's cleanup paths).
- Must **not** destroy the plugin or unregister its Aura bridge — only hides/tears down the UI.
- The panel must be reopenable afterward from its launcher (and from the Hub, if integrated), with
  **exactly one** UI instance existing at a time — verify this explicitly after close→reopen and
  after a hot unload/reload cycle.

---

## 8. Aura/ControlApi descriptor conventions

These are already enforced by `ErenshorSuiteHub`'s `SuiteDescriptorValidation`
(`src/SuiteModuleRegistry.cs`) — a descriptor that violates them is rejected outright and the module
disappears from the Hub. Two real violations were found and fixed in this pass; both are cautionary
examples below.

### Bool settings

- Wire `type=bool`, `value=true|false` (case-insensitive), `mutable=true|false`.
- The value must always be exactly `"true"` or `"false"` — no other casing/spelling.

### Choice settings

- Wire `type=choice`, `value=<current>`, `options=<comma-separated>`, `mutable=true|false`.
- **The current `value` must always be byte-for-byte one of the advertised `options`**, including
  casing. Validation uses ordinal, case-sensitive comparison.
- **Cautionary example (found and fixed this pass):** Deep Sims advertised options
  `Auto,LLM,Templates,Off` but derived the current value via `SomeEnum.ToString()`, where the enum
  member was declared `Llm` — producing `"Llm"` against advertised `"LLM"`. The config itself stored
  the correct `"LLM"` casing; the bug was introduced by re-deriving the wire value through an enum
  round-trip instead of using the already-normalized stored string. **Do not derive a choice's wire
  value via `enum.ToString()`** unless the enum's declared member names are guaranteed to exactly
  match the advertised options string. Prefer serializing from the same normalized string your
  setter already stores.

### Actions

- Advertise via `actions=<comma-separated ids>` in `describe`; the Hub only ever invokes an id it
  saw advertised, but the receiving `action` handler must independently revalidate the id anyway (it
  is a shared Aura function, other callers are not excluded by contract).
- `openPanel` is the conventional action id for "open my dedicated panel". Panels that support it
  should ensure the same gameplay-readiness gate their own launcher uses applies to Hub-triggered
  opens too.

### Status/summary text limits

- `summary`, `status`, and `warning` are each bounded to **240 characters** raw (post-unescape).
  Exceeding this rejects the *entire descriptor*, not just that field — the module disappears from
  the Hub with no partial degradation.
- **Cautionary example (found and fixed this pass):** PvP's status method concatenated verbose
  reward/record/match diagnostics and regularly exceeded 240 characters, silently hiding PvP from
  the Hub. Fixed by adding a separate, deliberately concise Hub-facing status method (`"Enabled |
  Idle"` / `"Enabled | Match active"`) rather than raising the limit. **Keep the Hub-facing status
  concise by construction** — a one-line state summary, not a diagnostic dump. Detailed
  match/combat/reward data belongs in the dedicated panel, not the Hub summary line. Do not increase
  `SuiteDescriptorValidation`'s limits to accommodate a verbose status; write a concise one instead.

---

## 9. Cleanup / hot-unload

- Unregister every Aura function (`describe`, settings, `setting.set`, `action`) explicitly on
  plugin `OnDestroy`, before tearing down anything else, so a lingering Aura reference can never call
  back into a half-torn-down plugin instance.
- Destroy the panel's Canvas/GameObjects and release any drag ownership (§3) before the Aura
  unregister, in `OnDestroy`.
- After a hot unload + reload, verify exactly one UI instance exists — no ghost canvases, no
  duplicate launchers.
- Zoning/scene teardown must hide the panel and release drag ownership the same way unload does; do
  not rely on `OnDestroy` alone if the panel can persist across a zone transition.

---

## 10. Live acceptance checklist (per panel)

Run this for every migrated panel before considering it done:

1. Panel's own launcher is visible once gameplay-ready (and, per §6, always visible if the Hub is
   absent/broken).
2. Single click opens the panel exactly once. No world click, no camera movement, no target change.
3. Drag the panel's grip/header — camera stays still, panel follows the pointer, no snap, stays put
   on release.
4. `X` closes exactly once, never starts a drag, never leaves `DraggingUIElement` latched.
5. Reopen from the launcher (and from the Hub, if integrated) — exactly one instance.
6. Reset Position restores default placement immediately, no flicker.
7. Idle 30 seconds with the panel open — zero further rebuilds/flicker.
8. Switching internal tabs/sections (if any) — no flicker, no frame with old+new content coexisting.
9. Hot unload/reload — exactly one instance afterward, no stuck cursor, camera control normal.
10. Zone — panel hides correctly and returns correctly; camera control is normal throughout.
