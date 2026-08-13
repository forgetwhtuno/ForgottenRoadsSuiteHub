using System;

internal static class TestRunner
{
    public static int Main()
    {
        try
        {
            int assertions = 0;
            assertions += GameplayReadinessPolicyTests.RunAll();
            assertions += ModDiscoveryTests.RunAll();
            assertions += SuiteModuleRegistryTests.RunAll();
            assertions += SuiteWireCodecTests.RunAll();
            assertions += SuiteUiGeometryTests.RunAll();
            assertions += SuiteHubViewTests.RunAll();
            Console.WriteLine("PASS Erenshor Suite Hub deterministic suite - " + assertions.ToString() + " assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL Erenshor Suite Hub deterministic suite: " + ex.Message);
            return 1;
        }
    }
}
