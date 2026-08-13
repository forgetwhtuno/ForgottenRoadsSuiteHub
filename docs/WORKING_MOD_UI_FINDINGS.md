# Phase 3 — Working Erenshor UI mod patterns

Status: **Research checkpoint. Architecture and patterns recorded only — no third-party source
copied into any repo, nothing committed.** Reference repos were cloned to a scratchpad temp
directory outside all project repos and are not tracked.

Sources actually inspected:

| Source | How obtained | Evidence quality |
|---|---|---|
| `drizzlx/erenshor-minimap` | public repo, cloned to temp | full C# source |
| `lucas-xk/Erenshor-UI-Manager` | public repo, cloned to temp | full C# source |
| `Auto_Sort` | **installed on this machine**, decompiled via Mono.Cecil | full IL |
| `AdventureGuide` | **installed on this machine**, string/marker scan | partial (Cecil could not parse it) |
| Recks Stat Menu UI | **not obtained** | none — see gap note |

Gap note: no legitimately reachable public source or decompilable package for Recks Stat Menu UI was
found from this machine. Its reported architecture (Canvas + GraphicRaycaster + CanvasGroup +
`Button.onClick` + EventTrigger) is *consistent with* everything confirmed below, but it is
**unverified** and no conclusion here depends on it.

---

## 1. The decisive split

A marker scan across every installed plugin DLL:

| Mod | UI technology |
|---|---|
| **Auto_Sort** (3rd party) | retained uGUI — `Button`, `Image`, `TMP`, `RectTransform`, `anchoredPosition` |
| **AdventureGuide** (3rd party) | OnGUI hybrid + manual `IsPointerOverGameObject`/`Cursor` handling |
| **erenshor-minimap** (3rd party) | retained uGUI — own `Canvas`+`CanvasScaler`+`GraphicRaycaster`, native `DragUI` |
| **Erenshor-UI-Manager** (3rd party) | operates on the game's *existing* uGUI hierarchy |
| Journal, Contracts, Guild Life, PvP, Crafting, **SuiteHub** (ours) | `GUILayout` + `GUI.DragWindow` — legacy OnGUI, all of them |

**Every suite mod we own is OnGUI. Every third-party mod that reliably takes clicks is retained
uGUI.** The one third-party mod that stayed on OnGUI (AdventureGuide) had to hand-roll the same
`IsPointerOverGameObject` + `Cursor` compensation the Suite Hub is now carrying — which is
independent corroboration that the OnGUI route *requires* that compensation layer, rather than it
being something we got wrong.

---

## 2. Pattern A — own Canvas + native `DragUI` (erenshor-minimap)

This is the closest match to the "◇ MODS" launcher design, and it is a working shipped mod.

Canvas root:
```
new GameObject("MinimapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
canvas.renderMode     = RenderMode.ScreenSpaceOverlay
canvas.overrideSorting = true
canvas.sortingOrder    = 0        // 0 = deliberately behind native UI
```

Diamond drag handle:
```
new GameObject("MinimapDragHandle",
    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(DragUI))
handleRect.sizeDelta = (14,14);  anchor/pivot top-right
handleImage.raycastTarget = true                  // REQUIRED - DragUI needs a raycastable graphic
dragHandle.transform.localRotation = Euler(0,0,45) // the "diamond" is a rotated square
handleDrag.Parent = <the panel to move>
handleDrag.isInv  = false
```

