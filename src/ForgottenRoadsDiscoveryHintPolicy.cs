namespace ErenshorSuiteHub
{
    // Pure policy deciding WHEN to emit the one-time Forgotten Roads discovery chat hint. Reuses
    // GameplayReadinessPolicy's authoritative stage, but tracks a separate, longer-lived concept:
    // "has THIS gameplay session already been told the hint". A session spans from the moment
    // GameplayReadinessStage first reaches Ready, through any number of later zone transitions
    // (Ready -> ZoneTransition/WorldInitializing/Stabilizing -> Ready again), and only ends when the
    // stage actually returns to CharacterSelect - i.e. a genuinely different character/session.
    //
    // The required-delay window (DelaySeconds, inside the requested ~3-5s) doubles as the "short
    // bounded settling window" for modules that register their Aura bridge slightly after Hub
    // becomes Ready: by the time this policy fires, ErenshorSuiteHubPlugin's own discovery/bridge
    // polling (every 1-2s) has already had at least one full pass to pick up a late registration, so
    // no separate settling mechanism is needed here.
    internal sealed class ForgottenRoadsDiscoveryHintPolicy
    {
        internal const float MinDelaySeconds = 3f;
        internal const float MaxDelaySeconds = 5f;
        internal const float DelaySeconds = 4f;

        private bool _emittedThisSession;
        private float _readySince = -1f;
        private GameplayReadinessStage _lastStage = GameplayReadinessStage.CharacterSelect;

        internal bool HasEmittedThisSession { get { return _emittedThisSession; } }

        // Call once per Update tick with the Hub's current authoritative readiness stage and
        // Time.unscaledTime. Returns true exactly once per gameplay session, approximately
        // DelaySeconds after that session's stage first settles on Ready.
        internal bool ShouldEmit(GameplayReadinessStage stage, float unscaledTime)
        {
            if (stage == GameplayReadinessStage.CharacterSelect)
            {
                // A genuinely new session starts the next time Ready is reached.
                _emittedThisSession = false;
                _readySince = -1f;
                _lastStage = stage;
                return false;
            }

            if (stage != GameplayReadinessStage.Ready)
            {
                // Still loading, or mid-zone-transition within the SAME session. Never reset
                // _emittedThisSession here - that is exactly what would make zoning repeat the
                // hint. Just stop accumulating the delay until Ready is reached again.
                _readySince = -1f;
                _lastStage = stage;
                return false;
            }

            if (_emittedThisSession)
            {
                _lastStage = stage;
                return false;
            }

            bool justBecameReady = _lastStage != GameplayReadinessStage.Ready;
            if (justBecameReady || _readySince < 0f || unscaledTime < _readySince) _readySince = unscaledTime;
            _lastStage = stage;

            if (unscaledTime - _readySince < DelaySeconds) return false;

            _emittedThisSession = true;
            return true;
        }

        internal void Reset()
        {
            _emittedThisSession = false;
            _readySince = -1f;
            _lastStage = GameplayReadinessStage.CharacterSelect;
        }
    }
}
