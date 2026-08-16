# Forgotten Roads discoverability findings

Date: 2026-08-15

## Outcome

All eleven player-facing modules now have a mouse-discoverable standalone route when Forgotten Roads Hub is absent or cannot truthfully advertise usable UI. The Hub remains optional. No gameplay state machine was moved into UI code.

Five gaps were repaired:

- Deep Sims: compact status and social-activity quick panel.
- Campmaster: Hunt Camp/Relax quick panel.
- Nemesis: rivalry status, first eligible candidate selection, and existing confirmation actions.
- Practice Duel: guide/status panel, first eligible nearby challenge, and stop.
- Follow: guide/status panel plus existing stop/pause/resume/cancel/return actions; the full target-specific workflow remains in Sim Actions.

Party Tools, Contracts, Journal, Guild Life, Crafting Expanded, and PvP already had retained-uGUI standalone windows and launchers. They were retained as their fallback surfaces.

## Architecture

The repaired modules source-link a small retained-uGUI shell into their own DLL. There is no shared runtime DLL and no dependency on Hub. Each launcher is 154 x 32 with a 20 px drag-only grip, programmatic three-dot disclosure, dark-blue/cyan styling, separate button hit target, bounded on-screen dragging, and lifecycle cleanup.

Hub presence is consumed through the existing Aura descriptor. A launcher is hidden only for `status=Ready` plus `uiAvailable=true`; missing, malformed, unavailable, or physically absent Hub data fails open to the standalone launcher.

Panel buttons call the existing module Control API. Hub calls the same Control API through each existing Aura action provider. Commands retain their existing handlers and authority boundaries. No fallback button writes native gameplay state directly.

The optional Hub dock was retained rather than made authoritative. Each module remains independently recoverable.

## Release status

Static inventory, deterministic tests, installed-reference compilation, transactional installation, SHA-256 equality, and active-DLL uniqueness pass. Live pointer/layout behavior still requires the checklist in `FORGOTTEN_ROADS_DISCOVERABILITY_LIVE_TEST.md`; offline tests are not represented as live-game proof.

