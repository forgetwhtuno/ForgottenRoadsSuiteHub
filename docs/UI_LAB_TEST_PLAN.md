# Suite Hub UI lab — live test plan

Installed build: `ErenshorSuiteHub.dll`, 58,880 bytes. Nothing committed, nothing merged.

**There is no keyboard hotkey. There must never be one.** `/mods` and `/modsuitest` are developer
recovery/switch tools only. The Definition of Done is that the **visible MODS control works entirely
by mouse**. If a MODS control cannot be clicked, that variant has failed — no matter what the chat
commands can do.

## What is installed

Three UIs coexist. The OnGUI baseline stays live so you always have a working Hub.

| | Access | Technology |
|---|---|---|
| **Baseline** | existing `MODS` launcher (always visible) | OnGUI + patched native gates |
| **Variant A** | `/modsuitest dragui` → `MODS TEST` launcher | uGUI Canvas + **native `DragUI`** |
| **Variant B** | `/modsuitest eventtrigger` → `MODS TEST` launcher | uGUI Canvas + **own `IPointer*`/`IDrag`** |

Commands: `/modsuitest dragui` · `/modsuitest eventtrigger` · `/modsuitest off` · `/modsuitest status`
(results go to `lunaris.log`, prefix `[SuiteUiLab]`). Variant C (Lunaris ImGui) was deliberately not
built — see the final report.

Variant A and B are visually identical on purpose: `◇ MODS TEST [Open]`, opening a
`SUITE UI TEST` window with `Click Me` / `Count:` / `Toggle` / `State:`. Only the drag mechanism
differs, so any behavioural difference is attributable.

---

## Five-minute A/B test

Do all of it with the **mouse only**. Use chat commands solely to switch variants.

### Round 0 — baseline (2 min)

Enter the world, wait until fully gameplay-ready.

1. Confirm the `MODS` launcher is visible without typing anything.
2. **Single-click MODS.** → opens exactly once? no camera movement? no world click? no target change?
3. Close it with its own `X` control.
4. Click `MODS` again → opens again, exactly once.
5. Drag the `::` grip (left ~20px only). → camera still? follows pointer? **no top-left snap?** stays put on release?
6. Operate the window's controls with the mouse.

### Round 1 — Variant A (1.5 min)

Type `/modsuitest dragui`. A `MODS TEST` launcher appears.

7. Click `Open` → window appears **once**. Click again → closes.
8. `Click Me` → `Count` increases by **exactly 1** per click.
9. `Toggle` → `State` flips **exactly once** per click.
10. Drag the **◇ diamond** → camera still, follows pointer, no snap, stays where released.
11. Drag the window **header** → same.
12. Confirm no attack fired, no target changed, cursor still usable, and normal camera control returns after release.

### Round 2 — Variant B (1.5 min)

Type `/modsuitest eventtrigger`. Repeat steps 7–12 exactly.

### Round 3 — lifecycle

13. `/modsuitest off` → test UI disappears completely, gameplay input normal, cursor not stuck.
14. `/modsuitest dragui` again → **exactly one** launcher (no duplicate).
15. Move the launcher, `/modsuitest off`, then back on → position retained.

---

## Acceptance matrix

Record PASS/FAIL per cell. A blank means untested, not passed.

| | Baseline (OnGUI) | A (native DragUI) | B (own handlers) |
|---|---|---|---|
| **CLICK** button increments exactly once | | | |
| toggle changes exactly once | | | |
| no attack fired | | | |
| no target loss/change | | | |
| **LAUNCHER DRAG** camera does not rotate | | | |
| follows pointer | | | |
| no top-left snap | | | |
| stays where released | | | |
| **WINDOW DRAG** camera does not rotate | | | |
| follows pointer / no snap / stays put | | | |
| **CURSOR** usable while interacting | | | |
| gameplay restored afterward | | | |
| no stuck unlocked cursor | | | |
| **LIFECYCLE** disable → UI gone, input returns | | | |
| re-enable → exactly one instance | | | |
| **POSITION** retained across close/reopen | | | |
| resolution-safe | | | |
| offscreen recovery | | | |

## What to send back

`<Erenshor>\lunaris.log`.

- `[HubGesture]` lines cover the **baseline only** — MouseDown/MouseUp with GUI coords, cursor lock
  state at press time, drag begin/end rects, toggle requests, window open flips.
- `[SuiteUiLab]` lines cover lab switching and `status` output.

The lab variants intentionally emit no per-gesture logging: if they work, they work *silently*
through the engine, which is the whole argument for them.

## Reading the result

- **Baseline passes** → the stale-DLL theory was the whole story, and the OnGUI fix is real. uGUI
  becomes a quality/maintenance decision, not a rescue.
- **Baseline fails but A and B pass** → OnGUI cannot be made reliable here; migrate to uGUI.
- **A passes, B fails (or vice versa)** → the drag mechanism is the deciding factor; take the winner.
- **All three fail the same way** → something outside this hypothesis is warping the pointer.
  Prime suspect: `Mouse.WarpCursorPosition`, confirmed present in `PlayerControl.Update`.
