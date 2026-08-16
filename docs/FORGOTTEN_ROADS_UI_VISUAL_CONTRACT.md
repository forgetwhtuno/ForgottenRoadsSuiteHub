# Forgotten Roads retained-UI visual contract

Canonical date: 2026-08-15

This is a source-level contract. Each standalone module carries a mechanical copy of `StandaloneLauncherVisual.cs`; there is no shared runtime DLL and no dependency on Suite Hub.

## Standalone launcher

| Token | Exact value |
|---|---:|
| Width | 154 px |
| Height | 32 px |
| Drag grip | 20 px |
| Border | 1 px |
| Label font size | 11 px TMP |
| Label style | normal, centered, uppercase, no wrapping |
| Background | RGBA `(0.015, 0.09, 0.125, 0.72)` |
| Grip background | RGBA `(0.025, 0.13, 0.17, 0.88)` |
| Body normal | RGBA `(0.035, 0.17, 0.22, 0.78)` |
| Body hover | RGBA `(0.12, 0.38, 0.48, 0.90)` |
| Body pressed | RGBA `(0.08, 0.28, 0.36, 0.94)` |
| Cyan frame/accent | RGBA `(0.03, 0.67, 0.86, 0.95)` |
| Text | RGBA `(0.88, 0.92, 0.91, 1.0)` |
| Transition fade | 0.08 seconds |

The grip is a dark 20px raycast target with a 2px cyan left accent and three 2×2 programmatic cyan dots spaced 5px vertically. No text glyph or external sprite is used. Only the grip owns drag. The remaining 134px body is the click target that opens/toggles the panel.

The root has a dark translucent fill and four mod-owned 1px `Image` edges. Labels are `JOURNAL`, `GUILD LIFE`, `CRAFTING`, `CONTRACTS`, `PVP [OFF]`/`PVP [ON]`, and `PARTY TOOLS`. Open state for the four window launchers is the restrained suffix `[OPEN]`; dimensions and background do not change.

Saved launcher positions remain normalized bottom-left coordinates. Existing values are retained and are re-clamped using the new fixed dimensions at the current resolution. A completed drag is the only persistence event.

## Input contract

- Retained Unity uGUI only: `Canvas`, `GraphicRaycaster`, raycastable `Image`, `Button`, TMP.
- Existing EventSystem only; never create a second EventSystem.
- Left-button grip claim only. The button body is never draggable.
- Existing per-module drag handlers retain camera containment, pointer release outside, focus/pause cleanup, zoning cleanup, disable cleanup, destroy cleanup, and shared `GameData.DraggingUIElement` ownership.
- No right- or middle-button drag.
- Standalone visibility remains owned by each module’s existing Suite fallback policy. A healthy Hub may hide it; an absent/unusable bridge must recover it.

## Panel header

- Header height stays 34px (or the module’s existing equivalent policy).
- Module titles are compact uppercase names at 15px: `JOURNAL`, `GUILD LIFE`, `CRAFTING`, `CONTRACTS`, `PVP`, `PARTY TOOLS`.
- Close buttons use the compact 28×24 family. Existing reset buttons and header-only drag surfaces are preserved.
- Existing collapsible panels use a 12×10 programmatic chevron made from two 2×7 cyan `Image` bars. Expanded points upward (collapse action); collapsed points downward (expand action). No triangle character, font fallback, or external asset is permitted.
- PvP and Party Tools were not made newly collapsible in this presentation-only pass; their existing body behavior is unchanged.

Inner panel content is deliberately outside this contract.
