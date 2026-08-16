using ErenshorSuiteHub;

internal static class SuitePointerOwnershipStateTests
{
    internal static int RunAll()
    {
        int a = 0;
        SuitePointerOwnershipState s = new SuitePointerOwnershipState();
        a += TestAssert.True(s.PointerDown(), "pointer-down acquires before drag threshold");
        a += TestAssert.True(s.OwnsPointer && !s.IsDragging, "press ownership is distinct from movement");
        a += TestAssert.False(s.BeginDrag(), "begin-drag does not double-acquire an owned press");
        a += TestAssert.True(s.IsDragging, "begin-drag marks movement");
        a += TestAssert.True(s.Release(), "pointer-up/end-drag releases ownership");
        a += TestAssert.False(s.OwnsPointer || s.IsDragging, "release clears all gesture state");
        a += TestAssert.False(s.Release(), "repeated release is idempotent");
        a += TestAssert.True(s.BeginDrag(), "begin-drag can recover if pointer-down was missed");
        a += TestAssert.True(s.Release(), "lost pointer recovery releases recovered ownership");
        for (int i = 0; i < 20; i++) { s.PointerDown(); s.BeginDrag(); s.Release(); }
        a += TestAssert.False(s.OwnsPointer || s.IsDragging, "repeated cycles never leave ownership stuck");
        return a;
    }
}
