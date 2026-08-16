using System;
using System.IO;

internal static class SuiteDockSourceContractTests
{
    internal static int RunAll()
    {
        int a = 0;
        string root = Environment.GetEnvironmentVariable("ERENSHOR_SUITEHUB_SOURCE_ROOT") ?? string.Empty;
        a += TestAssert.True(Directory.Exists(root), "source contract test root is available");
        if (!Directory.Exists(root)) return a;

        string ui = File.ReadAllText(Path.Combine(root, "src", "SuiteHubUi.cs"));
        string plugin = File.ReadAllText(Path.Combine(root, "src", "ErenshorSuiteHubPlugin.cs"));
        string drag = File.ReadAllText(Path.Combine(root, "src", "SuiteDragGuard.cs"));
        string policy = File.ReadAllText(Path.Combine(root, "src", "SuiteDockPolicy.cs"));

        a += TestAssert.Equal(1, Count(ui, "new GameObject(\"ErenshorSuiteHubCanvas\""),
            "one Hub root creation site");
        a += TestAssert.Equal(1, Count(ui, "NewUi(\"SuiteHubDockMenu\""),
            "one dock menu creation site");
        a += TestAssert.True(Ordered(ui, "_root.SetActive(false);", "UnityEngine.Object.Destroy(_root);"),
            "old root is deactivated before deferred Unity destroy");
        a += TestAssert.True(ui.IndexOf("b.onClick.AddListener(delegate { _dockToggleQueued = true; });", StringComparison.Ordinal) >= 0,
            "MODS click queues dock toggle rather than full Suite toggle");
        a += TestAssert.True(ui.IndexOf("AddRowButton(_dockMenuRect, \"Forgotten Roads\"", StringComparison.Ordinal) >= 0 &&
            ui.IndexOf("OnRequestOpenSuite", StringComparison.Ordinal) >= 0,
            "Forgotten Roads is a separate dock row");
        a += TestAssert.True(ui.IndexOf("AttachDrag(grip, _launcherRect", StringComparison.Ordinal) >= 0 &&
            ui.IndexOf("AttachDrag(button", StringComparison.Ordinal) < 0,
            "drag grip and launcher button stay separate");
        a += TestAssert.True(plugin.IndexOf("TryInvokeAction(SuiteDockPolicy.OpenPanelActionId", StringComparison.Ordinal) >= 0 &&
            policy.IndexOf("OpenPanelActionId = \"openPanel\"", StringComparison.Ordinal) >= 0,
            "dock module route is hard-bound to literal openPanel");
        a += TestAssert.True(plugin.IndexOf("_ui.CompleteDockLaunch(true);", StringComparison.Ordinal) >= 0,
            "production successful launch uses tested dock completion transition");
        a += TestAssert.True(PointerDownAcquires(drag),
            "pointer-down acquires native UI ownership before drag threshold");
        a += TestAssert.True(ui.IndexOf("bg.raycastTarget = true;", StringComparison.Ordinal) >= 0,
            "dock/panel background participates in raycasting");
        a += TestAssert.False(ContainsOnGuiImplementation(Path.Combine(root, "src")),
            "SuiteHub source has no OnGUI implementation");
        return a;
    }

    private static int Count(string text, string needle)
    {
        int count = 0;
        int at = 0;
        while (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(needle))
        {
            at = text.IndexOf(needle, at, StringComparison.Ordinal);
            if (at < 0) break;
            count++;
            at += needle.Length;
        }
        return count;
    }

    private static bool Ordered(string text, string first, string second)
    {
        int a = text.IndexOf(first, StringComparison.Ordinal);
        int b = text.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b > a;
    }

    private static bool PointerDownAcquires(string drag)
    {
        int start = drag.IndexOf("public void OnPointerDown", StringComparison.Ordinal);
        int end = drag.IndexOf("public void OnBeginDrag", StringComparison.Ordinal);
        if (start < 0 || end <= start) return false;
        string body = drag.Substring(start, end - start);
        return body.IndexOf("Acquire();", StringComparison.Ordinal) >= 0 ||
            body.IndexOf("AcquireOwnership();", StringComparison.Ordinal) >= 0;
    }

    private static bool ContainsOnGuiImplementation(string sourceDir)
    {
        string[] files = Directory.GetFiles(sourceDir, "*.cs", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string source = File.ReadAllText(files[i]);
            if (source.IndexOf("void OnGUI(", StringComparison.Ordinal) >= 0) return true;
        }
        return false;
    }
}
