namespace ErenshorSuiteHub
{
    // Unity-free state machine for the MODS dock. Keeping expansion/customization transitions out
    // of MonoBehaviour/uGUI code gives deterministic coverage for repeated click cycles and keeps
    // auto-collapse behavior explicit.
    internal sealed class SuiteDockInteractionState
    {
        internal bool IsExpanded { get; private set; }
        internal bool IsCustomizing { get; private set; }

        internal void Toggle()
        {
            if (IsExpanded) Collapse();
            else Expand(false);
        }

        internal void Expand(bool customize)
        {
            IsExpanded = true;
            IsCustomizing = customize;
        }

        internal void ShowCustomize()
        {
            IsExpanded = true;
            IsCustomizing = true;
        }

        internal void DoneCustomize()
        {
            if (!IsExpanded) return;
            IsCustomizing = false;
        }

        internal void Collapse()
        {
            IsExpanded = false;
            IsCustomizing = false;
        }

        internal bool CompleteLaunch(bool succeeded)
        {
            if (succeeded) Collapse();
            return succeeded;
        }
    }
}
