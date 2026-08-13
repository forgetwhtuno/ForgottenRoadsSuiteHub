using System.Collections.Generic;
using ErenshorSuiteHub;

// Deterministic tests for the Unity-free Suite Hub window state: navigation, open/close, per-page
// disclosure reset, and selection validity when a module disappears underneath the UI.
internal static class SuiteHubViewTests
{
    private static List<ModPresence> Mods(params string[] installedIds)
    {
        List<ModPresence> list = new List<ModPresence>();
        for (int i = 0; i < SuiteModuleCatalog.All.Length; i++)
        {
            SuiteModuleDefinition def = SuiteModuleCatalog.All[i];
            bool installed = false;
            for (int j = 0; j < installedIds.Length; j++)
                if (installedIds[j] == def.Id) { installed = true; break; }
            list.Add(new ModPresence(def, installed));
        }
        return list;
    }

    internal static int RunAll()
    {
        int a = 0;
        SuiteHubView view = new SuiteHubView();

        // --- initial state -------------------------------------------------------------------
        a += TestAssert.True(!view.IsOpen, "view starts closed");
        a += TestAssert.True(view.IsOverviewSelected, "view starts on Overview");
        a += TestAssert.Equal("", view.SelectedModuleId, "no module selected initially");

        // --- open/close state machine --------------------------------------------------------
        a += TestAssert.True(view.Toggle(), "toggle opens");
        a += TestAssert.True(view.IsOpen, "open after toggle");
        a += TestAssert.True(!view.Toggle(), "toggle closes");
        a += TestAssert.True(!view.IsOpen, "closed after second toggle");

        view.SetOpen(true);
        a += TestAssert.True(view.IsOpen, "SetOpen(true) opens");
        view.SetOpen(true);
        a += TestAssert.True(view.IsOpen, "SetOpen is idempotent");

        // --- navigation --------------------------------------------------------------------
        view.Select("journal");
        a += TestAssert.Equal("journal", view.SelectedModuleId, "module selected");
        a += TestAssert.True(!view.IsOverviewSelected, "module page is not Overview");

        // Selecting resets per-page disclosure so a new page never inherits the previous one's state.
        view.SetAdvanced(true);
        view.SetDeveloper(true);
        a += TestAssert.True(view.ShowAdvanced, "advanced expanded");
        a += TestAssert.True(view.ShowDeveloper, "developer expanded");
        view.Select("pvp");
        a += TestAssert.True(!view.ShowAdvanced, "advanced collapses on module change");
        a += TestAssert.True(!view.ShowDeveloper, "developer collapses on module change");

        view.SelectOverview();
        a += TestAssert.True(view.IsOverviewSelected, "SelectOverview returns to Overview");

        // --- action result surfacing ---------------------------------------------------------
        view.Select("journal");
        view.SetActionResult("Suite bridge unavailable");
        a += TestAssert.Equal("Suite bridge unavailable", view.LastActionResult, "action result retained");
        view.Select("journal");
        a += TestAssert.Equal("", view.LastActionResult, "action result cleared on reselect");

        // Closing the window clears any stale action result.
        view.SetActionResult("something happened");
        view.SetOpen(false);
        a += TestAssert.Equal("", view.LastActionResult, "action result cleared on close");

        // --- selection validity --------------------------------------------------------------
        view.Select("journal");
        view.EnsureSelectionValid(Mods("journal", "pvp"));
        a += TestAssert.Equal("journal", view.SelectedModuleId, "installed selection survives validation");

        // Module uninstalled underneath the open window -> fall back to Overview, not a dead page.
        view.EnsureSelectionValid(Mods("pvp"));
        a += TestAssert.True(view.IsOverviewSelected, "uninstalled selection falls back to Overview");

        // Present in the catalog but not installed is still not selectable.
        view.Select("crafting");
        view.EnsureSelectionValid(Mods("journal"));
        a += TestAssert.True(view.IsOverviewSelected, "not-installed module falls back to Overview");

        // Empty/null discovery (zoning) must not throw and must fall back safely.
        view.Select("journal");
        view.EnsureSelectionValid(new List<ModPresence>());
        a += TestAssert.True(view.IsOverviewSelected, "empty discovery falls back to Overview");

        view.Select("journal");
        view.EnsureSelectionValid(null);
        a += TestAssert.True(view.IsOverviewSelected, "null discovery falls back to Overview");

        // Overview selection is always valid and never disturbed.
        view.SelectOverview();
        view.EnsureSelectionValid(Mods());
        a += TestAssert.True(view.IsOverviewSelected, "Overview stays valid with nothing installed");

        // --- cleanup ---------------------------------------------------------------------------
        view.SetOpen(true);
        view.Select("pvp");
        view.SetAdvanced(true);
        view.Reset();
        a += TestAssert.True(!view.IsOpen, "reset closes");
        a += TestAssert.True(view.IsOverviewSelected, "reset returns to Overview");
        a += TestAssert.True(!view.ShowAdvanced, "reset collapses advanced");
        a += TestAssert.Equal("", view.LastActionResult, "reset clears action result");

        return a;
    }
}
