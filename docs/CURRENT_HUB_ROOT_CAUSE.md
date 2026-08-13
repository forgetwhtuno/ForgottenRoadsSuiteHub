# Suite Hub — Phase 4: Current implementation vs. native input gates

Status: **Checkpoint report. No code changed. Nothing built, nothing installed, nothing merged.**

Evidence base: IL scan of the live `Assembly-CSharp.dll` at
`D:\SteamLibrary\steamapps\common\Erenshor\Erenshor_Data\Managed`, plus a byte scan of the
**installed** `ErenshorSuiteHub.dll` and a full read of the working-tree source.

---

## 1. Headline finding — the live failure was produced by a stale build

**The installed Suite Hub DLL does not contain the fix that the current source already implements.**

Installed `plugins\ErenshorSuiteHub.dll`, built **Aug 13 09:02**. Working-tree
`src\ErenshorSuiteHubPlugin.cs` last modified **Aug 13 10:41**; `src\HubSettings.cs` **10:39**.

Raw byte scan of the installed DLL for identifiers that exist in the current source:

| Identifier | In installed DLL? | Meaning |
|---|---|---|
| `csMouseOrbit` | **yes** | old camera patch is live |
| `LeftClick` | **yes** | old click patch is live |
| `IsPointerOverGameObject` | **no** | EventSystem guard patch **absent** |
| `DraggingUIElement` | **no** | camera-ownership claim **absent** |
| `MouseLook` | **no** | MouseLook guard **absent** |
| `GestureDiagnostics` | **no** | diagnostics **absent** |
| `ToggleHotkey` | **no** | configurable hotkey **absent** |

Corroborated independently by the live config file
`plugins\config\forgetwhtuno.erenshor.suitehub.lpcfg`, which contains only
`LauncherX/Y`, `WindowX/Y`, `WindowWidth/Height`, `DeveloperUi` — it has **no**
`GestureDiagnostics` and **no** `ToggleHotkey` entry. And `lunaris.log` contains **zero**
`[HubGesture]` lines.

**Consequence:** the running build's only camera defence was a prefix on `csMouseOrbit.LateUpdate`.
Per the IL scan below, `csMouseOrbit` is *not* the live gameplay camera driver — `CameraController`
is. So the shipped build had, in practice, **no effective camera guard at all**. The reported
symptoms are exactly what that predicts.

The theory this investigation was asked to pursue is therefore **not disproven by the live test —
it was never in the live build.**

---

## 2. Native input gates — confirmed from IL, not inferred

`EventSystem.current.IsPointerOverGameObject()` is the game's single universal "pointer belongs to
UI" gate. Confirmed call sites:

| Type | Method | Gates observed in IL |
|---|---|---|
| `CameraController` | `Update` | `GetMouseButtonDown` → `GameData.DraggingUIElement` → `EventSystem.IsPointerOverGameObject` → `freeClick` → **`Cursor.set_lockState`** |
| `CameraController` | `Controls` | `IsPointerOverGameObject`, `GameData.DraggingUIElement`, `GameData.PlayerTyping`, `Cursor.set_lockState`, `Cursor.set_visible` |
| `CameraController` | `ModernControls` | `IsPointerOverGameObject`, `Cursor.set_lockState`, `Cursor.set_visible` |
| `PlayerControl` | `LeftClick` | `IsPointerOverGameObject`, then `EventSystem.RaycastAll` |
| `PlayerControl` | `RightClick` | `IsPointerOverGameObject` |
| `PlayerControl` | `LandMovement` / `WaterMovement` | `IsPointerOverGameObject` |
| `PlayerControl` | `MouseLook` | `IsPointerOverGameObject` |

Note: `CameraController` also owns a `public List<...> UIWindows` field — the game keeps its own
registry of UI windows on the camera controller. Worth investigating as a supported registration
point.

**Correction to an existing source comment:** `ErenshorSuiteHubPlugin.cs` states that
`PlayerControl.MouseLook` is "gated on a private, never-written `isOverUI` field — effectively dead
code." That is wrong. `MouseLook` genuinely calls `EventSystem.IsPointerOverGameObject()`.

### `DragUI` (native), confirmed

```
DragUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IEventSystemHandler
  public RectTransform Parent;      // the thing that MOVES (not necessarily self)
  public RectTransform MyAnchor;
  public Vector2 PrefPos;           // position persistence
  public bool isInv;
  private bool dragging; private Vector2 offset, _dragDelta;
  private RectTransform _parentRT, rt; private Image MyImg;
  OnPointerDown  -> sets GameData.DraggingUIElement
  LateUpdate     -> polls Input.mousePosition, writes Parent.anchoredPosition
```

