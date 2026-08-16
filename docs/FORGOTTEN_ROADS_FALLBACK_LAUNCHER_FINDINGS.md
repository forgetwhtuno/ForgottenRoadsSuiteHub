# Forgotten Roads fallback launcher findings

Date: 2026-08-15

## Source findings

Before this pass the six fallback launchers were visibly divergent:

| Module | Previous size | Previous grip/status issue |
|---|---:|---|
| Journal | 118×30 | 20px header grip with TMP diamond; open state used a bullet |
| Guild Life | 136×30 | same glyph dependency but different width |
| Crafting | 132×30 | much more opaque surface, TMP diamond, no launcher frame |
| Contracts | 130×30 | TMP diamond; font triangle collapse button |
| PvP | 148×32 | bright 18px cyan slab with TMP vertical-dot grip; mixed-case label and spaced status |
| Party Tools | 154×32 | bright 18px cyan slab with TMP vertical-dot grip; larger 13px mixed-case label |

Journal and Guild Life already had mod-owned `Image` chevrons. Crafting and Contracts still depended on TMP triangle coverage. Suite Hub already demonstrated the correct retained-uGUI palette, separate grip/body ownership, normalized positioning, and programmatic chevrons.

## Resolution

- All six launchers now use the exact 154×32 contract in `FORGOTTEN_ROADS_UI_VISUAL_CONTRACT.md`.
- All six use the same dark body, thin cyan frame, 20px dark grip, cyan accent, three programmatic dots, 11px uppercase label, hover, and pressed colors.
- Font-dependent launcher glyphs are disabled and do not render. The visible grip is entirely `Image` geometry.
- Crafting and Contracts collapse controls now use the same programmatic two-bar chevron family as Journal/Guild/Hub.
- PvP status is `PVP [ON]` or `PVP [OFF]`; enabling PvP no longer changes launcher size or turns the entire launcher into a bright slab.
- Header names and font scale were standardized. PvP and Party Tools close buttons were reduced to the same 28×24 family.
- Existing normalized positions, screen clamps, click targets, drag handlers, fallback visibility, Hub bridge behavior, and gameplay code paths were not replaced.

## Files changed by this UI pass

- Journal 0.1.8: `src/StandaloneLauncherVisual.cs`, `src/JournalLauncher.cs`, `src/JournalWindow.cs`, `src/ErenshorJournalPlugin.cs`, `RUN_TESTS.ps1`, `CHANGELOG.md`.
- Guild Life 0.1.3: `src/StandaloneLauncherVisual.cs`, `src/GuildLauncher.cs`, `src/GuildWindow.cs`, `src/ErenshorGuildLifePlugin.cs`, `RUN_TESTS.ps1`, `CHANGELOG.md`.
- Crafting Expanded 0.2.4: `src/StandaloneLauncherVisual.cs`, `src/UI/CraftingLauncher.cs`, `src/UI/CraftingWindow.cs`, `src/ErenshorCraftingExpandedPlugin.cs`, `tests/RUN_TESTS.ps1`, `BUILD_AND_INSTALL.ps1`, `CHANGELOG.md`.
- Contracts 0.4.3: `src/StandaloneLauncherVisual.cs`, `src/ContractLauncher.cs`, `src/ContractBoardWindow.cs`, the version in `src/ErenshorContractsPlugin.cs`, `RUN_TESTS.ps1`, `CHANGELOG.md`. Existing reward-config repair changes were preserved and not reworked here.
- PvP 0.5.3: `src/StandaloneLauncherVisual.cs`, `src/PvpPanel.cs`, `src/PvpUiGeometry.cs`, `src/ErenshorPvPPlugin.cs`, `tests/RUN_UI_TESTS.ps1`, `CHANGELOG.md`. Existing PvP runtime repair changes were preserved and not reworked here.
- Party Tools 0.1.6: `src/StandaloneLauncherVisual.cs`, `src/PartyToolsPanel.cs`, `src/ErenshorPartyToolsPlugin.cs`, `RUN_TESTS.ps1`, `CHANGELOG.md`.
- Suite Hub: documentation only; shipped Hub UI source and version were unchanged.

No Deep Sims, Follow, Duel, Nemesis, Campmaster, gameplay, rewards, claim, combat, or contract progression code was changed by this pass. No Git writes were performed.

## Remaining live evidence

Compilation cannot prove visual appearance or camera behavior. The installed candidates require the three acceptance screenshots and interaction sequence in `FORGOTTEN_ROADS_UI_LIVE_TEST.md` before this workstream can be called visually accepted.
