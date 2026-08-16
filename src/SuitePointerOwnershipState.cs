namespace ErenshorSuiteHub
{
    // Unity-free per-handle gesture state. Ownership begins on pointer-down, before uGUI's drag
    // threshold, because native camera input is frame-based and must be gated for the whole press.
    internal sealed class SuitePointerOwnershipState
    {
        internal bool OwnsPointer { get; private set; }
        internal bool IsDragging { get; private set; }

        internal bool PointerDown()
        {
            if (OwnsPointer) return false;
            OwnsPointer = true;
            return true;
        }

        internal bool BeginDrag()
        {
            IsDragging = true;
            return PointerDown();
        }

        internal bool Release()
        {
            bool owned = OwnsPointer;
            OwnsPointer = false;
            IsDragging = false;
            return owned;
        }
    }
}
