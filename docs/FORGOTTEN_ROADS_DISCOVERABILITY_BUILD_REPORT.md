# Forgotten Roads discoverability build report

Date: 2026-08-15

## Result

- Full enabled collection: 12/12 builds PASS.
- All declared deterministic test scripts: PASS.
- Suite Hub deterministic suite: PASS, 343 assertions (including the collection discoverability audit).
- Dirty local worktrees: accepted intentionally with `-AllowDirty`; branch/source SHA and dirty state were recorded without Git writes.
- Installation: one complete staged set installed transactionally after all tests passed.
- Persistent pre-install backup: `local-build-backups/discoverability-preinstall-20260815-232701`.
- Installed SHA verification: 12/12 match their staged bytes.
- Active plugin uniqueness: exactly one matching DLL for each module under the active `plugins` tree. Historical backups are outside the active plugin tree.

| Module | Version marker | SHA-256 |
|---|---:|---|
| Deep Sims | 0.7.3 | `75e563fcc6a24d07a38532bd1163e0994610ea4d86d30e98fa4d7570e0d4c534` |
| Party Tools | 0.1.6 | `fb834b85845aadc009477d25a2db54168456eb1002db5e1b5bad86cc0d94a267` |
| Contracts | 0.4.3 | `75d34366799bec2e1a9e64f0042b89a01cf458b805c707dcbd2f95bd06aa19e7` |
| Journal | 0.1.8 | `df00d4fb9f5936f2441c61246eb5a3d5007bf6a5039e0558e6bb56636fa7bb8e` |
| Guild Life | 0.1.3 | `fda813ba620d00a7ec3e1e4c95f4a1fe3a3583d5862ec38f0a5660834ff24fa2` |
| Campmaster | 0.4.0 | `31224ee636666d9e27b1e20aab3f3a323ab5e55724588454a67246fc84368726` |
| Nemesis | 0.2.0 | `6b6a23811c2f2b839f40c2fb76e1be0231fbc8561aeb065d6698ef852783aed3` |
| Crafting Expanded | 0.2.4 | `a6d6d0cb466fdda31ee123627d95a2eb7c09fee547efeddde7fd9cf3879673db` |
| Practice Duel | 0.4.1 | `6f85dd2a0079b6ff684ca444c9d0ea56aa8bdff22788742fcb9858d100320c37` |
| PvP | 0.5.3 | `97b7cf8c4bc77f3fb60733dca036b8693480a05c4918b873cdf463ae9f6383f2` |
| Follow | 0.6.4 | `cbf378aeb0553627b16c681e2c753c7ba593a2e69548b3b79d4d1fcd07990741` |
| Suite Hub | 0.5.2 | `19b97c732083209742bdf1f0e91cfc3b390d711d986e488c8099ca6c71dd2c11` |

The version column preserves the current source markers; SHA-256 is the exact final-candidate revision identifier for this dirty local build. Except for Deep Sims, most module DLLs do not currently emit Windows file-version metadata, so Lunaris attributes plus SHA-256 are the deployment identity boundary.