It is **not** an `IDragHandler`. It latches on pointer-down and then moves the target every frame
from polled mouse position.

---

## 3. Why the live build failed — mechanism

1. Pointer goes down over the OnGUI launcher. Legacy IMGUI is not in the EventSystem raycast graph,
   so `IsPointerOverGameObject()` returns **false**, and nothing set `DraggingUIElement`.
2. `CameraController.Update` therefore takes its free-look branch: sets `freeClick` **and calls
   `Cursor.set_lockState`**.
3. A locked cursor stops `Input.mousePosition` from tracking the pointer normally.
4. `GUI.DragWindow` computes its drag from that now-degenerate mouse position → the window is
   dragged toward a corner. **This is the "snap toward top-left" symptom.**
5. The same cursor re-lock happening under the press is a strong candidate for "click does not
   reliably open" — the button's mouse-up lands somewhere other than where the mouse-down was.

`PlayerControl.Update` additionally calls `Mouse.WarpCursorPosition`, a second cursor-teleport
source that can contribute to the same class of symptom.

This chain is consistent with every reported symptom, including the one that looked anomalous
(hover works, because OnGUI rendering/hover is independent of the EventSystem).

---

## 4. Answers to the Phase 4 questions

**Does the current Hub ever become part of EventSystem raycasting?**
No. `HubLauncher` and `HubWindow` are pure `GUI.Window` / `GUILayout`. No Canvas, no
GraphicRaycaster, no RectTransform. It can never be seen by `IsPointerOverGameObject()` natively.

**Does it ever set `GameData.DraggingUIElement`?**
In the **working-tree source**, yes — `ClaimCameraOwnership()` / `ReleaseCameraOwnership()`, latched
from OnGUI `MouseDown` to `MouseUp` with an `Update()` safety net.
In the **installed build**, no. The identifier is absent from the binary.

**Is that sufficient to explain the live camera/drag leak?**
Yes — and more strongly than expected: the live build had neither guard, and its only camera patch
targeted the wrong camera class. The observed behaviour is fully explained.

**Is the pending source therefore already a fix?**
It plausibly addresses the *camera* leak, because it patches both gates
(`EventSystem.IsPointerOverGameObject` postfix + `GameData.DraggingUIElement`). It is **unproven**:
never compiled, never installed, never observed. It also remains a geometric-rect approximation of
UI hit-testing, which is exactly the fragile pattern this spike exists to replace.

---

## 5. Harmony input code that retained uGUI would make unnecessary

Do not remove yet — listed for the eventual migration.

| Patch | Fate under retained uGUI |
|---|---|
| `SuiteHubEventSystemGuardPatch` (`IsPointerOverGameObject` postfix) | **Obsolete.** A real Canvas + GraphicRaycaster makes this true natively. |
| `SuiteHubPanelLeftClickPatch` (`PlayerControl.LeftClick`) | **Obsolete.** Gated by the same native call. |
| `SuiteHubMouseLookGuardPatch` (`PlayerControl.MouseLook`) | **Obsolete.** Same. |
| `SuiteHubCameraLookPatch` (`csMouseOrbit.LateUpdate`) | **Obsolete and always was** — wrong camera class. |
| `SuiteHubChatCommandPatch` (`TypeText.CheckCommands`) | **Keep.** Unrelated to input ownership. |
| `ClaimCameraOwnership` / `ReleaseCameraOwnership` | **Keep for Variant B only** (EventTrigger must set the flag itself). Variant A gets it free from native `DragUI.OnPointerDown`. |
| `PointerIsOverUi` / `PointerOwnsUi` rect math | **Obsolete.** Replaced by real raycasting. |

---

## 6. Bearing on the technology decision

This finding does not rescue OnGUI. Even with both gates patched, the OnGUI approach needs a
global Harmony postfix on `EventSystem.IsPointerOverGameObject` plus manual screen-rect hit-testing
for every panel, in GUI coordinate space, kept in sync by hand, for every mod in the suite.
Retained uGUI gets the identical result from the engine with no patches at all.

The stale-build discovery changes *what the live evidence proves*, not the architectural
conclusion. It does mean the OnGUI path deserves one honest baseline measurement rather than being
condemned on evidence that never tested it.
