# Forgotten Roads discoverability matrix

| Module | Existing full/context UI | Standalone launcher | Hub page/action | Command-only if Hub absent | Fallback surface | Shared action path |
|---|---|---:|---:|---:|---|---:|
| Deep Sims | No prior panel | Yes | Yes | No | Status + Quiet/Normal/Lively + refresh | Yes |
| Party Tools | Full panel | Yes | Yes | No | Existing full panel | Yes |
| Contracts | Full board | Yes | Yes | No | Existing contract board | Yes |
| Journal | Full window | Yes | Yes | No | Existing journal window | Yes |
| Guild Life | Full window | Yes | Yes | No | Existing guild window | Yes |
| Campmaster | No prior panel | Yes | Yes | No | Hunt Camp/Relax quick panel | Yes |
| Nemesis | No prior panel | Yes | Yes | No | Rivalry/candidate/confirmation quick panel | Yes |
| Crafting Expanded | Full panel | Yes | Yes | No | Existing crafting panel | Yes |
| Practice Duel | Contextual Sim Actions | Yes | Yes | No | Guide/status + nearby challenge/stop | Yes |
| PvP | Full panel | Yes | Yes | No | Existing PvP panel | Yes |
| Follow | Sim Actions/setup/status | Yes | Yes | No | Guide/status + travel actions | Yes |

Collection audit invariant: every catalog entry has a dedicated mouse fallback description and an `openPanel` route. A healthy Hub can therefore consolidate launchers without stranding any installed module; a provider fault prevents that ownership claim.