Key points:
- It creates its **own** EventSystem-participating Canvas; it does **not** create an EventSystem
  (it relies on the game's existing one).
- The drag handle is a *small separate raycast target*, not the whole panel — the same
  "narrow grip owns dragging" shape the current OnGUI Hub already uses. That shape is correct; only
  the technology underneath it was wrong.
- **No Harmony patches. No `IsPointerOverGameObject` interception. No camera patches. No cursor
  management.** It gets all of that free from being a real uGUI raycast target.

### Version-drift warning — this repo will not compile as published

`MiniMapPlugin.cs:550` does `handleDrag.Parent = panelGo.transform;` (assigning a `Transform`), and
`:147` re-fetches `GetComponent<RectTransform>()` from it. But in the **current** `Assembly-CSharp`,
confirmed independently by both my IL scan and Mono.Cecil:

```
DragUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IEventSystemHandler
    public RectTransform Parent;     // <-- RectTransform, NOT Transform
    public RectTransform MyAnchor;
    public Vector2 PrefPos;
    public bool isInv;
```

So the published source targets an older game build. **Our implementation must assign a
`RectTransform`.** Do not copy the published line shape.

---

## 3. Pattern B — inject into the native UI hierarchy (Auto_Sort)

Auto_Sort adds a working, clickable sort button to the inventory and is notable for how *little* it
does. Full IL of `SortButton.Create`:

```
Transform.get_parent / Transform.Find   -> locate an existing native UI transform
Transform.SetParent                     -> parent into the native hierarchy
GameObject.AddComponent                 -> Image, Button, TMP
Button.get_onClick                      -> real Button.onClick listener
RectTransform anchorMin/anchorMax/pivot/sizeDelta/anchoredPosition/offsetMin/offsetMax
TMP_Text.set_text / set_fontSize / set_alignment
```

Lifecycle: created from a Harmony **postfix on `Inventory.Start`**; torn down in `Plugin.OnDestroy`
→ `SortButton.Destroy`.

It creates **no Canvas, no CanvasScaler, no GraphicRaycaster, no EventSystem**, references **no**
`DragUI`, and contains **zero** input-suppression or camera code. By parenting under native UI it
inherits the game's canvas and raycaster wholesale. This is the cheapest correct path to a clickable
control, and it is proven working on this machine right now.

Trade-off: it is positionally coupled to a native transform path, and it is not draggable.

---

## 4. Pattern C — `IPointer*`/`IDragHandler` directly (minimap resize handles)

This is the reference implementation for **Variant B**. The minimap's resize corners do not use
`EventTrigger`; they implement the interfaces directly on a `MonoBehaviour`:

```csharp
class ResizeUIBottomRight : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    OnPointerDown -> GameData.DraggingUIElement = true;  cache eventData.position + sizeDelta
    OnDrag        -> delta = eventData.position - start;  write target.sizeDelta
    OnPointerUp   -> GameData.DraggingUIElement = false;
}
```

Two things to take from this and one to improve on:
- Use `eventData.position` deltas, **not** polled `Input.mousePosition`. This is immune to the
  cursor-lock corruption that is producing our top-left snap.
- Setting `GameData.DraggingUIElement` manually is the correct and expected thing for a custom
  gesture — a third-party mod does exactly this. It is not a hack.
- **Improve on it:** the minimap never clears the flag in `OnDisable`/`OnDestroy`. If the object dies
  mid-drag the game is left permanently believing a UI drag is in progress. Our Variant B must clear
  it in `OnDisable`, `OnDestroy`, and an exception path.

---

## 5. The native UI hierarchy — directly relevant to MODS placement

`Erenshor-UI-Manager` enumerates real native paths via `GameObject.Find`, giving us a verified map of
where a MODS control could live:

```
UI/UIElements/InvButton
UI/UIElements/SpellsButton
UI/UIElements/JournalButton
UI/UIElements/WorldMapButton
UI/UIElements/SettingsButton
UI/UIElements/MenuButton (1)
UI/UIElements/UIToggle
UI/UIElements/Canvas/CompassBarProLinear
UI/UIElements/TargCanv
```

There is a native button bar (Inv / Spells / Journal / WorldMap / Settings / Menu). A **MODS button
as a sibling in that bar** is the natural "option B" placement, and Auto_Sort proves the injection
technique works without touching prefabs or saves.

`Erenshor-UI-Manager` also demonstrates `CanvasGroup.blocksRaycasts` as the supported way to make a
native panel temporarily non-interactive (`blocksRaycasts = !enable`) — useful later for modal
behaviour, not needed for the spike.

---

## 6. Comparison for the MODS access decision

| | **A. Own uGUI launcher (◇ MODS)** | **B. Inject into native button bar** |
|---|---|---|
| Proven by | erenshor-minimap | Auto_Sort (installed, working) |
| Input plumbing needed | none (own GraphicRaycaster) | none (inherits native canvas) |
| Draggable / repositionable | yes, native `DragUI` | no, fixed to bar |
| Position persistence | ours to own (`DragUI.PrefPos` or config) | not applicable |
| Discoverability | good, always visible | **best** — sits with Inv/Journal/Map |
| Coupling to native layout | none | depends on `UI/UIElements/...` paths surviving patches |
| Failure mode if game updates | keeps working | button silently missing (must degrade safely) |
| Prefab/save modification | none | none (runtime `SetParent` only) |

Neither is brittle in the way the user was worried about — B does **not** modify prefabs or saves;
it only re-parents at runtime and cleans up on destroy. Its only real risk is a renamed transform
path, which is detectable and can fall back to A.

**Recommendation: build A now, and treat B as a follow-on enhancement.** A is self-contained, has no
dependency on native layout, and is what the "◇ MODS" design already assumes. Once A is proven, B
can be added as a *second* entry point (both can coexist — the Hub is one window with two openers)
with a safe fallback to A when the native path is not found.

---

## 7. What this means for the variants

- **Variant A (uGUI + native `DragUI`)** — directly validated by a shipped working mod. Must assign
  `RectTransform` to `DragUI.Parent`, must set `raycastTarget = true` on the handle graphic, must
  not create a second EventSystem.
- **Variant B (uGUI + own pointer handlers)** — validated by the same mod's resize handles. Prefer
  implementing `IPointerDownHandler`/`IDragHandler`/`IPointerUpHandler` directly over `EventTrigger`;
  that is what working code does, it is less indirection, and it is easier to guarantee cleanup. Must
  set **and reliably clear** `GameData.DraggingUIElement`.
- **Variant C (Lunaris ImGui)** — no third-party Erenshor precedent found in any inspected mod. It
  remains buildable (live `Lunaris.dll` is byte-identical to the vendored reference) but it would be
  the only mod in the ecosystem doing it, with no working example to check against.

The evidence points at Variant A as the primary and Variant B as the fallback/comparison, with C as
a distant third on ecosystem grounds rather than technical ones.
