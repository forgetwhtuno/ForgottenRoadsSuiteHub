# Forgotten Roads UI build/install report

Date: 2026-08-15
Result: **all deterministic tests passed; all six builds succeeded; serialized install and hash verification succeeded**

## Test results

- Journal: all core/API/progression/typing/character/legacy/UI/privacy/camera tests passed; new launcher visual-contract guard passed.
- Guild Life: 55 core assertions plus Suite/read-only/lifecycle/collapse/launcher guards passed.
- Crafting Expanded: all deterministic groups and source-contract checks passed, including launcher/chevron checks.
- Contracts: 412 core assertions plus Suite/reward-migration/launcher/chevron guards passed.
- PvP: UI policy, drag/Suite, native NPC runtime/reward, and launcher guards passed.
- Party Tools: friend availability, party roll, RNG, UI/command, positioning, authority, Escape, camera/gesture, and launcher guards passed.

## Build inputs

- Current installed `Assembly-CSharp.dll` SHA-256: `b840cb8076ed0553f7dc3beb4042aba653917882f763181ec0d2c13c26c17847`
- Current live/build-reference `Lunaris.dll` SHA-256: `5a70f3d1fd9441ceae6d8e1f80cafce86ff2a47245fbcfa36bfcf8e88fd20b29`
- Candidate directory: `local-build-output\ForgottenRoadsUI`
- Preinstall backup: `local-build-backups\ForgottenRoadsUI-preinstall-20260815-visual-contract`

All compilation occurred before the live install. The five install-oriented scripts targeted an isolated temporary game root; Crafting used its non-installing `BUILD.ps1`. Erenshor was not running during the serialized copy.

## Installed candidates

| DLL | Logical version | Bytes | Built = installed SHA-256 |
|---|---:|---:|---|
| `ErenshorJournal.dll` | 0.1.8 | 94,208 | `53ce881b904ee72b9bfd38d7190851f4e282b9101a09394a7fdd7cbab9d1ed57` |
| `ErenshorGuildLife.dll` | 0.1.3 | 74,240 | `bd4dd195c76a654230ad09b4cd4465cda5f73b4020ced40c9141dc876de9ba7a` |
| `ErenshorCraftingExpanded.dll` | 0.2.4 | 420,352 | `a816b5ce18f1e2977a02c0ccc14484557404cf58abbdd2d17e38201a82a1b549` |
| `ErenshorContracts.dll` | 0.4.3 | 143,360 | `fd842385bbc4eb1ba24c4b33b75998f370cd8bc379186a181577e03a1eb3d7b8` |
| `ErenshorPvP.dll` | 0.5.3 | 186,880 | `7ba61f53e52387561026e0116bb609a9d41bdf7a14dd24bf5f42975fd3c420c9` |
| `ErenshorPartyTools.dll` | 0.1.6 | 73,216 | `ac3e4bd128ea15e62433e48ce9394a9b2160d191542a94ac226dc41a3e571c80` |

Each installed hash was recomputed after copying and matched its candidate exactly. There is exactly one live copy of each DLL under `plugins`. Three older Crafting DLLs also exist under the game root’s `.plugins-backups` history; they are outside `plugins` and are not live plugin candidates.

## Status

Build/install status is successful. Visual acceptance remains pending because the game was not launched and no acceptance screenshot was fabricated. Capture the six-launcher collection, one expanded header, and one collapsed header using `FORGOTTEN_ROADS_UI_LIVE_TEST.md`.
