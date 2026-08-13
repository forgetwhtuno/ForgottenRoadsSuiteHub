# Erenshor Suite Hub

**Version:** 0.1.0 (Phase 1 skeleton)
**Author:** forgetwhtuno
**Loader:** native Lunaris
**License:** Apache-2.0

Compact launcher and overview window for forgetwhtuno's Erenshor mod suite. This repo is the
future permanent player-facing entry point for the whole suite — **this release is Phase 1 only**,
a working skeleton, not the finished multi-tab hub.

## What Phase 1 actually is

- A small draggable **MODS** launcher button (grip strip on the left to drag, the rest of the
  button toggles the main window), only shown once a real local character is loaded into the
  world — never at the title screen, character select, or during loading.
- One movable window with **exactly one working tab: Overview**. It is moved by its title bar
  only; body and controls never drag.
- The Overview tab shows the Hub's own version and a list of which other suite mods are currently
  detected in the game's `plugins` folder.
- **Mod detection is deliberately simple**: it checks whether each other suite mod's known plugin
  DLL file (e.g. `ErenshorJournal.dll`) exists in the same `plugins` folder this Hub's own DLL
  loaded from. That's it — no reflection into those DLLs, no type loading, no calls into them, no
  registration API, no dependency in either direction.

## What is explicitly NOT built yet

- **Per-mod tabs.** The main window does not have a tab per installed mod yet — only Overview.
- **Dedicated-panel integration.** The Hub does not open, embed, or control any other mod's own
  window/panel.
- **Live mod registration.** There is no bridge, event, or API that other mods call into to
  register themselves with the Hub, and no plan to add one to any other mod's repo from here.
- **Config read/write into other mods' settings.** The Hub cannot see or change any other mod's
  configuration.
- **Any Lunaris "Aura" integration.** Aura has not been verified suitable for mod
  registration/coordination, so this Hub does not touch it, guess at its shape, or depend on it.

None of the above is a bug — it is out of scope for this phase by design. See `AGENTS.md` for the
hard constraints and `Erenshor-Mod-Suite`'s `docs/UI_DESIGN.md` / `docs/ARCHITECTURE.md` for the
longer-term plan this skeleton is one step toward.

## Compatibility

The Hub requires nothing else to function and never becomes a hard dependency for any other suite
mod. It works identically whether zero, some, or all of the other suite mods are installed
alongside it; every other suite mod already works, and keeps working, whether or not the Hub is
present.

## Installation / build

This mod requires **native Lunaris**. This source package intentionally does not redistribute
Erenshor or Lunaris assemblies.

1. Install Lunaris for Erenshor and launch the game modded once.
2. Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1
```

The script locates your current Erenshor installation and the Lunaris developer reference, builds
against the installed Unity/Lunaris assemblies, then installs:

```text
plugins/ErenshorSuiteHub.dll
```

**Status:** this build compiles cleanly against the installed Lunaris/Assembly-CSharp/Unity
assemblies and passes its deterministic test suite (`RUN_TESTS.ps1`, covering the pure mod-
discovery logic). It has **not** been live-tested in-game — the launcher's actual on-screen
appearance, click/drag behavior, and mod-discovery results while running have not been observed
in a live session. Do not assume live-verified behavior from this document alone.

## Deterministic tests

```powershell
powershell -ExecutionPolicy Bypass -File .\RUN_TESTS.ps1
```

Covers `src/ModDiscovery.cs` only — the one piece of pure, Unity-free logic in this phase. The
launcher/window drawing itself is IMGUI and is not unit-testable without a live Unity instance.

## Development note

This project was developed with substantial AI-assisted coding and review. Erenshor Suite Hub is
an unofficial community mod and is not affiliated with or endorsed by Burgee Media.
