# Vanilla Erenshor `DragUI` — Findings (evidence-based, from installed assembly)

Source: `D:\SteamLibrary\steamapps\common\Erenshor\Erenshor_Data\Managed\Assembly-CSharp.dll`
(SHA256 `B840CB8076ED0553F7DC3BEB4042ABA653917882F763181EC0D2C13C26C17847`, per
`INTEGRATION_BRIEFING.md`). Extracted via `ildasm.exe` full-assembly IL dump
(`scratchpad/il/full.il`, class body at lines 331173-331948) and PowerShell reflection.
No decompiler heuristics used — every claim below cites exact IL.

## 1. Type identity

```
.class public auto ansi beforefieldinit DragUI
       extends [UnityEngine.CoreModule]UnityEngine.MonoBehaviour
       implements [UnityEngine.UI]UnityEngine.EventSystems.IPointerDownHandler,
                  [UnityEngine.UI]UnityEngine.EventSystems.IEventSystemHandler,
                  [UnityEngine.UI]UnityEngine.EventSystems.IPointerUpHandler
```

**It does NOT implement `IBeginDragHandler`/`IDragHandler`/`IEndDragHandler`.** Vanilla Erenshor's
drag is NOT built on Unity's canonical drag-event pipeline. It uses only pointer-down/pointer-up
events plus a manually polled `LateUpdate`.

## 2. Fields

```
RectTransform Parent          // the RectTransform actually being moved (may differ from this GO)
bool dragging                 // true between OnPointerDown(left) and OnPointerUp/Restore
Vector3 pos                   // scratch, computed in Start, appears otherwise unused after
bool isInv                    // "is inventory-style" slot flag, alters Start's saved-position load path
Vector2 offset                // Parent.anchoredPosition delta vs MyAnchor, used only in Start()
RectTransform MyAnchor        // optional anchor override applied in Start()
Vector2 PrefPos                // last-known-good anchoredPosition, refreshed on pointer-up/Restore
Image MyImg                   // this object's own Image (drag-handle glyph), toggled by Update()
List<Image> Borders           // border Images toggled together with MyImg by Update()
RectTransform _parentRT       // Parent's own parent transform (drag math coordinate space)
Vector2 _dragDelta            // Parent.anchoredPosition - pointerLocalPoint, captured at pointer-down
static Vector3[] _corners     // shared 4-corner scratch buffer for IsOffScreen()
RectTransform rt              // this GameObject's own RectTransform (cached in Awake)
```

`GameData.AllUIElements` is a static `List<DragUI>` that every `DragUI.Awake()` self-registers
into (dedup-checked). `GameData.DraggingUIElement` is a **static bool used by the whole game**,
not per-instance — see §5.

## 3. Mouse button handling — evidence

`OnPointerDown(PointerEventData eventData)` IL, first 3 instructions:
```
IL_0000: ldarg.1
IL_0001: callvirt instance ... PointerEventData::get_button()
IL_0006: brfalse.s IL_0009      // branch (continue drag-begin) only if button == 0 (Left)
IL_0008: ret                    // any other button: return immediately, no-op
```
**`DragUI` only reacts to the Left mouse button** (`PointerEventData.InputButton.Left == 0`).
Right/middle clicks on a drag handle are ignored outright — this matters because the game's own
camera-look is bound to the *right* mouse button (see §5), so there is no button-contention by
construction: drag uses left-click, camera-look uses right-click.

## 4. Drag mechanics — poll-based, not event-based

- `OnPointerDown`: sets `dragging = true`, sets the **global** `GameData.DraggingUIElement = true`,
  then computes `_dragDelta = Parent.anchoredPosition - localPointUnderPointer` via
  `RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRT, eventData.position, camera:null, out local)`.
  Passing `camera: null` is the standard uGUI idiom for a Screen-Space-Overlay canvas (or a
  Screen-Space-Camera canvas whose plane distance makes camera irrelevant); it means the vanilla
  UI canvas is overlay-mode, not world-space.
- **`LateUpdate` does the actual movement**, not `OnDrag`:
  ```
  if (!dragging) return;
  ScreenPointToLocalPointInRectangle(_parentRT, Input.mousePosition, camera:null, out local);
  Parent.anchoredPosition = local + _dragDelta;
  ```
  This runs every frame while `dragging` is true, polling `Input.mousePosition` directly rather
  than relying on `IDragHandler.OnDrag(PointerEventData)` deltas. Because it's `LateUpdate` (after
  `Update`), it applies after camera/gameplay updates, minimizing one-frame lag/fighting with other
  systems that move the same frame.
