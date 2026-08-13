# Erenshor Suite Hub

**Version:** 0.2.0 integration candidate
**Author:** forgetwhtuno
**Loader:** native Lunaris
**License:** Apache-2.0

One compact player-facing launcher and central menu for the Erenshor mod suite. The Hub is
optional: every sibling mod remains authoritative and usable without it.

## Player-facing shape

The launcher is a compact dark-cyan `MODS` control with a dedicated left drag grip. It is only
eligible to appear after the current character has reached a positively observed gameplay-ready
state. Clicking it opens **ERENSHOR MOD SUITE** with:

- Overview;
- installed-only left navigation;
- status and ordinary controls/settings when the owning mod exposes the Suite bridge;
- a dedicated-panel button only when that mod advertises the action;
- Advanced and Developer separation rather than a raw config dump.

An installed mod that has not implemented the bridge is shown honestly as **installed; bridge
unavailable**. The Hub does not guess private APIs or edit that mod's config file.

## Readiness change

The earlier player-object check was too early in live testing. The 0.2.0 candidate additionally
requires Erenshor's zoning transition to be clear, the Sim manager/grouping graph to be rebuilt,
and `PlayerControl.CanMove` to have become true before a short stability debounce completes.
Normal temporary `CanMove=false` after readiness does not hide the Hub; zoning/character-select
requires a fresh acquisition.

The exact fields used are documented in `Erenshor-Mod-Suite/docs/ARCHITECTURE.md`. The source
snapshot used for this change did **not** include `Assembly-CSharp.dll`, so the new conjunction
still requires compilation and live lifecycle tracing against the target install before release.

## Optional module bridge

Current Lunaris Aura is used as a carrier, not as gameplay authority. Endpoints are namespaced per
module and transport only strict bounded strings/primitives. The owning mod reports its descriptor,
settings, and allowed actions; it also performs validation and persistence.

The design has no load-order requirement:

- Hub first: subscribers exist and discover the provider later.
- Mod first: the next Hub poll sees the provider.
- Mod unload: provider unregisters; the module page falls back to standalone status.
- Mod reload: it registers again exactly once.
- Hub unload: sibling mods continue normally.

See `Erenshor-Mod-Suite/docs/HUB_INTEGRATION_CONTRACT.md` for the exact v1 contract. No sibling mod
in the attached source snapshot implements it yet.

## Input containment

The Hub keeps a pointer-capture latch from mouse-down over Hub UI through mouse-up, so dragging out
of the launcher/window cannot hand camera rotation or target clicks back to the game mid-drag.
The launcher uses a non-overlapping grip/button layout and the main window is header-drag only.
Positions persist, clamp back on screen, and can be reset from the header.

## Build and deterministic tests

```powershell
powershell -ExecutionPolicy Bypass -File .\RUN_TESTS.ps1
powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1
```

The deterministic suite covers readiness policy, module registration/update/unload/reload,
duplicate registration, descriptor/settings validation, installed-only navigation filtering,
launcher geometry, and off-screen recovery.

**Verification status for this handoff:** source-reviewed and offline test code added. The attached
handoff did not include the current Erenshor/Lunaris runtime DLLs and this execution environment
did not provide Windows `csc.exe`/PowerShell, so full compilation and the new deterministic runner
must be executed by the local integration agent. Live UI behavior remains `NEEDS LIVE TEST`.

This is an unofficial community mod and is not affiliated with or endorsed by Burgee Media.


### Lunaris permissions

Current Lunaris 0.1.9 statically classifies `System.IO` calls as `FileAccess`; Suite Hub therefore declares `FileAccess | Harmony` (file access is used only for plugin-DLL presence discovery).
