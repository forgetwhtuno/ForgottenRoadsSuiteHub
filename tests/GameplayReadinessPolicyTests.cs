using ErenshorSuiteHub;

internal static class GameplayReadinessPolicyTests
{
    internal static int RunAll()
    {
        int a = 0;
        GameplayReadinessPolicy p = new GameplayReadinessPolicy();
        GameplayReadinessSignals s = ReadySignals();

        s.InCharacterSelect = true;
        a += TestAssert.Equal(GameplayReadinessStage.CharacterSelect, p.Evaluate(s, 0f), "character select blocks");

        s = ReadySignals(); s.HasPlayer = false;
        a += TestAssert.Equal(GameplayReadinessStage.PlayerObjectCreating, p.Evaluate(s, 1f), "missing player blocks");

        s = ReadySignals(); s.IsZoning = true;
        a += TestAssert.Equal(GameplayReadinessStage.ZoneTransition, p.Evaluate(s, 2f), "zoning blocks");

        s = ReadySignals(); s.HasSimGrouping = false;
        a += TestAssert.Equal(GameplayReadinessStage.WorldInitializing, p.Evaluate(s, 3f), "grouping rebuild blocks");

        s = ReadySignals(); s.PlayerCanMove = false;
        a += TestAssert.Equal(GameplayReadinessStage.WorldInitializing, p.Evaluate(s, 4f), "CanMove required for first acquisition");

        s = ReadySignals();
        a += TestAssert.Equal(GameplayReadinessStage.Stabilizing, p.Evaluate(s, 5f), "first good sample stabilizes");
        a += TestAssert.Equal(GameplayReadinessStage.Stabilizing, p.Evaluate(s, 5.99f), "not ready before bounded stability");
        a += TestAssert.Equal(GameplayReadinessStage.Ready, p.Evaluate(s, 6f), "ready after bounded stability");

        s.PlayerCanMove = false;
        a += TestAssert.Equal(GameplayReadinessStage.Ready, p.Evaluate(s, 6.1f), "transient native UI CanMove=false does not hide established Hub");

        s.IsZoning = true;
        a += TestAssert.Equal(GameplayReadinessStage.ZoneTransition, p.Evaluate(s, 7f), "zoning revokes readiness");
        s.IsZoning = false; s.PlayerCanMove = true;
        a += TestAssert.Equal(GameplayReadinessStage.Stabilizing, p.Evaluate(s, 8f), "post-zone requires fresh stability");

        s.HasStats = false;
        a += TestAssert.Equal(GameplayReadinessStage.PlayerObjectCreating, p.Evaluate(s, 9f), "lost player graph fails closed");
        return a;
    }

    private static GameplayReadinessSignals ReadySignals()
    {
        GameplayReadinessSignals s = new GameplayReadinessSignals();
        s.HasPlayerControl = true;
        s.HasPlayer = true;
        s.HasStats = true;
        s.PlayerActive = true;
        s.PlayerCanMove = true;
        s.HasSimManager = true;
        s.HasSimGrouping = true;
        return s;
    }
}