- `OnPointerUp`: sets `dragging = false`, saves `Parent.anchoredPosition.x/y` to
  `PlayerPrefs` under keys `"<transformName>x"` / `"<transformName>y"` (string-concat, no
  separator — e.g. transform named `"ChatWindow"` saves keys `"ChatWindowx"`/`"ChatWindowy"`),
  clears the **global** `GameData.DraggingUIElement = false`, and refreshes `PrefPos`.
- `Restore()` (called from `Start()` when `IsOffScreen(0)` is true and `GameData.AutorecoverUI` is
  set, i.e. a self-healing "snap back on screen" pass) also touches `GameData.DraggingUIElement`,
  clearing it defensively and recentring `Parent` on the canvas.
- `IsOffScreen(float overshootPx = 0)` walks the RectTransform's 4 world corners
  (`GetWorldCorners`), converts each to screen space via `RectTransformUtility.WorldToScreenPoint`,
  and checks how many fall outside an inflated canvas `pixelRect`; returns true only if **all 4**
  corners are off-canvas. This is the anti-"pushed off top-left" safety net vanilla ships with, and
  it only self-heals on `Start()` (i.e., on load), not continuously.

## 5. How vanilla prevents "UI click/drag leaking into world/camera" — the actual answer

This is in `CameraController` (same assembly), not in `DragUI` itself. `CameraController`'s
per-frame mouse-look logic (Cinemachine orbital transposer axis updates, right-mouse-drag look)
is gated behind a **triple check**, all of which must be false/clear before camera-look input is
processed, evidenced at two call sites (`CameraController` IL, right around a `CinemachineTransposer`/
`CinemachineOrbitalTransposer` block, both legacy-mouse-button-1 and classic-Modern-input paths):

```
if (Input.GetMouseButton(1) && !Input.GetMouseButton(0)
    && CameraController.freeClick
    && !EventSystem.current.IsPointerOverGameObject()
    && !GameData.DraggingUIElement
    && !GameData.GM.ResizingUI
    && mouseDown > 10f)
{
    // lock cursor, enable mouse-look, orbit camera
}
```

`freeClick` itself is computed at right-mouse-button-down time using the same two guards:
```
if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(0)) {
    freeClick = !GameData.DraggingUIElement && !EventSystem.current.IsPointerOverGameObject();
}
```

So vanilla's containment is **two independent, redundant checks**, not one:
1. **`EventSystem.current.IsPointerOverGameObject()`** — the standard uGUI raycast-based check.
   This only works correctly if there is a live `EventSystem` in the scene and every interactive
   Canvas has a `GraphicRaycaster` (or a raycaster that participates in the same `EventSystem`),
   because `IsPointerOverGameObject()` is literally "did the last raycast performed by this
   EventSystem hit a UI graphic." No EventSystem/GraphicRaycaster ⇒ this always reports false ⇒
   camera-look would go through even when hovering UI.
2. **`GameData.DraggingUIElement`** — the explicit static flag `DragUI` itself sets/clears. This
   is a belt-and-suspenders guard specifically for **drag**, not just hover: it stays true for the
   *entire* drag duration even if the pointer briefly leaves the UI element's raycast bounds
   mid-drag (a case where `IsPointerOverGameObject()` alone could go false for a frame while the
   mouse is between UI elements), which would otherwise let camera-look sneak in mid-drag.

There's also a third guard, `GameData.GM.ResizingUI` (a `GameManager` field), used the same way —
covers window-resize-handle drags separately from move-drags.

