# Forgotten Roads retained-UI live test

The six candidates are installed. Run this in a fully loaded character, not character select or an active zoning transition.

## Screenshot A — fallback collection

1. Disable or unload Suite Hub.
2. Confirm all six standalone launchers recover: Journal, Guild Life, Crafting, Contracts, PvP, Party Tools.
3. Move them into one vertical stack with 4px visual gaps.
4. Capture at 1920×1080 with all six visible together.

Acceptance: every launcher is exactly the same apparent width and height; all have the same dark fill, thin cyan frame, 20px grip, three-dot mark, 11px uppercase label, padding, hover, and pressed family. PvP remains the same size and uses only `PVP [ON]`/`PVP [OFF]` for state. No square/tofu glyph appears.

## Interaction matrix

For every launcher:

1. Click the body and verify the correct panel opens.
2. Drag only the grip using the left button; verify the panel does not open from the drag.
3. Release outside the launcher; verify drag ownership clears.
4. Try right and middle buttons on the grip; neither may drag.
5. Drag while using the modern camera; the camera must remain stationary.
6. Alt-tab/focus-loss during a drag, zone during a drag, and disable/re-enable the module. No stuck camera or stale drag state may remain.
7. Move near every screen edge and restart. Saved normalized positions must persist and clamp fully on-screen.

Repeat the position/clamp check at one smaller common resolution, preferably 1280×720.

## Screenshot B — expanded header

Open one of Journal, Guild Life, Crafting, or Contracts while expanded. Capture the compact title, upward two-bar chevron, reset control, and X-close together.

Acceptance: the chevron is clean cyan `Image` geometry, points upward, and contains no font square. Header dragging and X-close still work.

## Screenshot C — collapsed header

Collapse the same panel and capture the header.

Acceptance: the body is hidden, dragging and X-close remain available, the chevron points downward, and expanding restores the prior size/content without an off-screen jump.

## Hub healthy

1. Re-enable Suite Hub and wait for healthy module discovery/bridges.
2. Confirm existing launcher preferences are respected: no duplicate-launcher fight and no repeated hide/show flicker.
3. Open each of the six modules from Hub.
4. Disable/unload Hub again. Each standalone fallback must recover without resetting its saved position.

## Log and acceptance gate

After zoning and one reload cycle, inspect `lunaris.log` for exceptions, duplicate UI roots, drag ownership warnings, or repeated build/teardown loops. Visual acceptance requires all three screenshots plus successful mouse/camera behavior at both resolutions. Builds and hashes alone are not visual proof.
