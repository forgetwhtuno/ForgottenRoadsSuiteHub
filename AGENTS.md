# AGENTS.md — Erenshor Suite Hub

Instructions for AI/coding agents working in this repository. Read this before making changes.

## What this mod is

The permanent player-facing launcher for the Erenshor mod suite (native Lunaris plugin, .NET
Framework 4.8, C# 5 effective language level via `csc`). **This is Phase 1**: a compact launcher,
one movable window with exactly one working tab (Overview), and simple file-presence discovery of
which other suite mods' plugin DLLs are present. It is deliberately not the finished multi-tab hub
yet — see `README.md` for the exact current scope and what is explicitly not built.

## Hard constraints

- **The Hub must never become a hard runtime dependency for any other suite mod.** Every other
  mod already works, and must keep working, with the Hub absent or not yet loaded. This repo does
  not add code to any other mod's repo, and does not require any other mod to register itself.
- **No invented Lunaris "Aura" API.** If Aura is not verified suitable for mod registration, do
  not guess at its shape or call into it speculatively. That verification has not happened as of
  Phase 1; until it has, mod discovery stays limited to the file-presence check in
  `src/ModDiscovery.cs` (checking for a known DLL file name in the plugins folder — no reflection
  into the DLL, no type loading, no cross-mod calls).
- **Launcher interaction model is fixed, do not "simplify" it:** an 18px-wide grip strip on the
  left is the *only* draggable area (`GUI.DragWindow(new Rect(0, 0, 18, Height))`); a separate
  `GUI.Button` fills the rest of the launcher as the *only* click action. The drag rect and the
  button rect must never overlap — that overlap was a live-confirmed bug independently found and
  fixed in three sibling mods (Contracts, Guild Life, PvP) before this repo existed. See
  `src/HubLauncher.cs`.
- **Open/close state mutation only happens in `Update()`, never inside `OnGUI()`.** A click
  observed during `OnGUI` only sets `_pendingToggle`/`_pendingClose`; the actual `_open` flip
  happens once per frame in `Update()`. Mutating it directly inside `OnGUI` desyncs Unity's
  Layout/Repaint IMGUI passes — also a live-confirmed bug this session. See
  `src/ErenshorSuiteHubPlugin.cs`.
- **The launcher/window only ever draw once `IsLocalCharacterReady()` is true** (not cached across
  scene loads, recomputed every frame). Never visible at title screen, character select, or any
  loading state.
- **Header-drag only for the main window** — the title bar is the only draggable region; body and
  controls never drag. Click-through guard (Harmony prefix on `PlayerControl.LeftClick` and
  `csMouseOrbit.LateUpdate`) so a click on the Hub doesn't also affect the game world.
- Do not build per-mod tabs, a dedicated-panel integration, live mod registration/interaction, or
  a config read/write bridge into any other mod's settings in this phase. That is explicitly
  Phase 2+ scope tracked in `Erenshor-Mod-Suite`, not this repo, until a separate task says so.
- No secrets, personal file paths, tokens, or real names in source, docs, or commit messages.
- Do not commit or push changes unrelated to the task at hand.

## Important source files

- `src/ErenshorSuiteHubPlugin.cs` — plugin entry point: ready-gate, deferred toggle, window/
  launcher rect persistence, click-through Harmony patches.
- `src/HubLauncher.cs` — the compact grip+button launcher.
- `src/HubWindow.cs` — the single Overview window.
- `src/ModDiscovery.cs` — pure, Unity-free file-presence discovery logic (testable without a live
  game instance). This is the entire "installed mod" detection mechanism for Phase 1.
- `src/HubSettings.cs` — Lunaris config entries (launcher/window position and size).

## Build / test procedure

- Deterministic tests: `powershell -ExecutionPolicy Bypass -File .\RUN_TESTS.ps1` (standalone
  `csc` compile + run of `src/ModDiscovery.cs` against `tests/ModDiscoveryTests.cs`, no game/
  Lunaris dependency).
- Full plugin build: `powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1` — locates
  the current Erenshor/Lunaris install and **installs over the live plugin folder**. Don't use it
  as a plain compile check; build to a scratch output path first when just verifying compilation.
- The shipped build compiles with the legacy .NET Framework `csc.exe` (effectively C# 5) despite
  the `.csproj` claiming `LangVersion 7.3`. Avoid string interpolation, `nameof`, null-conditional
  operators, expression-bodied members, and inline `out` variable declarations.

## Compatibility boundaries

- Requires nothing else to function as a launcher/Overview window. Works with zero, some, or all
  of the other ten suite mods installed.
- Does not own any other mod's gameplay state, settings, or UI. Discovery is read-only and
  file-presence-based only.
