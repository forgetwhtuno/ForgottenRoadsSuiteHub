namespace ErenshorSuiteHub
{
    // Pure policy: native/game reads are isolated in ErenshorSuiteHubPlugin. The policy only
    // decides when the Hub may become visible. "CanMove" is an acquisition signal: once a world
    // session has reached Ready, a normal native UI temporarily suppressing movement must not make
    // the Hub disappear. Zoning / character-select / missing world objects always revoke Ready.
    internal enum GameplayReadinessStage
    {
        CharacterSelect,
        PlayerObjectCreating,
        ZoneTransition,
        WorldInitializing,
        Stabilizing,
        Ready
    }

    internal struct GameplayReadinessSignals
    {
        internal bool InCharacterSelect;
        internal bool HasPlayerControl;
        internal bool HasPlayer;
        internal bool HasStats;
        internal bool PlayerActive;
        internal bool IsZoning;
        internal bool PlayerCanMove;
        internal bool HasSimManager;
        internal bool HasSimGrouping;
    }

    internal sealed class GameplayReadinessPolicy
    {
        // State is primary. This debounce merely prevents a single transient good frame from
        // presenting UI. One second is deliberately bounded and may be tuned after live tracing.
        internal const float RequiredStableSeconds = 1.0f;

        private float _candidateSince = -1f;
        private bool _readyLatched;
        private GameplayReadinessStage _stage = GameplayReadinessStage.CharacterSelect;

        internal GameplayReadinessStage Stage { get { return _stage; } }
        internal bool IsReady { get { return _stage == GameplayReadinessStage.Ready; } }

        internal GameplayReadinessStage Evaluate(GameplayReadinessSignals s, float unscaledTime)
        {
            if (s.InCharacterSelect)
                return ResetTo(GameplayReadinessStage.CharacterSelect);

            if (!s.HasPlayerControl || !s.HasPlayer || !s.HasStats || !s.PlayerActive)
                return ResetTo(GameplayReadinessStage.PlayerObjectCreating);

            if (s.IsZoning)
                return ResetTo(GameplayReadinessStage.ZoneTransition);

            if (!s.HasSimManager || !s.HasSimGrouping)
                return ResetTo(GameplayReadinessStage.WorldInitializing);

            // CanMove is positive evidence that the player has actually reached usable gameplay.
            // Do not revoke a previously established world solely because an ordinary native UI
            // temporarily disables movement.
            if (!_readyLatched && !s.PlayerCanMove)
                return ResetCandidate(GameplayReadinessStage.WorldInitializing);

            if (_readyLatched)
            {
                _stage = GameplayReadinessStage.Ready;
                return _stage;
            }

            if (_candidateSince < 0f || unscaledTime < _candidateSince)
            {
                _candidateSince = unscaledTime;
                _stage = GameplayReadinessStage.Stabilizing;
                return _stage;
            }

            if (unscaledTime - _candidateSince < RequiredStableSeconds)
            {
                _stage = GameplayReadinessStage.Stabilizing;
                return _stage;
            }

            _readyLatched = true;
            _stage = GameplayReadinessStage.Ready;
            return _stage;
        }

        internal void Reset()
        {
            _candidateSince = -1f;
            _readyLatched = false;
            _stage = GameplayReadinessStage.CharacterSelect;
        }

        private GameplayReadinessStage ResetTo(GameplayReadinessStage stage)
        {
            _readyLatched = false;
            return ResetCandidate(stage);
        }

        private GameplayReadinessStage ResetCandidate(GameplayReadinessStage stage)
        {
            _candidateSince = -1f;
            _stage = stage;
            return _stage;
        }
    }
}
