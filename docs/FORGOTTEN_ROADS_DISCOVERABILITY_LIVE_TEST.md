# Forgotten Roads discoverability live test

Use the exact installed DLL set recorded in the build report.

1. Move `ErenshorSuiteHub.dll` out of the active `plugins` directory and launch the game.
2. Load a character and wait for normal movement control.
3. Confirm visible, non-overlapping launchers for all eleven modules. Open every launcher with the mouse.
4. Drag every new launcher by its 20 px grip. Confirm button clicks do not drag, camera does not move, and no launcher/panel can be left off-screen.
5. Exercise Deep Sims refresh and Quiet/Normal/Lively. Verify immediate status feedback.
6. Exercise Campmaster Hunt Camp/Relax/End Relax only in valid contexts. Verify rejected contexts do not change state.
7. Exercise Nemesis candidate selection, confirmation/cancel, and clear. Verify the existing confirmation semantics remain intact.
8. Select a Sim and compare Practice Duel and Follow fallback guidance with the full Sim Actions workflow. Start/stop a duel and start/pause/resume/cancel an expedition.
9. Zone, logout/login, and reload/disable/re-enable each repaired plugin. Confirm one UI root per module and no stale drag flag.
10. Restore Hub, launch again, and wait for Hub `Ready`/`uiAvailable=true`. Confirm redundant standalone launchers disappear and MODS/dock opens every module.
11. Disable or remove Hub while the game is stopped, relaunch, and confirm all standalone launchers return.
12. Test 1920x1080 and one smaller resolution. Confirm headers/buttons remain contained and X closes only presentation.
13. Review the Lunaris log for exceptions, duplicate plugin initialization, Aura registration faults, per-frame spam, or stale UI warnings.

Record each module as PASS, FAIL with exact behavior, or NOT TESTED. A compile/test result is not a substitute for this live pass.

