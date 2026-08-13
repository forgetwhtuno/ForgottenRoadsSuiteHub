using System;

internal static class TestRunner
{
    public static int Main()
    {
        try
        {
            int assertions = 0;
            assertions += ModDiscoveryTests.RunAll();
            Console.WriteLine("PASS Erenshor Suite Hub test suite - " + assertions.ToString() + " assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL Erenshor Suite Hub test suite: " + ex.Message);
            return 1;
        }
    }
}
