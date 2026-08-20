using ErenshorSuiteHub;

internal static class ForgottenRoadsDiscoveryHintPolicyTests
{
    internal static int RunAll()
    {
        int a = 0;

        // Delay bounds sanity: the fixed delay actually used must sit inside the requested 3-5s.
        a += TestAssert.True(ForgottenRoadsDiscoveryHintPolicy.DelaySeconds >= ForgottenRoadsDiscoveryHintPolicy.MinDelaySeconds &&
            ForgottenRoadsDiscoveryHintPolicy.DelaySeconds <= ForgottenRoadsDiscoveryHintPolicy.MaxDelaySeconds,
            "delay is within the requested 3-5 second window");

        // 1: no message before Ready - every non-Ready stage, across many ticks, never emits.
        ForgottenRoadsDiscoveryHintPolicy notReady = new ForgottenRoadsDiscoveryHintPolicy();
        a += TestAssert.False(notReady.ShouldEmit(GameplayReadinessStage.CharacterSelect, 0f), "character select never emits");
        a += TestAssert.False(notReady.ShouldEmit(GameplayReadinessStage.PlayerObjectCreating, 1f), "player-object-creating never emits");
        a += TestAssert.False(notReady.ShouldEmit(GameplayReadinessStage.ZoneTransition, 2f), "zone-transition never emits");
        a += TestAssert.False(notReady.ShouldEmit(GameplayReadinessStage.WorldInitializing, 3f), "world-initializing never emits");
        a += TestAssert.False(notReady.ShouldEmit(GameplayReadinessStage.Stabilizing, 100f), "stabilizing never emits, even much later");
        a += TestAssert.False(notReady.HasEmittedThisSession, "nothing latched with no Ready stage yet");

        // 2: bounded 3-5s delay after Ready - not before DelaySeconds, true at/after it.
        ForgottenRoadsDiscoveryHintPolicy timing = new ForgottenRoadsDiscoveryHintPolicy();
        a += TestAssert.False(timing.ShouldEmit(GameplayReadinessStage.Ready, 10f), "no emit on the frame Ready is first reached");
        a += TestAssert.False(timing.ShouldEmit(GameplayReadinessStage.Ready, 10f + ForgottenRoadsDiscoveryHintPolicy.DelaySeconds - 0.01f),
            "no emit just under the delay");
        a += TestAssert.True(timing.ShouldEmit(GameplayReadinessStage.Ready, 10f + ForgottenRoadsDiscoveryHintPolicy.DelaySeconds),
            "emits once the delay has elapsed");
        a += TestAssert.True(timing.HasEmittedThisSession, "latch set after emission");

        // 3: only one message per gameplay session - repeated Ready ticks after the first emission
        // never fire again, even much later.
        a += TestAssert.False(timing.ShouldEmit(GameplayReadinessStage.Ready, 11f + ForgottenRoadsDiscoveryHintPolicy.DelaySeconds),
            "does not repeat on the very next tick");
        a += TestAssert.False(timing.ShouldEmit(GameplayReadinessStage.Ready, 9999f),
            "does not repeat arbitrarily later in the same session");

        // 4: zoning after the hint does not repeat it - Ready -> ZoneTransition -> Ready again
        // (settling past the delay a second time) must stay silent.
        ForgottenRoadsDiscoveryHintPolicy zoning = new ForgottenRoadsDiscoveryHintPolicy();
        a += TestAssert.False(zoning.ShouldEmit(GameplayReadinessStage.Ready, 0f), "zoning case: not ready yet at t=0");
        a += TestAssert.True(zoning.ShouldEmit(GameplayReadinessStage.Ready, ForgottenRoadsDiscoveryHintPolicy.DelaySeconds),
            "zoning case: emits once after the initial delay");
        a += TestAssert.False(zoning.ShouldEmit(GameplayReadinessStage.ZoneTransition, ForgottenRoadsDiscoveryHintPolicy.DelaySeconds + 1f),
            "zoning case: zone transition itself never emits");
        a += TestAssert.False(zoning.ShouldEmit(GameplayReadinessStage.WorldInitializing, ForgottenRoadsDiscoveryHintPolicy.DelaySeconds + 2f),
            "zoning case: world re-initializing after the zone never emits");
        float readyAgainAt = ForgottenRoadsDiscoveryHintPolicy.DelaySeconds + 3f;
        a += TestAssert.False(zoning.ShouldEmit(GameplayReadinessStage.Ready, readyAgainAt),
            "zoning case: re-reaching Ready in the SAME session does not emit immediately");
        a += TestAssert.False(zoning.ShouldEmit(GameplayReadinessStage.Ready, readyAgainAt + ForgottenRoadsDiscoveryHintPolicy.DelaySeconds),
            "zoning case: re-reaching Ready in the SAME session never emits again, even after the full delay elapses a second time");

        // 5: returning to character select and loading a new session may emit again.
        ForgottenRoadsDiscoveryHintPolicy newSession = new ForgottenRoadsDiscoveryHintPolicy();
        a += TestAssert.False(newSession.ShouldEmit(GameplayReadinessStage.Ready, 0f), "session 1: not ready on the first Ready tick");
        a += TestAssert.True(newSession.ShouldEmit(GameplayReadinessStage.Ready, ForgottenRoadsDiscoveryHintPolicy.DelaySeconds),
            "session 1: emits once its delay elapses");
        a += TestAssert.False(newSession.ShouldEmit(GameplayReadinessStage.CharacterSelect, 500f),
            "returning to character select never itself emits");
        a += TestAssert.False(newSession.HasEmittedThisSession, "latch clears once character select is reached");
        a += TestAssert.False(newSession.ShouldEmit(GameplayReadinessStage.Ready, 500f),
            "session 2: not ready yet at the moment Ready is (re-)reached");
        a += TestAssert.True(newSession.ShouldEmit(GameplayReadinessStage.Ready, 500f + ForgottenRoadsDiscoveryHintPolicy.DelaySeconds),
            "session 2: emits again after its own delay");

        // Reset() mirrors returning to character select for object reuse/hygiene (e.g. tests).
        ForgottenRoadsDiscoveryHintPolicy resettable = new ForgottenRoadsDiscoveryHintPolicy();
        resettable.ShouldEmit(GameplayReadinessStage.Ready, ForgottenRoadsDiscoveryHintPolicy.DelaySeconds);
        resettable.Reset();
        a += TestAssert.False(resettable.HasEmittedThisSession, "Reset clears the emitted latch");
        a += TestAssert.False(resettable.ShouldEmit(GameplayReadinessStage.Ready, ForgottenRoadsDiscoveryHintPolicy.DelaySeconds + 1000f),
            "Reset requires the full delay to elapse again before re-emitting");
        a += TestAssert.True(resettable.ShouldEmit(GameplayReadinessStage.Ready,
            ForgottenRoadsDiscoveryHintPolicy.DelaySeconds + 1000f + ForgottenRoadsDiscoveryHintPolicy.DelaySeconds),
            "Reset: emits again once its own delay elapses");

        return a;
    }
}
