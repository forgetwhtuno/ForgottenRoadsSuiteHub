using System;
using System.Collections.Generic;

namespace ErenshorSuiteHub
{
    // Unity-free view state for the Suite Hub window.
    //
    // Deliberately separated from SuiteHubUi (which owns Canvas/RectTransform/Button objects) so
    // navigation, open/close and selection-validity rules can be tested deterministically without a
    // running game. SuiteHubUi holds one of these and re-reads it when rebuilding the page.
    internal sealed class SuiteHubView
    {
        private string _selectedModuleId = string.Empty;
        private bool _open;
        private bool _showAdvanced;
        private bool _showDeveloper;
        private string _lastActionResult = string.Empty;

        internal string SelectedModuleId { get { return _selectedModuleId; } }
        internal bool IsOpen { get { return _open; } }
        internal bool ShowAdvanced { get { return _showAdvanced; } }
        internal bool ShowDeveloper { get { return _showDeveloper; } }
        internal string LastActionResult { get { return _lastActionResult; } }
        internal bool IsOverviewSelected { get { return string.IsNullOrEmpty(_selectedModuleId); } }

        internal void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            if (!open) _lastActionResult = string.Empty;
        }

        internal bool Toggle()
        {
            _open = !_open;
            if (!_open) _lastActionResult = string.Empty;
            return _open;
        }

        // Selecting a module resets the per-page disclosure state so a freshly opened page never
        // inherits the previous module's expanded Advanced/Developer sections.
        internal void Select(string moduleId)
        {
            _selectedModuleId = moduleId ?? string.Empty;
            _showAdvanced = false;
            _showDeveloper = false;
            _lastActionResult = string.Empty;
        }

        internal void SelectOverview()
        {
            Select(string.Empty);
        }

        internal void SetAdvanced(bool value) { _showAdvanced = value; }
        internal void SetDeveloper(bool value) { _showDeveloper = value; }
        internal void SetActionResult(string result) { _lastActionResult = result ?? string.Empty; }

        // A module can disappear underneath us (uninstalled between discovery polls, or the whole
        // list is empty during zoning). Fall back to Overview rather than rendering a dead page.
        internal void EnsureSelectionValid(List<ModPresence> mods)
        {
            if (string.IsNullOrEmpty(_selectedModuleId)) return;
            if (mods == null) { SelectOverview(); return; }

            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i].Installed &&
                    string.Equals(mods[i].ModuleId, _selectedModuleId, StringComparison.Ordinal))
                    return;
            }
            SelectOverview();
        }

        internal void Reset()
        {
            _selectedModuleId = string.Empty;
            _open = false;
            _showAdvanced = false;
            _showDeveloper = false;
            _lastActionResult = string.Empty;
        }
    }
}
