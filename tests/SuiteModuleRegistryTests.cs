using ErenshorSuiteHub;

internal static class SuiteModuleRegistryTests
{
    internal static int RunAll()
    {
        int a = 0;
        SuiteModuleRegistry r = new SuiteModuleRegistry();
        object owner1 = new object();
        object owner2 = new object();
        string error;
        SuiteModuleDescriptor d = Valid("journal", "1.2.3");

        a += TestAssert.True(r.Register(d, owner1, out error), "initial registration");
        a += TestAssert.Equal(1, r.Count, "count after registration");
        a += TestAssert.Equal("1.2.3", r.Get("journal").Version, "registered descriptor readable");

        SuiteModuleDescriptor update = Valid("journal", "1.2.4");
        a += TestAssert.True(r.Register(update, owner1, out error), "same provider can refresh registration");
        a += TestAssert.Equal(1, r.Count, "refresh does not duplicate");
        a += TestAssert.Equal("1.2.4", r.Get("journal").Version, "refresh replaces descriptor");

        a += TestAssert.False(r.Register(Valid("journal", "9"), owner2, out error), "second live provider rejected");
        a += TestAssert.Equal(1, r.Count, "duplicate provider did not replace");
        a += TestAssert.False(r.Unregister("journal", owner2), "wrong owner cannot unregister");
        a += TestAssert.True(r.Unregister("journal", owner1), "owner unregisters on unload");
        a += TestAssert.Equal(0, r.Count, "unload removes module");

        a += TestAssert.True(r.Register(Valid("journal", "2.0"), owner2, out error), "late reload registers after unload");
        a += TestAssert.Equal("2.0", r.Get("journal").Version, "late reload visible once");

        SuiteModuleDescriptor bad = Valid("not-a-suite-module", "1");
        a += TestAssert.False(r.Register(bad, owner1, out error), "unknown module rejected");

        SuiteModuleDefinition followDef = SuiteModuleCatalog.Find("follow");
        a += TestAssert.True(followDef != null, "follow module catalogued");
        a += TestAssert.True(followDef.HasDedicatedPanel, "follow advertises its standalone guide/status fallback panel");

        SuiteModuleDefinition partyToolsDef = SuiteModuleCatalog.Find("partytools");
        a += TestAssert.True(partyToolsDef.HasDedicatedPanel, "party tools keeps its dedicated/transient panel");
        return a;
    }

    private static SuiteModuleDescriptor Valid(string id, string version)
    {
        SuiteModuleDescriptor d = new SuiteModuleDescriptor();
        d.ProtocolVersion = 1;
        d.ModuleId = id;
        d.DisplayName = id;
        d.Version = version;
        d.Summary = "summary";
        return d;
    }
}