**Practical implication for Suite Hub**: reproducing this exact containment does not require using
`DragUI` itself. It requires (a) a live `EventSystem` + `GraphicRaycaster` so
`IsPointerOverGameObject()` is truthful, and (b) something that flips a flag the camera code
(or, since we can't edit `CameraController`, our own click-forwarding logic) can consult while a
drag is in progress. Since Suite Hub cannot patch `CameraController`, the actionable lesson is:
**as long as `EventSystem.current.IsPointerOverGameObject()` is true for the pointer position over
our UI, vanilla's own `CameraController` already won't rotate the camera** — no Suite Hub code has
to fight the camera at all, provided our UI is a real raycast-target Canvas element under a real
EventSystem. This reframes the live bug: if camera rotation is happening during drag on our
launcher, the most likely cause is that our UI is NOT a raycast target the EventSystem can hit
(e.g., no `GraphicRaycaster`, no `CanvasGroup.blocksRaycasts`, wrong render order, or OnGUI-drawn
content that Unity's uGUI event system cannot raycast at all — OnGUI and uGUI are different
systems; `IsPointerOverGameObject()` cannot see `OnGUI` calls).

## 6. `PlayerTyping` interaction

`GameData.PlayerTyping` is a separate static bool (chat-input-focus flag, set/read in many
unrelated places — chat box, key-binding handlers). **`DragUI` itself never reads or writes
`PlayerTyping`** (confirmed absent from the full extracted IL body in §"class body", no
`ldsfld`/`stsfld` of that field anywhere in `DragUI`). Drag and chat-typing focus are unrelated
systems in vanilla.

## 7. Unload / cleanup implications

`DragUI.Awake()` self-registers into the static `GameData.AllUIElements` list and is **never
removed from it** anywhere in the class (no `Remove`/`Clear` call in `DragUI` itself — only other
code, e.g. the iteration site around IL 378164 in `full.il`, reads that list). This means: if a
plugin ever instantiates real `DragUI` components at runtime and then destroys the GameObjects
without the game's own teardown path also clearing `AllUIElements`, a dangling reference could
remain in that static list until the list itself is cleared elsewhere (not confirmed where/if that
happens — out of scope to chase further for this pass). **This is a reason to avoid attaching the
native `DragUI` component from plugin code** unless we can prove clean removal; safer to use our
own `EventTrigger`-based analog (Variant B) that doesn't touch `GameData.AllUIElements` at all.

## 8. Confirmed real usages (2 vanilla consumers, both simple field references — evidence)

- `IDLog.ChatBoxDragUI` — field `class DragUI IDLog::ChatBoxDragUI` (the chat window's drag
  handle).
- `Minimap.UIDrag` — field `class DragUI Minimap::UIDrag` (the minimap's drag handle), referenced
  at 3 call sites in `Minimap`'s methods (position math against `UIDrag`'s owning RectTransform).

Both are plain `[SerializeField]`-style fields wired in the Unity Editor scene/prefab (component
reference, not code-instantiated), consistent with `DragUI` being a designer-attached MonoBehaviour
on a small drag-handle child object of each window, exactly matching the `Parent`/`MyAnchor`
field design (the handle drags a *different* RectTransform than itself).

## 9. Summary answers to the brief's checklist

| Question | Answer |
|---|---|
| Base type/interfaces | `MonoBehaviour`, `IPointerDownHandler`, `IPointerUpHandler`, `IEventSystemHandler`. NOT `IBeginDragHandler`/`IDragHandler`/`IEndDragHandler`. |
| Drag interfaces used | None of Unity's drag-specific ones — pointer-down/up + manual `LateUpdate` polling of `Input.mousePosition`. |
| Coordinate math | `RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRT, screenPoint, camera:null, out local)`, then `Parent.anchoredPosition = local + dragDelta` where `dragDelta` was captured at pointer-down. `camera:null` ⇒ overlay-space canvas assumption. |
| Mouse button | Left only (`PointerEventData.InputButton.Left == 0`); other buttons no-op in `OnPointerDown`. |
| EventSystem/Canvas requirements | Needs a live `EventSystem` for `IPointerDownHandler`/`IPointerUpHandler` to fire at all (uGUI event routing), and needs `GraphicRaycaster` on the Canvas so both those events fire AND so `EventSystem.IsPointerOverGameObject()` (consumed by `CameraController`) is truthful. |
| `PlayerTyping` interaction | None — `DragUI` never touches it. |
| Camera/world-click containment | Not inside `DragUI`. Enforced by `CameraController`, gated on `EventSystem.IsPointerOverGameObject()` AND the static `GameData.DraggingUIElement` flag (both must be clear) AND `GameData.GM.ResizingUI` clear, before mouse-look/camera-orbit input is processed. |
| Unload/cleanup | `Awake()` self-registers into static `GameData.AllUIElements` and is never deregistered inside `DragUI` — risk factor for plugin-instantiated `DragUI` components; favors a custom `EventTrigger` approach instead. |
